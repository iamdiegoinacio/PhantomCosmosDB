#nullable enable
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Core.Domain.Validation;

internal static class IndexMetricsBuilder
{
    public static string Build(CosmosSqlQuery query, IndexingPolicy? policy)
    {
        var spatial = SpatialIndexMatcher.Analyze(query, policy);
        var utilizedSingle = new JArray();
        var potentialSingle = new JArray();

        foreach (var path in spatial.CoveredSpatialPaths)
        {
            utilizedSingle.Add(new JObject
            {
                ["FilterExpression"] = spatial.UsesOrderByDistance ? "ST_DISTANCE" : "SpatialPredicate",
                ["IndexSpec"] = path,
                ["FilterPreciseSet"] = true,
                ["IndexPreciseSet"] = true,
                ["IndexImpactScore"] = "High"
            });
        }

        foreach (var path in spatial.MissingSpatialPaths)
        {
            potentialSingle.Add(new JObject
            {
                ["IndexSpec"] = path,
                ["IndexImpactScore"] = "High"
            });
        }

        if (query.WhereExpr is not null && !spatial.HasSpatialPredicate && policy?.IndexingMode != IndexingMode.None)
        {
            utilizedSingle.Add(new JObject
            {
                ["FilterExpression"] = "WHERE",
                ["IndexSpec"] = "/*",
                ["FilterPreciseSet"] = true,
                ["IndexPreciseSet"] = true,
                ["IndexImpactScore"] = "Medium"
            });
        }

        var utilizedComposite = new JArray();
        var potentialComposite = new JArray();
        if (query.OrderByFields is { Length: > 1 } && policy?.CompositeIndexes is { Count: > 0 })
        {
            utilizedComposite.Add(new JObject
            {
                ["IndexSpecs"] = new JArray(query.OrderByFields
                    .Where(f => !string.IsNullOrWhiteSpace(f.Field))
                    .Select(f => (JToken)$"/{f.Field!.TrimStart('/')} {(f.Ascending ? "ASC" : "DESC")}")),
                ["IndexImpactScore"] = "High"
            });
        }

        var root = new JObject
        {
            ["UtilizedSingleIndexes"] = utilizedSingle,
            ["PotentialSingleIndexes"] = potentialSingle,
            ["UtilizedCompositeIndexes"] = utilizedComposite,
            ["PotentialCompositeIndexes"] = potentialComposite
        };

        return root.ToString(Formatting.None);
    }
}
