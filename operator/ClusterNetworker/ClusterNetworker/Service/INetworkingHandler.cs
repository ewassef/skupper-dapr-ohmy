using System.Threading.Tasks;
using ClusterNetworker.Entities;

namespace ClusterNetworker.Service
{
    public interface INetworkingHandler
    {
        Task InitiateProductAsync(ClusterPoolEntity entity);

        Task PatchProductAsync(ClusterPoolEntity entity);

        Task MonitorAsync(ClusterPoolEntity entity);
         
    }
}