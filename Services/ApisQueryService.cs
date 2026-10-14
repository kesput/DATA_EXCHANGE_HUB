using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ApisQueryService(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    IOptions<ApisOptions> options) : IApisQueryService
{
    private const string QueryRoot = "Sql/Apis";
    private const string TextCommandType = "Text";
    private const string ProcedureCommandType = "Procedure";
    private const string OptionalFilterPlaceholder = "/* SQL_PARAM_FILTERS */";
    private readonly ApisOptions _options = options.Value;

    public IReadOnlyList<ApisQueryDefinition> ListQueries() => ListQueries(null);

    public IReadOnlyList<ApisQueryDefinition> ListQueries(IReadOnlySet<string>? countries)
    {
        var rootPath = Path.Combine(environment.ContentRootPath, QueryRoot);
        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(rootPath, "*.sql", SearchOption.AllDirectories)
            .Where(filePath => IsAllowedCountryPath(rootPath, filePath, countries))
            .Select(filePath => BuildDefinition(rootPath, filePath))
            .OrderBy(query => GetAreaOrder(query.Area))
            .ThenBy(query => query.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ApisQueryResult> ExecuteAsync(
        string queryId,
        IReadOnlyList<ApisParameterValue>? parameters = null,
        IReadOnlySet<string>? countries = null,
        CancellationToken cancellationToken = default)
    {
        var query = ListQueries(countries)
            .FirstOrDefault(item => item.Id.Equals(queryId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("APIS SQL uzklausa nerasta.");

        var queryPath = Path.Combine(environment.ContentRootPath, query.RelativePath);
        var sql = await File.ReadAllTextAsync(queryPath, cancellationToken);
        var connectionString = configuration.GetConnectionString(query.ConnectionName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{query.ConnectionName}' nesukonfiguruota.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = _options.CommandTimeoutSeconds is > 0 and <= 3600
            ? _options.CommandTimeoutSeconds
            : 180;
        command.CommandText = query.CommandType.Equals(ProcedureCommandType, StringComparison.OrdinalIgnoreCase)
            ? BuildProcedureCommand(command, query, parameters)
            : BuildTextCommand(command, sql, query, parameters);

        using var cancellationRegistration = cancellationToken.Register(command.Cancel);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyDictionary<string, string?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                var rawValue = reader[column];
                values[column] = rawValue == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(rawValue, CultureInfo.CurrentCulture);
            }

            rows.Add(values);
        }

        return new ApisQueryResult(columns, rows);
    }

    private static bool IsAllowedCountryPath(
        string rootPath,
        string filePath,
        IReadOnlySet<string>? countries)
    {
        if (countries is null)
        {
            return true;
        }

        var relativeToRoot = Path.GetRelativePath(rootPath, filePath);
        var parts = relativeToRoot.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 0 && countries.Contains(parts[0]);
    }
    private static ApisQueryDefinition BuildDefinition(string rootPath, string filePath)
    {
        var relativeToRoot = Path.GetRelativePath(rootPath, filePath);
        var parts = relativeToRoot.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var connectionName = parts.Length > 1 ? parts[1] : string.Empty;
        var area = parts.Length > 2 ? parts[2] : string.Empty;
        var name = Path.GetFileNameWithoutExtension(filePath);
        var relativePath = Path.Combine(QueryRoot, relativeToRoot);
        var id = relativePath.Replace('\\', '/');
        var procedureName = ReadSingleMetadataValue(filePath, "-- SQL_PROCEDURE:", "-- APIS_PROCEDURE:");
        var description = ReadSingleMetadataValue(filePath, "-- SQL_DESCRIPTION:", "-- APIS_DESCRIPTION:") ?? string.Empty;
        var viewMode = ReadSingleMetadataValue(filePath, "-- SQL_VIEW:", "-- APIS_VIEW:") ?? string.Empty;
        var parameters = ReadParameterDefinitions(filePath);
        var commandType = string.IsNullOrWhiteSpace(procedureName) ? TextCommandType : ProcedureCommandType;

        return new ApisQueryDefinition(
            id,
            name,
            connectionName,
            area,
            relativePath,
            commandType,
            procedureName,
            description,
            parameters);
    }

    private static string BuildTextCommand(
        SqlCommand command,
        string sql,
        ApisQueryDefinition query,
        IReadOnlyList<ApisParameterValue>? parameters)
    {
        AddNamedParameters(command, query, parameters);
        return InjectOptionalParameterFilters(sql, query, parameters);
    }

    private static string BuildProcedureCommand(
        SqlCommand command,
        ApisQueryDefinition query,
        IReadOnlyList<ApisParameterValue>? parameters)
    {
        if (string.IsNullOrWhiteSpace(query.ProcedureName))
        {
            throw new InvalidOperationException("APIS proceduros failui truksta SQL_PROCEDURE metaduomens.");
        }

        parameters ??= [];
        var values = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
        var parameterNames = new List<string>();

        foreach (var definition in query.Parameters)
        {
            var rawValue = ResolveParameterValue(values, definition);
            var sqlParameterName = $"@apisParam{parameterNames.Count}";
            command.Parameters.Add(sqlParameterName, GetSqlDbType(definition)).Value = ConvertParameterValue(definition, rawValue);
            parameterNames.Add(sqlParameterName);
        }

        return parameterNames.Count == 0
            ? $"EXEC {query.ProcedureName}"
            : $"EXEC {query.ProcedureName} {string.Join(", ", parameterNames)}";
    }

    private static void AddNamedParameters(
        SqlCommand command,
        ApisQueryDefinition query,
        IReadOnlyList<ApisParameterValue>? parameters)
    {
        if (query.Parameters.Count == 0)
        {
            return;
        }

        parameters ??= [];
        var values = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
        var operators = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Operator, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in query.Parameters)
        {
            var rawValue = ResolveParameterValue(values, definition);
            var normalizedOperator = ResolveParameterOperator(operators, definition);
            command.Parameters.Add(GetSqlParameterName(definition.Name), GetSqlDbType(definition)).Value = ConvertParameterValue(definition, rawValue);
            command.Parameters.Add(GetOperatorParameterName(definition.Name), SqlDbType.NVarChar, 20).Value = normalizedOperator;
        }
    }

    private static string InjectOptionalParameterFilters(
        string sql,
        ApisQueryDefinition query,
        IReadOnlyList<ApisParameterValue>? parameters)
    {
        if (!sql.Contains(OptionalFilterPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            return sql;
        }

        parameters ??= [];
        var values = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name) && !string.IsNullOrWhiteSpace(parameter.Value))
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
        var operators = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Operator, StringComparer.OrdinalIgnoreCase);
        var connectors = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Connector, StringComparer.OrdinalIgnoreCase);
        var clauses = new List<string>();

        foreach (var definition in query.Parameters.Where(parameter => !parameter.IsRequired && !string.IsNullOrWhiteSpace(parameter.FilterExpression)))
        {
            if (!values.TryGetValue(definition.Name, out var rawValue) || string.IsNullOrWhiteSpace(rawValue) || IsNullLiteral(rawValue))
            {
                continue;
            }

            var normalizedOperator = ResolveParameterOperator(operators, definition);
            var parameterName = GetSqlParameterName(definition.Name);
            var expression = definition.FilterExpression!;

            if (normalizedOperator.Equals(ExchangeTableFilter.InOperator, StringComparison.OrdinalIgnoreCase))
            {
                var inConnectorSql = clauses.Count == 0 ? string.Empty : ResolveParameterConnector(connectors, definition);
                var inClause = $"{expression} IN (SELECT LTRIM(RTRIM([value])) FROM STRING_SPLIT({parameterName}, ',') WHERE LTRIM(RTRIM([value])) <> '')";
                clauses.Add(string.IsNullOrWhiteSpace(inConnectorSql) ? inClause : $"{inConnectorSql} {inClause}");
                continue;
            }

            var operatorSql = normalizedOperator switch
            {
                ExchangeTableFilter.EqualsOperator => "=",
                ExchangeTableFilter.NotEqualsOperator => "<>",
                ExchangeTableFilter.LikeOperator => "LIKE",
                ExchangeTableFilter.GreaterThanOperator => ">",
                ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
                ExchangeTableFilter.LessThanOperator => "<",
                ExchangeTableFilter.LessThanOrEqualOperator => "<=",
                _ => throw new InvalidOperationException("APIS parametrui parinktas nepalaikomas operatorius.")
            };
            var valueExpression = normalizedOperator.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase)
                ? $"CASE WHEN {parameterName} LIKE '%[%_]%' THEN {parameterName} ELSE '%' + {parameterName} + '%' END"
                : parameterName;
            var connectorSql = clauses.Count == 0 ? string.Empty : ResolveParameterConnector(connectors, definition);
            var clause = $"{expression} {operatorSql} {valueExpression}";
            clauses.Add(string.IsNullOrWhiteSpace(connectorSql) ? clause : $"{connectorSql} {clause}");
        }

        var replacement = clauses.Count == 0
            ? string.Empty
            : $"AND {string.Join(" ", clauses)}";
        return sql.Replace(OptionalFilterPlaceholder, replacement, StringComparison.OrdinalIgnoreCase);
    }
    private static string? ResolveParameterValue(
        IReadOnlyDictionary<string, string?> values,
        ApisParameterDefinition definition)
    {
        values.TryGetValue(definition.Name, out var rawValue);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            rawValue = definition.DefaultValue;
        }

        if (definition.IsRequired && string.IsNullOrWhiteSpace(rawValue))
        {
            throw new InvalidOperationException($"Truksta privalomo SQL parametro: {definition.Name}.");
        }

        ValidateParameterValue(definition, rawValue);
        return rawValue;
    }

    private static string ResolveParameterOperator(
        IReadOnlyDictionary<string, string> operators,
        ApisParameterDefinition definition)
    {
        operators.TryGetValue(definition.Name, out var rawOperator);
        var normalizedOperator = string.IsNullOrWhiteSpace(rawOperator)
            ? ExchangeTableFilter.EqualsOperator
            : NormalizeParameterOperator(rawOperator);

        if (!definition.AllowedOperators.Contains(normalizedOperator, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Operatorius '{GetOperatorLabel(normalizedOperator)}' negalimas parametrui {definition.Name}.");
        }

        if (normalizedOperator.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase) &&
            !definition.DataType.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"LIKE operatorius galimas tik tekstiniam parametrui: {definition.Name}.");
        }

        return normalizedOperator;
    }

    private static string ResolveParameterConnector(
        IReadOnlyDictionary<string, string> connectors,
        ApisParameterDefinition definition)
    {
        connectors.TryGetValue(definition.Name, out var rawConnector);
        if (rawConnector?.Equals(ExchangeTableFilter.OrConnector, StringComparison.OrdinalIgnoreCase) == true)
        {
            return "OR";
        }

        return "AND";
    }

    private static string GetSqlParameterName(string name)
    {
        return name.StartsWith('@') ? name : $"@{name}";
    }

    private static string GetOperatorParameterName(string name)
    {
        return $"{GetSqlParameterName(name)}_OPERATOR";
    }

    private static string? ReadSingleMetadataValue(string filePath, params string[] markers)
    {
        return File
            .ReadLines(filePath)
            .Select(line => line.Trim())
            .Select(line => markers
                .Where(marker => line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                .Select(marker => line[marker.Length..].Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static IReadOnlyList<ApisParameterDefinition> ReadParameterDefinitions(string filePath)
    {
        return File
            .ReadLines(filePath)
            .Select(line => line.Trim())
            .Select(TryReadParameterLine)
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .Select(item => new
            {
                item.IsRequired,
                Parts = item.Definition.Split('|', 5, StringSplitOptions.TrimEntries)
            })
            .Where(item => item.Parts.Length >= 1 && !string.IsNullOrWhiteSpace(item.Parts[0]))
            .Select(item =>
            {
                var rules = ParseParameterRules(item.Parts.Length >= 5 ? item.Parts[4] : string.Empty);
                return new ApisParameterDefinition(
                    item.Parts[0],
                    item.IsRequired,
                    item.Parts.Length >= 2 ? NormalizeParameterDataType(item.Parts[1]) : "text",
                    item.Parts.Length >= 3 ? NormalizeDefaultValue(item.Parts[2]) : null,
                    item.Parts.Length >= 4 ? item.Parts[3] : string.Empty,
                    rules.AllowedValues,
                    rules.Pattern,
                    rules.PatternMessage,
                    rules.AllowedOperators,
                    rules.FilterExpression,
                    rules.OutputFormat);
            })
            .ToArray();
    }

    private static (bool IsRequired, string Definition)? TryReadParameterLine(string line)
    {
        foreach (var marker in new[] { "-- SQL_PARAM_MUST:", "-- APIS_PARAM_MUST:" })
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (true, line[marker.Length..].Trim());
            }
        }

        foreach (var marker in new[] { "-- SQL_PARAM:", "-- APIS_PARAM:" })
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (false, line[marker.Length..].Trim());
            }
        }

        return null;
    }

    private static ApisParameterRules ParseParameterRules(string rulesText)
    {
        if (string.IsNullOrWhiteSpace(rulesText))
        {
            return new ApisParameterRules([], null, null, [], null, null);
        }

        var rules = rulesText
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(rule => rule.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        var allowedValues = rules.TryGetValue("values", out var values)
            ? values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeRuleValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        var operatorsText = rules.TryGetValue("operators", out var operators)
            ? operators
            : rules.TryGetValue("ops", out var ops) ? ops : string.Empty;
        var filterExpression = rules.TryGetValue("expression", out var expression)
            ? expression
            : rules.TryGetValue("expr", out var expr)
                ? expr
                : rules.TryGetValue("column", out var column) ? column : null;

        return new ApisParameterRules(
            allowedValues,
            rules.TryGetValue("regex", out var pattern) ? pattern : null,
            rules.TryGetValue("message", out var message) ? message : null,
            NormalizeAllowedOperators(operatorsText),
            string.IsNullOrWhiteSpace(filterExpression) ? null : filterExpression,
            rules.TryGetValue("format", out var outputFormat) ? outputFormat : null);
    }

    private static void ValidateParameterValue(ApisParameterDefinition definition, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue) || IsNullLiteral(rawValue))
        {
            if (definition.AllowedValues.Count > 0 && !definition.AllowedValues.Contains("NULL", StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Parametro {definition.Name} reiksme turi buti viena is: {string.Join(", ", definition.AllowedValues)}.");
            }

            return;
        }

        var value = rawValue.Trim();
        if (definition.AllowedValues.Count > 0 && !definition.AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Parametro {definition.Name} reiksme turi buti viena is: {string.Join(", ", definition.AllowedValues)}.");
        }

        if (!string.IsNullOrWhiteSpace(definition.Pattern) &&
            !Regex.IsMatch(value, definition.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(definition.PatternMessage)
                ? $"Parametro {definition.Name} reiksme neatitinka taisykles."
                : definition.PatternMessage);
        }
    }

    private static string? NormalizeDefaultValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) || IsNullLiteral(value) ? null : value;
    }

    private static string NormalizeRuleValue(string value)
    {
        return IsNullLiteral(value) ? "NULL" : value;
    }

    private static string NormalizeParameterOperator(string parameterOperator)
    {
        return parameterOperator.Trim().ToLowerInvariant() switch
        {
            "=" or "eq" => ExchangeTableFilter.EqualsOperator,
            "!=" or "<>" or "ne" => ExchangeTableFilter.NotEqualsOperator,
            "like" => ExchangeTableFilter.LikeOperator,
            "in" => ExchangeTableFilter.InOperator,
            ">" or "gt" => ExchangeTableFilter.GreaterThanOperator,
            ">=" or "gte" => ExchangeTableFilter.GreaterThanOrEqualOperator,
            "<" or "lt" => ExchangeTableFilter.LessThanOperator,
            "<=" or "lte" => ExchangeTableFilter.LessThanOrEqualOperator,
            _ => ExchangeTableFilter.EqualsOperator
        };
    }

    private static IReadOnlyList<string> NormalizeAllowedOperators(string operators)
    {
        var normalizedOperators = operators
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeParameterOperator)
            .Where(operatorName => operatorName is
                ExchangeTableFilter.EqualsOperator or
                ExchangeTableFilter.NotEqualsOperator or
                ExchangeTableFilter.LikeOperator or
                ExchangeTableFilter.InOperator or
                ExchangeTableFilter.GreaterThanOperator or
                ExchangeTableFilter.GreaterThanOrEqualOperator or
                ExchangeTableFilter.LessThanOperator or
                ExchangeTableFilter.LessThanOrEqualOperator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalizedOperators.Length > 0
            ? normalizedOperators
            : [ExchangeTableFilter.EqualsOperator];
    }

    private static string NormalizeParameterDataType(string dataType)
    {
        var normalized = dataType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "int" or "integer" or "bigint" => "int",
            "bit" or "bool" or "boolean" => "bit",
            "decimal" or "number" or "numeric" or "money" => "decimal",
            "date" => "date",
            "datetime" or "datetime2" => "datetime",
            _ => "text"
        };
    }

    private static string GetOperatorLabel(string parameterOperator)
    {
        return parameterOperator switch
        {
            ExchangeTableFilter.EqualsOperator => "=",
            ExchangeTableFilter.NotEqualsOperator => "!=",
            ExchangeTableFilter.LikeOperator => "LIKE",
            ExchangeTableFilter.InOperator => "IN",
            ExchangeTableFilter.GreaterThanOperator => ">",
            ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
            ExchangeTableFilter.LessThanOperator => "<",
            ExchangeTableFilter.LessThanOrEqualOperator => "<=",
            _ => parameterOperator
        };
    }

    private static SqlDbType GetSqlDbType(ApisParameterDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.OutputFormat))
        {
            return SqlDbType.NVarChar;
        }

        return definition.DataType switch
        {
            "int" => SqlDbType.BigInt,
            "bit" => SqlDbType.Bit,
            "decimal" => SqlDbType.Decimal,
            "date" => SqlDbType.Date,
            "datetime" => SqlDbType.DateTime2,
            _ => SqlDbType.NVarChar
        };
    }

    private static object ConvertParameterValue(ApisParameterDefinition definition, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue) || IsNullLiteral(rawValue))
        {
            return DBNull.Value;
        }

        var value = rawValue.Trim();
        return definition.DataType switch
        {
            "int" => long.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var integerValue) ||
                     long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue)
                ? integerValue
                : throw new InvalidOperationException($"Parametro {definition.Name} reiksme turi buti sveikas skaicius."),
            "bit" => TryParseBit(value, out var bitValue)
                ? bitValue
                : throw new InvalidOperationException($"Parametro {definition.Name} reiksme turi buti 0/1 arba true/false."),
            "decimal" => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var decimalValue) ||
                         decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimalValue)
                ? decimalValue
                : throw new InvalidOperationException($"Parametro {definition.Name} reiksme turi buti skaicius."),
            "date" => TryParseDate(value, out var dateValue)
                ? FormatTemporalValue(definition, dateValue)
                : throw new InvalidOperationException($"Parametro {definition.Name} reiksme turi buti data."),
            "datetime" => TryParseDateTime(value, out var dateTimeValue)
                ? FormatTemporalValue(definition, dateTimeValue)
                : throw new InvalidOperationException($"Parametro {definition.Name} reiksme turi buti data ir laikas."),
            _ => value
        };
    }

    private static object FormatTemporalValue(ApisParameterDefinition definition, DateTime value)
    {
        return string.IsNullOrWhiteSpace(definition.OutputFormat)
            ? value
            : value.ToString(definition.OutputFormat, CultureInfo.InvariantCulture);
    }

    private static bool TryParseDate(string value, out DateTime result)
    {
        return DateTime.TryParseExact(value, ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ||
               DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out result) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    private static bool TryParseDateTime(string value, out DateTime result)
    {
        return DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ||
               DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out result) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    private static bool TryParseBit(string value, out bool result)
    {
        if (bool.TryParse(value, out result))
        {
            return true;
        }

        if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        return false;
    }

    private static bool IsNullLiteral(string value)
    {
        return value.Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetAreaOrder(string area)
    {
        return area.ToUpperInvariant() switch
        {
            "ANALYTICS" => 0,
            "PROCESSES" => 1,
            "INDICATORS" => 2,
            "STATISTICS" => 3,
            _ => 99
        };
    }

    private sealed record ApisParameterRules(
        IReadOnlyList<string> AllowedValues,
        string? Pattern,
        string? PatternMessage,
        IReadOnlyList<string> AllowedOperators,
        string? FilterExpression,
        string? OutputFormat);
}

