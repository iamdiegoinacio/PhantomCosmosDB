#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Primitives;

namespace Phantom.CosmosDB.Core.Application.Query.Functions;

/// <summary>
/// Evaluates Cosmos SQL string and type-conversion functions via a declarative handler registry.
/// </summary>
internal static class StringFunctions
{
    private static readonly IReadOnlyDictionary<string, Func<object?[], object?>> Registry =
        new Dictionary<string, Func<object?[], object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["STARTSWITH"] = args => MatchPrefixSuffixContains(args, static (s, p, cmp) => s.StartsWith(p, cmp)),
            ["ENDSWITH"] = args => MatchPrefixSuffixContains(args, static (s, p, cmp) => s.EndsWith(p, cmp)),
            ["CONTAINS"] = args => MatchPrefixSuffixContains(args, static (s, p, cmp) => s.Contains(p, cmp)),
            ["CONCAT"] = args => args.Any(static a => a is null or UndefinedValue or not string)
                ? UndefinedValue.Instance
                : string.Concat(args.Cast<string>()),
            ["LENGTH"] = args => args.Length == 0 ? null : args[0] is string s ? (long)s.Length : UndefinedValue.Instance,
            ["LOWER"] = args => TransformUnaryString(args, static s => s.ToLowerInvariant()),
            ["UPPER"] = args => TransformUnaryString(args, static s => s.ToUpperInvariant()),
            ["TRIM"] = args => TransformUnaryString(args, static s => s.Trim()),
            ["LTRIM"] = args => TransformUnaryString(args, static s => s.TrimStart()),
            ["RTRIM"] = args => TransformUnaryString(args, static s => s.TrimEnd()),
            ["REVERSE"] = args => TransformUnaryString(args, static s => new string(s.Reverse().ToArray())),
            ["LEFT"] = args => SliceEdge(args, fromStart: true),
            ["RIGHT"] = args => SliceEdge(args, fromStart: false),
            ["SUBSTRING"] = Substring,
            ["REPLACE"] = Replace,
            ["INDEX_OF"] = IndexOf,
            ["REGEXMATCH"] = RegexMatch,
            ["REPLICATE"] = Replicate,
            ["STRING_EQUALS"] = StringEquals,
            ["STRINGEQUALS"] = StringEquals,
            ["STRINGTOARRAY"] = args => ParseJsonToken<JArray>(args),
            ["STRINGTOOBJECT"] = args => ParseJsonToken<JObject>(args),
            ["STRINGTOBOOLEAN"] = StringToBoolean,
            ["STRINGTONULL"] = args => args.Length == 0 ? null
                : args[0] is null or UndefinedValue ? UndefinedValue.Instance
                : args[0]?.ToString()?.Trim() == "null" ? null : UndefinedValue.Instance,
            ["STRINGTONUMBER"] = StringToNumber,
            ["TOSTRING"] = ToStringValue,
            ["TONUMBER"] = ToNumberValue,
            ["TOBOOLEAN"] = ToBooleanValue,
            ["STRINGJOIN"] = StringJoin,
            ["STRINGSPLIT"] = StringSplit
        };

    public static bool TryEvaluate(string functionName, object?[] args, out object? result)
    {
        if (Registry.TryGetValue(functionName, out var handler))
        {
            result = handler(args);
            return true;
        }

        result = null;
        return false;
    }

    private static object? MatchPrefixSuffixContains(
        object?[] args,
        Func<string, string, StringComparison, bool> predicate)
    {
        if (args.Length < 2) return false;
        if (args[0] is not string str || args[1] is not string target) return UndefinedValue.Instance;
        var ignoreCase = args.Length >= 3 && string.Equals(args[2]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        return predicate(str, target, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static object? TransformUnaryString(object?[] args, Func<string, string> transform)
        => args.Length == 0 ? null : args[0] is string s ? transform(s) : UndefinedValue.Instance;

    private static object? SliceEdge(object?[] args, bool fromStart)
    {
        if (args.Length < 2) return null;
        if (args[0] is not string str) return UndefinedValue.Instance;
        var count = SqlMathHelpers.ToLong(args[1]);
        if (!count.HasValue || count.Value < 0) return UndefinedValue.Instance;
        return fromStart
            ? str[..(int)Math.Min(count.Value, str.Length)]
            : str[Math.Max(0, str.Length - (int)count.Value)..];
    }

    private static object? Substring(object?[] args)
    {
        if (args.Length < 3 || args[0] is not string str) return UndefinedValue.Instance;
        var start = SqlMathHelpers.ToLong(args[1]);
        var len = SqlMathHelpers.ToLong(args[2]);
        if (!start.HasValue || !len.HasValue) return UndefinedValue.Instance;

        var si = (int)Math.Max(0, Math.Min(start.Value, str.Length));
        var li = (int)Math.Max(0, Math.Min(len.Value, str.Length - si));
        return str.Substring(si, li);
    }

    private static object? Replace(object?[] args)
    {
        if (args.Length < 3) return null;
        if (args[0] is not string src || args[1] is not string find || args[2] is not (null or UndefinedValue or string))
            return UndefinedValue.Instance;

        return find.Length == 0 ? src : src.Replace(find, args[2] as string ?? string.Empty);
    }

    private static object? IndexOf(object?[] args)
    {
        if (args.Length < 2 || args[0] is not string str || args[1] is not string sub)
            return UndefinedValue.Instance;

        if (args.Length >= 3 && double.TryParse(args[2]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var startPos))
        {
            var sp = (int)startPos;
            return sp < 0 || sp > str.Length
                ? UndefinedValue.Instance
                : (long)str.IndexOf(sub, sp, StringComparison.Ordinal);
        }

        return (long)str.IndexOf(sub, StringComparison.Ordinal);
    }

    private static object? RegexMatch(object?[] args)
    {
        if (args.Length < 2) return false;
        if (args[0] is not string input || args[1] is not string pattern) return UndefinedValue.Instance;

        var options = RegexOptions.None;
        if (args.Length >= 3)
        {
            foreach (var ch in args[2]?.ToString() ?? string.Empty)
            {
                options |= ch switch
                {
                    'i' => RegexOptions.IgnoreCase,
                    'm' => RegexOptions.Multiline,
                    's' => RegexOptions.Singleline,
                    'x' => RegexOptions.IgnorePatternWhitespace,
                    _ => RegexOptions.None
                };
            }
        }

        try { return SqlMathHelpers.GetOrCreateRegex(pattern, options).IsMatch(input); }
        catch (ArgumentException) { return UndefinedValue.Instance; }
    }

    private static object? Replicate(object?[] args)
    {
        if (args.Length < 2) return null;
        if (args[0] is not string str) return UndefinedValue.Instance;
        var count = SqlMathHelpers.ToLong(args[1]);
        if (!count.HasValue || count.Value < 0 || count.Value > 10000) return UndefinedValue.Instance;
        return count.Value == 0 ? string.Empty : string.Concat(Enumerable.Repeat(str, (int)count.Value));
    }

    private static object? StringEquals(object?[] args)
    {
        if (args.Length < 2 || args[0] is not string s1 || args[1] is not string s2)
            return UndefinedValue.Instance;

        var ignoreCase = args.Length >= 3 && string.Equals(args[2]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        return string.Equals(s1, s2, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static object? ParseJsonToken<TToken>(object?[] args) where TToken : JToken
    {
        if (args.Length == 0) return null;
        if (args[0] is null or UndefinedValue) return UndefinedValue.Instance;
        var raw = args[0]?.ToString();
        if (raw is null) return UndefinedValue.Instance;

        try { return JToken.Parse(raw) is TToken typed ? typed : UndefinedValue.Instance; }
        catch { return UndefinedValue.Instance; }
    }

    private static object? StringToBoolean(object?[] args)
    {
        if (args.Length == 0) return null;
        if (args[0] is null or UndefinedValue) return UndefinedValue.Instance;
        return args[0]?.ToString()?.Trim() switch
        {
            "true" => true,
            "false" => false,
            _ => UndefinedValue.Instance
        };
    }

    private static object? StringToNumber(object?[] args)
    {
        if (args.Length == 0) return null;
        if (args[0] is null or UndefinedValue) return UndefinedValue.Instance;
        var raw = args[0]?.ToString()?.Trim();
        if (raw is null) return UndefinedValue.Instance;

        if (long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var longVal))
            return longVal;
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleVal))
            return double.IsNaN(doubleVal) || double.IsInfinity(doubleVal) ? UndefinedValue.Instance : doubleVal;

        return UndefinedValue.Instance;
    }

    private static object? ToStringValue(object?[] args)
    {
        if (args.Length == 0) return null;
        var v = args[0];
        if (v is null or UndefinedValue) return UndefinedValue.Instance;
        if (v is bool b) return b ? "true" : "false";
        if (v is JArray or JObject) return ((JToken)v).ToString(Formatting.None);
        if (v is JValue { Type: JTokenType.Null }) return UndefinedValue.Instance;
        return Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    private static object? ToNumberValue(object?[] args)
    {
        if (args.Length == 0) return null;
        if (args[0] is null) return UndefinedValue.Instance;
        if (args[0] is long or double) return args[0];
        return double.TryParse(args[0]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var num)
            ? num
            : UndefinedValue.Instance;
    }

    private static object? ToBooleanValue(object?[] args)
    {
        if (args.Length == 0) return null;
        if (args[0] is bool b) return b;
        return args[0] is not null && bool.TryParse(args[0]?.ToString(), out var parsed)
            ? parsed
            : UndefinedValue.Instance;
    }

    private static object? StringJoin(object?[] args)
    {
        if (args.Length < 2) return null;
        var separator = args[1]?.ToString();
        if (separator is null) return null;

        JArray arr;
        if (args[0] is JArray ja) arr = ja;
        else if (args[0] is string s) { try { arr = JArray.Parse(s); } catch { return null; } }
        else return null;

        return string.Join(separator, arr.Select(static t => t.Value<string>()));
    }

    private static object? StringSplit(object?[] args)
    {
        if (args.Length < 2) return null;
        var input = args[0]?.ToString();
        var delimiter = args[1]?.ToString();
        if (input is null || string.IsNullOrEmpty(delimiter)) return UndefinedValue.Instance;

        return new JArray(input.Split(delimiter).Select(static p => (JToken)p));
    }
}
