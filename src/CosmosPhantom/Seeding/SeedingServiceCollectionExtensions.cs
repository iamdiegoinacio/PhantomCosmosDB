using Microsoft.Extensions.DependencyInjection;
using CosmosPhantom.Seeding.Interfaces;
using CosmosPhantom.Seeding.Services;

namespace CosmosPhantom.Seeding;

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

        return services;
    }
}
