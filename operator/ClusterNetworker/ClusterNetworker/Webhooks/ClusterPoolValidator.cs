using System.Linq;
using System.Net;
using ClusterNetworker.Entities;
using DotnetKubernetesClient;
using k8s;
using k8s.Models;
using KubeOps.Operator.Webhooks;
using Microsoft.AspNetCore.Http;

namespace ClusterNetworker.Webhooks
{
    public class ClusterPoolValidator : IValidationWebhook<ClusterPoolEntity>
    {
        private const string Namespace = "skupper-site-controller";
        private readonly IKubernetesClient _client;

        public ClusterPoolValidator(IKubernetesClient client)
        {
            _client = client;
        }

        public AdmissionOperations Operations => AdmissionOperations.Create;

        public ValidationResult Create(ClusterPoolEntity newEntity, bool dryRun)
        {
            var cms = _client.ApiClient.ListNamespacedConfigMap(Namespace);
            if (cms.Items.Any(x => x.Name() == "skupper-site"))
            {
                return ValidationResult.Fail((int)HttpStatusCode.Conflict, "This cluster is already participating in a pool. Please evict it and then add it to another one");
            }
            return ValidationResult.Success();
        }
    }
}