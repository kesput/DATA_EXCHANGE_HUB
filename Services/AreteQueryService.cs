using System.Data;
using System.Globalization;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class AreteQueryService(
    IConfiguration configuration,
    IWebHostEnvironment environment) : IAreteQueryService
{
    private const string QueryRoot = "Sql/Arete";
    private const int DefaultCommandTimeoutSeconds = 180;
    private const string OptionalFilterPlaceholder = "/* SQL_PARAM_FILTERS */";

    public IReadOnlyList<AreteQueryDefinition> ListQueries() => ListQueries(null);

    public IReadOnlyList<AreteQueryDefinition> ListQueries(IReadOnlySet<string>? countries)
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
            .OrderBy(query => query.ConnectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(query => query.Area, StringComparer.OrdinalIgnoreCase)
            .ThenBy(query => query.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AreteQueryResult> ExecuteAsync(
        string queryId,
        IReadOnlyList<ExchangeTableFilter>? filters = null,
        IReadOnlySet<string>? countries = null,
        CancellationToken cancellationToken = default)
    {
        var query = ListQueries(countries)
            .FirstOrDefault(item => item.Id.Equals(queryId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("ARETE SQL uzklausa nerasta.");

        var queryPath = Path.Combine(environment.ContentRootPath, query.RelativePath);
        var sql = await File.ReadAllTextAsync(queryPath, cancellationToken);
        var connectionString = configuration.GetConnectionString(query.ConnectionName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{query.ConnectionName}' nesukonfiguruota.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = GetCommandTimeoutSeconds(configuration);
        command.CommandText = BuildSqlWithFilters(command, sql, query.Filters, filters);

        using var cancellationRegistration = cancellationToken.Register(command.Cancel);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = Enumerable
            .Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
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

        return new AreteQueryResult(columns, rows);
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
    private static AreteQueryDefinition BuildDefinition(string rootPath, string filePath)
    {
        var relativeToRoot = Path.GetRelativePath(rootPath, filePath);
        var parts = relativeToRoot.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var connectionName = parts.Length > 1 ? parts[1] : string.Empty;
        var area = parts.Length > 2 ? parts[2] : string.Empty;
        var name = Path.GetFileNameWithoutExtension(filePath);
        var relativePath = Path.Combine(QueryRoot, relativeToRoot);
        var id = relativePath.Replace('\\', '/');
        var description = ReadSingleMetadataValue(filePath, "-- SQL_DESCRIPTION:", "-- ARETE_DESCRIPTION:") ?? string.Empty;
        var filters = ReadFilterDefinitions(filePath);

        return new AreteQueryDefinition(
            id,
            name,
            connectionName,
            area,
            relativePath,
            description,
            filters);
    }

    private static string? ReadSingleMetadataValue(string filePath, params string[] markers)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            foreach (var marker in markers)
            {
                if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[marker.Length..].Trim();
                }
            }
        }

        return null;
    }
    private static IReadOnlyList<AreteFilterDefinition> ReadFilterDefinitions(string filePath)
    {
        return File
            .ReadLines(filePath)
            .Select(line => line.Trim())
            .Select(TryReadDefinitionLine)
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .Select(item => item.IsQueryParameter
                ? BuildParameterDefinition(item.IsRequired, item.Definition)
                : BuildFilterDefinition(item.IsRequired, item.Definition))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    private static AreteFilterDefinition? BuildFilterDefinition(bool isRequired, string definition)
    {
        var parts = definition.Split('|', 5, StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return null;
        }

        var dataType = parts.Length >= 3 ? NormalizeFilterDataType(parts[2]) : "text";
        return new AreteFilterDefinition(
            parts[0],
            parts[1],
            isRequired,
            dataType,
            NormalizeAllowedOperators(parts.Length >= 4 ? parts[3] : string.Empty, dataType),
            Description: parts.Length >= 5 ? parts[4] : string.Empty);
    }

    private static AreteFilterDefinition? BuildParameterDefinition(bool isRequired, string definition)
    {
        var parts = definition.Split('|', 5, StringSplitOptions.TrimEntries);
        if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return null;
        }

        var dataType = parts.Length >= 2 ? NormalizeFilterDataType(parts[1]) : "text";
        var description = parts.Length >= 4 ? parts[3] : string.Empty;
        var rules = ParseParameterRules(parts.Length >= 5 ? parts[4] : string.Empty, dataType);
        if (!isRequired && !string.IsNullOrWhiteSpace(rules.FilterExpression))
        {
            return new AreteFilterDefinition(
                parts[0],
                rules.FilterExpression,
                false,
                dataType,
                rules.AllowedOperators,
                Description: description);
        }

        return new AreteFilterDefinition(
            parts[0],
            parts[0],
            isRequired,
            dataType,
            [ExchangeTableFilter.EqualsOperator],
            IsQueryParameter: true,
            OutputFormat: rules.OutputFormat,
            Description: description);
    }

    private static (bool IsRequired, bool IsQueryParameter, string Definition)? TryReadDefinitionLine(string line)
    {
        foreach (var marker in new[] { "-- SQL_PARAM_MUST:", "-- ARETE_PARAM_MUST:" })
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (true, true, line[marker.Length..].Trim());
            }
        }

        foreach (var marker in new[] { "-- SQL_PARAM:", "-- ARETE_PARAM:" })
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (false, true, line[marker.Length..].Trim());
            }
        }

        foreach (var marker in new[] { "-- SQL_FILTER_MUST:", "-- ARETE_FILTER_MUST:", "-- HYDRA_FILTER_MUST:" })
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (true, false, line[marker.Length..].Trim());
            }
        }

        foreach (var marker in new[] { "-- SQL_FILTER:", "-- ARETE_FILTER:", "-- HYDRA_FILTER:" })
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (false, false, line[marker.Length..].Trim());
            }
        }

        return null;
    }

    private static AreteParameterRules ParseParameterRules(string rulesText, string dataType)
    {
        if (string.IsNullOrWhiteSpace(rulesText))
        {
            return new AreteParameterRules(null, [ExchangeTableFilter.EqualsOperator], null);
        }

        var rules = rulesText
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(rule => rule.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        var operatorsText = rules.TryGetValue("operators", out var operators)
            ? operators
            : rules.TryGetValue("ops", out var ops) ? ops : string.Empty;
        var filterExpression = rules.TryGetValue("expression", out var expression)
            ? expression
            : rules.TryGetValue("expr", out var expr)
                ? expr
                : rules.TryGetValue("column", out var column) ? column : null;

        return new AreteParameterRules(
            rules.TryGetValue("format", out var outputFormat) ? outputFormat : null,
            NormalizeAllowedOperators(operatorsText, dataType),
            string.IsNullOrWhiteSpace(filterExpression) ? null : filterExpression);
    }

    private static string BuildSqlWithFilters(
        SqlCommand command,
        string sql,
        IReadOnlyList<AreteFilterDefinition> filterDefinitions,
        IReadOnlyList<ExchangeTableFilter>? filters)
    {
        filters ??= [];
        ValidateRequiredFilters(filterDefinitions, filters);

        var activeFilters = filters.Where(filter => filter.IsActive).ToArray();
        if (activeFilters.Length == 0)
        {
            return sql.Replace(OptionalFilterPlaceholder, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        var allowedFilters = filterDefinitions.ToDictionary(
            filter => filter.Name,
            StringComparer.OrdinalIgnoreCase);
        var clauses = new List<string>();
        var parameterIndex = 0;

        foreach (var filter in activeFilters)
        {
            if (!allowedFilters.TryGetValue(filter.ColumnName, out var definition))
            {
                throw new InvalidOperationException($"Netinkamas ARETE filtro laukas: {filter.ColumnName}");
            }

            if (definition.IsQueryParameter)
            {
                AddQueryParameter(command, definition, filter.Value);
                continue;
            }

            var connectorSql = clauses.Count == 0
                ? string.Empty
                : GetFilterConnectorSql(filter.Connector);
            var clause = BuildFilterClause(command, definition, filter, ref parameterIndex);
            clauses.Add(string.IsNullOrWhiteSpace(connectorSql) ? clause : $"{connectorSql} {clause}");
        }

        if (clauses.Count == 0)
        {
            return sql.Replace(OptionalFilterPlaceholder, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        var filterSql = $"AND {string.Join(" ", clauses)}";
        return sql.Contains(OptionalFilterPlaceholder, StringComparison.OrdinalIgnoreCase)
            ? sql.Replace(OptionalFilterPlaceholder, filterSql, StringComparison.OrdinalIgnoreCase)
            : $"{sql.TrimEnd().TrimEnd(';')}{Environment.NewLine}{filterSql}";
    }

    private static void ValidateRequiredFilters(
        IReadOnlyList<AreteFilterDefinition> filterDefinitions,
        IReadOnlyList<ExchangeTableFilter> filters)
    {
        var activeFilterNames = filters
            .Where(filter => filter.IsActive)
            .Select(filter => filter.ColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingFilters = filterDefinitions
            .Where(filter => filter.IsRequired && !activeFilterNames.Contains(filter.Name))
            .Select(filter => filter.Name)
            .ToArray();

        if (missingFilters.Length > 0)
        {
            throw new InvalidOperationException(
                $"Truksta privalomu ARETE parametro/filtro reiksmiu: {string.Join(", ", missingFilters)}.");
        }
    }

    private static void AddQueryParameter(SqlCommand command, AreteFilterDefinition definition, string rawValue)
    {
        command.Parameters.Add(definition.Name, GetSqlDbType(definition)).Value = ConvertFilterValue(definition, rawValue);
    }

    private static string BuildFilterClause(
        SqlCommand command,
        AreteFilterDefinition definition,
        ExchangeTableFilter filter,
        ref int parameterIndex)
    {
        var normalizedOperator = NormalizeFilterOperator(filter.Operator);
        if (!definition.AllowedOperators.Contains(normalizedOperator, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Operatorius '{GetOperatorLabel(normalizedOperator)}' negalimas filtrui {definition.Name}.");
        }

        if (normalizedOperator.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase) &&
            !definition.DataType.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"LIKE operatorius galimas tik tekstiniam filtrui: {definition.Name}.");
        }

        if (normalizedOperator.Equals(ExchangeTableFilter.InOperator, StringComparison.OrdinalIgnoreCase))
        {
            var parameterNames = new List<string>();
            foreach (var value in SplitFilterValues(filter.Value))
            {
                parameterNames.Add(AddFilterParameter(command, definition, ref parameterIndex, value));
            }

            if (parameterNames.Count == 0)
            {
                throw new InvalidOperationException("IN filtrui reikia bent vienos reiksmes.");
            }

            return $"{definition.Expression} IN ({string.Join(", ", parameterNames)})";
        }

        var parameterName = AddFilterParameter(
            command,
            definition,
            ref parameterIndex,
            normalizedOperator.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase)
                ? NormalizeLikeValue(filter.Value)
                : filter.Value);

        var operatorSql = normalizedOperator switch
        {
            ExchangeTableFilter.EqualsOperator => "=",
            ExchangeTableFilter.NotEqualsOperator => "<>",
            ExchangeTableFilter.LikeOperator => "LIKE",
            ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
            ExchangeTableFilter.LessThanOrEqualOperator => "<=",
            _ => throw new InvalidOperationException("ARETE filtrui parinktas nepalaikomas operatorius.")
        };

        return $"{definition.Expression} {operatorSql} {parameterName}";
    }

    private static string NormalizeFilterOperator(string filterOperator)
    {
        if (filterOperator.Equals(ExchangeTableFilter.NotEqualsOperator, StringComparison.OrdinalIgnoreCase) ||
            filterOperator.Equals("!=", StringComparison.OrdinalIgnoreCase) ||
            filterOperator.Equals("<>", StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.NotEqualsOperator;
        }

        if (filterOperator.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.LikeOperator;
        }

        if (filterOperator.Equals(ExchangeTableFilter.InOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.InOperator;
        }

        if (filterOperator.Equals(ExchangeTableFilter.GreaterThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) ||
            filterOperator.Equals(">=", StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.GreaterThanOrEqualOperator;
        }

        if (filterOperator.Equals(ExchangeTableFilter.LessThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) ||
            filterOperator.Equals("<=", StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.LessThanOrEqualOperator;
        }

        return ExchangeTableFilter.EqualsOperator;
    }

    private static string AddFilterParameter(
        SqlCommand command,
        AreteFilterDefinition definition,
        ref int parameterIndex,
        string rawValue)
    {
        var parameterName = $"@areteFilter{parameterIndex++}";
        command.Parameters.Add(parameterName, GetSqlDbType(definition)).Value =
            ConvertFilterValue(definition, rawValue);
        return parameterName;
    }

    private static string NormalizeFilterDataType(string dataType)
    {
        var normalized = dataType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "int" or "integer" or "bigint" => "int",
            "decimal" or "number" or "numeric" or "money" => "decimal",
            "date" => "date",
            "datetime" or "datetime2" => "datetime",
            _ => "text"
        };
    }

    private static IReadOnlyList<string> NormalizeAllowedOperators(string operators, string dataType)
    {
        var normalizedOperators = operators
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeFilterOperator)
            .Where(filterOperator =>
                filterOperator.Equals(ExchangeTableFilter.EqualsOperator, StringComparison.OrdinalIgnoreCase) ||
                filterOperator.Equals(ExchangeTableFilter.NotEqualsOperator, StringComparison.OrdinalIgnoreCase) ||
                filterOperator.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase) ||
                filterOperator.Equals(ExchangeTableFilter.InOperator, StringComparison.OrdinalIgnoreCase) ||
                filterOperator.Equals(ExchangeTableFilter.GreaterThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) ||
                filterOperator.Equals(ExchangeTableFilter.LessThanOrEqualOperator, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedOperators.Length > 0)
        {
            return normalizedOperators;
        }

        return dataType.Equals("text", StringComparison.OrdinalIgnoreCase)
            ? [ExchangeTableFilter.EqualsOperator, ExchangeTableFilter.LikeOperator, ExchangeTableFilter.InOperator]
            : [ExchangeTableFilter.EqualsOperator, ExchangeTableFilter.InOperator];
    }

    private static string GetOperatorLabel(string filterOperator)
    {
        return filterOperator switch
        {
            ExchangeTableFilter.EqualsOperator => "=",
            ExchangeTableFilter.NotEqualsOperator => "!=",
            ExchangeTableFilter.LikeOperator => "LIKE",
            ExchangeTableFilter.InOperator => "IN",
            ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
            ExchangeTableFilter.LessThanOrEqualOperator => "<=",
            _ => filterOperator
        };
    }

    private static SqlDbType GetSqlDbType(AreteFilterDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.OutputFormat))
        {
            return SqlDbType.NVarChar;
        }

        return definition.DataType switch
        {
            "int" => SqlDbType.BigInt,
            "decimal" => SqlDbType.Decimal,
            "date" => SqlDbType.Date,
            "datetime" => SqlDbType.DateTime2,
            _ => SqlDbType.NVarChar
        };
    }

    private static object ConvertFilterValue(AreteFilterDefinition definition, string rawValue)
    {
        var value = rawValue.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return DBNull.Value;
        }

        return definition.DataType switch
        {
            "int" => long.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var integerValue) ||
                     long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue)
                ? integerValue
                : throw new InvalidOperationException($"Filtro {definition.Name} reiksme turi buti sveikas skaicius."),
            "decimal" => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var decimalValue) ||
                         decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimalValue)
                ? decimalValue
                : throw new InvalidOperationException($"Filtro {definition.Name} reiksme turi buti skaicius."),
            "date" => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue) ||
                      DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out dateValue) ||
                      DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateValue)
                ? FormatTemporalValue(definition, dateValue.Date)
                : throw new InvalidOperationException($"Filtro {definition.Name} reiksme turi buti data."),
            "datetime" => DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeValue) ||
                          DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out dateTimeValue) ||
                          DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTimeValue)
                ? FormatTemporalValue(definition, dateTimeValue)
                : throw new InvalidOperationException($"Filtro {definition.Name} reiksme turi buti data ir laikas."),
            _ => value
        };
    }

    private static object FormatTemporalValue(AreteFilterDefinition definition, DateTime value)
    {
        return string.IsNullOrWhiteSpace(definition.OutputFormat)
            ? value
            : value.ToString(definition.OutputFormat, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> SplitFilterValues(string rawValue)
    {
        return rawValue
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string NormalizeLikeValue(string rawValue)
    {
        var value = rawValue.Trim();
        return value.IndexOfAny(['%', '_', '[']) >= 0
            ? value
            : $"%{value}%";
    }

    private static string GetFilterConnectorSql(string connector)
    {
        if (connector.Equals(ExchangeTableFilter.OrConnector, StringComparison.OrdinalIgnoreCase))
        {
            return "OR";
        }

        if (connector.Equals(ExchangeTableFilter.AndConnector, StringComparison.OrdinalIgnoreCase))
        {
            return "AND";
        }

        throw new InvalidOperationException("Netinkamas filtro jungiklis.");
    }

    private static int GetCommandTimeoutSeconds(IConfiguration configuration)
    {
        var timeout = configuration.GetValue<int?>("Arete:CommandTimeoutSeconds");
        return timeout is > 0 and <= 3600
            ? timeout.Value
            : DefaultCommandTimeoutSeconds;
    }
}

sealed record AreteParameterRules(string? OutputFormat, IReadOnlyList<string> AllowedOperators, string? FilterExpression);



