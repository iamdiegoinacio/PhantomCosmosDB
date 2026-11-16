using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Phantom.CosmosDB.Configuration.Cost;

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
internal sealed record CosmosRegionPricing(
    string Id,
    string Country,
    string RegionName,
    string Flag,
    double Monthly1000RuProvisionedUsd,
    double ServerlessPerMillionRuUsd,
    double StoragePerGbMonthUsd)
{
    public double ProvisionedPer100RuPerHourUsd => Math.Round(Monthly1000RuProvisionedUsd / (10.0 * 730.0), 6);
}

/// <summary>
/// Catálogo regional de preços baseado no estudo <c>EstudoCustos.md</c> (730h/mês, USD, pay-as-you-go).
/// </summary>
internal static class CosmosRegionalPricingCatalog
{
    public const string DefaultRegionId = "brazil-south";
    public const string BaselineRegionId = "east-us-2";
    public const double HoursPerMonth = 730.0;
    public const double AutoscaleMultiplier = 1.5;

    private static readonly IReadOnlyList<CosmosRegionPricing> RegionsList =
    [
        new("east-us-2", "EUA", "East US 2", "US", 58.40, 0.250, 0.2500),
        new("west-europe", "Holanda", "West Europe", "NL", 58.40, 0.305, 0.2500),
        new("uk-south", "Reino Unido", "UK South", "GB", 58.40, 0.297, 0.2500),
        new("southeast-asia", "Singapura", "Southeast Asia", "SG", 58.40, 0.285, 0.2500),
        new("korea-central", "Coreia do Sul", "Korea Central", "KR", 58.40, 0.271, 0.2500),
        new("canada-central", "Canadá", "Canada Central", "CA", 64.24, 0.275, 0.2750),
        new("mexico-central", "México", "Mexico Central", "MX", 64.24, 0.275, 0.2750),
        new("japan-east", "Japão", "Japan East", "JP", 65.70, 0.285, 0.2825),
        new("central-india", "Índia", "Central India", "IN", 66.43, 0.285, 0.2850),
        new("australia-east", "Austrália", "Australia East", "AU", 67.16, 0.285, 0.2875),
        new("france-central", "França", "France Central", "FR", 73.00, 0.297, 0.3125),
        new("uae-north", "Emirados Árabes", "UAE North", "AE", 77.09, 0.311, 0.3300),
        new("south-africa-north", "África do Sul", "South Africa North", "ZA", 85.41, 0.335, 0.3650),
        new("brazil-south", "Brasil", "Brazil South", "BR", 87.60, 0.375, 0.3750)
    ];

    private static readonly Dictionary<string, CosmosRegionPricing> RegionsById =
        RegionsList.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CosmosRegionPricing> All => RegionsList;

    public static CosmosRegionPricing GetOrDefault(string? regionId)
    {
        if (!string.IsNullOrWhiteSpace(regionId) && RegionsById.TryGetValue(regionId.Trim(), out var region))
            return region;

        return RegionsById[DefaultRegionId];
    }

    public static bool TryGetRegion(string? regionId, out CosmosRegionPricing region)
    {
        if (!string.IsNullOrWhiteSpace(regionId) && RegionsById.TryGetValue(regionId.Trim(), out var found))
        {
            region = found;
            return true;
        }

        region = RegionsById[DefaultRegionId];
        return false;
    }
}
