using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Phantom.CosmosDB.ChaosEngineering;
using Phantom.CosmosDB.Dashboard.Infrastructure;

namespace Phantom.CosmosDB.Dashboard.Endpoints;

internal sealed class ChaosEndpoint : IDashboardEndpointHandler
{
    public async Task<bool> TryHandleAsync(DashboardRequestContext ctx)
    {
        if (!ctx.Path.Equals("/api/chaos", StringComparison.OrdinalIgnoreCase))
            return false;

        if (ctx.Method == HttpMethods.Get)
        {
            await ctx.HttpContext.WriteJsonAsync(ctx.Registry.LiveChaosConfig);
            return true;
        }

        if (ctx.Method == HttpMethods.Put)
        {
            var body = await ctx.HttpContext.ReadBodyAsync();
            var updated = JsonConvert.DeserializeObject<ChaosConfig>(body);
            if (updated == null)
            {
                await ctx.HttpContext.WriteJsonAsync(new { error = "Payload de ChaosConfig inválido." }, StatusCodes.Status400BadRequest);
                return true;
            }

            CopyChaosProperties(updated, ctx.Registry.LiveChaosConfig);
            await ctx.HttpContext.WriteJsonAsync(ctx.Registry.LiveChaosConfig);
            return true;
        }

        return false;
    }

    internal static void CopyChaosProperties(ChaosConfig source, ChaosConfig target)
    {
        target.EnableRandomThrottling = source.EnableRandomThrottling;
        target.ThrottlingProbability = source.ThrottlingProbability;
        target.SimulateHighLatency = source.SimulateHighLatency;
        target.MinLatencyMs = source.MinLatencyMs;
        target.MaxLatencyMs = source.MaxLatencyMs;
        target.RetryAfterMs = source.RetryAfterMs;
        target.Simulate429_TooManyRequests = source.Simulate429_TooManyRequests;
        target.Simulate503_ServiceUnavailable = source.Simulate503_ServiceUnavailable;
        target.Simulate408_RequestTimeout = source.Simulate408_RequestTimeout;
        target.Simulate403_Forbidden = source.Simulate403_Forbidden;
        target.Simulate401_Unauthorized = source.Simulate401_Unauthorized;
        target.Simulate409_Conflict = source.Simulate409_Conflict;
        target.Simulate413_EntityTooLarge = source.Simulate413_EntityTooLarge;
        target.Simulate412_PreconditionFailed = source.Simulate412_PreconditionFailed;
        target.Simulate400_BadRequest = source.Simulate400_BadRequest;
    }
}
