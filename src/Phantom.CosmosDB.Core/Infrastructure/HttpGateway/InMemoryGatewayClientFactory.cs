#nullable enable
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Factory for creating and wrapping <see cref="CosmosClient"/> instances backed by <see cref="FakeCosmosHandler"/>.
/// </summary>
internal static class InMemoryGatewayClientFactory
{
    public static HttpMessageHandler CreateRouter(IReadOnlyDictionary<string, FakeCosmosHandler> handlers)
        => new CosmosRoutingMessageHandler(handlers);

    public static CosmosClient WrapClient(CosmosClient innerClient, IReadOnlyDictionary<string, FakeCosmosHandler> handlers)
        => new PkAwareCosmosClient(innerClient, handlers);

    public static CosmosClient CreateClient(IReadOnlyDictionary<string, FakeCosmosHandler> handlers)
    {
        HttpMessageHandler httpHandler = handlers.Count == 1
            ? handlers.Values.First()
            : CreateRouter(handlers);

        var innerClient = new CosmosClient(
            FakeCosmosHandler.FakeConnectionString,
            new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                HttpClientFactory = () => new HttpClient(httpHandler)
            });

        return WrapClient(innerClient, handlers);
    }
}
