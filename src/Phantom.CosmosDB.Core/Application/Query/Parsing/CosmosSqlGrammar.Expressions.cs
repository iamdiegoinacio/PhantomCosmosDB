#nullable enable
using Superpower;
using Superpower.Parsers;

namespace Phantom.CosmosDB.Core.Application.Query.Parsing;

internal static class CosmosSqlExpressionGrammar
{
    // ── Unary ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> UnaryExpr =
        (from op in Token.EqualTo(CosmosSqlToken.Not)
         from operand in Superpower.Parse.Ref(() => UnaryExpr!)
         select (SqlExpression)new UnaryExpression(UnaryOp.Not, operand))
        .Or(
            from op in Token.EqualTo(CosmosSqlToken.Minus)
            from operand in CosmosSqlLiteralGrammar.Primary
            select (SqlExpression)new UnaryExpression(UnaryOp.Negate, operand))
        .Or(
            from op in Token.EqualTo(CosmosSqlToken.Tilde)
            from operand in CosmosSqlLiteralGrammar.Primary
            select (SqlExpression)new UnaryExpression(UnaryOp.BitwiseNot, operand))
        .Or(CosmosSqlLiteralGrammar.Primary);

    // ── Multiplicative: *, /, % ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> Multiplicative =
        Superpower.Parse.Chain(
            Token.EqualTo(CosmosSqlToken.Star).Select(_ => BinaryOp.Multiply)
                .Or(Token.EqualTo(CosmosSqlToken.Slash).Select(_ => BinaryOp.Divide))
                .Or(Token.EqualTo(CosmosSqlToken.Percent).Select(_ => BinaryOp.Modulo)),
            UnaryExpr,
            (op, left, right) => new BinaryExpression(left, op, right));

    // ── Additive: +, - ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> Additive =
        Superpower.Parse.Chain(
            Token.EqualTo(CosmosSqlToken.Plus).Select(_ => BinaryOp.Add)
                .Or(Token.EqualTo(CosmosSqlToken.Minus).Select(_ => BinaryOp.Subtract)),
            Multiplicative,
            (op, left, right) => new BinaryExpression(left, op, right));

    // ── Bitwise AND: & ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> BitwiseAndExpr =
        Superpower.Parse.Chain(
            Token.EqualTo(CosmosSqlToken.Ampersand).Select(_ => BinaryOp.BitwiseAnd),
            Additive,
            (op, left, right) => new BinaryExpression(left, op, right));

    // ── Bitwise XOR: ^ ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> BitwiseXorExpr =
        Superpower.Parse.Chain(
            Token.EqualTo(CosmosSqlToken.Caret).Select(_ => BinaryOp.BitwiseXor),
            BitwiseAndExpr,
            (op, left, right) => new BinaryExpression(left, op, right));

    // ── Bitwise OR: | ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> BitwiseOrExpr =
        Superpower.Parse.Chain(
            Token.EqualTo(CosmosSqlToken.Pipe).Select(_ => BinaryOp.BitwiseOr),
            BitwiseXorExpr,
            (op, left, right) => new BinaryExpression(left, op, right));

    // ── String concat: || ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> StringConcatExpr =
        Superpower.Parse.Chain(
            Token.EqualTo(CosmosSqlToken.DoublePipe).Select(_ => BinaryOp.StringConcat),
            BitwiseOrExpr,
            (op, left, right) => new BinaryExpression(left, op, right));

    // ── Comparison: =, !=, <, >, <=, >=, LIKE ──

