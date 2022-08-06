using ClusterNetworker.Entities;
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

            Task<ResourceControllerResult?> t = entity.Status.State switch
            {
                ClusterPoolEntity.State.New => NewAsync(entity),
                ClusterPoolEntity.State.InstallationInitialized => InstallationInitializedAsync(entity),
                ClusterPoolEntity.State.Patching => PatchingAsync(entity),
                ClusterPoolEntity.State.Registered => RegisteredAsync(entity),
                _ => throw new ArgumentOutOfRangeException()
            };

            var result = await t;
            await _client.UpdateStatus(entity);
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
            return ResourceControllerResult.RequeueEvent(TimeSpan.FromSeconds(5));
        }

        private async Task<ResourceControllerResult> PatchingAsync(ClusterPoolEntity entity)
        {
            throw new NotImplementedException();
        }

        private async Task<ResourceControllerResult> RegisteredAsync(ClusterPoolEntity entity)
        {
            await _networkingHandler.MonitorAsync(entity);
            return ResourceControllerResult.RequeueEvent(TimeSpan.FromSeconds(60));
        }
    }
}