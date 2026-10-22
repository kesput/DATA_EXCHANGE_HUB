using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DATA_EXCHANGE_HUB.Services;

public static class IrisSelectPolicy
{
    public static void Validate(string sql) => Prepare(sql);

    public static string Prepare(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 1_000_000)
            throw new InvalidOperationException("Iris.Preview.InvalidSql");
        var fragment = new TSql160Parser(true).Parse(new StringReader(sql), out var errors);
        if (errors.Count != 0) throw new InvalidOperationException("Iris.Preview.SyntaxError");
        if (fragment is not TSqlScript script || script.Batches.Count != 1 ||
            script.Batches[0].Statements.Count == 0 || !script.Batches[0].Statements.All(Allowed))
            throw new InvalidOperationException("Iris.Preview.SelectOnly");
        var visitor = new ReadOnlyVisitor();
        fragment.Accept(visitor);
        if (visitor.Rejected) throw new InvalidOperationException("Iris.Preview.SelectOnly");
        // Both # and ## names get a private local SQL-session name. No submitted
        // global name can touch another session's global temporary table.
        var isolate = new TempNameVisitor();
        fragment.Accept(isolate);
        if (!isolate.Changed) return sql;
        new Sql160ScriptGenerator().GenerateScript(fragment, out var executable);
        return executable;
    }

    private static bool IsTemp(SchemaObjectName? name) => name is not null && name.Identifiers.Count == 1 &&
        name.BaseIdentifier.Value.StartsWith("#", StringComparison.Ordinal) && name.BaseIdentifier.Value.Trim('#').Length > 0;
    private static bool Target(TableReference? target) => target is NamedTableReference named && IsTemp(named.SchemaObject);
    private static bool Allowed(TSqlStatement statement) => statement switch
    {
        SelectStatement select => select.Into is null || IsTemp(select.Into),
        CreateTableStatement create => IsTemp(create.SchemaObjectName),
        DropTableStatement drop => drop.Objects.Count > 0 && drop.Objects.All(IsTemp),
        InsertStatement insert => Target(insert.InsertSpecification.Target),
        UpdateStatement update => Target(update.UpdateSpecification.Target),
        DeleteStatement delete => Target(delete.DeleteSpecification.Target),
        TruncateTableStatement truncate => IsTemp(truncate.TableName),
        BeginEndBlockStatement block => block.StatementList.Statements.All(Allowed),
        IfStatement conditional => Allowed(conditional.ThenStatement) &&
            (conditional.ElseStatement is null || Allowed(conditional.ElseStatement)),
        _ => false
    };

    private sealed class TempNameVisitor : TSqlFragmentVisitor
    {
        private readonly string prefix = "#iris_" + Guid.NewGuid().ToString("N") + "_";
        private readonly Dictionary<string,string> names = new(StringComparer.OrdinalIgnoreCase);
        public bool Changed => names.Count > 0;
        private string Name(string original)
        {
            if (!names.TryGetValue(original, out var mapped)) names[original] = mapped = prefix + names.Count;
            return mapped;
        }
        public override void Visit(SchemaObjectName node)
        {
            if (IsTemp(node)) node.BaseIdentifier.Value = Name(node.BaseIdentifier.Value);
        }
        public override void Visit(ColumnReferenceExpression node)
        {
            var parts = node.MultiPartIdentifier?.Identifiers;
            if (parts is null) return;
            foreach (var part in parts.Take(Math.Max(0, parts.Count - 1)))
                if (part.Value.StartsWith("#")) part.Value = Name(part.Value);
        }
        public override void Visit(SelectStarExpression node)
        {
            if (node.Qualifier is null) return;
            foreach (var part in node.Qualifier.Identifiers)
                if (part.Value.StartsWith("#")) part.Value = Name(part.Value);
        }
        public override void Visit(FunctionCall node)
        {
            if (!node.FunctionName.Value.Equals("OBJECT_ID", StringComparison.OrdinalIgnoreCase) ||
                node.Parameters.FirstOrDefault() is not StringLiteral literal) return;
            var name = literal.Value;
            if (name.StartsWith("tempdb..", StringComparison.OrdinalIgnoreCase)) name = name[8..];
            name = name.Trim('[', ']');
            if (name.StartsWith("#") && !name.Contains('.')) literal.Value = "tempdb.." + Name(name);
        }
    }
    private sealed class ReadOnlyVisitor : TSqlFragmentVisitor
    {
        public bool Rejected { get; private set; }
        public override void Visit(SelectStatement node) { if (node.Into is not null && !IsTemp(node.Into)) Rejected = true; }
        public override void Visit(ExecuteSpecification node) => Rejected = true;
        public override void Visit(OutputIntoClause node) => Rejected = true;
        public override void Visit(NamedTableReference node) { if (node.SchemaObject.BaseIdentifier.Value.StartsWith("#") && !IsTemp(node.SchemaObject)) Rejected = true; }
        public override void Visit(NextValueForExpression node) => Rejected = true;
        public override void Visit(SelectSetVariable node) => Rejected = true;
        public override void Visit(OpenQueryTableReference node) => Rejected = true;
        public override void Visit(OpenRowsetTableReference node) => Rejected = true;
        public override void Visit(OpenRowsetCosmos node) => Rejected = true;
        public override void Visit(BulkOpenRowset node) => Rejected = true;
        public override void Visit(DataModificationTableReference node) => Rejected = true;
        public override void Visit(AdHocTableReference node) => Rejected = true;
        public override void Visit(AdHocDataSource node) => Rejected = true;
        // Custom SQL/CLR functions may conceal external actions. Built-in scalar
        // functions, joins, CTEs, subqueries and aggregates remain available.
        public override void Visit(SchemaObjectFunctionTableReference node) => Rejected = true;
        public override void Visit(FunctionCall node) { if (node.CallTarget is not null) Rejected = true; }
    }
}
