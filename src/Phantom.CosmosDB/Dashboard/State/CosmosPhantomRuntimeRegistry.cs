using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Phantom.CosmosDB.ChaosEngineering;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Configuration.Cost;
using Phantom.CosmosDB.Core;

namespace Phantom.CosmosDB.Dashboard.State;

/// <summary>
/// Mantém referências vivas ao estado do emulador em execução para inspeção e controle
/// via <c>UsePhantomDashboard</c>.
/// </summary>
internal sealed class CosmosPhantomRuntimeRegistry
{
    public string DatabaseName { get; set; } = string.Empty;

    public CosmosDbEmulatorConfig EmulatorConfig { get; set; } = new() { DatabaseName = "in-memory-db" };

    public ChaosConfig LiveChaosConfig { get; set; } = new();

    public CosmosCostOptions LiveCostOptions { get; set; } = new();

    public ILogger? TelemetryLogger { get; set; }

    public string? RootConfigFilePath { get; set; }

    public CosmosPhantomTelemetryLog TelemetryLog { get; } = new();

    public ConcurrentDictionary<string, FakeCosmosHandler> Handlers { get; } = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> ContainerNames => (IReadOnlyCollection<string>)Handlers.Keys;
}
