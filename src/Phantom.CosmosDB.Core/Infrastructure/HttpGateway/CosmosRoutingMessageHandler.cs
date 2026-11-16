#nullable enable
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Multi-container HTTP router and dynamic DDL handler (<c>POST/PUT/DELETE/GET /dbs/{db}/colls</c>).
/// Dispatches requests to the appropriate per-container <see cref="FakeCosmosHandler"/>.
/// </summary>
internal sealed class CosmosRoutingMessageHandler : HttpMessageHandler
{
    private readonly DynamicContainerRegistry _registry;
    private readonly FakeCosmosHandler _default;
    private readonly GatewayDatabaseLifecycleRouter _databaseRouter;
    private readonly GatewayContainerLifecycleRouter _containerRouter;

    public CosmosRoutingMessageHandler(IReadOnlyDictionary<string, FakeCosmosHandler> handlers)
        : this(handlers, new DynamicContainerRegistry())
    {
    }

    public CosmosRoutingMessageHandler(
        IReadOnlyDictionary<string, FakeCosmosHandler> handlers,
        DynamicContainerRegistry registry)
    {
        if (!handlers.Any())
            throw new ArgumentException("At least one handler must be registered.", nameof(handlers));
        _registry = registry;
        foreach (var kvp in handlers)
            _registry.Handlers[kvp.Key] = kvp.Value;
        _default = handlers.Values.First();
        _databaseRouter = new GatewayDatabaseLifecycleRouter(_registry, _default);
        _containerRouter = new GatewayContainerLifecycleRouter(_registry, _default);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var method = request.Method.Method;

        if (path is "/" or "")
            return await InvokeHandlerAsync(_default, request, cancellationToken);

        if (path.Contains("/offers", StringComparison.OrdinalIgnoreCase))
            return await _databaseRouter.HandleOffersAsync(request, path, method, cancellationToken);

        var collsMatch = Regex.Match(path, @"^/dbs/([^/]+)/colls/?$");
        if (collsMatch.Success)
        {
            var dbNameForColls = collsMatch.Groups[1].Value;
            if (method == "POST")
                return await _containerRouter.HandleCreateContainerAsync(request, dbNameForColls, cancellationToken);
            if (method == "GET")
                return _containerRouter.HandleListContainers(dbNameForColls);
        }

        if (Regex.IsMatch(path, @"^/dbs/?$"))
            return await InvokeHandlerAsync(_default, request, cancellationToken);

        if (Regex.IsMatch(path, @"^/dbs/[^/]+/?$") && !path.Contains("/colls"))
            return _databaseRouter.HandleDatabaseCrud(path, method);

        var match = Regex.Match(path, @"/dbs/([^/]+)/colls/([^/]+)");
        if (match.Success)
        {
            var dbName = match.Groups[1].Value;
            var containerName = match.Groups[2].Value;

            if (Regex.IsMatch(path, @"/colls/[^/]+/?$"))
            {
                if (method == "DELETE")
                    return _containerRouter.HandleDeleteContainer(dbName, containerName);
                if (method == "GET")
                    return _containerRouter.HandleReadContainer(dbName, containerName);
                if (method == "PUT")
                    return await _containerRouter.HandleReplaceContainerAsync(request, dbName, containerName, cancellationToken);
            }

            var resolvedKey = _containerRouter.ResolveExistingRegistryKey(dbName, containerName);
            if (resolvedKey is not null && _registry.Handlers.TryGetValue(resolvedKey, out var resolvedHandler))
                return await InvokeHandlerAsync(resolvedHandler, request, cancellationToken);

            // SDK internal routes use base64-encoded RIDs (e.g. "AQAAAA==") for
            // partition key range and other metadata requests. Fall back to the
            // default handler for these rather than throwing.
            if (containerName.Contains('=') || path.Contains("/pkranges"))
                return await InvokeHandlerAsync(_default, request, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    $"{{\"message\":\"Container '{containerName}' not found. " +
                    $"Available containers: {string.Join(", ", _registry.Handlers.Keys.OrderBy(k => k))}\"}}",
                    Encoding.UTF8, "application/json")
            };
        }

        return await InvokeHandlerAsync(_default, request, cancellationToken);
    }

    private static Task<HttpResponseMessage> InvokeHandlerAsync(
        FakeCosmosHandler handler,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        return invoker.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var handler in _registry.Handlers.Values)
                handler.Dispose();
        }

        base.Dispose(disposing);
    }
}
