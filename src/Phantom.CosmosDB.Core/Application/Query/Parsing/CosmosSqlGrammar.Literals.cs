#nullable enable
using System.Globalization;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Phantom.CosmosDB.Core.Application.Query.Parsing;

/// <summary>
/// Token-based Superpower parser combinator grammar for Cosmos DB SQL queries.
/// </summary>
internal static class CosmosSqlLiteralGrammar
{
    internal static string TokenSpanToString(Token<CosmosSqlToken> token) =>
        token.Span.ToStringValue();

    internal static TokenListParser<CosmosSqlToken, string> AnyIdentifierOrKeyword =>
        Token.EqualTo(CosmosSqlToken.Identifier).Select(TokenSpanToString)
            .Or(Token.EqualTo(CosmosSqlToken.Value).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.Array).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.Defined).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.Asc).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.Desc).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.Top).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.Limit).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.Offset).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.Null).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.True).Select(TokenSpanToString))
            .Or(Token.EqualTo(CosmosSqlToken.False).Select(TokenSpanToString));

    // ── Dotted path: ident.ident.ident ──

    internal static readonly TokenListParser<CosmosSqlToken, string> DottedPath =
        from first in AnyIdentifierOrKeyword
        from rest in (
            from dot in Token.EqualTo(CosmosSqlToken.Dot)
            from next in AnyIdentifierOrKeyword
            select "." + next
        ).Many()
        select first + string.Concat(rest);

    // ── Dotted path with optional array indexing: ident.ident[0].ident ──

    private static readonly TokenListParser<CosmosSqlToken, string> BracketedStringContent =
        Token.EqualTo(CosmosSqlToken.StringLiteral).Select(t => t.Span.ToStringValue()[1..^1])
            .Or(Token.EqualTo(CosmosSqlToken.DoubleQuotedString).Select(t => t.Span.ToStringValue()[1..^1]));

    internal static readonly TokenListParser<CosmosSqlToken, string> DottedPathWithIndex =
        from first in AnyIdentifierOrKeyword
        from rest in (
            from dot in Token.EqualTo(CosmosSqlToken.Dot)
            from next in AnyIdentifierOrKeyword
            select "." + next
        ).Or(
            (from open in Token.EqualTo(CosmosSqlToken.OpenBracket)
             from idx in Token.EqualTo(CosmosSqlToken.NumberLiteral).Select(TokenSpanToString)
             from close in Token.EqualTo(CosmosSqlToken.CloseBracket)
             select "[" + idx + "]").Try()
        ).Or(
            (from open in Token.EqualTo(CosmosSqlToken.OpenBracket)
             from str in BracketedStringContent
             from close in Token.EqualTo(CosmosSqlToken.CloseBracket)
             select "." + str).Try()
        ).Many()
        select first + string.Concat(rest);

    // ── Primary expression ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> StringLiteral =
        Token.EqualTo(CosmosSqlToken.StringLiteral)
            .Select(t =>
            {
                var raw = t.Span.ToStringValue();
                var unquoted = raw[1..^1].Replace("''", "'");
                return (SqlExpression)new LiteralExpression(unquoted);
            })
        .Or(Token.EqualTo(CosmosSqlToken.DoubleQuotedString)
            .Select(t =>
            {
                var raw = t.Span.ToStringValue();
                var unquoted = raw[1..^1];
                return (SqlExpression)new LiteralExpression(unquoted);
            }));

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> NumberLiteral =
        Token.EqualTo(CosmosSqlToken.NumberLiteral)
            .Select(t =>
            {
                var raw = t.Span.ToStringValue();
                if (long.TryParse(raw, out var longVal))
                {
                    return (SqlExpression)new LiteralExpression(longVal);
                }

                return (SqlExpression)new LiteralExpression(double.Parse(raw, CultureInfo.InvariantCulture));
            });

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> TrueLiteral =
        Token.EqualTo(CosmosSqlToken.True).Select(_ => (SqlExpression)new LiteralExpression(true));

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> FalseLiteral =
        Token.EqualTo(CosmosSqlToken.False).Select(_ => (SqlExpression)new LiteralExpression(false));

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> NullLiteral =
        Token.EqualTo(CosmosSqlToken.Null).Select(_ => (SqlExpression)new LiteralExpression(null));

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> UndefinedLiteral =
        Token.EqualTo(CosmosSqlToken.Undefined).Select(_ => (SqlExpression)new UndefinedLiteralExpression());

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> ParameterExpr =
        Token.EqualTo(CosmosSqlToken.Parameter)
            .Select(t => (SqlExpression)new ParameterExpression(t.Span.ToStringValue()));

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> FunctionCall =
        from name in AnyIdentifierOrKeyword
        from open in Token.EqualTo(CosmosSqlToken.OpenParen)
        from args in Token.EqualTo(CosmosSqlToken.Star).Select(_ => (SqlExpression)new IdentifierExpression("*"))
            .Or(Superpower.Parse.Ref(() => CosmosSqlExpressionGrammar.Expr!))
            .ManyDelimitedBy(Token.EqualTo(CosmosSqlToken.Comma))
        from close in Token.EqualTo(CosmosSqlToken.CloseParen)
        select (SqlExpression)new FunctionCallExpression(name.ToUpperInvariant(), args);

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> DottedFunctionCall =
        from first in AnyIdentifierOrKeyword
        from dot in Token.EqualTo(CosmosSqlToken.Dot)
        from second in AnyIdentifierOrKeyword
        from open in Token.EqualTo(CosmosSqlToken.OpenParen)
        from args in Superpower.Parse.Ref(() => CosmosSqlExpressionGrammar.Expr!).ManyDelimitedBy(Token.EqualTo(CosmosSqlToken.Comma))
        from close in Token.EqualTo(CosmosSqlToken.CloseParen)
        select (SqlExpression)new FunctionCallExpression(
            first.Equals("udf", StringComparison.OrdinalIgnoreCase)
                ? "UDF." + second
                : (first + "." + second).ToUpperInvariant(), args);

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> ExistsExpr =
        from kw in Token.EqualTo(CosmosSqlToken.Exists)
        from open in Token.EqualTo(CosmosSqlToken.OpenParen)
        from inner in CaptureBalancedParens()
        from close in Token.EqualTo(CosmosSqlToken.CloseParen)
        select (SqlExpression)new ExistsExpression(inner);

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> SubqueryParens =
        from open in Token.EqualTo(CosmosSqlToken.OpenParen)
        from subquery in Superpower.Parse.Ref(() => CosmosSqlGrammar.QueryParser!)
        from close in Token.EqualTo(CosmosSqlToken.CloseParen)
        select (SqlExpression)new SubqueryExpression(subquery);

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> Parens =
        SubqueryParens.Try()
        .Or(
            from open in Token.EqualTo(CosmosSqlToken.OpenParen)
            from expr in Superpower.Parse.Ref(() => CosmosSqlExpressionGrammar.Expr!)
            from close in Token.EqualTo(CosmosSqlToken.CloseParen)
            select expr);

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> ObjectLiteral =
        from open in Token.EqualTo(CosmosSqlToken.OpenBrace)
        from props in (
            from key in AnyIdentifierOrKeyword
                .Or(Token.EqualTo(CosmosSqlToken.DoubleQuotedString).Select(t => t.Span.ToStringValue()[1..^1]))
                .Or(Token.EqualTo(CosmosSqlToken.StringLiteral).Select(t => t.Span.ToStringValue()[1..^1]))
            from colon in Token.EqualTo(CosmosSqlToken.Colon)
            from value in Superpower.Parse.Ref(() => CosmosSqlExpressionGrammar.Expr!)
            select new KeyValuePair<string, SqlExpression>(key, value)
        ).ManyDelimitedBy(Token.EqualTo(CosmosSqlToken.Comma))
        from close in Token.EqualTo(CosmosSqlToken.CloseBrace)
        select (SqlExpression)new ObjectLiteralExpression(props);

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> ArrayLiteral =
        from open in Token.EqualTo(CosmosSqlToken.OpenBracket)
        from elements in Superpower.Parse.Ref(() => CosmosSqlExpressionGrammar.Expr!).ManyDelimitedBy(Token.EqualTo(CosmosSqlToken.Comma))
        from close in Token.EqualTo(CosmosSqlToken.CloseBracket)
        select (SqlExpression)new ArrayLiteralExpression(elements);

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> IdentExpr =
        DottedPathWithIndex.Select(path => (SqlExpression)new IdentifierExpression(path));

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> ArraySubqueryCall =
        from name in Token.EqualTo(CosmosSqlToken.Array)
        from open in Token.EqualTo(CosmosSqlToken.OpenParen)
        from subquery in Superpower.Parse.Ref(() => CosmosSqlGrammar.QueryParser!)
        from close in Token.EqualTo(CosmosSqlToken.CloseParen)
        select (SqlExpression)new FunctionCallExpression("ARRAY", [new SubqueryExpression(subquery)]);

    internal static readonly TokenListParser<CosmosSqlToken, SqlExpression> Primary =
        StringLiteral
            .Or(NumberLiteral)
            .Or(TrueLiteral)
            .Or(FalseLiteral)
            .Or(NullLiteral)
            .Or(UndefinedLiteral)
            .Or(ParameterExpr)
            .Or(ExistsExpr.Try())
            .Or(ArraySubqueryCall.Try())
            .Or(DottedFunctionCall.Try())
            .Or(FunctionCall.Try())
            .Or(Parens)
            .Or(ObjectLiteral.Try())
            .Or(ArrayLiteral.Try())
            .Or(IdentExpr);

    private static TokenListParser<CosmosSqlToken, string> CaptureBalancedParens()
    {
        return input =>
        {
            var depth = 0;
            var current = input;
            var parts = new List<string>();

            while (!current.IsAtEnd)
            {
                var token = current.ConsumeToken();
                if (!token.HasValue)
                {
                    break;
                }

                if (token.Value.Kind == CosmosSqlToken.OpenParen)
                {
                    depth++;
                    parts.Add("(");
                    current = token.Remainder;
                }
                else if (token.Value.Kind == CosmosSqlToken.CloseParen)
                {
                    if (depth == 0)
                    {
                        return TokenListParserResult.Value(string.Join(" ", parts), input, current);
                    }
                    depth--;
                    parts.Add(")");
                    current = token.Remainder;
                }
                else
                {
                    parts.Add(token.Value.Span.ToStringValue());
                    current = token.Remainder;
                }
            }

            return TokenListParserResult.Value(string.Join(" ", parts), input, current);
        };
    }
}
