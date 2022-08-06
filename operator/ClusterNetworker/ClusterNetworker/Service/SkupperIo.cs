using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using ClusterNetworker.Entities;
using DotnetKubernetesClient;
using k8s;
using k8s.Models;
using KubeOps.Operator.Events;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace ClusterNetworker.Service
{
    public class SkupperIo : INetworkingHandler
    {
        private const string Namespace = "skupper-site-controller";

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

        public SkupperIo(IKubernetesClient client, IEventManager eventManager, HttpClient web, ITokenStore tokenStore, ILogger<SkupperIo> logger)
        {
            _client = client;
            _eventManager = eventManager;
            _web = web;
            _tokenStore = tokenStore;
            _logger = logger;
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
                await _client.ApiClient.CreateNamespaceAsync(
                    new V1Namespace(metadata: new V1ObjectMeta(name: Namespace)));
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

                await _client.ApiClient.CreateNamespacedConfigMapAsync(cm, Namespace);

                var objects = await Yaml.LoadAllFromStreamAsync(await _web.GetStreamAsync(
                    "https://raw.githubusercontent.com/skupperproject/skupper/master/cmd/site-controller/deploy-watch-all-ns.yaml"));

                foreach (object o in objects)
                {
                    Task ok = o switch
                    {
                        V1ServiceAccount sa => _client.ApiClient.CreateNamespacedServiceAccountAsync(sa, Namespace),
                        V1ClusterRole cr => _client.ApiClient.CreateClusterRoleAsync(cr),
                        V1ClusterRoleBinding crb => _client.ApiClient.CreateClusterRoleBindingAsync(crb),
                        V1Deployment dep => _client.ApiClient.CreateNamespacedDeploymentAsync(dep, Namespace),
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
            }
        }

        public async Task MonitorAsync(ClusterPoolEntity entity)
        {
            if (!entity
                .Spec.DeploymentsExposed.Any())
                return;
            var deployments = await _client.ApiClient.ListDeploymentForAllNamespacesAsync();
            foreach (var deploymentExposed in entity.Spec.DeploymentsExposed)
            {
                var dep = deployments.Items.FirstOrDefault(x =>
                    x.Namespace() == deploymentExposed.Namespace && x.Name() == deploymentExposed.Name);

                if (dep == null) continue; // log this
            }
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
        }

        private async Task<bool> IsSkuppperInstalledAndReady()
        {
            try
            {
                var deployment =
                        await _client.ApiClient.ReadNamespacedDeploymentStatusAsync("skupper-site-controller",
                            "skupper-site-controller");

                return deployment.Status.ReadyReplicas == deployment.Status.AvailableReplicas;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}