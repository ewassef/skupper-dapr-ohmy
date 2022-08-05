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

namespace ClusterNetworker.Controller
{
    [EntityRbac(typeof(ClusterPoolEntity), Verbs = RbacVerb.All)]
    public class ClusterPoolController : IResourceController<ClusterPoolEntity>
    {
        private readonly IFinalizerManager<ClusterPoolEntity> _finalizerManager;
        private readonly ILogger<ClusterPoolController> _logger;
        private readonly INetworkingHandler _networkingHandler;

        public ClusterPoolController(ILogger<ClusterPoolController> logger, INetworkingHandler networkingHandler, IFinalizerManager<ClusterPoolEntity> finalizerManager)
        {
            _logger = logger;
            _networkingHandler = networkingHandler;
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
            await _finalizerManager.RegisterFinalizerAsync<ClusterPoolFinalizer>(entity);
            await _networkingHandler.InitiateProductAsync(entity);
            return ResourceControllerResult.RequeueEvent(TimeSpan.FromSeconds(5));
        }

        public Task StatusModifiedAsync(ClusterPoolEntity entity)
        {
            _logger.LogInformation($"entity {entity.Name()} called {nameof(StatusModifiedAsync)}.");

            return Task.CompletedTask;
        }
    }
}