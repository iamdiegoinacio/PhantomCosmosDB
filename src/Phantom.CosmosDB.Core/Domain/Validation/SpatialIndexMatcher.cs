#nullable enable
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core.Domain.Validation;

internal sealed record SpatialQueryAnalysis(
    IReadOnlyList<string> QueriedSpatialPaths,
    IReadOnlyList<string> CoveredSpatialPaths,
    IReadOnlyList<string> MissingSpatialPaths,
    bool UsesOrderByDistance,
    bool HasSpatialPredicate);

internal static class SpatialIndexMatcher
{
    private static readonly HashSet<string> SpatialFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ST_DISTANCE",
        "ST_WITHIN",
        "ST_INTERSECTS"
    };

    public static SpatialQueryAnalysis Analyze(CosmosSqlQuery query, IndexingPolicy? policy)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fromAlias = string.IsNullOrWhiteSpace(query.FromAlias) ? "c" : query.FromAlias;

        if (query.WhereExpr is not null)
            CollectSpatialPaths(query.WhereExpr, fromAlias, paths);

        var usesOrderByDistance = false;
        if (query.OrderByFields is { Length: > 0 })
        {
            foreach (var ob in query.OrderByFields)
            {
                if (ob.Expression is not null)
                {
                    if (ContainsFunction(ob.Expression, "ST_DISTANCE"))
                        usesOrderByDistance = true;

                    CollectSpatialPaths(ob.Expression, fromAlias, paths);
                }
            }
        }

        foreach (var sf in query.SelectFields)
        {
            if (sf.SqlExpr is not null)
                CollectSpatialPaths(sf.SqlExpr, fromAlias, paths);
        }

        var queried = paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var covered = new List<string>();
        var missing = new List<string>();

        foreach (var path in queried)
        {
            if (IsPathCoveredBySpatialIndex(path, policy))
                covered.Add(path);
            else
                missing.Add(path);
        }

        return new SpatialQueryAnalysis(
            queried,
            covered,
            missing,
            usesOrderByDistance,
            queried.Count > 0);
    }

    public static bool IsPathCoveredBySpatialIndex(string normalizedPath, IndexingPolicy? policy)
    {
        if (policy is null || policy.IndexingMode == IndexingMode.None || policy.SpatialIndexes is null)
            return false;

        foreach (var si in policy.SpatialIndexes)
        {
            if (string.IsNullOrWhiteSpace(si.Path)) continue;
            if (si.Path == "/*") return true;

            var prefix = si.Path.EndsWith("/*", StringComparison.Ordinal)
                ? si.Path[..^1]
                : si.Path.TrimEnd('/') + "/";

            if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath + "/", prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectSpatialPaths(SqlExpression expr, string fromAlias, HashSet<string> paths)
    {
        switch (expr)
        {
            case FunctionCallExpression func:
                if (SpatialFunctions.Contains(func.FunctionName))
                {
                    foreach (var arg in func.Arguments)
                    {
                        if (arg is IdentifierExpression ident)
                        {
                            var normalized = NormalizeDocumentPath(ident.Name, fromAlias);
                            if (normalized is not null)
                                paths.Add(normalized);
                        }
                    }
                }
                foreach (var arg in func.Arguments)
                    CollectSpatialPaths(arg, fromAlias, paths);
                break;

            case BinaryExpression bin:
                CollectSpatialPaths(bin.Left, fromAlias, paths);
                CollectSpatialPaths(bin.Right, fromAlias, paths);
                break;

            case UnaryExpression un:
                CollectSpatialPaths(un.Operand, fromAlias, paths);
                break;

            case BetweenExpression bet:
                CollectSpatialPaths(bet.Value, fromAlias, paths);
                CollectSpatialPaths(bet.Low, fromAlias, paths);
                CollectSpatialPaths(bet.High, fromAlias, paths);
                break;

            case InExpression ine:
                CollectSpatialPaths(ine.Value, fromAlias, paths);
                foreach (var v in ine.List) CollectSpatialPaths(v, fromAlias, paths);
                break;

            case CoalesceExpression coal:
                CollectSpatialPaths(coal.Left, fromAlias, paths);
                CollectSpatialPaths(coal.Right, fromAlias, paths);
                break;

            case TernaryExpression tern:
                CollectSpatialPaths(tern.Condition, fromAlias, paths);
                CollectSpatialPaths(tern.IfTrue, fromAlias, paths);
                CollectSpatialPaths(tern.IfFalse, fromAlias, paths);
                break;
        }
    }

    private static bool ContainsFunction(SqlExpression expr, string fnName)
    {
        return expr switch
        {
            FunctionCallExpression f => string.Equals(f.FunctionName, fnName, StringComparison.OrdinalIgnoreCase)
                                        || f.Arguments.Any(a => ContainsFunction(a, fnName)),
            BinaryExpression b => ContainsFunction(b.Left, fnName) || ContainsFunction(b.Right, fnName),
            UnaryExpression u => ContainsFunction(u.Operand, fnName),
            _ => false
        };
    }

    private static string? NormalizeDocumentPath(string raw, string fromAlias)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "*") return null;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith(fromAlias + ".", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[(fromAlias.Length + 1)..];
        else if (trimmed.StartsWith("c.", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];

        return "/" + trimmed.Replace('.', '/').TrimStart('/') + "/*";
    }
}
