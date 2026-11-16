using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Phantom.CosmosDB.Dashboard.Infrastructure;

namespace Phantom.CosmosDB.Dashboard.Endpoints;

internal sealed class LogsEndpoint : IDashboardEndpointHandler
{
    public async Task<bool> TryHandleAsync(DashboardRequestContext ctx)
    {
        if (!ctx.Path.Equals("/api/logs", StringComparison.OrdinalIgnoreCase))
            return false;

        if (ctx.Method == HttpMethods.Get)
        {
            var take = int.TryParse(ctx.HttpContext.Request.Query["take"], out var t) ? t : 200;
            await ctx.HttpContext.WriteJsonAsync(new { items = ctx.Registry.TelemetryLog.GetRecent(take) });
            return true;
        }

        if (ctx.Method == HttpMethods.Delete)
        {
            ctx.Registry.TelemetryLog.Clear();
            await ctx.HttpContext.WriteJsonAsync(new { cleared = true });
            return true;
        }

        return false;
    }
}
