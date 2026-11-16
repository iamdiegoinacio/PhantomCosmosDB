#nullable enable
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Comparison;
using Phantom.CosmosDB.Core.Domain.Primitives;

namespace Phantom.CosmosDB.Core.Application.Query.Functions;

/// <summary>
/// Evaluates Cosmos SQL array, set, and object-conversion functions via a declarative handler registry.
/// </summary>
internal static class ArrayObjectFunctions
{
    private delegate object? ArrayFuncHandler(
        FunctionCallExpression func,
        object?[] args,
        JObject item,
        string fromAlias,
        Func<SqlExpression, object?> evaluateExpr);

    private static readonly IReadOnlyDictionary<string, ArrayFuncHandler> Registry =
        new Dictionary<string, ArrayFuncHandler>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARRAY_CONTAINS"] = (f, a, i, alias, eval) => ArrayContainsMatcher.EvaluateContains(f, a, i, alias, eval),
            ["ARRAY_CONTAINS_ANY"] = (f, _, i, alias, eval) => ArrayContainsMatcher.EvaluateContainsAnyOrAll(f, i, alias, eval, requireAll: false),
            ["ARRAY_CONTAINS_ALL"] = (f, _, i, alias, eval) => ArrayContainsMatcher.EvaluateContainsAnyOrAll(f, i, alias, eval, requireAll: true),
            ["ARRAY_LENGTH"] = (f, _, i, alias, eval) => ArrayLength(f, i, alias, eval),
            ["ARRAY_SLICE"] = (f, a, i, alias, eval) => ArraySlice(f, a, i, alias, eval),
            ["ARRAY_CONCAT"] = (f, _, i, alias, eval) => ArrayConcat(f, i, alias, eval),
            ["SETINTERSECT"] = (f, _, i, alias, eval) => SetIntersect(f, i, alias, eval),
            ["SETUNION"] = (f, _, i, alias, eval) => SetUnion(f, i, alias, eval),
            ["SETDIFFERENCE"] = (f, _, i, alias, eval) => SetDifference(f, i, alias, eval),
            ["CHOOSE"] = (_, a, _, _, _) => Choose(a),
            ["OBJECTTOARRAY"] = (_, a, _, _, _) => ObjectToArray(a),
            ["ARRAYTOOBJECT"] = (_, a, _, _, _) => ArrayToObject(a)
        };

    public static bool TryEvaluate(
        FunctionCallExpression func,
        object?[] args,
        JObject item,
        string fromAlias,
        Func<SqlExpression, object?> evaluateExpr,
        out object? result)
    {
        if (Registry.TryGetValue(func.FunctionName, out var handler))
        {
            result = handler(func, args, item, fromAlias, evaluateExpr);
            return true;
        }

        result = null;
        return false;
    }

    public static bool ArrayContainsMatch(JArray jArray, string searchStr, bool partial)
        => ArrayContainsMatcher.MatchValue(jArray, searchStr, partial);

    public static bool ArrayContainsMatchJObject(JArray jArray, JObject searchObj, bool partial)
        => ArrayContainsMatcher.MatchJObject(jArray, searchObj, partial);

    internal static JArray? ResolveJArray(
        SqlExpression argument,
        JObject item,
        string fromAlias,
        Func<SqlExpression, object?> evaluateExpr)
    {
        if (argument is IdentifierExpression ident)
        {
            var path = ident.Name;
            if (path.StartsWith(fromAlias + ".", StringComparison.OrdinalIgnoreCase))
                path = path[(fromAlias.Length + 1)..];

            return item.SelectToken(path) as JArray;
        }

        return evaluateExpr(argument) as JArray;
    }

    private static object? ArrayLength(FunctionCallExpression func, JObject item, string fromAlias, Func<SqlExpression, object?> evaluateExpr)
    {
        if (func.Arguments.Length < 1) return null;
        var arr = ResolveJArray(func.Arguments[0], item, fromAlias, evaluateExpr);
        return arr is not null ? (long)arr.Count : UndefinedValue.Instance;
    }

    private static object? ArraySlice(FunctionCallExpression func, object?[] args, JObject item, string fromAlias, Func<SqlExpression, object?> evaluateExpr)
    {
        if (func.Arguments.Length < 2) return null;
        var arr = ResolveJArray(func.Arguments[0], item, fromAlias, evaluateExpr);
        if (arr is null) return null;

        var start = (int)(SqlMathHelpers.ToLong(args[1]) ?? 0);
        if (start < 0) start = Math.Max(0, arr.Count + start);
        var length = args.Length >= 3 ? (int)(SqlMathHelpers.ToLong(args[2]) ?? arr.Count) : arr.Count;
        return new JArray(arr.Skip(start).Take(length));
    }

    private static object ArrayConcat(FunctionCallExpression func, JObject item, string fromAlias, Func<SqlExpression, object?> evaluateExpr)
    {
        var concatResult = new JArray();
        foreach (var argExpr in func.Arguments)
        {
            var arr = ResolveJArray(argExpr, item, fromAlias, evaluateExpr);
            if (arr is null) return UndefinedValue.Instance;
            foreach (var el in arr) concatResult.Add(el.DeepClone());
        }
        return concatResult;
    }

    private static object SetIntersect(FunctionCallExpression func, JObject item, string fromAlias, Func<SqlExpression, object?> evaluateExpr)
    {
        if (func.Arguments.Length < 2) return new JArray();
        var arr1 = ResolveJArray(func.Arguments[0], item, fromAlias, evaluateExpr);
        var arr2 = ResolveJArray(func.Arguments[1], item, fromAlias, evaluateExpr);
        if (arr1 is null || arr2 is null) return UndefinedValue.Instance;

        var set2 = new HashSet<JToken?>(arr2, JTokenValueComparer.Instance);
        var seen = new HashSet<JToken?>(JTokenValueComparer.Instance);
        var result = new JArray();
        foreach (var el in arr1)
        {
            if (set2.Contains(el) && seen.Add(el)) result.Add(el.DeepClone());
        }
        return result;
    }

    private static object SetUnion(FunctionCallExpression func, JObject item, string fromAlias, Func<SqlExpression, object?> evaluateExpr)
    {
        if (func.Arguments.Length < 2) return new JArray();
        var arr1 = ResolveJArray(func.Arguments[0], item, fromAlias, evaluateExpr);
        var arr2 = ResolveJArray(func.Arguments[1], item, fromAlias, evaluateExpr);
        if (arr1 is null || arr2 is null) return UndefinedValue.Instance;

        var seen = new HashSet<JToken?>(JTokenValueComparer.Instance);
        var result = new JArray();
        foreach (var el in arr1.Concat(arr2))
        {
            if (seen.Add(el)) result.Add(el.DeepClone());
        }
        return result;
    }

    private static object SetDifference(FunctionCallExpression func, JObject item, string fromAlias, Func<SqlExpression, object?> evaluateExpr)
    {
        if (func.Arguments.Length < 2) return new JArray();
        var arr1 = ResolveJArray(func.Arguments[0], item, fromAlias, evaluateExpr);
        var arr2 = ResolveJArray(func.Arguments[1], item, fromAlias, evaluateExpr);
        if (arr1 is null || arr2 is null) return UndefinedValue.Instance;

        var exclude = new HashSet<JToken?>(arr2, JTokenValueComparer.Instance);
        var seen = new HashSet<JToken?>(JTokenValueComparer.Instance);
        var result = new JArray();
        foreach (var el in arr1)
        {
            if (!exclude.Contains(el) && seen.Add(el)) result.Add(el.DeepClone());
        }
        return result;
    }

    private static object? Choose(object?[] args)
    {
        if (args.Length < 2) return UndefinedValue.Instance;
        var idx = SqlMathHelpers.ToLong(args[0]);
        return idx.HasValue && idx.Value >= 1 && idx.Value < args.Length ? args[(int)idx.Value] : UndefinedValue.Instance;
    }

    private static object? ObjectToArray(object?[] args)
    {
        if (args.Length < 1) return null;
        JObject obj;
        if (args[0] is JObject jo) obj = jo;
        else if (args[0] is string s) { try { obj = JObject.Parse(s); } catch { return UndefinedValue.Instance; } }
        else return UndefinedValue.Instance;

        var res = new JArray();
        foreach (var prop in obj.Properties())
            res.Add(new JObject { ["k"] = prop.Name, ["v"] = prop.Value.DeepClone() });
        return res;
    }

    private static object ArrayToObject(object?[] args)
    {
        if (args.Length < 1) return UndefinedValue.Instance;
        JArray arr;
        if (args[0] is JArray ja) arr = ja;
        else if (args[0] is string s) { try { arr = JArray.Parse(s); } catch { return UndefinedValue.Instance; } }
        else return UndefinedValue.Instance;

        var res = new JObject();
        foreach (var element in arr)
        {
            if (element is not JObject kvObj || kvObj["k"] is null || kvObj["v"] is null)
                return UndefinedValue.Instance;

            var keyName = kvObj["k"]!.Value<string>();
            if (keyName is not null)
                res[keyName] = kvObj["v"]!.DeepClone();
        }
        return res;
    }
}
