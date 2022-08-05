using System;
using System.Linq;
using System.Threading.Tasks;
using k8s.Models;
using VaultSharp;

namespace ClusterNetworker.Service
{
    public interface ITokenStore
    {
        Task<V1Secret?> RetrieveTokenAsync(string clusterPoolName);

        Task UpsertTokenAsync(string clusterPoolName, V1Secret token);
    }

    public class Vault : ITokenStore
    {
        private readonly IVaultClient _client;

        public Vault(IVaultClient client)
        {
            _client = client;
        }

        public async Task<V1Secret?> RetrieveTokenAsync(string clusterPoolName)
        {
            try
            {
                var result = await _client.V1.Secrets.KeyValue.V2.ReadSecretAsync<V1Secret>($"{clusterPoolName}", mountPoint: "secret");
                return result.Data.Data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task UpsertTokenAsync(string clusterPoolName, V1Secret token)
        {
            var name = token.Name();
            var namespaceVal = token.Namespace();
            var labels = token.Labels();
            token.Metadata = new V1ObjectMeta(name: name, namespaceProperty: namespaceVal, labels: labels);
            token.StringData = token.Data.ToDictionary(x => x.Key, y => System.Convert.ToBase64String(y.Value));
            await _client.V1.Secrets.KeyValue.V2.WriteSecretAsync($"{clusterPoolName}", token, null, "secret");
        }
    }
}