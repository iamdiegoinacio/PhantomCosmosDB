#nullable enable
using System.Globalization;
using Phantom.CosmosDB.Core.Domain.Primitives;

namespace Phantom.CosmosDB.Core.Application.Query.Functions;

/// <summary>
/// Evaluates Cosmos SQL mathematical, trigonometric, rounding, and integer/bitwise functions via a declarative registry.
/// </summary>
internal static class MathFunctions
{
    private static readonly IReadOnlyDictionary<string, Func<object?[], object?>> Registry =
        new Dictionary<string, Func<object?[], object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ABS"] = args => UnaryMath(args, Math.Abs),
            ["CEILING"] = args => UnaryMath(args, Math.Ceiling),
            ["FLOOR"] = args => UnaryMath(args, Math.Floor),
            ["ROUND"] = Round,
            ["SQRT"] = args => UnaryMath(args, Math.Sqrt),
            ["SQUARE"] = args => UnaryMath(args, static v => v * v),
            ["POWER"] = args => BinaryArith(args, Math.Pow),
            ["EXP"] = args => UnaryMath(args, Math.Exp),
            ["LOG"] = Log,
            ["LOG10"] = args => UnaryMath(args, Math.Log10),
            ["SIGN"] = args => UnaryMath(args, static v => Math.Sign(v)),
            ["TRUNC"] = args => UnaryMath(args, Math.Truncate),
            ["PI"] = _ => Math.PI,
            ["SIN"] = args => UnaryMath(args, Math.Sin),
            ["COS"] = args => UnaryMath(args, Math.Cos),
            ["TAN"] = args => UnaryMath(args, Math.Tan),
            ["COT"] = args => UnaryMath(args, static v => 1.0 / Math.Tan(v)),
            ["ASIN"] = args => UnaryMath(args, Math.Asin),
            ["ACOS"] = args => UnaryMath(args, Math.Acos),
            ["ATAN"] = args => UnaryMath(args, Math.Atan),
            ["ATN2"] = args => BinaryArith(args, Math.Atan2),
            ["DEGREES"] = args => UnaryMath(args, static v => v * (180.0 / Math.PI)),
            ["RADIANS"] = args => UnaryMath(args, static v => v * (Math.PI / 180.0)),
            ["RAND"] = _ => Random.Shared.NextDouble(),
            ["NUMBERBIN"] = NumberBin,
            ["INTADD"] = args => BinaryBitwise(args, static (a, b) => a + b),
            ["INTSUB"] = args => BinaryBitwise(args, static (a, b) => a - b),
            ["INTMUL"] = args => BinaryBitwise(args, static (a, b) => a * b),
            ["INTDIV"] = args => IntDivOrMod(args, isMod: false),
            ["INTMOD"] = args => IntDivOrMod(args, isMod: true),
            ["INTBITAND"] = args => BinaryBitwise(args, static (a, b) => a & b),
            ["INTBITOR"] = args => BinaryBitwise(args, static (a, b) => a | b),
            ["INTBITXOR"] = args => BinaryBitwise(args, static (a, b) => a ^ b),
            ["INTBITNOT"] = IntBitNot,
            ["INTBITLEFTSHIFT"] = args => args.Length >= 2 ? SqlMathHelpers.BitwiseShiftOp(args[0], args[1], isLeft: true) : null,
            ["INTBITRIGHTSHIFT"] = args => args.Length >= 2 ? SqlMathHelpers.BitwiseShiftOp(args[0], args[1], isLeft: false) : null
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

    private static object? UnaryMath(object?[] args, Func<double, double> op)
        => args.Length > 0 ? SqlMathHelpers.MathOp(args[0], op) : null;

    private static object? BinaryArith(object?[] args, Func<double, double, double> op)
        => args.Length >= 2 ? SqlMathHelpers.ArithmeticOp(args[0], args[1], op) : null;

    private static object? BinaryBitwise(object?[] args, Func<long, long, long> op)
        => args.Length >= 2 ? SqlMathHelpers.BitwiseOp(args[0], args[1], op) : null;

    private static object? Round(object?[] args)
    {
        if (args.Length >= 2 &&
            double.TryParse(args[0]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val) &&
            double.TryParse(args[1]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var precRaw))
        {
            var prec = (int)precRaw;
            if (prec < 0)
            {
                var factor = Math.Pow(10, -prec);
                return Math.Round(val / factor, MidpointRounding.AwayFromZero) * factor;
            }
            return Math.Round(val, prec, MidpointRounding.AwayFromZero);
        }

        return args.Length > 0 ? SqlMathHelpers.MathOp(args[0], static v => Math.Round(v, MidpointRounding.AwayFromZero)) : null;
    }

    private static object? Log(object?[] args)
    {
        if (args.Length >= 2 &&
            double.TryParse(args[0]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val) &&
            double.TryParse(args[1]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var logBase))
        {
            var logResult = Math.Log(val, logBase);
            return double.IsNaN(logResult) || double.IsInfinity(logResult) ? UndefinedValue.Instance : logResult;
        }

        return args.Length > 0 ? SqlMathHelpers.MathOp(args[0], Math.Log) : null;
    }

    private static object? NumberBin(object?[] args)
    {
        if (args.Length < 2) return null;
        if (double.TryParse(args[0]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val) &&
            double.TryParse(args[1]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var binSize) &&
            binSize > 0)
        {
            return Math.Floor(val / binSize) * binSize;
        }

        return UndefinedValue.Instance;
    }

    private static object? IntDivOrMod(object?[] args, bool isMod)
    {
        if (args.Length < 2) return UndefinedValue.Instance;
        var dividend = SqlMathHelpers.ToLong(args[0]);
        var divisor = SqlMathHelpers.ToLong(args[1]);
        if (!dividend.HasValue || !divisor.HasValue || divisor.Value == 0)
            return UndefinedValue.Instance;

        return isMod ? dividend.Value % divisor.Value : dividend.Value / divisor.Value;
    }

    private static object? IntBitNot(object?[] args)
    {
        if (args.Length == 0) return null;
        var notVal = SqlMathHelpers.ToLong(args[0]);
        return notVal.HasValue ? ~notVal.Value : null;
    }
}
