#nullable enable
using Superpower;
using Superpower.Parsers;

namespace Phantom.CosmosDB.Core.Application.Query.Parsing;

internal static class CosmosSqlGrammar
{
    // ── SELECT field parsing ──

    private static readonly TokenListParser<CosmosSqlToken, SelectField> StarField =
        Token.EqualTo(CosmosSqlToken.Star).Select(_ => new SelectField("*", null));

    private static readonly TokenListParser<CosmosSqlToken, SelectField> AliasDotStarField =
        from ident in CosmosSqlLiteralGrammar.AnyIdentifierOrKeyword
        from dot in Token.EqualTo(CosmosSqlToken.Dot)
        from star in Token.EqualTo(CosmosSqlToken.Star)
        select new SelectField("*", null);

    private static readonly TokenListParser<CosmosSqlToken, SelectField> ExpressionField =
        from expr in CosmosSqlExpressionGrammar.Expr
        from alias in (
            from as_ in Token.EqualTo(CosmosSqlToken.As)
            from name in CosmosSqlLiteralGrammar.AnyIdentifierOrKeyword
            select (string?)name
        ).OptionalOrDefault(null)
        select new SelectField(SqlAstFormatter.ExprToString(expr), alias, expr);

    private static readonly TokenListParser<CosmosSqlToken, SelectField> SingleSelectField =
        StarField.Try().Or(AliasDotStarField.Try()).Or(ExpressionField);

    // ── JOIN clause parsing ──

    private static readonly TokenListParser<CosmosSqlToken, JoinClause> JoinParser =
        from kw in Token.EqualTo(CosmosSqlToken.Join)
        from alias in CosmosSqlLiteralGrammar.AnyIdentifierOrKeyword
        from in_ in Token.EqualTo(CosmosSqlToken.In)
        from source in CosmosSqlLiteralGrammar.DottedPathWithIndex
        select ParseJoinSource(alias, source);

    // ── ORDER BY parsing ──

    private static readonly TokenListParser<CosmosSqlToken, OrderByField> OrderByFieldParser =
        (from expr in Superpower.Parse.Ref(() => CosmosSqlExpressionGrammar.Expr!)
         from dir in Token.EqualTo(CosmosSqlToken.Asc).Select(_ => true)
             .Or(Token.EqualTo(CosmosSqlToken.Desc).Select(_ => false))
             .OptionalOrDefault(true)
         select expr is IdentifierExpression ident
             ? new OrderByField(ident.Name, dir)
             : new OrderByField(null, dir, expr)).Try()
        .Or(
        from field in CosmosSqlLiteralGrammar.DottedPathWithIndex
        from dir in Token.EqualTo(CosmosSqlToken.Asc).Select(_ => true)
            .Or(Token.EqualTo(CosmosSqlToken.Desc).Select(_ => false))
            .OptionalOrDefault(true)
        select new OrderByField(field, dir));

    // ── Full query parsing ──

