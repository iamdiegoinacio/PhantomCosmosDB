using Microsoft.Extensions.Configuration;

namespace Phantom.CosmosDB.Configuration.Cost;

internal static class CosmosCostConfigResolver
{
    public static CosmosCostOptions Resolve(IConfiguration configuration, string? contentRootPath = null)
    {
        var rootJson = PhantomRootConfigResolver.LoadRootConfigJson(contentRootPath);
        var fromRoot = rootJson?["CosmosCostOptions"]?.ToObject<CosmosCostOptions>()
                       ?? rootJson?["CostOptions"]?.ToObject<CosmosCostOptions>();

        var resolved = fromRoot
                       ?? configuration.GetSection("CosmosCostOptions").Get<CosmosCostOptions>()
                       ?? configuration.GetSection("CosmosDb:Cost").Get<CosmosCostOptions>()
                       ?? new CosmosCostOptions();

        if (!CosmosRegionalPricingCatalog.TryGetRegion(resolved.Region, out _))
        {
            resolved.Region = CosmosRegionalPricingCatalog.DefaultRegionId;
        }

        return resolved;
    }
}
