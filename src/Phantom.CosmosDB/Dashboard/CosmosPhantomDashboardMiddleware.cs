using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Phantom.CosmosDB.Dashboard.Endpoints;
using Phantom.CosmosDB.Dashboard.State;
using Phantom.CosmosDB.Seeding.Interfaces;

namespace Phantom.CosmosDB.Dashboard;

internal sealed class CosmosPhantomDashboardMiddleware
{
    private static readonly IReadOnlyList<IDashboardEndpointHandler> EndpointHandlers =
    [
        new DashboardAssetsEndpoint(),
        new OverviewAndSeedEndpoint(),
        new ContainerExplorerEndpoint(),
        new ChaosEndpoint(),
        new LogsEndpoint(),
        new CostEndpoint(),
        new ConfigEndpoint()
    ];

    private readonly RequestDelegate _next;
    private readonly string _routePrefix;

    public CosmosPhantomDashboardMiddleware(RequestDelegate next, string routePrefix)
    {
        _next = next;
        _routePrefix = routePrefix;
    }

    public async Task InvokeAsync(
        HttpContext context,
        CosmosPhantomRuntimeRegistry registry,
        IHostEnvironment environment,
        ICosmosDbSeederService? seederService = null)
    {
        var reqCtx = new DashboardRequestContext(
            context,
            context.Request.Path.Value ?? string.Empty,
            context.Request.Method,
            _routePrefix,
            registry,
            environment,
            seederService);

        foreach (var handler in EndpointHandlers)
        {
            if (await handler.TryHandleAsync(reqCtx))
                return;
        }

        await _next(context);
    }
}
