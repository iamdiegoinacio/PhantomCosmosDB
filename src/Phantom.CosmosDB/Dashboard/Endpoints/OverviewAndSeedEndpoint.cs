using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Phantom.CosmosDB.ChaosEngineering;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Configuration.Cost;
using Phantom.CosmosDB.Dashboard.Infrastructure;
using Phantom.CosmosDB.Dashboard.State;

namespace Phantom.CosmosDB.Dashboard.Endpoints;

internal sealed class OverviewAndSeedEndpoint : IDashboardEndpointHandler
{
    public async Task<bool> TryHandleAsync(DashboardRequestContext ctx)
    {
        if (ctx.Method == HttpMethods.Get && ctx.Path.Equals("/api/overview", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.HttpContext.WriteJsonAsync(BuildOverviewPayload(ctx.Registry));
            return true;
        }

        if (ctx.Method == HttpMethods.Post && ctx.Path.Equals("/api/seed/reset", StringComparison.OrdinalIgnoreCase))
        {
            await HandleResetSeedAsync(ctx);
            return true;
        }

        return false;
    }

    private static async Task HandleResetSeedAsync(DashboardRequestContext ctx)
    {
        foreach (var handler in ctx.Registry.Handlers.Values)
        {
            handler.BackingInMemoryContainer.ClearItems();
        }

        if (ctx.SeederService != null)
        {
            var seedsFolder = PhantomResourcePathResolver.ResolvePrimarySeedsFolder(ctx.Environment.ContentRootPath);
            try
            {
                ChaosEngineeringConfigurator.IsBypassed = true;
                await ctx.SeederService.SeedAsync(seedsFolder, ctx.HttpContext.RequestAborted);
            }
            finally
            {
                ChaosEngineeringConfigurator.IsBypassed = false;
            }
        }

        await ctx.HttpContext.WriteJsonAsync(BuildOverviewPayload(ctx.Registry));
    }

    private static object BuildOverviewPayload(CosmosPhantomRuntimeRegistry registry)
    {
        var containers = registry.Handlers.Select(kvp =>
        {
            var c = kvp.Value.BackingInMemoryContainer;
            var cfg = registry.EmulatorConfig.Containers
                .FirstOrDefault(x => string.Equals(x.Name, kvp.Key, StringComparison.Ordinal));

            return new
            {
                name = c.Id,
                itemCount = c.ItemCount,
                throughput = c._throughput,
                defaultTtl = c.DefaultTimeToLive,
                partitionKeyPaths = c.PartitionKeyPaths,
                indexingMode = c.IndexingPolicy.IndexingMode.ToString(),
                automaticIndexing = c.IndexingPolicy.Automatic,
                compositeIndexCount = c.IndexingPolicy.CompositeIndexes.Count,
                geospatialType = (c.ContainerPropertiesInternal.GeospatialConfig?.GeospatialType ?? Microsoft.Azure.Cosmos.GeospatialType.Geography).ToString(),
                spatialIndexes = c.IndexingPolicy.SpatialIndexes.Select(si => new
                {
                    path = si.Path,
                    types = si.SpatialTypes.Select(t => t.ToString()).ToList()
                }).ToList(),
                uniqueKeys = c.UniqueKeyPolicy.UniqueKeys.Select(uk => uk.Paths).ToList(),
                computedProperties = c.ContainerPropertiesInternal.ComputedProperties
                    .Select(cp => new { cp.Name, cp.Query })
                    .ToList(),
                configured = cfg != null
            };
        }).OrderBy(x => x.name, StringComparer.Ordinal).ToList();

        return new
        {
            databaseName = registry.DatabaseName,
            configFilePath = registry.RootConfigFilePath,
            totalItems = containers.Sum(c => c.itemCount),
            containerCount = containers.Count,
            chaosActive = IsAnyChaosEnabled(registry.LiveChaosConfig),
            costOptions = registry.LiveCostOptions,
            activeRegion = CosmosRegionalPricingCatalog.GetOrDefault(registry.LiveCostOptions.Region),
            containers
        };
    }

    private static bool IsAnyChaosEnabled(ChaosConfig c) =>
        c.EnableRandomThrottling ||
        c.SimulateHighLatency ||
        c.Simulate429_TooManyRequests ||
        c.Simulate503_ServiceUnavailable ||
        c.Simulate408_RequestTimeout ||
        c.Simulate403_Forbidden ||
        c.Simulate401_Unauthorized ||
        c.Simulate409_Conflict ||
        c.Simulate413_EntityTooLarge ||
        c.Simulate412_PreconditionFailed ||
        c.Simulate400_BadRequest;
}
