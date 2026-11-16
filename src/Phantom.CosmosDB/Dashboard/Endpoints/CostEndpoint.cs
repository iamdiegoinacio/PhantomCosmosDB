using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Phantom.CosmosDB.Configuration.Cost;
using Phantom.CosmosDB.Cost;
using Phantom.CosmosDB.Dashboard.Infrastructure;

namespace Phantom.CosmosDB.Dashboard.Endpoints;

internal sealed class CostEndpoint : IDashboardEndpointHandler
{
    public async Task<bool> TryHandleAsync(DashboardRequestContext ctx)
    {
        if (ctx.Method == HttpMethods.Get && ctx.Path.Equals("/api/cost/regions", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.HttpContext.WriteJsonAsync(new { regions = CosmosRegionalPricingCatalog.All });
            return true;
        }

        if (ctx.Path.Equals("/api/cost/settings", StringComparison.OrdinalIgnoreCase))
        {
            if (ctx.Method == HttpMethods.Get)
            {
                await ctx.HttpContext.WriteJsonAsync(new
                {
                    settings = ctx.Registry.LiveCostOptions,
                    activeRegion = CosmosRegionalPricingCatalog.GetOrDefault(ctx.Registry.LiveCostOptions.Region)
                });
                return true;
            }

            if (ctx.Method == HttpMethods.Put)
            {
                var body = await ctx.HttpContext.ReadBodyAsync();
                var updated = JsonConvert.DeserializeObject<CosmosCostOptions>(body);
                if (updated == null)
                {
                    await ctx.HttpContext.WriteJsonAsync(new { error = "Payload de CosmosCostOptions inválido." }, StatusCodes.Status400BadRequest);
                    return true;
                }

                CopyCostOptions(updated, ctx.Registry.LiveCostOptions);
                await ctx.HttpContext.WriteJsonAsync(new
                {
                    settings = ctx.Registry.LiveCostOptions,
                    activeRegion = CosmosRegionalPricingCatalog.GetOrDefault(ctx.Registry.LiveCostOptions.Region)
                });
                return true;
            }
        }

        if (ctx.Method == HttpMethods.Post && ctx.Path.Equals("/api/cost/simulate", StringComparison.OrdinalIgnoreCase))
        {
            var body = await ctx.HttpContext.ReadBodyAsync();
            var req = JsonConvert.DeserializeObject<QueryCostSimulationRequest>(body) ?? new QueryCostSimulationRequest();
            if (string.IsNullOrWhiteSpace(req.Region))
                req.Region = ctx.Registry.LiveCostOptions.Region;

            var result = CosmosQueryCostSimulator.Simulate(req);
            await ctx.HttpContext.WriteJsonAsync(result);
            return true;
        }

        return false;
    }

    internal static void CopyCostOptions(CosmosCostOptions source, CosmosCostOptions target)
    {
        if (CosmosRegionalPricingCatalog.TryGetRegion(source.Region, out var region))
            target.Region = region.Id;
        if (!string.IsNullOrWhiteSpace(source.BillingMode))
            target.BillingMode = source.BillingMode;
        target.LogQueryCost = source.LogQueryCost;
        target.ApplyFreeTier = source.ApplyFreeTier;
        if (!string.IsNullOrWhiteSpace(source.ConsistencyLevel))
            target.ConsistencyLevel = source.ConsistencyLevel;
        target.AvailabilityZones = source.AvailabilityZones;
        target.AdditionalRegionsCount = Math.Max(0, source.AdditionalRegionsCount);
        target.MultiRegionWrites = source.MultiRegionWrites;
    }
}
