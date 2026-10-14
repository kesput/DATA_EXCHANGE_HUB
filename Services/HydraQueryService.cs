using System.Data;
using System.Globalization;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class HydraQueryService(
    IConfiguration configuration,
    IWebHostEnvironment environment) : IHydraQueryService
{
    private const string QueryRoot = "Sql/Hydra";
    private const int DefaultCommandTimeoutSeconds = 180;

    public IReadOnlyList<HydraQueryDefinition> ListQueries() => ListQueries(null);

    public IReadOnlyList<HydraQueryDefinition> ListQueries(IReadOnlySet<string>? countries)
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

    public async Task<HydraQueryResult> ExecuteAsync(
        string queryId,
        IReadOnlyList<ExchangeTableFilter>? filters = null,
        IReadOnlySet<string>? countries = null,
        CancellationToken cancellationToken = default)
    {
        var query = ListQueries(countries)
            .FirstOrDefault(item => item.Id.Equals(queryId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("HYDRA SQL uzklausa nerasta.");

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

        return new HydraQueryResult(columns, rows);
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
    private static HydraQueryDefinition BuildDefinition(string rootPath, string filePath)
    {
        var relativeToRoot = Path.GetRelativePath(rootPath, filePath);
        var parts = relativeToRoot.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var connectionName = parts.Length > 1 ? parts[1] : string.Empty;
        var area = parts.Length > 2 ? parts[2] : string.Empty;
        var name = Path.GetFileNameWithoutExtension(filePath);
        var relativePath = Path.Combine(QueryRoot, relativeToRoot);
        var id = relativePath.Replace('\\', '/');
        var filters = ReadFilterDefinitions(filePath);

        return new HydraQueryDefinition(
            id,
            name,
            connectionName,
            area,
            relativePath,
            filters);
    }

    private static IReadOnlyList<HydraFilterDefinition> ReadFilterDefinitions(string filePath)
    {
        return File
            .ReadLines(filePath)
            .Select(line => line.Trim())
            .Select(TryReadFilterLine)
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .Select(item => new
            {
                item.IsRequired,
                item.Definition
            })
            .Select(item => new
            {
                item.IsRequired,
                Parts = item.Definition.Split('|', 5, StringSplitOptions.TrimEntries)
            })
            .Where(item => item.Parts.Length >= 2 &&
                           !string.IsNullOrWhiteSpace(item.Parts[0]) &&
                           !string.IsNullOrWhiteSpace(item.Parts[1]))
            .Select(item => new HydraFilterDefinition(
                item.Parts[0],
                item.Parts[1],
                item.IsRequired,
                item.Parts.Length >= 3 ? NormalizeFilterDataType(item.Parts[2]) : "text",
                NormalizeAllowedOperators(
                    item.Parts.Length >= 4 ? item.Parts[3] : string.Empty,
                    item.Parts.Length >= 3 ? NormalizeFilterDataType(item.Parts[2]) : "text"),
                item.Parts.Length >= 5 ? item.Parts[4] : string.Empty))
            .ToArray();
    }

    private static (bool IsRequired, string Definition)? TryReadFilterLine(string line)
    {
        foreach (var marker in new[] { "-- SQL_FILTER_MUST:", "-- HYDRA_FILTER_MUST:" })
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (true, line[marker.Length..].Trim());
            }
        }

        foreach (var marker in new[] { "-- SQL_FILTER:", "-- HYDRA_FILTER:" })
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (false, line[marker.Length..].Trim());
            }
        }

        return null;
    }
    private static string BuildSqlWithFilters(
        SqlCommand command,
        string sql,
        IReadOnlyList<HydraFilterDefinition> filterDefinitions,
        IReadOnlyList<ExchangeTableFilter>? filters)
    {
        filters ??= [];
        ValidateRequiredFilters(filterDefinitions, filters);

        if (filters.Count == 0 || filters.All(filter => !filter.IsActive))
        {
            return sql;
        }

        var allowedFilters = filterDefinitions.ToDictionary(
            filter => filter.Name,
            StringComparer.OrdinalIgnoreCase);
        var clauses = new List<string>();
        var parameterIndex = 0;

        foreach (var filter in filters.Where(filter => filter.IsActive))
        {
            if (!allowedFilters.TryGetValue(filter.ColumnName, out var definition))
            {
                throw new InvalidOperationException($"Netinkamas HYDRA filtro laukas: {filter.ColumnName}");
            }

            var connectorSql = clauses.Count == 0
                ? string.Empty
                : GetFilterConnectorSql(filter.Connector);
            var clause = BuildFilterClause(command, definition, filter, ref parameterIndex);
            clauses.Add(string.IsNullOrWhiteSpace(connectorSql) ? clause : $"{connectorSql} {clause}");
        }

        if (clauses.Count == 0)
        {
            return sql;
        }

        return $"{sql.TrimEnd().TrimEnd(';')}{Environment.NewLine}AND {string.Join(" ", clauses)}";
    }

    private static void ValidateRequiredFilters(
        IReadOnlyList<HydraFilterDefinition> filterDefinitions,
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
                $"Truksta privalomu HYDRA filtro reiksmiu: {string.Join(", ", missingFilters)}.");
        }
    }

    private static string BuildFilterClause(
        SqlCommand command,
        HydraFilterDefinition definition,
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
            _ => throw new InvalidOperationException("HYDRA filtrui parinktas nepalaikomas operatorius.")
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
        HydraFilterDefinition definition,
        ref int parameterIndex,
        string rawValue)
    {
        var parameterName = $"@hydraFilter{parameterIndex++}";
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

    private static SqlDbType GetSqlDbType(HydraFilterDefinition definition)
    {
        return definition.DataType switch
        {
            "int" => SqlDbType.BigInt,
            "decimal" => SqlDbType.Decimal,
            "date" => SqlDbType.Date,
            "datetime" => SqlDbType.DateTime2,
            _ => SqlDbType.NVarChar
        };
    }

    private static object ConvertFilterValue(HydraFilterDefinition definition, string rawValue)
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
                ? dateValue.Date
                : throw new InvalidOperationException($"Filtro {definition.Name} reiksme turi buti data."),
            "datetime" => DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeValue) ||
                          DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out dateTimeValue) ||
                          DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTimeValue)
                ? dateTimeValue
                : throw new InvalidOperationException($"Filtro {definition.Name} reiksme turi buti data ir laikas."),
            _ => value
        };
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
        var timeout = configuration.GetValue<int?>("Hydra:CommandTimeoutSeconds");
        return timeout is > 0 and <= 3600
            ? timeout.Value
            : DefaultCommandTimeoutSeconds;
    }
}



