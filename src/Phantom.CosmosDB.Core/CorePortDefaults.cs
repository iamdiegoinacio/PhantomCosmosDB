#nullable enable
using Phantom.CosmosDB.Core.Application.Contracts;
using Phantom.CosmosDB.Core.Infrastructure.FeedIterators;
using Phantom.CosmosDB.Core.Infrastructure.HttpGateway;
using Phantom.CosmosDB.Core.Infrastructure.SdkInterop;
using Phantom.CosmosDB.Core.Infrastructure.Storage;

namespace Phantom.CosmosDB.Core;

/// <summary>
/// Assembly composition root providing default infrastructure implementations
/// for Application-layer DIP ports.
/// </summary>
internal static class CorePortDefaults
{
    public static ISdkResponseMapper SdkMapper { get; } = new SdkResponseMapperAdapter();
    public static IFeedIteratorFactory FeedIterators { get; } = new InMemoryFeedIteratorFactory();
    public static IGatewayClientFactory GatewayClients { get; } = new GatewayClientFactoryAdapter();

    public static IDynamicContainerRegistry CreateRegistry() => new DynamicContainerRegistry();
    public static IDocumentStore CreateDocumentStore() => new InMemoryItemStore();
    public static IChangeFeedLog CreateChangeFeedLog() => new InMemoryChangeFeedLog();
}
