using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Phantom.CosmosDB.ChaosEngineering;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Dashboard.State;
using Phantom.CosmosDB.Seeding.Interfaces;

namespace Phantom.CosmosDB.Seeding;

/// <summary>
/// Executa o auto-seeding dos containers em memória na inicialização do Host
/// quando o CosmosPhantom é ativado via Connection String.
/// </summary>
internal sealed class CosmosPhantomSeedingHostedService(
    ICosmosDbSeederService seederService,
    IHostEnvironment environment,
    CosmosSeedingExecutionTracker tracker,
    CosmosPhantomRuntimeRegistry runtimeRegistry,
    ILogger<CosmosPhantomTelemetryHandler> telemetryLogger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        runtimeRegistry.TelemetryLogger = telemetryLogger;

        if (!tracker.TryMarkExecuted())
            return;

        var seedsFolderPath = PhantomResourcePathResolver.ResolvePrimarySeedsFolder(environment.ContentRootPath);

        try
        {
            ChaosEngineeringConfigurator.IsBypassed = true;
            await seederService.SeedAsync(seedsFolderPath, cancellationToken);
        }
        finally
        {
            ChaosEngineeringConfigurator.IsBypassed = false;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
