using ClusterNetworker.Entities;
using k8s.Models;
using KubeOps.Operator.Finalizer;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace ClusterNetworker.Finalizer
{
    public class ClusterPoolFinalizer : IResourceFinalizer<ClusterPoolEntity>
    {
        private readonly ILogger<ClusterPoolFinalizer> _logger;

        public ClusterPoolFinalizer(ILogger<ClusterPoolFinalizer> logger)
        {
            _logger = logger;
        }

        public Task FinalizeAsync(ClusterPoolEntity entity)
        {
            _logger.LogInformation($"entity {entity.Name()} called {nameof(FinalizeAsync)}.");

            return Task.CompletedTask;
        }
    }
}