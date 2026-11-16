using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CosmosPhantom.Configuration;
using CosmosPhantom.Configuration.Connection;
using CosmosPhantom.DependencyInjection;

namespace CosmosPhantom;

public static class PhantomCosmosExtensions
{
    private static readonly IReadOnlyList<ICosmosClientRegistrationStrategy> Strategies =
    [
        new AzureCosmosClientStrategy(),
        new PhantomCosmosClientStrategy()
    ];

    /// <summary>
    /// Registra o cliente Cosmos DB orientado pela Connection String do <c>appsettings*.json</c>:
    /// conecta ao Azure Cosmos DB real quando a string aponta para a nuvem, ou ativa o emulador
    /// em memória + auto-seeding + Chaos Engineering de <c>CosmosPhantom.config.json</c> quando
    /// a string aponta para o CosmosPhantom.
    /// </summary>
    public static IHostApplicationBuilder AddCosmosDb(
        this IHostApplicationBuilder builder,
        Action<CosmosDbConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCosmosDb(builder.Environment, builder.Configuration, configure);
        return builder;
    }

    /// <summary>
    /// Registra o cliente Cosmos DB orientado pela Connection String do <see cref="IConfiguration"/>.
    /// </summary>
    public static IServiceCollection AddCosmosDb(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration,
        Action<CosmosDbConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CosmosDbConnectionOptions();
        configuration.GetSection("CosmosDb").Bind(options);
        configure?.Invoke(options);

        var rawConnectionString =
            options.ConnectionString
            ?? configuration.GetConnectionString(options.ConnectionStringName)
            ?? configuration["CosmosDb:ConnectionString"];

        var spec = CosmosConnectionStringParser.Parse(rawConnectionString);
        var strategy = Strategies.First(s => s.Mode == spec.Mode);

        strategy.Register(services, environment, configuration, spec, options);
        return services;
    }

    /// <summary>
    /// Registra apenas o CosmosClient emulado e os serviços de Seeding quando <c>UseCosmosPhantom=true</c>.
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
        if (emulatorConfig == null) return services;

        PhantomCosmosClientStrategy.RegisterEmulatorCore(services, environment, configuration, emulatorConfig);
        return services;
    }
}
