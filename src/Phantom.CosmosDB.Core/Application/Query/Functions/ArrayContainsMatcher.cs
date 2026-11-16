#nullable enable
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Comparison;
using Phantom.CosmosDB.Core.Domain.Primitives;

namespace Phantom.CosmosDB.Core.Application.Query.Functions;

/// <summary>
/// Evaluates <c>ARRAY_CONTAINS</c>, <c>ARRAY_CONTAINS_ANY</c>, and <c>ARRAY_CONTAINS_ALL</c>.
/// </summary>
internal static class ArrayContainsMatcher
{
    public static object EvaluateContains(
        FunctionCallExpression func,
        object?[] args,
        JObject item,
        string fromAlias,
        Func<SqlExpression, object?> evaluateExpr)
    {
        if (func.Arguments.Length < 2) return false;

        var jArray = func.Arguments[0] is IdentifierExpression
            ? ArrayObjectFunctions.ResolveJArray(func.Arguments[0], item, fromAlias, evaluateExpr)
            : args[0] as JArray;
        if (jArray is null) return false;

        var partial = args.Length >= 3 && string.Equals(args[2]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        var searchValue = args[1];
        if (searchValue is JObject searchObj)
            return MatchJObject(jArray, searchObj, partial);

        if (searchValue is null)
            return jArray.Any(static t => t.Type == JTokenType.Null);

        return MatchValue(jArray, searchValue.ToString() ?? string.Empty, partial);
    }

    public static object EvaluateContainsAnyOrAll(
        FunctionCallExpression func,
        JObject item,
        string fromAlias,
        Func<SqlExpression, object?> evaluateExpr,
        bool requireAll)
    {
        if (func.Arguments.Length < 2) return false;

        var source = ArrayObjectFunctions.ResolveJArray(func.Arguments[0], item, fromAlias, evaluateExpr);
        if (source is null || (!requireAll && source.Count == 0))
            return false;

        var search = ResolveSearchArray(func, item, fromAlias, evaluateExpr);
        if (search is null || search.Count == 0)
            return requireAll;

        var sourceSet = new HashSet<JToken?>(source, JTokenValueComparer.Instance);
        return requireAll
            ? search.All(t => sourceSet.Contains(t))
            : search.Any(t => sourceSet.Contains(t));
    }

    public static bool MatchValue(JArray jArray, string searchStr, bool partial)
    {
        var parsedObj = searchStr.StartsWith("{", StringComparison.Ordinal)
            ? JsonParseHelpers.ParseJson(searchStr)
            : null;

        foreach (var element in jArray)
        {
            if (element.Type == JTokenType.Object && parsedObj is not null)
            {
                if (MatchesObject((JObject)element, parsedObj, partial)) return true;
            }
            else if (string.Equals(element.ToString(), searchStr, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public static bool MatchJObject(JArray jArray, JObject searchObj, bool partial)
    {
        foreach (var element in jArray)
        {
            if (element is JObject objElement && MatchesObject(objElement, searchObj, partial))
                return true;
        }
        return false;
    }

    private static bool MatchesObject(JObject element, JObject searchObj, bool partial)
    {
        if (!partial)
            return JToken.DeepEquals(element, searchObj);

        return searchObj.Properties().All(prop =>
        {
            var elementValue = element[prop.Name];
            return elementValue is not null && JToken.DeepEquals(elementValue, prop.Value);
        });
    }

    private static JArray? ResolveSearchArray(
        FunctionCallExpression func,
        JObject item,
        string fromAlias,
        Func<SqlExpression, object?> evaluateExpr)
    {
        var resolved = ArrayObjectFunctions.ResolveJArray(func.Arguments[1], item, fromAlias, evaluateExpr);
        if (resolved is not null) return resolved;

        var arr = new JArray();
        for (var i = 1; i < func.Arguments.Length; i++)
        {
            var val = evaluateExpr(func.Arguments[i]);
            if (val is UndefinedValue) continue;
            arr.Add(val is JToken jt ? jt : (val is null ? JValue.CreateNull() : JToken.FromObject(val)));
        }
        return arr;
    }
}
