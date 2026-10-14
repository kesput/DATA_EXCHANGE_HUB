using System.Data;
using System.Globalization;
using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ClientInfoService(
    IConfiguration configuration,
    IOptions<ClientInfoOptions> options,
    IUserNameResolver userNameResolver,
    IWebHostEnvironment environment) : IClientInfoService
{
    private const string DefaultAccessProcedureName = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    private const string FilterMarker = "-- HYDRA_FILTER:";
    private const string RequiredFilterMarker = "-- HYDRA_FILTER_MUST:";
    private readonly ClientInfoOptions _options = options.Value;

    public async Task<IReadOnlyList<HydraFilterDefinition>> GetFilterDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var sqlDefinition = await ReadSqlDefinitionAsync(cancellationToken);
        return sqlDefinition.Filters;
    }

    public async Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var connectionString = GetConnectionString(_options.AccessConnectionStringName);
        var procedureName = SqlName.NormalizeTableName(
            _options.AccessProcedureName,
            DefaultAccessProcedureName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC {procedureName} @AppName, @UserName";
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = _options.AppName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    public async Task<ClientInfoResult> ExecuteAsync(
        IReadOnlyList<ExchangeTableFilter> filters,
        CancellationToken cancellationToken = default)
    {
        var sqlDefinition = await ReadSqlDefinitionAsync(cancellationToken);
        ValidateRequiredFilters(sqlDefinition.Filters, filters);

        var connectionString = GetConnectionString(_options.DataConnectionStringName);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = _options.CommandTimeoutSeconds is > 0 and <= 3600
            ? _options.CommandTimeoutSeconds
            : 180;
        command.CommandText = BuildSql(command, sqlDefinition.Sql, sqlDefinition.Filters, filters);

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

        return new ClientInfoResult(columns, rows);
    }

    private static string BuildSql(
        SqlCommand command,
        string baseSql,
        IReadOnlyList<HydraFilterDefinition> filterDefinitions,
        IReadOnlyList<ExchangeTableFilter> filters)
    {
        var allowedFilters = filterDefinitions.ToDictionary(
            filter => filter.Name,
            StringComparer.OrdinalIgnoreCase);
        var clauses = new List<string>();
        var parameterIndex = 0;

        foreach (var filter in filters.Where(filter => filter.IsActive))
        {
            if (!allowedFilters.TryGetValue(filter.ColumnName, out var definition))
            {
                continue;
            }

            var normalizedOperator = NormalizeOperator(filter.Operator);
            if (!definition.AllowedOperators.Contains(normalizedOperator, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Filtrui {definition.Name} operatorius '{filter.Operator}' neleidziamas.");
            }

            var clause = CreateFilterClause(
                command,
                definition,
                filter,
                normalizedOperator,
                parameterIndex);
            if (clause is null)
            {
                continue;
            }

            clauses.Add(clauses.Count == 0
                ? clause.Value.Sql
                : $"{NormalizeConnector(filter.Connector)} {clause.Value.Sql}");
            parameterIndex = clause.Value.NextParameterIndex;
        }

        var filterSql = clauses.Count > 0
            ? $"{Environment.NewLine}  AND {string.Join($"{Environment.NewLine}  ", clauses)}"
            : string.Empty;

        return $"""
            {baseSql.TrimEnd().TrimEnd(';')}
            {filterSql}
            ORDER BY ORGALL.[FIRMID], ORGALL.[ORGID], ORGPOS.[POSID];
            """;
    }

    private static (string Sql, int NextParameterIndex)? CreateFilterClause(
        SqlCommand command,
        HydraFilterDefinition definition,
        ExchangeTableFilter filter,
        string normalizedOperator,
        int parameterIndex)
    {
        if (normalizedOperator == ExchangeTableFilter.InOperator)
        {
            var values = SplitFilterValues(filter.Value);
            if (values.Count == 0)
            {
                return null;
            }

            var parameterNames = values
                .Select(value =>
                {
                    var parameterName = $"@Filter{parameterIndex++}";
                    AddFilterParameter(command, parameterName, definition, value);
                    return parameterName;
                })
                .ToArray();

            return ($"{definition.Expression} IN ({string.Join(", ", parameterNames)})", parameterIndex);
        }

        if (string.IsNullOrWhiteSpace(filter.Value))
        {
            return null;
        }

        var singleParameterName = $"@Filter{parameterIndex++}";
        var parameterValue = normalizedOperator == ExchangeTableFilter.LikeOperator
            ? $"%{filter.Value.Trim()}%"
            : filter.Value.Trim();
        AddFilterParameter(command, singleParameterName, definition, parameterValue);

        var sqlOperator = normalizedOperator switch
        {
            ExchangeTableFilter.NotEqualsOperator => "<>",
            ExchangeTableFilter.LikeOperator => "LIKE",
            ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
            ExchangeTableFilter.LessThanOrEqualOperator => "<=",
            _ => "="
        };

        return ($"{definition.Expression} {sqlOperator} {singleParameterName}", parameterIndex);
    }

    private static void AddFilterParameter(
        SqlCommand command,
        string parameterName,
        HydraFilterDefinition definition,
        string rawValue)
    {
        var parameter = definition.DataType switch
        {
            "number" => command.Parameters.Add(parameterName, SqlDbType.Decimal),
            "date" or "datetime" => command.Parameters.Add(parameterName, SqlDbType.DateTime2),
            _ => command.Parameters.Add(parameterName, SqlDbType.NVarChar, 4000)
        };

        parameter.Value = definition.DataType switch
        {
            "number" when decimal.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue) => decimalValue,
            "number" => throw new InvalidOperationException($"Filtrui {definition.Name} nurodykite skaiciu."),
            "date" or "datetime" when DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateValue) => dateValue,
            "date" or "datetime" => throw new InvalidOperationException($"Filtrui {definition.Name} nurodykite data."),
            _ => rawValue
        };
    }

    private async Task<ClientInfoSqlDefinition> ReadSqlDefinitionAsync(CancellationToken cancellationToken)
    {
        var relativePath = _options.QueryPath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(environment.ContentRootPath, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Klientu info SQL failas nerastas: {_options.QueryPath}");
        }

        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        var filters = lines
            .Select(line => line.Trim())
            .Where(line =>
                line.StartsWith(FilterMarker, StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith(RequiredFilterMarker, StringComparison.OrdinalIgnoreCase))
            .Select(ParseFilterDefinition)
            .Where(filter => filter is not null)
            .Cast<HydraFilterDefinition>()
            .ToArray();

        _ = filters.FirstOrDefault(filter => filter.IsRequired)
            ?? throw new InvalidOperationException(
                "Klientu info SQL faile nerastas privalomas filtro aprasymas (-- HYDRA_FILTER_MUST: ...).");

        var sql = string.Join(
            Environment.NewLine,
            lines.Where(line =>
            {
                var trimmed = line.Trim();
                return !trimmed.StartsWith(FilterMarker, StringComparison.OrdinalIgnoreCase) &&
                       !trimmed.StartsWith(RequiredFilterMarker, StringComparison.OrdinalIgnoreCase);
            }));

        return new ClientInfoSqlDefinition(sql, filters);
    }

    private static HydraFilterDefinition? ParseFilterDefinition(string line)
    {
        var isRequired = line.StartsWith(RequiredFilterMarker, StringComparison.OrdinalIgnoreCase);
        var definition = isRequired
            ? line[RequiredFilterMarker.Length..].Trim()
            : line[FilterMarker.Length..].Trim();
        var parts = definition.Split('|', 5, StringSplitOptions.TrimEntries);
        if (parts.Length < 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            return null;
        }

        var dataType = parts.Length >= 3 ? NormalizeFilterDataType(parts[2]) : "text";
        return new HydraFilterDefinition(
            parts[0],
            parts[1],
            isRequired,
            dataType,
            NormalizeAllowedOperators(parts.Length >= 4 ? parts[3] : string.Empty),
            parts.Length >= 5 ? parts[4] : string.Empty);
    }

    private static IReadOnlyList<string> SplitFilterValues(string rawValue)
    {
        return rawValue
            .Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeOperator(string filterOperator)
    {
        return filterOperator.Trim().ToLowerInvariant() switch
        {
            "=" or "eq" => ExchangeTableFilter.EqualsOperator,
            "!=" or "<>" or "ne" => ExchangeTableFilter.NotEqualsOperator,
            "like" => ExchangeTableFilter.LikeOperator,
            "in" => ExchangeTableFilter.InOperator,
            ">=" or "gte" => ExchangeTableFilter.GreaterThanOrEqualOperator,
            "<=" or "lte" => ExchangeTableFilter.LessThanOrEqualOperator,
            _ => ExchangeTableFilter.EqualsOperator
        };
    }

    private static string NormalizeFilterDataType(string dataType)
    {
        return dataType.Trim().ToLowerInvariant() switch
        {
            "number" or "numeric" or "int" or "decimal" => "number",
            "date" or "datetime" => "date",
            _ => "text"
        };
    }

    private static IReadOnlyList<string> NormalizeAllowedOperators(string operators)
    {
        var normalizedOperators = operators
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeOperator)
            .Where(operatorName => operatorName is
                ExchangeTableFilter.EqualsOperator or
                ExchangeTableFilter.NotEqualsOperator or
                ExchangeTableFilter.LikeOperator or
                ExchangeTableFilter.InOperator or
                ExchangeTableFilter.GreaterThanOrEqualOperator or
                ExchangeTableFilter.LessThanOrEqualOperator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalizedOperators.Length > 0
            ? normalizedOperators
            : [ExchangeTableFilter.EqualsOperator, ExchangeTableFilter.InOperator];
    }

    private static string NormalizeConnector(string connector)
    {
        return connector.Equals(ExchangeTableFilter.OrConnector, StringComparison.OrdinalIgnoreCase)
            ? "OR"
            : "AND";
    }

    private static void ValidateRequiredFilters(
        IReadOnlyList<HydraFilterDefinition> filterDefinitions,
        IReadOnlyList<ExchangeTableFilter> filters)
    {
        foreach (var requiredFilter in filterDefinitions.Where(filter => filter.IsRequired))
        {
            var submittedFilter = filters.FirstOrDefault(filter =>
                filter.ColumnName.Equals(requiredFilter.Name, StringComparison.OrdinalIgnoreCase));
            if (submittedFilter is null || !submittedFilter.IsActive)
            {
                throw new InvalidOperationException($"{requiredFilter.Name} yra privalomas.");
            }

            var values = SplitFilterValues(submittedFilter.Value);
            if (NormalizeOperator(submittedFilter.Operator) == ExchangeTableFilter.EqualsOperator &&
                values.Count != 1)
            {
                throw new InvalidOperationException($"Operatoriui '=' nurodykite viena {requiredFilter.Name} reiksme.");
            }
        }
    }

    private string GetConnectionString(string connectionStringName)
    {
        return configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");
    }

    private sealed record ClientInfoSqlDefinition(
        string Sql,
        IReadOnlyList<HydraFilterDefinition> Filters);
}


