using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DATA_EXCHANGE_HUB.Services;

internal sealed record QvdTextEquality(string Column, string Value);
internal sealed record QvdSourceReadPlan(string Source, string Alias, string Predicate, IReadOnlySet<string> Parameters,
    IReadOnlyList<QvdTextEquality> Equalities);
internal sealed record QvdMatch(string Source, string[] Columns, string Parent, string[] ParentColumns);

internal static class QvdReadPlanner
{
    public static QvdSourceReadPlan? Build(SelectStatement select, IReadOnlySet<string> sources,
        IReadOnlyDictionary<string, object?> parameters, Func<TSqlFragment, string> render)
    {
        if (select.QueryExpression is not QuerySpecification query || query.WhereClause is null || query.FromClause.TableReferences.Count != 1)
            return null;
        var from = query.FromClause.TableReferences[0];
        var allowBare = from is NamedTableReference;
        while (from is QualifiedJoin join)
        {
            // Only the preserved left input can be filtered ahead of an outer join.
            if (join.QualifiedJoinType is not (QualifiedJoinType.Inner or QualifiedJoinType.LeftOuter)) return null;
            from = join.FirstTableReference;
        }
        if (from is not NamedTableReference named || !sources.Contains(named.SchemaObject.BaseIdentifier.Value)) return null;
        var source = named.SchemaObject.BaseIdentifier.Value;
        if (select.WithCtesAndXmlNamespaces?.CommonTableExpressions.Any(cte => cte.ExpressionName.Value.Equals(source, StringComparison.OrdinalIgnoreCase)) == true)
            return null;
        var uses = new SourceUses(source);
        select.Accept(uses);
        if (uses.Count != 1) return null; // A shared source / self join must retain its other uses.
        var alias = named.Alias?.Value ?? source;
        var filters = new List<BooleanExpression>();
        var equalities = new List<QvdTextEquality>();
        var variables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in Conjuncts(query.WhereClause.SearchCondition))
        {
            var dependencies = new Dependencies(alias, allowBare);
            term.Accept(dependencies);
            if (!dependencies.Valid) continue;
            filters.Add(term);
            variables.UnionWith(dependencies.Variables);
            if (Equality(term, alias, allowBare, parameters) is { } equality) equalities.Add(equality);
        }
        return filters.Count == 0 ? null : new(source, alias,
            string.Join(" AND ", filters.Select(filter => "(" + render(filter) + ")")), variables, equalities.AsEnumerable().Reverse().ToArray());
    }

    public static IReadOnlyDictionary<string, QvdMatch> ReadMatches(string sql, IReadOnlyList<QvdTableSource> sources)
    {
        var result = new Dictionary<string, QvdMatch>(StringComparer.OrdinalIgnoreCase);
        var order = sources.Select((source, index) => (source.Alias, index)).ToDictionary(pair => pair.Alias, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        foreach (var line in sql.Split('\n').Select(line => line.Trim()))
        {
            if (!line.StartsWith("-- QVD_MATCH:", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line[13..].Trim().Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length != 4) throw MatchError();
            var columns = parts[1].Split(',', StringSplitOptions.TrimEntries);
            var parentColumns = parts[3].Split(',', StringSplitOptions.TrimEntries);
            if (!order.TryGetValue(parts[0], out var child) || !order.TryGetValue(parts[2], out var parent) || parent >= child ||
                columns.Length is < 1 or > 8 || columns.Length != parentColumns.Length ||
                columns.Concat(parentColumns).Any(column => !QvdReportFiles.IdentifierPattern().IsMatch(column)) ||
                !result.TryAdd(parts[0], new(parts[0], columns, parts[2], parentColumns))) throw MatchError();
        }
        return result;
    }

    private static InvalidOperationException MatchError() => QvdReportError.Create(
        "Neteisingas QVD_MATCH: alias|stulpeliai|anksciau deklaruotas saltinis|jo stulpeliai.",
        "Invalid QVD_MATCH: alias|columns|previously declared source|its columns.");

    private static BooleanExpression Unwrap(BooleanExpression expression) => expression is BooleanParenthesisExpression parenthesis ? Unwrap(parenthesis.Expression) : expression;
    private static IEnumerable<BooleanExpression> Conjuncts(BooleanExpression expression)
    {
        expression = Unwrap(expression);
        if (expression is BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } binary)
        {
            foreach (var item in Conjuncts(binary.FirstExpression)) yield return item;
            foreach (var item in Conjuncts(binary.SecondExpression)) yield return item;
        }
        else yield return expression; // Never pull only one branch from OR.
    }

    private static QvdTextEquality? Equality(BooleanExpression expression, string alias, bool allowBare, IReadOnlyDictionary<string, object?> parameters)
    {
        expression = Unwrap(expression);
        if (expression is BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } binary)
        {
            // Common optional parameter form: @P IS NULL OR source.COLUMN = @P.
            var gate = Unwrap(binary.FirstExpression) as BooleanIsNullExpression;
            var comparison = Unwrap(binary.SecondExpression) as BooleanComparisonExpression;
            if (gate is not { IsNot: false, Expression: VariableReference variable } || comparison is null ||
                comparison.SecondExpression is not VariableReference right || !right.Name.Equals(variable.Name, StringComparison.OrdinalIgnoreCase)) return null;
            expression = comparison;
        }
        if (expression is not BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } equal) return null;
        if (equal.FirstExpression is PrimaryExpression { Collation: not null } ||
            equal.SecondExpression is PrimaryExpression { Collation: not null }) return null;
        var column = equal.FirstExpression as ColumnReferenceExpression;
        var value = equal.SecondExpression;
        if (column is null) { column = equal.SecondExpression as ColumnReferenceExpression; value = equal.FirstExpression; }
        if (column?.MultiPartIdentifier is not { } identifier ||
            !(identifier.Identifiers.Count == 1 && allowBare || identifier.Identifiers.Count == 2 && identifier.Identifiers[0].Value.Equals(alias, StringComparison.OrdinalIgnoreCase))) return null;
        var text = value switch
        {
            VariableReference variable when parameters.TryGetValue(variable.Name, out var parameter) => parameter as string,
            StringLiteral literal => literal.Value,
            _ => null
        };
        return text is null ? null : new(identifier.Identifiers.Last().Value, text);
    }

    private sealed class SourceUses(string source) : TSqlFragmentVisitor
    {
        public int Count { get; private set; }
        public override void ExplicitVisit(NamedTableReference node)
        {
            if (node.SchemaObject.BaseIdentifier.Value.Equals(source, StringComparison.OrdinalIgnoreCase)) Count++;
            base.ExplicitVisit(node);
        }
    }

    private sealed class Dependencies(string alias, bool allowBare) : TSqlFragmentVisitor
    {
        public bool Valid { get; private set; } = true;
        public HashSet<string> Variables { get; } = new(StringComparer.Ordinal);
        public override void ExplicitVisit(VariableReference node) => Variables.Add(node.Name[1..]);
        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            var identifiers = node.MultiPartIdentifier?.Identifiers;
            if (identifiers is null || !(identifiers.Count == 1 && allowBare ||
                identifiers.Count == 2 && identifiers[0].Value.Equals(alias, StringComparison.OrdinalIgnoreCase))) Valid = false;
        }
        public override void ExplicitVisit(ScalarSubquery node)
        {
            // The API's IN filter reads only a parameter via STRING_SPLIT, never another data source.
            var check = new SplitSubquery();
            node.QueryExpression.Accept(check);
            Valid &= check.Valid && check.HasSplit;
            Variables.UnionWith(check.Variables);
        }
    }

    private sealed class SplitSubquery : TSqlFragmentVisitor
    {
        public bool Valid { get; private set; } = true;
        public bool HasSplit { get; private set; }
        public HashSet<string> Variables { get; } = new(StringComparer.Ordinal);
        public override void ExplicitVisit(VariableReference node) => Variables.Add(node.Name[1..]);
        public override void ExplicitVisit(NamedTableReference node) => Valid = false;
        public override void ExplicitVisit(ScalarSubquery node) => Valid = false;
        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier?.Identifiers is not { Count: 1 } identifiers ||
                !identifiers[0].Value.Equals("value", StringComparison.OrdinalIgnoreCase)) Valid = false;
        }
        public override void ExplicitVisit(SchemaObjectFunctionTableReference node)
        {
            HasSplit |= node.SchemaObject.BaseIdentifier.Value.Equals("STRING_SPLIT", StringComparison.OrdinalIgnoreCase);
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(GlobalFunctionTableReference node)
        {
            HasSplit |= node.Name.Value.Equals("STRING_SPLIT", StringComparison.OrdinalIgnoreCase);
            base.ExplicitVisit(node);
        }
    }
}
