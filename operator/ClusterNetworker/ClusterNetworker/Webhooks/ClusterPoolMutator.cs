using ClusterNetworker.Entities;
using KubeOps.Operator.Webhooks;
using Microsoft.AspNetCore.Http;

namespace ClusterNetworker.Webhooks
{
    public class ClusterPoolMutator : IMutationWebhook<ClusterPoolEntity>
    {
        public AdmissionOperations Operations => AdmissionOperations.Create;

        public MutationResult Create(ClusterPoolEntity newEntity, bool dryRun)
        {
            return MutationResult.Modified(newEntity);
        }
    }
}