﻿using ClusterNetworker.Entities;
using ClusterNetworker.Finalizer;
using k8s.Models;
using KubeOps.Operator.Controller;
using KubeOps.Operator.Controller.Results;
using KubeOps.Operator.Finalizer;
using KubeOps.Operator.Rbac;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using ClusterNetworker.Service;
using DotnetKubernetesClient;

namespace ClusterNetworker.Controller
{
    [EntityRbac(typeof(ClusterPoolEntity), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Namespace),
        typeof(V1Secret),
        typeof(V1ConfigMap),
        typeof(V1ServiceAccount),
        typeof(V1Deployment),
        typeof(V1ClusterRole),
        typeof(V1ClusterRoleBinding),
        typeof(V1Service),
        typeof(V1Pod),
        Verbs = RbacVerb.All)] // Dont do this, add only the rbac you need
    [GenericRbac(Groups = new[] { "" }, Resources = new[] { "pods/exec" }, Verbs = RbacVerb.All)]
    public class ClusterPoolController : IResourceController<ClusterPoolEntity>
    {
        private readonly IKubernetesClient _client;
        private readonly IFinalizerManager<ClusterPoolEntity> _finalizerManager;
        private readonly ILogger<ClusterPoolController> _logger;
        private readonly INetworkingHandler _networkingHandler;

        public ClusterPoolController(ILogger<ClusterPoolController> logger, INetworkingHandler networkingHandler, IKubernetesClient client, IFinalizerManager<ClusterPoolEntity> finalizerManager)
        {
            _logger = logger;
            _networkingHandler = networkingHandler;
            _client = client;
            _finalizerManager = finalizerManager;
        }

        public Task DeletedAsync(ClusterPoolEntity entity)
        {
            _logger.LogInformation($"entity {entity.Name()} called {nameof(DeletedAsync)}.");

            return Task.CompletedTask;
        }

        public async Task<ResourceControllerResult?> ReconcileAsync(ClusterPoolEntity entity)
        {
            _logger.LogInformation($"entity {entity.Name()} called {nameof(ReconcileAsync)}.");

            Task<ResourceControllerResult> t = entity.Status.State switch
            {
                ClusterPoolEntity.State.New => NewAsync(entity),
                ClusterPoolEntity.State.InstallationInitialized => InstallationInitializedAsync(entity),
                ClusterPoolEntity.State.Patching => PatchingAsync(entity),
                ClusterPoolEntity.State.Registered => RegisteredAsync(entity),
                _ => throw new ArgumentOutOfRangeException()
            };

            var result = await t;

            return result;
        }

        public Task StatusModifiedAsync(ClusterPoolEntity entity)
        {
            _logger.LogInformation($"entity {entity.Name()} called {nameof(StatusModifiedAsync)}.");

            return Task.CompletedTask;
        }

        private async Task<ResourceControllerResult?> InstallationInitializedAsync(ClusterPoolEntity entity)
        {
            await _networkingHandler.InitiateProductAsync(entity);
            return ResourceControllerResult.RequeueEvent(TimeSpan.FromSeconds(60));
        }

        private async Task<ResourceControllerResult> NewAsync(ClusterPoolEntity entity)
        {
            await _finalizerManager.RegisterFinalizerAsync<ClusterPoolFinalizer>(entity);
            entity = await _client.Get<ClusterPoolEntity>(entity.Name(), entity.Namespace());
            entity.Status.State = ClusterPoolEntity.State.InstallationInitialized;
            await _client.UpdateStatus(entity);
            return ResourceControllerResult.RequeueEvent(TimeSpan.FromSeconds(5));
        }

        private async Task<ResourceControllerResult> PatchingAsync(ClusterPoolEntity entity)
        {
            await _networkingHandler.PatchProductAsync(entity);
            return ResourceControllerResult.RequeueEvent(TimeSpan.FromSeconds(10));
        }

        private async Task<ResourceControllerResult> RegisteredAsync(ClusterPoolEntity entity)
        {
            await _networkingHandler.MonitorAsync(entity);
            return ResourceControllerResult.RequeueEvent(TimeSpan.FromSeconds(60));
        }
    }
}