    private static readonly TokenListParser<CosmosSqlToken, BinaryOp> CompOp =
        Token.EqualTo(CosmosSqlToken.Equals).Select(_ => BinaryOp.Equal)
            .Or(Token.EqualTo(CosmosSqlToken.NotEquals).Select(_ => BinaryOp.NotEqual))
            .Or(Token.EqualTo(CosmosSqlToken.LessThanOrEqual).Select(_ => BinaryOp.LessThanOrEqual))
            .Or(Token.EqualTo(CosmosSqlToken.GreaterThanOrEqual).Select(_ => BinaryOp.GreaterThanOrEqual))
            .Or(Token.EqualTo(CosmosSqlToken.LessThan).Select(_ => BinaryOp.LessThan))
            .Or(Token.EqualTo(CosmosSqlToken.GreaterThan).Select(_ => BinaryOp.GreaterThan))
            .Or(Token.EqualTo(CosmosSqlToken.Like).Select(_ => BinaryOp.Like));

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> Comparison =
        from left in StringConcatExpr
        from rest in (
            (from kw in Token.EqualTo(CosmosSqlToken.Between)
             from low in StringConcatExpr
             from and in Token.EqualTo(CosmosSqlToken.And)
             from high in StringConcatExpr
             select (Func<SqlExpression, SqlExpression>)(l => new BetweenExpression(l, low, high)))
            .Or(
                (from not in Token.EqualTo(CosmosSqlToken.Not)
                 from kw in Token.EqualTo(CosmosSqlToken.Between)
                 from low in StringConcatExpr
                 from and in Token.EqualTo(CosmosSqlToken.And)
                 from high in StringConcatExpr
                 select (Func<SqlExpression, SqlExpression>)(l => new UnaryExpression(UnaryOp.Not, new BetweenExpression(l, low, high))))
                .Try())
            .Or(
                from kw in Token.EqualTo(CosmosSqlToken.In)
                from open in Token.EqualTo(CosmosSqlToken.OpenParen)
                from vals in Superpower.Parse.Ref(() => Expr!).ManyDelimitedBy(Token.EqualTo(CosmosSqlToken.Comma))
                from close in Token.EqualTo(CosmosSqlToken.CloseParen)
                select (Func<SqlExpression, SqlExpression>)(l => new InExpression(l, vals)))
            .Or(
                (from not in Token.EqualTo(CosmosSqlToken.Not)
                 from kw in Token.EqualTo(CosmosSqlToken.In)
                 from open in Token.EqualTo(CosmosSqlToken.OpenParen)
                 from vals in Superpower.Parse.Ref(() => Expr!).ManyDelimitedBy(Token.EqualTo(CosmosSqlToken.Comma))
                 from close in Token.EqualTo(CosmosSqlToken.CloseParen)
                 select (Func<SqlExpression, SqlExpression>)(l => new UnaryExpression(UnaryOp.Not, new InExpression(l, vals))))
                .Try())
            .Or(
                (from kw in Token.EqualTo(CosmosSqlToken.Like)
                 from pattern in StringConcatExpr
                 from esc in Token.EqualTo(CosmosSqlToken.Escape)
                 from escChar in Token.EqualTo(CosmosSqlToken.StringLiteral)
                 select (Func<SqlExpression, SqlExpression>)(l => new LikeExpression(l, pattern, escChar.Span.ToStringValue()[1..^1])))
                .Try())
            .Or(
                (from not in Token.EqualTo(CosmosSqlToken.Not)
                 from kw in Token.EqualTo(CosmosSqlToken.Like)
                 from pattern in StringConcatExpr
                 from esc in Token.EqualTo(CosmosSqlToken.Escape)
                 from escChar in Token.EqualTo(CosmosSqlToken.StringLiteral)
                 select (Func<SqlExpression, SqlExpression>)(l => new UnaryExpression(UnaryOp.Not, new LikeExpression(l, pattern, escChar.Span.ToStringValue()[1..^1]))))
                .Try())
            .Or(
                (from not in Token.EqualTo(CosmosSqlToken.Not)
                 from kw in Token.EqualTo(CosmosSqlToken.Like)
                 from pattern in StringConcatExpr
                 select (Func<SqlExpression, SqlExpression>)(l => new UnaryExpression(UnaryOp.Not, new BinaryExpression(l, BinaryOp.Like, pattern))))
                .Try())
            .Or(
                (from is_ in Token.EqualTo(CosmosSqlToken.Is)
                 from not_ in Token.EqualTo(CosmosSqlToken.Not)
                 from null_ in Token.EqualTo(CosmosSqlToken.Null)
                 select (Func<SqlExpression, SqlExpression>)(l => new BinaryExpression(l, BinaryOp.NotEqual, new LiteralExpression(null))))
                .Try())
            .Or(
                from is_ in Token.EqualTo(CosmosSqlToken.Is)
                from null_ in Token.EqualTo(CosmosSqlToken.Null)
                select (Func<SqlExpression, SqlExpression>)(l => new BinaryExpression(l, BinaryOp.Equal, new LiteralExpression(null))))
            .Or(
                from op in CompOp
                from right in StringConcatExpr
                select (Func<SqlExpression, SqlExpression>)(l => new BinaryExpression(l, op, right)))
        ).OptionalOrDefault(null)
        select rest != null ? rest(left) : left;

    // ── Logical AND ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> AndExpr =
        Superpower.Parse.Chain(
            Token.EqualTo(CosmosSqlToken.And).Select(_ => BinaryOp.And),
            Comparison,
            (op, left, right) => new BinaryExpression(left, op, right));

    // ── Logical OR ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> OrExpr =
        Superpower.Parse.Chain(
            Token.EqualTo(CosmosSqlToken.Or).Select(_ => BinaryOp.Or),
            AndExpr,
            (op, left, right) => new BinaryExpression(left, op, right));

    // ── Null coalesce: ?? ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> CoalesceExpr =
        from left in OrExpr
        from rest in (
            from op in Token.EqualTo(CosmosSqlToken.QuestionQuestion)
            from right in Superpower.Parse.Ref(() => CoalesceExpr!)
            select right
        ).OptionalOrDefault(null)
        select rest != null ? new CoalesceExpression(left, rest) : left;

    // ── Ternary: cond ? then : else ──

    private static readonly TokenListParser<CosmosSqlToken, SqlExpression> TernaryExpr =
        from cond in CoalesceExpr
        from rest in (
            from q in Token.EqualTo(CosmosSqlToken.Question)
            from ifTrue in Superpower.Parse.Ref(() => Expr!)
            from colon in Token.EqualTo(CosmosSqlToken.Colon)
            from ifFalse in Superpower.Parse.Ref(() => Expr!)
            select new { ifTrue, ifFalse }
        ).OptionalOrDefault(null)
        select rest != null ? (SqlExpression)new TernaryExpression(cond, rest.ifTrue, rest.ifFalse) : cond;

    // ── Top-level expression ──

    internal static readonly TokenListParser<CosmosSqlToken, SqlExpression> Expr = TernaryExpr;
}
