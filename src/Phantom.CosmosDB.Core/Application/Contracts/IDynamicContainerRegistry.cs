#nullable enable
using System.Collections.Concurrent;
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core.Application.Contracts;

/// <summary>
/// Port representing the shared registry of in-memory databases, containers, and HTTP handlers.
/// </summary>
internal interface IDynamicContainerRegistry
{
    ConcurrentDictionary<string, FakeCosmosHandler> Handlers { get; }
    ConcurrentDictionary<string, InMemoryContainer> BackingContainers { get; }
    ConcurrentDictionary<string, Container> SdkContainers { get; }
    ConcurrentDictionary<string, ConcurrentDictionary<string, string>> DatabaseContainerKeys { get; }
    HashSet<string> CompoundKeyDatabases { get; }
    HashSet<string> KnownDatabases { get; }
    CosmosClient? Client { get; set; }
    string DatabaseName { get; set; }
}

/// <summary>
/// Port for constructing gateway HTTP routing handlers backed by an <see cref="IDynamicContainerRegistry"/>.
/// </summary>
internal interface IGatewayClientFactory
{
    HttpMessageHandler CreateRoutingHandler(
        IReadOnlyDictionary<string, FakeCosmosHandler> handlers,
        IDynamicContainerRegistry registry);
}
