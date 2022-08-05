using ClusterNetworker.Service;
using KubeOps.Operator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ClusterNetworker
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app)
        {
            app.UseKubernetesOperator();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddTransient<INetworkingHandler, SkupperIo>()
                .AddTransient<ITokenStore, MockStore>()
                .AddHttpClient()
                .AddKubernetesOperator();
        }
    }
}