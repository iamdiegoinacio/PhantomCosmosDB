using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CosmosPhantom.Configuration;
using CosmosPhantom.Configuration.Connection;

namespace CosmosPhantom.DependencyInjection;

/// <summary>
/// Estratégia de produção/nuvem: registra o <see cref="CosmosClient"/> oficial da Microsoft
/// conectado diretamente ao Azure Cosmos DB real, sem carregar emulador, seeding ou Chaos.
/// </summary>
internal sealed class AzureCosmosClientStrategy : ICosmosClientRegistrationStrategy
{
    public CosmosConnectionMode Mode => CosmosConnectionMode.Real;

    public void Register(
        IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration,
        CosmosConnectionSpec connectionSpec,
        CosmosDbConnectionOptions connectionOptions)
    {
        var clientOptions = connectionOptions.CosmosClientOptions ?? new CosmosClientOptions();
        connectionOptions.ConfigureClientOptions?.Invoke(clientOptions);

        services.AddSingleton(_ => new CosmosClient(connectionSpec.SanitizedAzureConnectionString, clientOptions));

        var databaseName = connectionOptions.DatabaseName ?? connectionSpec.DatabaseName;
        var containerName = connectionOptions.ContainerName ?? connectionSpec.ContainerName;

        if (!string.IsNullOrWhiteSpace(databaseName) && !string.IsNullOrWhiteSpace(containerName))
        {
            services.AddSingleton(sp =>
                sp.GetRequiredService<CosmosClient>().GetContainer(databaseName, containerName));
            services.AddKeyedSingleton(containerName, (sp, _) =>
                sp.GetRequiredService<CosmosClient>().GetContainer(databaseName, containerName));
        }
    }
}
