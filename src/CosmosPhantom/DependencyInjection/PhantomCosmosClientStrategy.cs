using System;
using System.Linq;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CosmosPhantom.ChaosEngineering;
using CosmosPhantom.Configuration;
using CosmosPhantom.Configuration.Connection;
using CosmosPhantom.CosmosDB.Core;
using CosmosPhantom.Exceptions;
using CosmosPhantom.Seeding;

namespace CosmosPhantom.DependencyInjection;

/// <summary>
/// Estratégia local/em memória: provisiona os containers e políticas declarados em
/// <c>CosmosPhantom.config.json</c>, aplica <see cref="ChaosEngineeringConfigurator"/>
/// e registra <see cref="CosmosPhantomSeedingHostedService"/> para auto-seeding no startup.
/// </summary>
internal sealed class PhantomCosmosClientStrategy : ICosmosClientRegistrationStrategy
{
    public CosmosConnectionMode Mode => CosmosConnectionMode.Phantom;

    public void Register(
        IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration,
        CosmosConnectionSpec connectionSpec,
        CosmosDbConnectionOptions connectionOptions)
    {
        if (environment.IsProduction() && !connectionOptions.AllowPhantomInProduction)
        {
            throw new CosmosDbEmulatorConfigurationException(
                "Connection String do CosmosPhantom detectada em ambiente 'Production'. " +
                "Por segurança, o emulador em memória é bloqueado em produção (defina AllowPhantomInProduction=true apenas se intencional).");
        }

        var emulatorConfig = CosmosEmulatorConfigResolver.Resolve(configuration, environment.ContentRootPath);
        if (emulatorConfig == null)
        {
            throw new CosmosDbEmulatorConfigurationException(
                "Configuração do CosmosDbEmulator não encontrada. Defina 'CosmosDbEmulator' em CosmosPhantom.config.json.");
        }

        var requestedDatabase = connectionOptions.DatabaseName ?? connectionSpec.DatabaseName;
        var requestedContainer = connectionOptions.ContainerName ?? connectionSpec.ContainerName;

        ValidateTopologyCompatibility(emulatorConfig, requestedDatabase, requestedContainer);

        RegisterEmulatorCore(services, environment, configuration, emulatorConfig);

        if (!string.IsNullOrWhiteSpace(requestedContainer))
        {
            services.AddSingleton(sp =>
                sp.GetRequiredService<CosmosClient>().GetContainer(emulatorConfig.DatabaseName, requestedContainer));
        }

        services.AddSingleton<CosmosSeedingExecutionTracker>();
        services.AddHostedService<CosmosPhantomSeedingHostedService>();
    }

    internal static void RegisterEmulatorCore(
        IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration,
        CosmosDbEmulatorConfig emulatorConfig)
    {
        var chaosConfig = CosmosChaosConfigResolver.Resolve(configuration, environment.ContentRootPath);

        services.Configure<CosmosDbEmulatorConfig>(options =>
        {
            options.DatabaseName = emulatorConfig.DatabaseName;
            options.Containers = emulatorConfig.Containers;
        });

        services.AddOptions<CosmosDbEmulatorConfig>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

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

            options.OnHandlerCreated = (_, handler) =>
            {
                ChaosEngineeringConfigurator.ConfigureFaultInjector(handler, chaosConfig);
            };
        });

        if (emulatorConfig.Containers != null)
        {
            foreach (var container in emulatorConfig.Containers)
            {
                var containerName = container.Name;
                services.AddKeyedSingleton(containerName, (sp, _) =>
                    sp.GetRequiredService<CosmosClient>().GetContainer(emulatorConfig.DatabaseName, containerName));
            }
        }

        services.AddPhantomSeedingServices();
    }

    private static void ValidateTopologyCompatibility(
        CosmosDbEmulatorConfig emulatorConfig,
        string? requestedDatabase,
        string? requestedContainer)
    {
        if (!string.IsNullOrWhiteSpace(requestedDatabase) &&
            !string.Equals(emulatorConfig.DatabaseName, requestedDatabase, StringComparison.Ordinal))
        {
            throw new CosmosDbEmulatorConfigurationException(
                $"O banco '{requestedDatabase}' configurado para conexão no appsettings diverge do banco " +
                $"'{emulatorConfig.DatabaseName}' definido em CosmosPhantom.config.json.");
        }

        if (!string.IsNullOrWhiteSpace(requestedContainer) &&
            !emulatorConfig.Containers.Any(c => string.Equals(c.Name, requestedContainer, StringComparison.Ordinal)))
        {
            var available = string.Join(", ", emulatorConfig.Containers.Select(c => c.Name));
            throw new CosmosDbEmulatorConfigurationException(
                $"O container '{requestedContainer}' configurado no appsettings não foi encontrado em CosmosPhantom.config.json. " +
                $"Containers configurados: [{available}].");
        }
    }
}
