#nullable enable
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core.Application.Query;

/// <summary>
/// Compiles and caches container <see cref="ComputedProperty"/> SQL definitions into AST expressions.
/// </summary>
internal sealed class ComputedPropertyCompiler
{
    private volatile (string Name, string FromAlias, SqlExpression? Expr)[]? _cached;

    public void Reset() => _cached = null;

    public (string Name, string FromAlias, SqlExpression? Expr)[] GetCompiled(ContainerProperties containerProperties)
    {
        var cached = _cached;
        if (cached is not null) return cached;

        var cps = containerProperties.ComputedProperties;
        if (cps is null or { Count: 0 })
        {
            _cached = [];
            return _cached;
        }

        var result = new (string Name, string FromAlias, SqlExpression? Expr)[cps.Count];
        for (var i = 0; i < cps.Count; i++)
        {
            var parsed = CosmosSqlParser.Parse(cps[i].Query);
            result[i] = parsed.IsValueSelect && parsed.SelectFields.Length == 1 && parsed.SelectFields[0].SqlExpr is not null
                ? (cps[i].Name, parsed.FromAlias, parsed.SelectFields[0].SqlExpr)
                : (cps[i].Name, parsed.FromAlias, null);
        }

        _cached = result;
        return result;
    }
}