    public static readonly TokenListParser<CosmosSqlToken, CosmosSqlQuery> QueryParser =
        from select_ in Token.EqualTo(CosmosSqlToken.Select)
        from distinct in Token.EqualTo(CosmosSqlToken.Distinct).OptionalOrDefault()
        from top in (
            from topKw in Token.EqualTo(CosmosSqlToken.Top)
            from n in Token.EqualTo(CosmosSqlToken.NumberLiteral).Select(t => int.Parse(t.Span.ToStringValue()))
            select (int?)n
        ).OptionalOrDefault(null)
        from value_ in Token.EqualTo(CosmosSqlToken.Value).OptionalOrDefault()
        from fields in SingleSelectField.ManyDelimitedBy(Token.EqualTo(CosmosSqlToken.Comma))
        from fromKw in Token.EqualTo(CosmosSqlToken.From)
        from fromFirstIdent in CosmosSqlLiteralGrammar.AnyIdentifierOrKeyword
        from fromClause in (
            from in_ in Token.EqualTo(CosmosSqlToken.In)
            from source in CosmosSqlLiteralGrammar.DottedPath
            select (Alias: fromFirstIdent, Source: (string?)source)
        ).Try().Or(
            from as_ in Token.EqualTo(CosmosSqlToken.As)
            from alias in CosmosSqlLiteralGrammar.AnyIdentifierOrKeyword
            select (Alias: alias, Source: (string?)null)
        ).Try().Or(
            from alias in Token.EqualTo(CosmosSqlToken.Identifier).Select(CosmosSqlLiteralGrammar.TokenSpanToString)
            select (Alias: alias, Source: (string?)null)
        ).Try().OptionalOrDefault((Alias: fromFirstIdent, Source: (string?)null))
        from joins in JoinParser.Many()
        from where_ in (
            from whereKw in Token.EqualTo(CosmosSqlToken.Where)
            from expr in CosmosSqlExpressionGrammar.Expr
            select (SqlExpression?)expr
        ).OptionalOrDefault(null)
        from groupBy in (
            from groupKw in Token.EqualTo(CosmosSqlToken.Group)
            from byKw in Token.EqualTo(CosmosSqlToken.By)
            from groupFields in CosmosSqlExpressionGrammar.Expr.ManyDelimitedBy(Token.EqualTo(CosmosSqlToken.Comma))
            select (SqlExpression[]?)groupFields
        ).OptionalOrDefault(null)
        from having in (
            from havingKw in Token.EqualTo(CosmosSqlToken.Having)
            from expr in CosmosSqlExpressionGrammar.Expr
            select (SqlExpression?)expr
        ).OptionalOrDefault(null)
        from orderByResult in (
            from orderKw in Token.EqualTo(CosmosSqlToken.Order)
            from byKw in Token.EqualTo(CosmosSqlToken.By)
            from result in (
                from rankKw in Token.EqualTo(CosmosSqlToken.Identifier)
                    .Where(t => string.Equals(t.Span.ToStringValue(), "RANK", StringComparison.OrdinalIgnoreCase))
                from expr in CosmosSqlExpressionGrammar.Expr
                select (Fields: (OrderByField[]?)null, RankExpr: (SqlExpression?)expr)
            ).Try().Or(
                from orderFields in OrderByFieldParser.ManyDelimitedBy(Token.EqualTo(CosmosSqlToken.Comma))
                select (Fields: (OrderByField[]?)orderFields, RankExpr: (SqlExpression?)null)
            )
            select result
        ).OptionalOrDefault(default)
        from offset in (
            from offsetKw in Token.EqualTo(CosmosSqlToken.Offset)
            from n in Token.EqualTo(CosmosSqlToken.NumberLiteral).Select(t => int.Parse(t.Span.ToStringValue()))
            select (int?)n
        ).OptionalOrDefault(null)
        from limit in (
            from limitKw in Token.EqualTo(CosmosSqlToken.Limit)
            from n in Token.EqualTo(CosmosSqlToken.NumberLiteral).Select(t => int.Parse(t.Span.ToStringValue()))
            select (int?)n
        ).OptionalOrDefault(null)
        select BuildQuery(
            distinct.HasValue, top, value_.HasValue, fields,
            fromClause.Alias, fromClause.Source, joins, where_,
            groupBy?.Select(SqlAstFormatter.ExprToString).ToArray(),
            groupBy?.ToArray(),
            having, orderByResult.Fields, offset, limit, orderByResult.RankExpr);

    private static JoinClause ParseJoinSource(string alias, string source)
    {
        var dotIdx = source.IndexOf('.');
        if (dotIdx < 0)
        {
            return new JoinClause(alias, source, source);
        }

        return new JoinClause(alias, source[..dotIdx], source[(dotIdx + 1)..]);
    }

    private static CosmosSqlQuery BuildQuery(
        bool isDistinct, int? top, bool isValue,
        SelectField[] fields, string fromAlias, string? fromSource,
        JoinClause[] joins, SqlExpression? whereExpr,
        string[]? groupBy, SqlExpression[]? groupByExpressions, SqlExpression? havingExpr,
        OrderByField[]? orderByFields,
        int? offset, int? limit, SqlExpression? rankExpr = null)
    {
        var isSelectAll = fields.Length == 1 && fields[0].Expression == "*";
        var where = whereExpr != null ? WhereExpressionConverter.ToWhereExpression(whereExpr) : null;
        var having = havingExpr != null ? new SqlExpressionCondition(havingExpr) : null;
        var rawHavingExpr = havingExpr;

        var firstJoin = joins.Length > 0 ? joins[0] : null;
        OrderByClause? legacyOrderBy = null;
        if (orderByFields is { Length: > 0 })
        {
            legacyOrderBy = new OrderByClause(orderByFields[0].Field, orderByFields[0].Ascending);
        }

        return new CosmosSqlQuery(
            SelectFields: fields,
            IsSelectAll: isSelectAll,
            TopCount: top,
            FromAlias: fromAlias,
            Join: firstJoin,
            Where: where,
            Offset: offset,
            Limit: limit,
            OrderBy: legacyOrderBy,
            IsDistinct: isDistinct,
            IsValueSelect: isValue,
            Joins: joins,
            OrderByFields: orderByFields,
            GroupByFields: groupBy,
            GroupByExpressions: groupByExpressions,
            Having: having,
            WhereExpr: whereExpr,
            HavingExpr: rawHavingExpr,
            FromSource: fromSource,
            RankExpression: rankExpr);
    }
}
