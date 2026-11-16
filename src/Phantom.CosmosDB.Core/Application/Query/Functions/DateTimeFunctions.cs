#nullable enable
using System.Globalization;
using Phantom.CosmosDB.Core.Domain.Primitives;

namespace Phantom.CosmosDB.Core.Application.Query.Functions;

/// <summary>
/// Evaluates Cosmos SQL date and time functions via a declarative handler registry.
/// </summary>
internal static class DateTimeFunctions
{
    private const string IsoFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    private static readonly IReadOnlyDictionary<string, Func<object?[], QueryExecutionContext, object?>> Registry =
        new Dictionary<string, Func<object?[], QueryExecutionContext, object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GETCURRENTDATETIME"] = static (_, ctx) => ctx.StaticDateTime,
            ["GETCURRENTDATETIMESTATIC"] = static (_, ctx) => ctx.StaticDateTime,
            ["GETCURRENTTIMESTAMP"] = static (_, ctx) => ctx.StaticTimestamp,
            ["GETCURRENTTIMESTAMPSTATIC"] = static (_, ctx) => ctx.StaticTimestamp,
            ["GETCURRENTTICKS"] = static (_, ctx) => ctx.StaticTicks,
            ["GETCURRENTTICKSSTATIC"] = static (_, ctx) => ctx.StaticTicks,
            ["DATETIMEADD"] = static (args, _) => DateTimeAdd(args),
            ["DATETIMEPART"] = static (args, _) => DateTimePart(args),
            ["DATETIMEDIFF"] = static (args, _) => DateTimeDiff(args),
            ["DATETIMEFROMPARTS"] = static (args, _) => DateTimeFromParts(args),
            ["DATETIMEBIN"] = static (args, _) => DateTimeBin(args),
            ["DATETIMETOTICKS"] = static (args, _) => DateTimeToTicks(args),
            ["TICKSTODATETIME"] = static (args, _) => TicksToDateTime(args),
            ["DATETIMETOTIMESTAMP"] = static (args, _) => DateTimeToTimestamp(args),
            ["TIMESTAMPTODATETIME"] = static (args, _) => TimestampToDateTime(args)
        };

    public static bool TryEvaluate(
        string functionName,
        object?[] args,
        QueryExecutionContext context,
        out object? result)
    {
        if (Registry.TryGetValue(functionName, out var handler))
        {
            result = handler(args, context);
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryParseDate(object? raw, out DateTime dt)
    {
        dt = default;
        if (raw is null or UndefinedValue) return false;
        var str = raw.ToString();
        return str is not null && DateTime.TryParse(str, null, DateTimeStyles.RoundtripKind, out dt);
    }

    private static DateTime EnsureUtc(DateTime dt)
        => dt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : dt.ToUniversalTime();

    private static object DateTimeAdd(object?[] args)
    {
        if (args.Length < 3) return UndefinedValue.Instance;
        var part = args[0]?.ToString()?.ToLowerInvariant();
        var num = SqlMathHelpers.ToLong(args[1]);
        if (part is null || !num.HasValue || !TryParseDate(args[2], out var dt))
            return UndefinedValue.Instance;

        var n = (int)num.Value;
        try
        {
            DateTime? added = part switch
            {
                "year" or "yyyy" or "yy" => dt.AddYears(n),
                "month" or "mm" or "m" => dt.AddMonths(n),
                "day" or "dd" or "d" => dt.AddDays(n),
                "hour" or "hh" => dt.AddHours(n),
                "minute" or "mi" or "n" => dt.AddMinutes(n),
                "second" or "ss" or "s" => dt.AddSeconds(n),
                "millisecond" or "ms" => dt.AddMilliseconds(n),
                "microsecond" or "mcs" => dt.AddTicks(n * 10L),
                "nanosecond" or "ns" => dt.AddTicks(n / 100L),
                _ => null
            };
            return added.HasValue ? added.Value.ToString(IsoFormat) : UndefinedValue.Instance;
        }
        catch (ArgumentOutOfRangeException)
        {
            return UndefinedValue.Instance;
        }
    }

    private static object? DateTimePart(object?[] args)
    {
        if (args.Length < 2) return UndefinedValue.Instance;
        var part = args[0]?.ToString()?.ToLowerInvariant();
        if (part is null || !TryParseDate(args[1], out var dt))
            return UndefinedValue.Instance;

        return part switch
        {
            "year" or "yyyy" or "yy" => (long)dt.Year,
            "month" or "mm" or "m" => (long)dt.Month,
            "day" or "dd" or "d" => (long)dt.Day,
            "hour" or "hh" => (long)dt.Hour,
            "minute" or "mi" or "n" => (long)dt.Minute,
            "second" or "ss" or "s" => (long)dt.Second,
            "millisecond" or "ms" => (long)dt.Millisecond,
            "microsecond" or "mcs" => dt.Ticks % TimeSpan.TicksPerSecond / 10,
            "nanosecond" or "ns" => dt.Ticks % TimeSpan.TicksPerSecond * 100,
            "weekday" or "dw" or "w" => (long)(dt.DayOfWeek + 1),
            _ => null
        };
    }

    private static object DateTimeDiff(object?[] args)
    {
        if (args.Length < 3) return UndefinedValue.Instance;
        var part = args[0]?.ToString()?.ToLowerInvariant();
        if (part is null || !TryParseDate(args[1], out var dtStart) || !TryParseDate(args[2], out var dtEnd))
            return UndefinedValue.Instance;

        return part switch
        {
            "year" or "yyyy" or "yy" => (long)(dtEnd.Year - dtStart.Year),
            "month" or "mm" or "m" => (long)((dtEnd.Year - dtStart.Year) * 12 + dtEnd.Month - dtStart.Month),
            "day" or "dd" or "d" => (long)(dtEnd.Date - dtStart.Date).TotalDays,
            "hour" or "hh" => SqlMathHelpers.FloorToUnit(dtEnd, TimeSpan.TicksPerHour) - SqlMathHelpers.FloorToUnit(dtStart, TimeSpan.TicksPerHour),
            "minute" or "mi" or "n" => SqlMathHelpers.FloorToUnit(dtEnd, TimeSpan.TicksPerMinute) - SqlMathHelpers.FloorToUnit(dtStart, TimeSpan.TicksPerMinute),
            "second" or "ss" or "s" => SqlMathHelpers.FloorToUnit(dtEnd, TimeSpan.TicksPerSecond) - SqlMathHelpers.FloorToUnit(dtStart, TimeSpan.TicksPerSecond),
            "millisecond" or "ms" => SqlMathHelpers.FloorToUnit(dtEnd, TimeSpan.TicksPerMillisecond) - SqlMathHelpers.FloorToUnit(dtStart, TimeSpan.TicksPerMillisecond),
            "microsecond" or "mcs" => (dtEnd - dtStart).Ticks / 10,
            "nanosecond" or "ns" => (dtEnd - dtStart).Ticks * 100,
            _ => UndefinedValue.Instance
        };
    }

    private static object DateTimeFromParts(object?[] args)
    {
        if (args.Length < 3) return UndefinedValue.Instance;
        var y = SqlMathHelpers.ToLong(args[0]);
        var mo = SqlMathHelpers.ToLong(args[1]);
        var d = SqlMathHelpers.ToLong(args[2]);
        var h = args.Length > 3 ? SqlMathHelpers.ToLong(args[3]) : 0;
        var mi = args.Length > 4 ? SqlMathHelpers.ToLong(args[4]) : 0;
        var s = args.Length > 5 ? SqlMathHelpers.ToLong(args[5]) : 0;
        var fraction = args.Length > 6 ? SqlMathHelpers.ToLong(args[6]) : 0;
        if (!y.HasValue || !mo.HasValue || !d.HasValue || !h.HasValue || !mi.HasValue || !s.HasValue || !fraction.HasValue ||
            fraction.Value < 0 || fraction.Value > 9999999)
        {
            return UndefinedValue.Instance;
        }

        try
        {
            var dt = new DateTime((int)y.Value, (int)mo.Value, (int)d.Value,
                (int)h.Value, (int)mi.Value, (int)s.Value, DateTimeKind.Utc).AddTicks(fraction.Value);
            return dt.ToString(IsoFormat);
        }
        catch (ArgumentOutOfRangeException)
        {
            return UndefinedValue.Instance;
        }
    }

    private static object DateTimeBin(object?[] args)
    {
        if (args.Length < 2) return UndefinedValue.Instance;
        var part = args[1]?.ToString()?.ToLowerInvariant();
        var binSize = args.Length >= 3 ? SqlMathHelpers.ToLong(args[2]) : 1;
        if (part is null || !binSize.HasValue || binSize.Value <= 0 || !TryParseDate(args[0], out var binDt))
            return UndefinedValue.Instance;

        var bs = (int)binSize.Value;
        var origin = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (args.Length >= 4 && (args[3] is not string originStr || !DateTime.TryParse(originStr, null, DateTimeStyles.RoundtripKind, out origin)))
            return UndefinedValue.Instance;

        if (origin.Year < 1601) return UndefinedValue.Instance;

        if (part is "year" or "yyyy" or "yy")
        {
            var yearBin = (int)(Math.Floor((double)(binDt.Year - origin.Year) / bs) * bs);
            return new DateTime(origin.Year + yearBin, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString(IsoFormat);
        }

        if (part is "month" or "mm" or "m")
        {
            var totalMonths = (binDt.Year - origin.Year) * 12 + (binDt.Month - origin.Month);
            var binned = (int)(Math.Floor((double)totalMonths / bs) * bs);
            return new DateTime(origin.Year, origin.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(binned).ToString(IsoFormat);
        }

        var ticksPerUnit = part switch
        {
            "day" or "dd" or "d" => TimeSpan.TicksPerDay,
            "hour" or "hh" => TimeSpan.TicksPerHour,
            "minute" or "mi" or "n" => TimeSpan.TicksPerMinute,
            "second" or "ss" or "s" => TimeSpan.TicksPerSecond,
            "millisecond" or "ms" => TimeSpan.TicksPerMillisecond,
            "microsecond" or "mcs" => 10L,
            "nanosecond" or "ns" => 1L,
            _ => -1L
        };
        if (ticksPerUnit < 0) return UndefinedValue.Instance;

        var binTicks = ticksPerUnit * bs;
        var binnedTicks = (long)Math.Floor((double)(binDt - origin).Ticks / binTicks) * binTicks;
        return origin.AddTicks(binnedTicks).ToString(IsoFormat);
    }

    private static object? DateTimeToTicks(object?[] args)
    {
        if (args.Length < 1) return null;
        return TryParseDate(args[0], out var dt) ? EnsureUtc(dt).Ticks : UndefinedValue.Instance;
    }

    private static object TicksToDateTime(object?[] args)
    {
        if (args.Length < 1) return UndefinedValue.Instance;
        var ticks = SqlMathHelpers.ToLong(args[0]);
        if (!ticks.HasValue) return UndefinedValue.Instance;
        try { return new DateTime(ticks.Value, DateTimeKind.Utc).ToString(IsoFormat); }
        catch (ArgumentOutOfRangeException) { return UndefinedValue.Instance; }
    }

    private static object DateTimeToTimestamp(object?[] args)
    {
        if (args.Length < 1) return UndefinedValue.Instance;
        return TryParseDate(args[0], out var dt)
            ? new DateTimeOffset(EnsureUtc(dt)).ToUnixTimeMilliseconds()
            : UndefinedValue.Instance;
    }

    private static object TimestampToDateTime(object?[] args)
    {
        if (args.Length < 1) return UndefinedValue.Instance;
        var ms = SqlMathHelpers.ToLong(args[0]);
        return ms.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime.ToString(IsoFormat)
            : UndefinedValue.Instance;
    }
}
