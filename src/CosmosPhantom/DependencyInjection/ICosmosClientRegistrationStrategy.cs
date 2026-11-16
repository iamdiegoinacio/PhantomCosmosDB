using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CosmosPhantom.Configuration;
using CosmosPhantom.Configuration.Connection;

namespace CosmosPhantom.DependencyInjection;

internal interface ICosmosClientRegistrationStrategy
{
    CosmosConnectionMode Mode { get; }

    void Register(
        IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration,
        CosmosConnectionSpec connectionSpec,
        CosmosDbConnectionOptions connectionOptions);
}
