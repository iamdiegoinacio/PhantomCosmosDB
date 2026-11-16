#nullable enable
using Phantom.CosmosDB.Core.Application.Contracts;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Infrastructure adapter implementing <see cref="IGatewayClientFactory"/>.
/// </summary>
internal sealed class GatewayClientFactoryAdapter : IGatewayClientFactory
{
    public HttpMessageHandler CreateRoutingHandler(
        IReadOnlyDictionary<string, FakeCosmosHandler> handlers,
        IDynamicContainerRegistry registry) =>
        new CosmosRoutingMessageHandler(handlers, (DynamicContainerRegistry)registry);
}
