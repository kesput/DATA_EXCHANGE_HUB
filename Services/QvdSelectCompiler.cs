using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DATA_EXCHANGE_HUB.Services;

internal sealed record QvdCompiledSelect(string Sql, IReadOnlySet<string> Parameters, IReadOnlySet<string>? RequiredColumns, QvdSourceReadPlan? ReadPlan);

internal static class QvdSelectCompiler
{
    public static QvdCompiledSelect Compile(string sql, IReadOnlySet<string> tables, IReadOnlyDictionary<string, object?> parameters)
    {
        if (sql.Length > 1_000_000) throw Error();
        var fragment = new TSql160Parser(true).Parse(new StringReader(sql), out var errors);
        if (errors.Count != 0 || fragment is not TSqlScript script || script.Batches.Count != 1 ||
            script.Batches[0].Statements.Count != 1 || script.Batches[0].Statements[0] is not SelectStatement select ||
            select.Into is not null || select.OptimizerHints.Count != 0)
            throw Error();
        var names = tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (select.WithCtesAndXmlNamespaces is { } withClause)
        {
            if (withClause.XmlNamespaces is not null || withClause.ChangeTrackingContext is not null) throw Error();
            foreach (var cte in withClause.CommonTableExpressions) names.Add(cte.ExpressionName.Value);
        }
        var visitor = new SelectValidator(names, select, parameters);
        select.Accept(visitor);
        var start = select.StartOffset;
        var text = sql.Substring(start, select.FragmentLength);
        // Translate only lexer tokens. Values, comments and string literals are never interpolated.
        foreach (var token in select.ScriptTokenStream.Where(token => token.Offset >= start && token.Offset < start + select.FragmentLength))
        {
            if (visitor.Edits.Any(edit => token.Offset >= edit.Offset && token.Offset + token.Text.Length <= edit.Offset + edit.Length)) continue;
            if (token.TokenType == TSqlTokenType.QuotedIdentifier && token.Text.StartsWith('['))
                visitor.Edits.Add((token.Offset, token.Text.Length, Quote(token.Text[1..^1].Replace("]]", "]"))));
            if (token.TokenType == TSqlTokenType.Variable)
            {
                if (token.Text.StartsWith("@@", StringComparison.Ordinal) || !parameters.ContainsKey(token.Text)) throw Error();
                visitor.Variables.Add(token.Text[1..]);
                visitor.Edits.Add((token.Offset, token.Text.Length, "$" + token.Text[1..]));
            }
        }
        string Render(TSqlFragment part)
        {
            var result = sql.Substring(part.StartOffset, part.FragmentLength);
            foreach (var edit in visitor.Edits.Where(edit => edit.Offset >= part.StartOffset && edit.Offset + edit.Length <= part.StartOffset + part.FragmentLength).OrderByDescending(edit => edit.Offset))
                result = result.Remove(edit.Offset - part.StartOffset, edit.Length).Insert(edit.Offset - part.StartOffset, edit.Text);
            return result;
        }
        text = Render(select);
        text = text.TrimEnd().TrimEnd(';');
        if (visitor.Top is not null) text += "\nLIMIT " + visitor.Top.Value.ToString(CultureInfo.InvariantCulture);
        return new(text, visitor.Variables, visitor.NeedsAllColumns ? null : visitor.Columns,
            QvdReadPlanner.Build(select, tables, parameters, Render));
    }

    internal static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static InvalidOperationException Error() => QvdReportError.Create(
        "QVD palaiko viena SELECT / CTE su deklaruotais saltiniais. Keitimo komandos, SELECT INTO, isoriniai saltiniai ir nepalaikomos SQL funkcijos neleidziami.",
        "QVD supports one SELECT / CTE using declared sources. Modification commands, SELECT INTO, external sources and unsupported SQL functions are not allowed.");

    private sealed class SelectValidator(HashSet<string> tables, SelectStatement statement, IReadOnlyDictionary<string, object?> parameters) : TSqlFragmentVisitor
    {
        private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            "SUM", "COUNT", "AVG", "MIN", "MAX", "ABS", "ROUND", "FLOOR", "CEILING", "UPPER", "LOWER", "LTRIM", "RTRIM", "TRIM",
            "REPLACE", "CONCAT", "SUBSTRING", "LEFT", "RIGHT", "LENGTH", "LEN", "ISNULL", "COUNT_BIG", "YEAR", "MONTH", "DAY"
        };
        public List<(int Offset, int Length, string Text)> Edits { get; } = [];
        public HashSet<string> Variables { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool NeedsAllColumns { get; private set; }
        public int? Top { get; private set; }

        public override void ExplicitVisit(SelectStarExpression node)
        {
            NeedsAllColumns = true;
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (node.ColumnType == ColumnType.Regular && node.MultiPartIdentifier?.Identifiers.LastOrDefault() is { } column)
                Columns.Add(column.Value);
            base.ExplicitVisit(node);
        }

        public override void Visit(TSqlFragment node)
        {
            if (node is TableReference && node is not NamedTableReference and not QualifiedJoin and not UnqualifiedJoin
                and not JoinParenthesisTableReference and not QueryDerivedTable and not SchemaObjectFunctionTableReference and not GlobalFunctionTableReference)
                throw Error();
            if (node is SelectSetVariable or ConvertCall or TryConvertCall or NextValueForExpression or GlobalVariableExpression or ForClause)
                throw Error();
            base.Visit(node);
        }
        public override void ExplicitVisit(NamedTableReference node)
        {
            if (node.SchemaObject.Identifiers.Count != 1 || !tables.Contains(node.SchemaObject.BaseIdentifier.Value) || node.TableHints.Count != 0) throw Error();
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(SchemaObjectFunctionTableReference node)
        {
            if (node.SchemaObject.Identifiers.Count != 1 || !node.SchemaObject.BaseIdentifier.Value.Equals("STRING_SPLIT", StringComparison.OrdinalIgnoreCase)) throw Error();
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(GlobalFunctionTableReference node)
        {
            if (!node.Name.Value.Equals("STRING_SPLIT", StringComparison.OrdinalIgnoreCase)) throw Error();
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(FunctionCall node)
        {
            if (node.CallTarget is not null || !Functions.Contains(node.FunctionName.Value)) throw Error();
            var replacement = node.FunctionName.Value.ToUpperInvariant() switch { "LEN" => "LENGTH", "ISNULL" => "COALESCE", "COUNT_BIG" => "COUNT", _ => null };
            if (replacement is not null) Edits.Add((node.FunctionName.StartOffset, node.FunctionName.FragmentLength, replacement));
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(QuerySpecification node)
        {
            if (node.TopRowFilter is { } top)
            {
                if (!ReferenceEquals(node, statement.QueryExpression) || top.Percent || top.WithTies) throw Error();
                var raw = top.Expression switch
                {
                    IntegerLiteral number => number.Value,
                    ParenthesisExpression { Expression: IntegerLiteral number } => number.Value,
                    VariableReference variable when parameters.TryGetValue(variable.Name, out var value) => Convert.ToString(value, CultureInfo.InvariantCulture),
                    _ => null
                };
                if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var count)) throw Error();
                Top = count;
                Edits.Add((top.StartOffset, top.FragmentLength, ""));
            }
            base.ExplicitVisit(node);
        }
    }
}
