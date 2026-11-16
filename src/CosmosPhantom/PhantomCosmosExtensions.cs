using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CosmosPhantom.Seeding;
using CosmosPhantom.Exceptions;
using CosmosPhantom.Configuration;
using CosmosPhantom.ChaosEngineering;
using System;

using CosmosPhantom.CosmosDB.Core;

namespace CosmosPhantom;

public static class PhantomCosmosExtensions
{
    /// <summary>
    /// Registra apenas o CosmosClient emulado e os serviços de Seeding.
    /// Em produção, a API consumidora deve registrar seu próprio CosmosClient.
    /// </summary>
    public static IServiceCollection AddCosmosPhantomEmulator(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        // Fail-Fast Silencioso: Se não for dev ou UseCosmosPhantom não estiver explicitamente true, ignora o SDK.
        if (!environment.IsDevelopment() || !PhantomRootConfigResolver.IsCosmosPhantomEnabled(configuration, environment.ContentRootPath))
        {
            return services;
        }

        // 1. Resolve configurações (CosmosPhantom.config.json -> Resources/ -> IConfiguration -> Fallback)
        var emulatorConfig = CosmosEmulatorConfigResolver.Resolve(configuration, environment.ContentRootPath);
        var chaosConfig = CosmosChaosConfigResolver.Resolve(configuration, environment.ContentRootPath);
        if (emulatorConfig == null) return services;

        // 2. Configura e valida as Options
        services.Configure<CosmosDbEmulatorConfig>(options =>
        {
            options.DatabaseName = emulatorConfig.DatabaseName;
            options.Containers = emulatorConfig.Containers;
        });

        services.AddOptions<CosmosDbEmulatorConfig>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 3. Inicializa o emulador e o injetor de caos
        services.UseInMemoryCosmosDB(options =>
        {
            options.DatabaseName = emulatorConfig.DatabaseName;
            if (emulatorConfig.Containers != null)
            {
                foreach (var container in emulatorConfig.Containers)
                {
                    options.AddContainer(container.ToContainerProperties(), throughput: container.Throughput);
                }
            }

            options.OnHandlerCreated = (name, handler) =>
            {
                ChaosEngineeringConfigurator.ConfigureFaultInjector(handler, chaosConfig);
            };
        });

        // 4. Registra serviços complementares
        services.AddPhantomSeedingServices();

        return services;
    }
}
