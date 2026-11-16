using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Configuration.Connection;

namespace Phantom.CosmosDB.DependencyInjection;

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
