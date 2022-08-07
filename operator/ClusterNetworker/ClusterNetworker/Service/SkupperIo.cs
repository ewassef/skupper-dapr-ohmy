using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using ClusterNetworker.Entities;
using DotnetKubernetesClient;
using k8s;
using k8s.Models;
using KubeOps.Operator.Events;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace ClusterNetworker.Service
{
    public class SkupperIo : INetworkingHandler
    {
        private const string DEFAULT_NAMESPACE = "skupper-site-controller";

        private const string patchStr = @"
{
    ""metadata"": {
        ""annotations"": {
            ""skupper.io/proxy"": ""http""
        }
    }
}";

        private readonly IKubernetesClient _client;
        private readonly IEventManager _eventManager;
        private readonly ILogger<SkupperIo> _logger;
        private readonly ITokenStore _tokenStore;
        private readonly HttpClient _web;
        private readonly string Namespace;

        public SkupperIo(IKubernetesClient client, IEventManager eventManager, HttpClient web, ITokenStore tokenStore, IConfiguration config, ILogger<SkupperIo> logger)
        {
            _client = client;
            _eventManager = eventManager;
            _web = web;
            _tokenStore = tokenStore;
            _logger = logger;
            Namespace = config["Namespace"] ?? DEFAULT_NAMESPACE;
        }

        /// <summary>
        /// This method will check to see if skupper is installed and if it isnt, follow the
        /// directions listed in the skupper website for YAML based installation
        /// </summary>
        /// <param name="entity">the entity initiating the pool</param>
        /// <see cref="https://skupper.io/docs/declarative/tutorial.html"/>
        /// <returns>void</returns>
        public async Task InitiateProductAsync(ClusterPoolEntity entity)
        {
            if (!(await IsSkuppperInstalledAndReady()))
            {
                // create it

                await _eventManager.PublishAsync(entity, "Initializing Skupper.io",
                     "Installing skupper.io from [https://raw.githubusercontent.com/skupperproject/skupper/master/cmd/site-controller/deploy-watch-all-ns.yaml]to all namespaces");
                await IgnoreConflicts(() => _client.ApiClient.CreateNamespaceAsync(
                    new V1Namespace(metadata: new V1ObjectMeta(name: Namespace))));

                var cm = new V1ConfigMap(metadata: new V1ObjectMeta(name: "skupper-site",
                    namespaceProperty: Namespace));
                cm.Data = new Dictionary<string, string>
                {
                    { "cluster-local", "false" },
                    { "console", "true" },
                    { "console-authentication", "internal" },
                    { "console-password", "rubble" },
                    { "console-user", "barney" },
                    { "edge", !entity.Spec.HasExternalAccess ? "true" : "false" },
                    { "ingress", entity.Spec.ExposureType.ToString().ToLower() },
                    { "ingress-host", entity.Spec.ClusterName },
                    { "router-ingress-host", entity.Spec.ClusterName },
                    { "name", entity.Spec.ClusterName },
                    { "router-console", "true" },
                    { "service-controller", "true" },
                    { "service-sync", "true" }
                };

                if (_client.ApiClient.ListNamespacedConfigMap(Namespace).Items.Any(x => x.Name() == "skupper-site"))
                    await _client.ApiClient.ReplaceNamespacedConfigMapAsync(cm, cm.Name(), cm.Namespace());
                else
                    await _client.ApiClient.CreateNamespacedConfigMapAsync(cm, Namespace);

                var objects = await Yaml.LoadAllFromStreamAsync(await _web.GetStreamAsync(
                    "https://raw.githubusercontent.com/skupperproject/skupper/master/cmd/site-controller/deploy-watch-all-ns.yaml"));

                foreach (object o in objects)
                {
                    Task ok = o switch
                    {
                        V1ServiceAccount sa => IgnoreConflicts(() =>
                        {
                            sa.Metadata.NamespaceProperty = Namespace;
                            return _client.ApiClient.CreateNamespacedServiceAccountAsync(sa, Namespace);
                        }),
                        V1ClusterRole cr => IgnoreConflicts(() => _client.ApiClient.CreateClusterRoleAsync(cr)),
                        V1ClusterRoleBinding crb => IgnoreConflicts(() =>
                        {
                            foreach (var crbSubject in crb.Subjects)
                            {
                                crbSubject.NamespaceProperty = Namespace;
                            }

                            return _client.ApiClient.CreateClusterRoleBindingAsync(crb);
                        }),
                        V1Deployment dep => IgnoreConflicts(() =>
                        {
                            dep.Metadata.NamespaceProperty = Namespace;
                            return _client.ApiClient.CreateNamespacedDeploymentAsync(dep, Namespace);
                        }),
                        _ => throw new ArgumentException()
                    };
                    await ok;
                    await Task.Delay(550);
                }

                // When using kind, be sure to patch the svc to point to the right nodeport
                if (entity.Spec.HasExternalAccess && entity.Spec.ExposureType == ClusterPoolEntity.ExposureType.NodePort)
                {
                    while ((await _client.ApiClient.ListNamespacedServiceAsync(Namespace)).Items
                           .All(x => x.Name() != "skupper-router"))
                    {
                        await Task.Delay(5000);
                    }

                    var svc = await _client.ApiClient.ReadNamespacedServiceAsync("skupper-router", Namespace);
                    foreach (var v1ServicePort in svc.Spec.Ports)
                    {
                        _ = v1ServicePort.Name switch
                        {
                            "inter-router" => v1ServicePort.NodePort =
                                entity.Spec.NodeportConfigurations.InterRouterSvc,
                            "edge" => v1ServicePort.NodePort = entity.Spec.NodeportConfigurations.EdgeSvc,
                            _ => v1ServicePort.NodePort = v1ServicePort.NodePort
                        };
                    }

                    await _client.ApiClient.ReplaceNamespacedServiceAsync(svc, svc.Name(), svc.Namespace());
                }

                entity.Status.State = ClusterPoolEntity.State.Patching;
                await _client.UpdateStatus(entity);
            }
        }

        public async Task MonitorAsync(ClusterPoolEntity entity)
        {
            if (entity
                    .Spec.DeploymentsExposed != null && entity
                    .Spec.DeploymentsExposed.Any())
            {
                var deployments = await _client.ApiClient.ListDeploymentForAllNamespacesAsync();
                foreach (var deploymentExposed in entity.Spec.DeploymentsExposed)
                {
                    var dep = deployments.Items.FirstOrDefault(x =>
                        x.Namespace() == deploymentExposed.Namespace && x.Name() == deploymentExposed.Name);

                    if (dep == null) continue; // log this

                    await _client.ApiClient.PatchNamespacedDeploymentAsync(
                        new V1Patch(patchStr, V1Patch.PatchType.MergePatch), dep.Name(), dep.Namespace());
                }
            }
            // update status
            var pods = await _client.ApiClient.ListNamespacedPodAsync(Namespace);
            var pod = pods.Items.FirstOrDefault(x =>
                x.Metadata.Labels.Any(x =>
                    x.Key == "app.kubernetes.io/name" && x.Value == "skupper-service-controller"));
            if (pod == null) return; // also how?
            var result = await ExecOnPodAsync<Site[]>(pod, new[] { "get", "sites", "-o", "json" });
            var svcs = await ExecOnPodAsync<Service[]>(pod, new[] { "get", "services", "-o", "json" });
            entity.Status.NumberOfClusters = result?.Length ?? 0;
            entity.Status.OverallNumberOfExposedServices = svcs.Length;
            await _client.UpdateStatus(entity);
        }

        public async Task PatchProductAsync(ClusterPoolEntity entity)
        {
            // things are ready, either create a new join token and broadcast it, OR pull the pool
            // token from secrets storage

            var token = await _tokenStore.RetrieveTokenAsync(entity.Name());
            if (token == null)
            {
                token = new V1Secret(metadata: new V1ObjectMeta(name: $"{entity.Name()}-token"));
                token.EnsureMetadata().EnsureLabels()["skupper.io/type"] = "connection-token-request";
                await _client.ApiClient.CreateNamespacedSecretAsync(token, Namespace);
                do
                {
                    await Task.Delay(1000);
                    token = await _client.ApiClient.ReadNamespacedSecretAsync($"{entity.Name()}-token", Namespace);
                } while (token.Data == null || token.Data?.Count == 0);

                await _tokenStore.UpsertTokenAsync(entity.Name(), token);
            }
            else
            {
                await _client.ApiClient.CreateNamespacedSecretAsync(token, Namespace);
            }

            entity.Status.State = ClusterPoolEntity.State.Registered;
            await _client.UpdateStatus(entity);
        }

        protected async Task<T?> IgnoreConflicts<T>(Func<Task<T>> callToK8s)
        {
            T? result = default(T);
            try
            {
                result = await callToK8s();
            }
            catch (Microsoft.Rest.HttpOperationException hoe)
            {
                if (hoe.Response.StatusCode != HttpStatusCode.Conflict)
                {
                    _logger.LogError(hoe.Response.Content ?? hoe.Message);
                    throw;
                }
            }

            return result;
        }

        private async Task<T?> ExecOnPodAsync<T>(V1Pod pod, string[] command) where T : class
        {
            var socket = await _client.ApiClient.WebSocketNamespacedPodExecAsync(
                    pod.Name(),
                    pod.Namespace(),
                    command: command)
                .ConfigureAwait(false);

            var demux = new StreamDemuxer(socket);
            demux.Start();

            var stream = demux.GetStream(1, 1);
            var reader = new StreamReader(stream);
            var tmp = await reader.ReadToEndAsync();
            if (typeof(T) == typeof(string))
                return tmp as T;
            return JsonSerializer.Deserialize<T>(tmp);
        }

        private async Task<bool> IsSkuppperInstalledAndReady()
        {
            try
            {
                var deployment =
                        await _client.ApiClient.ReadNamespacedDeploymentStatusAsync("skupper-site-controller",
                            Namespace);

                return deployment.Status.ReadyReplicas != null && deployment.Status.ReadyReplicas == deployment.Status.AvailableReplicas;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private class Service
        {
            public string address { get; set; }
            public string protocol { get; set; }
            public object requests_handled { get; set; }
            public object requests_received { get; set; }
            public List<Target> targets { get; set; }
        }

        private class Site
        {
            public string @namespace { get; set; }
            public List<object> connected { get; set; }
            public bool edge { get; set; }
            public bool gateway { get; set; }
            public string site_id { get; set; }
            public string site_name { get; set; }
            public string url { get; set; }
            public string version { get; set; }
        }

        private class Target
        {
            public string name { get; set; }
            public string site_id { get; set; }
            public string target { get; set; }
        }
    }
}