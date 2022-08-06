using System.Threading.Tasks;
using k8s.Models;

namespace ClusterNetworker.Service
{
    public interface ITokenStore
    {
        Task<V1Secret?> RetrieveTokenAsync(string clusterPoolName);

        Task UpsertTokenAsync(string clusterPoolName, V1Secret token);
    }
}