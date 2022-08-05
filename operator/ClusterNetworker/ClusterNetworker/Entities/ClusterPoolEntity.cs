using k8s.Models;
using KubeOps.Operator.Entities;
using KubeOps.Operator.Entities.Annotations;

namespace ClusterNetworker.Entities
{
    [KubernetesEntity(Group = "demo.ewassef.dev", ApiVersion = "v1", Kind = "ClusterPool")]
    public class ClusterPoolEntity : CustomKubernetesEntity<ClusterPoolEntity.ClusterPoolEntitySpec, ClusterPoolEntity.ClusterPoolEntityStatus>
    {
        public enum ExposureType
        {
            NodePort,
            LoadBalancer
        }

        public class ClusterPoolEntitySpec
        {
            public string ClusterName { get; set; }
            public ExposureType ExposureType { get; set; } = ExposureType.NodePort;

            [AdditionalPrinterColumn(Name = "Has External Access")]
            public bool HasExternalAccess { get; set; } = true;

            public ClusterPoolEntitySpecNodePort NodeportConfigurations { get; set; }

            public class ClusterPoolEntitySpecNodePort
            {
                public int EdgeSvc { get; set; }
                public int InterRouterSvc { get; set; }
            }
        }

        public class ClusterPoolEntityStatus
        {
            [AdditionalPrinterColumn(Name = "Connected Clusters")]
            public int NumberOfClusters { get; set; }

            [AdditionalPrinterColumn(Name = "Exposed Services")]
            public int NumberOfExposedServices { get; set; }
        }
    }
}