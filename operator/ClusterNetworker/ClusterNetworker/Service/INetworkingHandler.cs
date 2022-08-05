using System.Threading.Tasks;
using ClusterNetworker.Entities;

namespace ClusterNetworker.Service
{
    public interface INetworkingHandler
    {
        Task InitiateProductAsync(ClusterPoolEntity entity);

        Task<bool> IsCompletedAsync(ClusterPoolEntity entity);
    }
}