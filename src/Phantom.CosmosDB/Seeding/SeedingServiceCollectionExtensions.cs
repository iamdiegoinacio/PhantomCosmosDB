using Microsoft.Extensions.DependencyInjection;
using Phantom.CosmosDB.Seeding.Generation;
using Phantom.CosmosDB.Seeding.Interfaces;
using Phantom.CosmosDB.Seeding.Services;

namespace Phantom.CosmosDB.Seeding;

internal static class SeedingServiceCollectionExtensions
{
    /// <summary>
    /// Registra os serviços responsáveis pela carga de dados (Seeding) no Cosmos DB.
    /// </summary>
    public static IServiceCollection AddPhantomSeedingServices(this IServiceCollection services)
    {
        services.AddTransient<ISeedFileReader, SeedFileReader>();
        services.AddTransient<ICosmosDbManager, CosmosDbManager>();
        services.AddTransient<ICosmosDbBulkInserter, CosmosDbBulkInserter>();
        services.AddTransient<ICosmosDbSeederService, CosmosDbSeederService>();
        services.AddSingleton<SeedGenerationJobCoordinator>();

        return services;
    }
}
