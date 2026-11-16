#nullable enable
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Primitives;

namespace Phantom.CosmosDB.Core.Application.Query.Functions;

/// <summary>
/// Evaluates Cosmos SQL type-checking functions via a declarative registry.
/// </summary>
internal static class TypeCheckFunctions
{
    private delegate object? TypeCheckHandler(
        FunctionCallExpression func,
        object?[] args,
        JObject item,
        string fromAlias,
        IDictionary<string, object?> parameters);

    private static readonly IReadOnlyDictionary<string, TypeCheckHandler> Registry =
        new Dictionary<string, TypeCheckHandler>(StringComparer.OrdinalIgnoreCase)
        {
            ["IS_DEFINED"] = (f, a, i, alias, p) => IsDefined(f, a, i, alias, p),
            ["IS_NULL"] = (_, a, _, _, _) => a.Length > 0 && a[0] is null,
            ["IS_ARRAY"] = (f, a, i, alias, _) => a.Length > 0 && (ResolveTokenType(f.Arguments, i, alias) is JArray || a[0] is JArray),
            ["IS_BOOL"] = (_, a, _, _, _) => a.Length > 0 && a[0] is bool,
            ["IS_NUMBER"] = (_, a, _, _, _) => a.Length > 0 && a[0] is long or double or int or float or decimal,
            ["IS_STRING"] = (_, a, _, _, _) => a.Length > 0 && a[0] is string,
            ["IS_OBJECT"] = (f, a, i, alias, _) => a.Length > 0 && (ResolveTokenType(f.Arguments, i, alias) is JObject || a[0] is JObject),
            ["IS_PRIMITIVE"] = (f, a, i, alias, _) => IsPrimitive(f, a, i, alias),
            ["IS_FINITE_NUMBER"] = (_, a, _, _, _) => IsFiniteNumber(a),
            ["IS_INTEGER"] = (_, a, _, _, _) => a.Length > 0 && a[0] is long or int,
            ["IS_NAN"] = (_, a, _, _, _) => a.Length > 0 && a[0] is double d && double.IsNaN(d),
            ["TYPE"] = (f, a, i, alias, _) => GetCosmosTypeName(f, a, i, alias)
        };

    public static bool TryEvaluate(
        FunctionCallExpression func,
        object?[] args,
        JObject item,
        string fromAlias,
        IDictionary<string, object?> parameters,
        out object? result)
    {
        if (Registry.TryGetValue(func.FunctionName, out var handler))
        {
            result = handler(func, args, item, fromAlias, parameters);
            return true;
        }

        result = null;
        return false;
    }

    public static JToken? ResolveTokenType(SqlExpression[] arguments, JObject item, string fromAlias)
    {
        if (arguments.Length < 1 || arguments[0] is not IdentifierExpression ident)
            return null;

        var path = ident.Name;
        if (path.StartsWith(fromAlias + ".", StringComparison.OrdinalIgnoreCase))
            path = path[(fromAlias.Length + 1)..];

        return item.SelectToken(path);
    }

    private static bool IsDefined(
        FunctionCallExpression func,
        object?[] args,
        JObject item,
        string fromAlias,
        IDictionary<string, object?> parameters)
    {
        if (func.Arguments.Length < 1) return false;
        if (func.Arguments[0] is IdentifierExpression) return ResolveTokenType(func.Arguments, item, fromAlias) != null;
        if (func.Arguments[0] is ParameterExpression param) return parameters.ContainsKey(param.Name);
        return args[0] != null;
    }

    private static bool IsPrimitive(FunctionCallExpression func, object?[] args, JObject item, string fromAlias)
    {
        if (args.Length == 0) return false;
        if (args[0] is null) return true;
        if (ResolveTokenType(func.Arguments, item, fromAlias) is JArray or JObject) return false;
        return args[0] is string or bool or long or double;
    }

    private static bool IsFiniteNumber(object?[] args)
    {
        if (args.Length == 0) return false;
        return args[0] switch
        {
            long or int or decimal => true,
            double d => !double.IsInfinity(d) && !double.IsNaN(d),
            float f => !float.IsInfinity(f) && !float.IsNaN(f),
            _ => false
        };
    }

    private static object GetCosmosTypeName(FunctionCallExpression func, object?[] args, JObject item, string fromAlias)
    {
        if (args.Length == 0 || args[0] is UndefinedValue) return UndefinedValue.Instance;

        var token = ResolveTokenType(func.Arguments, item, fromAlias);
        if (token is JArray) return "array";
        if (token is JObject) return "object";

        return args[0] switch
        {
            null => "null",
            bool => "boolean",
            long or int or double or float or decimal => "number",
            string => "string",
            _ => "undefined"
        };
    }
}
