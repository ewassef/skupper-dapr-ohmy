using ClusterNetworker.Service;
using KubeOps.Operator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;

namespace ClusterNetworker
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public static IConfiguration Configuration { get; private set; }

        public void Configure(IApplicationBuilder app)
        {
            app.UseKubernetesOperator();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddTransient<INetworkingHandler, SkupperIo>()
                .AddTransient<ITokenStore, Service.Vault>()
                .AddScoped<IVaultClient>(provider => new VaultClient(new VaultClientSettings(Configuration["Vault:Address"],
                    new TokenAuthMethodInfo(Configuration["Vault:Token"]))))
                .AddHttpClient()
                .AddKubernetesOperator();
        }
    }
}