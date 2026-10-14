using System.Data;
using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ExchangeAccessService(
    IConfiguration configuration,
    IOptions<DataExchangeOptions> options,
    IUserNameResolver userNameResolver) : IExchangeAccessService
{
    private readonly DataExchangeOptions _options = options.Value;
    private const string DefaultExchangeAccessTableName = "BLS_LOGIT_STAT.dbo.BS_EXCHANGE_ACCESS";

    public async Task<UserAccessResult> GetCurrentUserAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        return (await GetCurrentUserAccessDetailsAsync(user, cancellationToken)).UserAccess;
    }

    public async Task<UserExchangeAccessResult> GetCurrentUserAccessDetailsAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);

        if (_options.TestMode)
        {
            var testTables = new[]
            {
                new ExchangeTableAccess(
                    _options.TestModeTableName,
                    "TEST",
                    ExchangePermission.UploadAndReceive)
            };

            return CreateAccessResult(userName, testTables, isTestMode: true);
        }

        var connectionString = configuration.GetConnectionString(_options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return CreateAccessResult(userName, [], isTestMode: false);
        }

        var tables = await LoadTableAccessAsync(userName, connectionString, cancellationToken);
        return CreateAccessResult(userName, tables, isTestMode: false);
    }

    private async Task<IReadOnlyList<ExchangeTableAccess>> LoadTableAccessAsync(
        string userName,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var accessTableName = SqlName.NormalizeTableName(
            _options.ExchangeAccessTableName,
            DefaultExchangeAccessTableName);
        var shortUserName = userName.Contains('\\', StringComparison.Ordinal)
            ? userName[(userName.LastIndexOf('\\') + 1)..]
            : userName;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                TABLE_NAME,
                [ACCESS],
                CAN_RECEIVE,
                CAN_UPLOAD,
                BEHAVIOR_PROFILE
            FROM {accessTableName}
            WHERE IS_ACTIVE = 1
                AND (DOMEN_USER = @UserName OR DOMEN_USER = @ShortUserName)
            ORDER BY TABLE_NAME
            """;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;
        command.Parameters.Add("@ShortUserName", SqlDbType.NVarChar, 256).Value = shortUserName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ExchangeTableAccess>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var tableName = reader.GetString(0).Trim();
            var accessRole = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
            var behaviorProfile = reader.IsDBNull(4) ? null : reader.GetString(4).Trim();
            var rolePermission = PermissionFromRole(accessRole);
            var flagPermission = ExchangePermission.None;

            if (!reader.IsDBNull(2) && reader.GetBoolean(2))
            {
                flagPermission |= ExchangePermission.Receive;
            }

            if (!reader.IsDBNull(3) && reader.GetBoolean(3))
            {
                flagPermission |= ExchangePermission.Upload;
            }

            var permission = flagPermission == ExchangePermission.None
                ? rolePermission
                : flagPermission;

            if (!string.IsNullOrWhiteSpace(tableName) && permission != ExchangePermission.None)
            {
                rows.Add(new ExchangeTableAccess(tableName, accessRole, permission, behaviorProfile));
            }
        }

        if (rows.Any(row => row.IsAdmin) &&
            rows.All(row => !IsSameTable(row.TableName, accessTableName)))
        {
            rows.Add(new ExchangeTableAccess(
                accessTableName,
                "ADMIN",
                ExchangePermission.UploadAndReceive));
        }

        return rows
            .GroupBy(row => row.TableName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ExchangeTableAccess(
                group.First().TableName,
                string.Join(", ", group
                    .Select(row => row.AccessRole)
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Distinct(StringComparer.OrdinalIgnoreCase)),
                group.Aggregate(ExchangePermission.None, (permission, row) => permission | row.Permission),
                group
                    .Select(row => row.BehaviorProfile)
                    .FirstOrDefault(profile => !string.IsNullOrWhiteSpace(profile))))
            .OrderBy(row => row.TableName)
            .ToArray();
    }

    private static bool IsSameTable(string leftTableName, string rightTableName)
    {
        return SqlName.NormalizeTableName(leftTableName, DefaultExchangeAccessTableName)
            .Equals(
                SqlName.NormalizeTableName(rightTableName, DefaultExchangeAccessTableName),
                StringComparison.OrdinalIgnoreCase);
    }

    private static UserExchangeAccessResult CreateAccessResult(
        string userName,
        IReadOnlyList<ExchangeTableAccess> tables,
        bool isTestMode)
    {
        var permission = tables.Aggregate(
            ExchangePermission.None,
            (current, table) => current | table.Permission);

        return new UserExchangeAccessResult(
            new UserAccessResult(userName, permission, isTestMode),
            tables);
    }

    private static ExchangePermission PermissionFromRole(string accessRole)
    {
        var normalized = accessRole
            .Trim()
            .ToUpperInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');

        return normalized switch
        {
            "ADMIN" or "FULL" or "RW" or "READ_WRITE" or "UPLOAD_RECEIVE" or "UPLOAD_AND_RECEIVE" => ExchangePermission.UploadAndReceive,
            "RECEIVE" or "READ" or "VIEW" or "GET" or "RO" => ExchangePermission.Receive,
            "UPLOAD" or "WRITE" or "INSERT" or "PUT" => ExchangePermission.Upload,
            _ => ExchangePermission.None
        };
    }

}
