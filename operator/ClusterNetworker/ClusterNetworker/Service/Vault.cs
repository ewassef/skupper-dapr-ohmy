﻿using System;
using System.Linq;
using System.Threading.Tasks;
using k8s.Models;
using VaultSharp;

namespace ClusterNetworker.Service
{
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
            token.Metadata = new V1ObjectMeta(name: name, namespaceProperty: namespaceVal, annotations: token.Annotations(), labels: labels);
            await _client.V1.Secrets.KeyValue.V2.WriteSecretAsync($"{clusterPoolName}", token, null, "secret");
        }
    }
}