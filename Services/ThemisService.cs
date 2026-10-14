using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ThemisService(
    IConfiguration configuration,
    IOptions<ThemisOptions> options,
    IUserNameResolver userNameResolver) : IThemisService
{
    private const string DefaultAccessProcedureName = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    private const string DefaultLogTableName = "BLS_LOGIT_STAT.dbo.SL_CONFIG_LOG";
    private const string UpdateTimeFormat = "yyyy.MM.dd HH:mm:ss";
    private static readonly CultureInfo LithuanianCulture = CultureInfo.GetCultureInfo("lt-LT");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ThemisOptions _options = options.Value;

    private sealed record ColumnValue(ExchangeTableColumn Column, object Value);
    private sealed record RowPage(IReadOnlyList<ExchangeTableRow> Rows, bool HasNextPage);
    private sealed record LogDetails(
        long LogId,
        Guid BatchId,
        string ActionType,
        string Source,
        string TableName,
        string? KeyColumn,
        string? KeyValue,
        string ActorUser,
        IReadOnlyDictionary<string, string>? BeforeValues,
        IReadOnlyDictionary<string, string>? AfterValues,
        bool IsReverted);

    public async Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var connectionString = GetConnectionString(_options.AccessConnectionStringName);
        var procedureName = SqlName.NormalizeObjectName(
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

    public IReadOnlyList<ExchangeTableAccess> GetConfiguredTables()
    {
        return _options.TableNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new ExchangeTableAccess(name.Trim(), "ADMIN", ExchangePermission.UploadAndReceive))
            .ToArray();
    }

    public async Task<ExchangeTableData> GetTableDataAsync(
        ExchangeTableAccess table,
        int rowLimit,
        int pageNumber,
        IReadOnlyList<ExchangeTableFilter>? filters = null,
        CancellationToken cancellationToken = default)
    {
        var tableName = NormalizeAllowedTableName(table);
        var normalizedLimit = NormalizeRowLimit(rowLimit);
        var normalizedPage = Math.Max(1, pageNumber);

        await using var connection = new SqlConnection(GetConnectionString(_options.ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        var uniqueKeys = await LoadUniqueKeysAsync(connection, tableName, cancellationToken);
        var rowPage = await LoadRowsAsync(
            connection, tableName, columns, keyColumn,
            normalizedLimit, normalizedPage, filters, cancellationToken);

        return new ExchangeTableData(
            table.TableName, columns, uniqueKeys, rowPage.Rows,
            keyColumn, normalizedLimit, normalizedPage, rowPage.HasNextPage);
    }

    public async Task<int> CountRowsByFilterAsync(
        ExchangeTableAccess table,
        IReadOnlyList<ExchangeTableFilter> filters,
        CancellationToken cancellationToken = default)
    {
        if (filters.Count == 0 || filters.All(filter => !filter.IsActive))
        {
            throw new InvalidOperationException("Blokinio trynimo perziurai privalomas bent vienas aktyvus filtras.");
        }

        var tableName = NormalizeAllowedTableName(table);

        await using var connection = new SqlConnection(GetConnectionString(_options.ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        await using var command = connection.CreateCommand();
        var whereSql = BuildWhereClause(command, columns, filters);
        command.CommandText = $"SELECT COUNT_BIG(1) FROM {tableName}{whereSql}";

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async Task AddRowAsync(
        ExchangeTableAccess table,
        IFormCollection formData,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        var tableName = NormalizeAllowedTableName(table);

        await using var connection = new SqlConnection(GetConnectionString(_options.ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        var insertable = columns
            .Where(col => col.CanInsert && formData.ContainsKey(col.Name) &&
                          ShouldIncludeInsertValue(col, formData[col.Name].ToString()))
            .ToArray();

        if (insertable.Length == 0)
        {
            throw new InvalidOperationException("Nera stulpeliu, kuriuos galima prideti.");
        }

        var columnValues = insertable
            .Select(col => new ColumnValue(col, ToUserInputDbValue(formData[col.Name].ToString())))
            .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName))
            .ToArray();
        var colNames = columnValues.Select(cv => SqlName.QuoteIdentifier(cv.Column.Name)).ToArray();
        var paramNames = columnValues.Select((_, i) => $"@p{i}").ToArray();

        var batchId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {tableName} ({string.Join(", ", colNames)})
            OUTPUT INSERTED.*
            VALUES ({string.Join(", ", paramNames)})
            """;

        for (var i = 0; i < columnValues.Length; i++)
        {
            AddColumnParameter(command, paramNames[i], columnValues[i]);
        }

        var insertedRow = await ExecuteInsertAndReadRowAsync(command, columns, keyColumn, cancellationToken);
        await InsertLogAsync(connection, transaction, batchId, "INSERT", "WEB_FORM",
            table.TableName, keyColumn, insertedRow?.KeyValue, actorUserName,
            beforeValues: null, afterValues: insertedRow?.Values,
            changedColumns: columnValues.Select(cv => cv.Column.Name), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> AddRowsAsync(
        ExchangeTableAccess table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var tableName = NormalizeAllowedTableName(table);

        await using var connection = new SqlConnection(GetConnectionString(_options.ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        var allowedColumns = columns.ToDictionary(col => col.Name, StringComparer.OrdinalIgnoreCase);
        var insertableColumns = columns.Where(col => col.CanInsert).ToArray();

        if (insertableColumns.Length == 0)
        {
            throw new InvalidOperationException("Sioje lenteleje nera stulpeliu, kuriuos galima pildyti.");
        }

        var invalidColumns = rows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(colName =>
                !allowedColumns.ContainsKey(colName) &&
                !ExchangeTableColumn.IsSystemManagedColumnName(colName))
            .ToArray();

        if (invalidColumns.Length > 0)
        {
            throw new InvalidOperationException(
                $"Faile yra lenteles neatitinkanciu stulpeliu: {string.Join(", ", invalidColumns)}.");
        }

        var insertedRows = 0;
        var batchId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (insertableColumns.All(col =>
                    !row.TryGetValue(col.Name, out var value) ||
                    string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            var rowColumns = insertableColumns
                .Where(col =>
                {
                    row.TryGetValue(col.Name, out var value);
                    return ShouldIncludeInsertValue(col, value);
                })
                .ToArray();

            if (rowColumns.Length == 0)
            {
                continue;
            }

            var columnValues = rowColumns
                .Select(col =>
                {
                    row.TryGetValue(col.Name, out var rawValue);
                    return new ColumnValue(col, ToUserInputDbValue(rawValue));
                })
                .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName))
                .ToArray();

            var colNames = columnValues.Select(cv => SqlName.QuoteIdentifier(cv.Column.Name)).ToArray();
            var paramNames = columnValues.Select((_, i) => $"@p{i}").ToArray();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO {tableName} ({string.Join(", ", colNames)})
                OUTPUT INSERTED.*
                VALUES ({string.Join(", ", paramNames)})
                """;

            for (var i = 0; i < columnValues.Length; i++)
            {
                AddColumnParameter(command, paramNames[i], columnValues[i]);
            }

            var insertedRow = await ExecuteInsertAndReadRowAsync(command, columns, keyColumn, cancellationToken);
            await InsertLogAsync(connection, transaction, batchId, "INSERT", "WEB_EXCEL",
                table.TableName, keyColumn, insertedRow?.KeyValue, actorUserName,
                beforeValues: null, afterValues: insertedRow?.Values,
                changedColumns: columnValues.Select(cv => cv.Column.Name), cancellationToken);

            insertedRows++;
        }

        await transaction.CommitAsync(cancellationToken);
        return insertedRows;
    }

    public async Task UpdateRowsAsync(
        ExchangeTableAccess table,
        IReadOnlyDictionary<string, Dictionary<string, string?>> updates,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
        {
            return;
        }

        var tableName = NormalizeAllowedTableName(table);

        await using var connection = new SqlConnection(GetConnectionString(_options.ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        if (string.IsNullOrWhiteSpace(keyColumn))
        {
            throw new InvalidOperationException("Lentele neturi ID stulpelio, todel koregavimas negalimas.");
        }

        var editableColumns = columns
            .Where(col => col.CanUpdate && !col.Name.Equals(keyColumn, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(col => col.Name, StringComparer.OrdinalIgnoreCase);

        var batchId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var (keyValue, rowUpdate) in updates)
        {
            var changes = rowUpdate.Where(ch => editableColumns.ContainsKey(ch.Key)).ToArray();
            if (changes.Length == 0)
            {
                continue;
            }

            var beforeRow = await LoadRowByKeyAsync(connection, transaction, tableName, columns, keyColumn, keyValue, cancellationToken);
            if (beforeRow is null)
            {
                throw new InvalidOperationException("Koreguojamas irasas nerastas arba jau pasalintas.");
            }

            var columnValues = changes
                .Select(ch => new ColumnValue(editableColumns[ch.Key], ToUserInputDbValue(ch.Value)))
                .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName)
                    .Where(cv => !cv.Column.Name.Equals(keyColumn, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var setClauses = columnValues
                .Select((cv, i) => $"{SqlName.QuoteIdentifier(cv.Column.Name)} = @p{i}")
                .ToArray();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE {tableName}
                SET {string.Join(", ", setClauses)}
                WHERE {SqlName.QuoteIdentifier(keyColumn)} = @keyValue
                """;

            for (var i = 0; i < columnValues.Length; i++)
            {
                AddColumnParameter(command, $"@p{i}", columnValues[i]);
            }

            command.Parameters.AddWithValue("@keyValue", keyValue);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new InvalidOperationException("Koreguojamas irasas nerastas arba jau pasalintas.");
            }

            var afterRow = await LoadRowByKeyAsync(connection, transaction, tableName, columns, keyColumn, keyValue, cancellationToken);
            await InsertLogAsync(connection, transaction, batchId, "UPDATE", "WEB_EDIT",
                table.TableName, keyColumn, beforeRow.KeyValue, actorUserName,
                beforeValues: beforeRow.Values, afterValues: afterRow?.Values,
                changedColumns: columnValues.Select(cv => cv.Column.Name), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteRowAsync(
        ExchangeTableAccess table,
        string keyValue,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyValue))
        {
            throw new InvalidOperationException("Nepavyko nustatyti salinamo iraso rakto.");
        }

        var tableName = NormalizeAllowedTableName(table);

        await using var connection = new SqlConnection(GetConnectionString(_options.ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        if (string.IsNullOrWhiteSpace(keyColumn))
        {
            throw new InvalidOperationException("Lentele neturi ID stulpelio, todel salinimas negalimas.");
        }

        var batchId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var beforeRow = await LoadRowByKeyAsync(connection, transaction, tableName, columns, keyColumn, keyValue, cancellationToken);
        if (beforeRow is null)
        {
            throw new InvalidOperationException("Irasas nerastas arba jau pasalintas.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {tableName} WHERE {SqlName.QuoteIdentifier(keyColumn)} = @keyValue";
        command.Parameters.AddWithValue("@keyValue", keyValue.Trim());

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Irasas nerastas arba jau pasalintas.");
        }

        await InsertLogAsync(connection, transaction, batchId, "DELETE", "WEB_DELETE",
            table.TableName, keyColumn, beforeRow.KeyValue, actorUserName,
            beforeValues: beforeRow.Values, afterValues: null,
            changedColumns: columns.Select(col => col.Name), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> DeleteRowsByFilterAsync(
        ExchangeTableAccess table,
        IReadOnlyList<ExchangeTableFilter> filters,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (filters.Count == 0 || filters.All(filter => !filter.IsActive))
        {
            throw new InvalidOperationException("Blokiniam trynimui privalomas bent vienas aktyvus filtras.");
        }

        var tableName = NormalizeAllowedTableName(table);

        await using var connection = new SqlConnection(GetConnectionString(_options.ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);

        var batchId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var whereSql = BuildWhereClause(command, columns, filters);
        if (string.IsNullOrWhiteSpace(whereSql))
        {
            throw new InvalidOperationException("Blokiniam trynimui privalomas bent vienas aktyvus filtras.");
        }

        command.CommandText = $"""
            DELETE FROM {tableName}
            OUTPUT DELETED.*
            {whereSql}
            """;

        var deletedRows = new List<ExchangeTableRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                deletedRows.Add(ReadRow(reader, columns, keyColumn));
            }
        }

        foreach (var deletedRow in deletedRows)
        {
            await InsertLogAsync(connection, transaction, batchId, "DELETE", "WEB_FILTER_DELETE",
                table.TableName, keyColumn, deletedRow.KeyValue, actorUserName,
                beforeValues: deletedRow.Values, afterValues: null,
                changedColumns: columns.Select(col => col.Name), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return deletedRows.Count;
    }

    public async Task<IReadOnlyList<ExchangeLogEntry>> ListLogsAsync(
        ExchangeTableAccess table,
        int rowLimit = 25,
        CancellationToken cancellationToken = default)
    {
        var logTableName = NormalizeLogTableName();

        await using var connection = new SqlConnection(GetConnectionString(_options.ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@RowLimit)
                LOG_ID,
                EVENT_AT,
                ACTION_TYPE,
                SOURCE,
                TABLE_NAME,
                KEY_COLUMN,
                KEY_VALUE,
                ACTOR_USER,
                CHANGED_COLUMNS,
                IS_REVERTED,
                REVERTED_AT,
                REVERTED_BY
            FROM {logTableName}
            WHERE TABLE_NAME = @TableName
            ORDER BY EVENT_AT DESC, LOG_ID DESC
            """;
        command.Parameters.Add("@RowLimit", SqlDbType.Int).Value = Math.Clamp(rowLimit, 1, 200);
        command.Parameters.Add("@TableName", SqlDbType.NVarChar, 776).Value = table.TableName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var logs = new List<ExchangeLogEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(new ExchangeLogEntry(
                reader.GetInt64(0),
                reader.GetDateTimeOffset(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetBoolean(9),
                reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return logs;
    }

    public async Task RevertLogEntryAsync(
        ExchangeTableAccess table,
        long logId,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        var tableName = NormalizeAllowedTableName(table);

        await using var connection = new SqlConnection(GetConnectionString(_options.ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var log = await LoadLogDetailsAsync(connection, transaction, logId, cancellationToken);

        if (!SqlName.NormalizeTableName(log.TableName, string.Empty)
                .Equals(SqlName.NormalizeTableName(table.TableName, string.Empty), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Audit irasas nepriklauso pasirinktai lentelei.");
        }

        if (log.IsReverted)
        {
            throw new InvalidOperationException("Sis audit irasas jau atstatytas.");
        }

        long revertSourceLogId;
        if (log.ActionType.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            revertSourceLogId = await RevertUpdatedRowAsync(
                connection, transaction, table, tableName, columns, keyColumn, log, actorUserName, cancellationToken);
        }
        else if (log.ActionType.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            revertSourceLogId = await RestoreDeletedRowAsync(
                connection, transaction, table, tableName, columns, keyColumn, log, actorUserName, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("Atstatymas galimas tik UPDATE arba DELETE veiksmams.");
        }

        await MarkLogRevertedAsync(connection, transaction, log.LogId, revertSourceLogId, actorUserName, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // --- Private helpers ---

    private async Task<long> RevertUpdatedRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ExchangeTableAccess table,
        string tableName,
        IReadOnlyList<ExchangeTableColumn> columns,
        string? keyColumn,
        LogDetails log,
        string actorUserName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyColumn) || string.IsNullOrWhiteSpace(log.KeyValue))
        {
            throw new InvalidOperationException("UPDATE atstatymui reikalingas iraso raktas.");
        }

        if (log.BeforeValues is null || log.BeforeValues.Count == 0)
        {
            throw new InvalidOperationException("Audit irase nera ankstesniu reiksmiu.");
        }

        var editableColumns = columns
            .Where(col => col.CanUpdate && !col.Name.Equals(keyColumn, StringComparison.OrdinalIgnoreCase))
            .Where(col => log.BeforeValues.ContainsKey(col.Name))
            .Select(col => new ColumnValue(col, ToUserInputDbValue(log.BeforeValues[col.Name])))
            .ToArray();

        if (editableColumns.Length == 0)
        {
            throw new InvalidOperationException("Nera stulpeliu, kuriuos galima atstatyti.");
        }

        var beforeRow = await LoadRowByKeyAsync(connection, transaction, tableName, columns, keyColumn, log.KeyValue, cancellationToken);
        if (beforeRow is null)
        {
            throw new InvalidOperationException("Atstatomas irasas nerastas arba jau pasalintas.");
        }

        var columnValues = editableColumns
            .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName))
            .ToArray();
        var setClauses = columnValues
            .Select((cv, i) => $"{SqlName.QuoteIdentifier(cv.Column.Name)} = @p{i}")
            .ToArray();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {tableName}
            SET {string.Join(", ", setClauses)}
            WHERE {SqlName.QuoteIdentifier(keyColumn)} = @KeyValue
            """;

        for (var i = 0; i < columnValues.Length; i++)
        {
            AddColumnParameter(command, $"@p{i}", columnValues[i]);
        }

        command.Parameters.Add("@KeyValue", SqlDbType.NVarChar, 4000).Value = log.KeyValue;
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Atstatomas irasas nerastas arba jau pasalintas.");
        }

        var afterRow = await LoadRowByKeyAsync(connection, transaction, tableName, columns, keyColumn, log.KeyValue, cancellationToken);
        return await InsertLogAsync(connection, transaction, Guid.NewGuid(), "UPDATE", "WEB_REVERT",
            table.TableName, keyColumn, beforeRow.KeyValue, actorUserName,
            beforeValues: beforeRow.Values, afterValues: afterRow?.Values,
            changedColumns: columnValues.Select(cv => cv.Column.Name), cancellationToken);
    }

    private async Task<long> RestoreDeletedRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ExchangeTableAccess table,
        string tableName,
        IReadOnlyList<ExchangeTableColumn> columns,
        string? keyColumn,
        LogDetails log,
        string actorUserName,
        CancellationToken cancellationToken)
    {
        if (log.BeforeValues is null || log.BeforeValues.Count == 0)
        {
            throw new InvalidOperationException("Audit irase nera istrinto iraso duomenu.");
        }

        var restoredValues = columns
            .Where(col => col.CanInsert)
            .Where(col => log.BeforeValues.TryGetValue(col.Name, out var value) && ShouldIncludeInsertValue(col, value))
            .Select(col => new ColumnValue(col, ToUserInputDbValue(log.BeforeValues[col.Name])))
            .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName))
            .ToArray();

        if (restoredValues.Length == 0)
        {
            throw new InvalidOperationException("Nera stulpeliu, kuriuos galima atkurti.");
        }

        var colNames = restoredValues.Select(cv => SqlName.QuoteIdentifier(cv.Column.Name)).ToArray();
        var paramNames = restoredValues.Select((_, i) => $"@p{i}").ToArray();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {tableName} ({string.Join(", ", colNames)})
            OUTPUT INSERTED.*
            VALUES ({string.Join(", ", paramNames)})
            """;

        for (var i = 0; i < restoredValues.Length; i++)
        {
            AddColumnParameter(command, paramNames[i], restoredValues[i]);
        }

        var insertedRow = await ExecuteInsertAndReadRowAsync(command, columns, keyColumn, cancellationToken);
        return await InsertLogAsync(connection, transaction, Guid.NewGuid(), "INSERT", "WEB_REVERT",
            table.TableName, keyColumn, insertedRow?.KeyValue, actorUserName,
            beforeValues: null, afterValues: insertedRow?.Values,
            changedColumns: restoredValues.Select(cv => cv.Column.Name), cancellationToken);
    }

    private async Task<IReadOnlyList<ExchangeTableColumn>> LoadColumnsAsync(
        SqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                c.name,
                t.name,
                c.is_nullable,
                c.is_identity,
                c.is_computed,
                CASE WHEN dc.object_id IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END
            FROM sys.columns c
            INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc
                ON dc.parent_object_id = c.object_id
                AND dc.parent_column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(@TableName)
            ORDER BY c.column_id
            """;
        command.Parameters.Add("@TableName", SqlDbType.NVarChar, 776).Value = tableName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<ExchangeTableColumn>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ExchangeTableColumn(
                reader.GetString(0), reader.GetString(1),
                reader.GetBoolean(2), reader.GetBoolean(3),
                reader.GetBoolean(4), reader.GetBoolean(5)));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Lentele nerasta arba neturi stulpeliu: {tableName}");
        }

        return columns;
    }

    private static async Task<string?> LoadKeyColumnAsync(
        SqlConnection connection,
        string tableName,
        IReadOnlyList<ExchangeTableColumn> columns,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) c.name
            FROM sys.key_constraints kc
            INNER JOIN sys.index_columns ic
                ON ic.object_id = kc.parent_object_id
                AND ic.index_id = kc.unique_index_id
            INNER JOIN sys.columns c
                ON c.object_id = ic.object_id
                AND c.column_id = ic.column_id
            WHERE kc.type = 'PK'
                AND kc.parent_object_id = OBJECT_ID(@TableName)
            ORDER BY ic.key_ordinal
            """;
        command.Parameters.Add("@TableName", SqlDbType.NVarChar, 776).Value = tableName;

        var pk = await command.ExecuteScalarAsync(cancellationToken);
        if (pk is string pkCol && !string.IsNullOrWhiteSpace(pkCol))
        {
            return pkCol;
        }

        return columns.FirstOrDefault(col => col.Name.Equals("ID", StringComparison.OrdinalIgnoreCase))?.Name;
    }

    private static async Task<IReadOnlyList<ExchangeUniqueKey>> LoadUniqueKeysAsync(
        SqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                i.name,
                CASE
                    WHEN kc.type = 'PK' THEN N'PRIMARY KEY'
                    WHEN kc.type = 'UQ' THEN N'UNIQUE'
                    ELSE N'UNIQUE INDEX'
                END AS KeyType,
                c.name,
                ic.key_ordinal
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic
                ON ic.object_id = i.object_id
                AND ic.index_id = i.index_id
            INNER JOIN sys.columns c
                ON c.object_id = ic.object_id
                AND c.column_id = ic.column_id
            LEFT JOIN sys.key_constraints kc
                ON kc.parent_object_id = i.object_id
                AND kc.unique_index_id = i.index_id
            WHERE i.object_id = OBJECT_ID(@TableName)
                AND i.is_unique = 1
                AND i.is_hypothetical = 0
                AND ic.is_included_column = 0
            ORDER BY i.index_id, ic.key_ordinal
            """;
        command.Parameters.Add("@TableName", SqlDbType.NVarChar, 776).Value = tableName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var keyColumns = new Dictionary<string, (string Type, List<string> Columns)>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            var keyName = reader.GetString(0);
            var keyType = reader.GetString(1);
            var columnName = reader.GetString(2);

            if (!keyColumns.TryGetValue(keyName, out var uniqueKey))
            {
                uniqueKey = (keyType, []);
                keyColumns[keyName] = uniqueKey;
            }

            uniqueKey.Columns.Add(columnName);
        }

        return keyColumns
            .Select(key => new ExchangeUniqueKey(key.Key, key.Value.Type, key.Value.Columns))
            .ToArray();
    }

    private static async Task<RowPage> LoadRowsAsync(
        SqlConnection connection,
        string tableName,
        IReadOnlyList<ExchangeTableColumn> columns,
        string? keyColumn,
        int rowLimit,
        int pageNumber,
        IReadOnlyList<ExchangeTableFilter>? filters,
        CancellationToken cancellationToken)
    {
        var orderColumn = !string.IsNullOrWhiteSpace(keyColumn) ? keyColumn : columns.First().Name;
        var offset = (Math.Max(1, pageNumber) - 1) * rowLimit;

        await using var command = connection.CreateCommand();
        var whereSql = BuildWhereClause(command, columns, filters);
        command.CommandText = $"""
            SELECT *
            FROM {tableName}
            {whereSql}
            ORDER BY {SqlName.QuoteIdentifier(orderColumn)} DESC
            OFFSET @Offset ROWS
            FETCH NEXT @Fetch ROWS ONLY
            """;
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        command.Parameters.Add("@Fetch", SqlDbType.Int).Value = rowLimit + 1;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ExchangeTableRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadRow(reader, columns, keyColumn));
        }

        var hasNext = rows.Count > rowLimit;
        if (hasNext)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new RowPage(rows, hasNext);
    }

    private static async Task<ExchangeTableRow?> LoadRowByKeyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tableName,
        IReadOnlyList<ExchangeTableColumn> columns,
        string keyColumn,
        string keyValue,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) *
            FROM {tableName}
            WHERE {SqlName.QuoteIdentifier(keyColumn)} = @keyValue
            """;
        command.Parameters.AddWithValue("@keyValue", keyValue.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRow(reader, columns, keyColumn) : null;
    }

    private static async Task<ExchangeTableRow?> ExecuteInsertAndReadRowAsync(
        SqlCommand command,
        IReadOnlyList<ExchangeTableColumn> columns,
        string? keyColumn,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRow(reader, columns, keyColumn) : null;
    }

    private async Task<LogDetails> LoadLogDetailsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long logId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT LOG_ID, BATCH_ID, ACTION_TYPE, SOURCE, TABLE_NAME,
                   KEY_COLUMN, KEY_VALUE, ACTOR_USER,
                   ROW_DATA_BEFORE, ROW_DATA_AFTER, IS_REVERTED
            FROM {NormalizeLogTableName()}
            WHERE LOG_ID = @LogId
            """;
        command.Parameters.Add("@LogId", SqlDbType.BigInt).Value = logId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Audit irasas nerastas.");
        }

        return new LogDetails(
            reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            DeserializeRowValues(reader.IsDBNull(8) ? null : reader.GetString(8)),
            DeserializeRowValues(reader.IsDBNull(9) ? null : reader.GetString(9)),
            reader.GetBoolean(10));
    }

    private async Task<long> InsertLogAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid batchId,
        string actionType,
        string source,
        string tableName,
        string? keyColumn,
        string? keyValue,
        string actorUserName,
        IReadOnlyDictionary<string, string>? beforeValues,
        IReadOnlyDictionary<string, string>? afterValues,
        IEnumerable<string> changedColumns,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {NormalizeLogTableName()}
                (BATCH_ID, EVENT_AT, ACTION_TYPE, SOURCE, TABLE_NAME,
                 KEY_COLUMN, KEY_VALUE, ACTOR_USER,
                 CHANGED_COLUMNS, ROW_DATA_BEFORE, ROW_DATA_AFTER, IS_REVERTED)
            OUTPUT INSERTED.LOG_ID
            VALUES
                (@BatchId, SYSDATETIMEOFFSET(), @ActionType, @Source, @TableName,
                 @KeyColumn, @KeyValue, @ActorUser,
                 @ChangedColumns, @RowDataBefore, @RowDataAfter, 0)
            """;

        command.Parameters.Add("@BatchId", SqlDbType.UniqueIdentifier).Value = batchId;
        command.Parameters.Add("@ActionType", SqlDbType.NVarChar, 30).Value = actionType;
        command.Parameters.Add("@Source", SqlDbType.NVarChar, 50).Value = source;
        command.Parameters.Add("@TableName", SqlDbType.NVarChar, 776).Value = tableName;
        command.Parameters.Add("@KeyColumn", SqlDbType.NVarChar, 128).Value = ToDbValue(keyColumn);
        command.Parameters.Add("@KeyValue", SqlDbType.NVarChar, 4000).Value = ToDbValue(keyValue);
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value =
            string.IsNullOrWhiteSpace(actorUserName) ? "unknown" : actorUserName.Trim();
        command.Parameters.Add("@ChangedColumns", SqlDbType.NVarChar, -1).Value =
            ToDbValue(SerializeChangedColumns(changedColumns));
        command.Parameters.Add("@RowDataBefore", SqlDbType.NVarChar, -1).Value =
            ToDbValue(beforeValues is null ? null : JsonSerializer.Serialize(beforeValues, JsonOptions));
        command.Parameters.Add("@RowDataAfter", SqlDbType.NVarChar, -1).Value =
            ToDbValue(afterValues is null ? null : JsonSerializer.Serialize(afterValues, JsonOptions));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task MarkLogRevertedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long logId,
        long revertSourceLogId,
        string actorUserName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {NormalizeLogTableName()}
            SET IS_REVERTED = 1,
                REVERTED_AT = SYSDATETIMEOFFSET(),
                REVERTED_BY = @ActorUser,
                REVERT_SOURCE_LOG_ID = @RevertSourceLogId
            WHERE LOG_ID = @LogId
            """;
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = actorUserName.Trim();
        command.Parameters.Add("@RevertSourceLogId", SqlDbType.BigInt).Value = revertSourceLogId;
        command.Parameters.Add("@LogId", SqlDbType.BigInt).Value = logId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string NormalizeAllowedTableName(ExchangeTableAccess table)
    {
        var normalized = SqlName.NormalizeTableName(table.TableName, string.Empty);
        var allowed = GetConfiguredTables()
            .Select(t => SqlName.NormalizeTableName(t.TableName, string.Empty))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!allowed.Contains(normalized))
        {
            throw new InvalidOperationException($"Lentele '{table.TableName}' nepriklauso THEMIS konfiguracijai.");
        }

        return normalized;
    }

    private string NormalizeLogTableName()
    {
        return SqlName.NormalizeTableName(_options.LogTableName, DefaultLogTableName);
    }

    private string GetConnectionString(string connectionStringName)
    {
        return configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"Prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");
    }

    private static ExchangeTableRow ReadRow(
        SqlDataReader reader,
        IReadOnlyList<ExchangeTableColumn> columns,
        string? keyColumn)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            var raw = reader[col.Name];
            values[col.Name] = raw == DBNull.Value
                ? string.Empty
                : Convert.ToString(raw, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        var keyValue = !string.IsNullOrWhiteSpace(keyColumn) && values.TryGetValue(keyColumn, out var v) ? v : null;
        return new ExchangeTableRow(values, keyValue);
    }

    private static IReadOnlyList<ColumnValue> BuildAutomaticAuditColumnValues(
        IReadOnlyList<ExchangeTableColumn> columns,
        string actorUserName)
    {
        var updateTime = DateTime.Now.ToString(UpdateTimeFormat, CultureInfo.InvariantCulture);
        var actor = string.IsNullOrWhiteSpace(actorUserName) ? "unknown" : actorUserName.Trim();

        return columns
            .Where(col => !col.IsIdentity && !col.IsComputed && !col.IsRowVersion &&
                          (col.IsUpdateTimeColumn || col.IsUpdateUserColumn))
            .Select(col => new ColumnValue(col, col.IsUpdateTimeColumn ? updateTime : actor))
            .ToArray();
    }

    private static bool ShouldIncludeInsertValue(ExchangeTableColumn column, string? rawValue)
    {
        return !column.HasDefaultValue || !string.IsNullOrWhiteSpace(rawValue);
    }

    private static object ToUserInputDbValue(string? rawValue)
    {
        return string.IsNullOrWhiteSpace(rawValue) ? DBNull.Value : rawValue.Trim();
    }

    private static void AddColumnParameter(SqlCommand command, string paramName, ColumnValue cv)
    {
        command.Parameters.AddWithValue(paramName, ConvertColumnValue(cv.Column, cv.Value));
    }

    private static object ConvertColumnValue(ExchangeTableColumn column, object rawValue)
    {
        if (rawValue == DBNull.Value)
        {
            return DBNull.Value;
        }

        var value = Convert.ToString(rawValue, CultureInfo.CurrentCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return DBNull.Value;
        }

        var dt = column.DataType.ToLowerInvariant();
        if (dt == "bit")
        {
            return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("taip", StringComparison.OrdinalIgnoreCase);
        }

        if (dt is "tinyint" or "smallint" or "int" or "bigint")
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return l;
            }
        }

        if (dt is "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real")
        {
            if (decimal.TryParse(value, NumberStyles.Number, LithuanianCulture, out var d) ||
                decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
            {
                return d;
            }
        }

        if (dt is "date" or "datetime" or "datetime2" or "smalldatetime")
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
            {
                return date;
            }

            if (TryParseExcelSerialDate(value, out date))
            {
                return date;
            }
        }

        if (dt == "datetimeoffset")
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                return dto;
            }
        }

        return IsTextSqlType(dt) && !IsDateNamedTextColumn(column)
            ? NormalizeDottedNumericText(value)
            : value;
    }

    private static bool IsTextSqlType(string dataType)
    {
        return dataType is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";
    }

    private static bool IsDateNamedTextColumn(ExchangeTableColumn column)
    {
        return IsTextSqlType(column.DataType.ToLowerInvariant()) &&
               column.Name.Contains("DATE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseExcelSerialDate(string value, out DateTime dateTimeValue)
    {
        dateTimeValue = default;
        var normalized = value.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (!double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var serialValue))
        {
            return false;
        }

        if (serialValue is < 1 or > 2958465)
        {
            return false;
        }

        try
        {
            dateTimeValue = DateTime.FromOADate(serialValue);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string NormalizeDottedNumericText(string value)
    {
        return LooksLikeDottedNumericText(value)
            ? value.Replace(".", ",", StringComparison.Ordinal)
            : value;
    }

    private static bool LooksLikeDottedNumericText(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.Contains('.', StringComparison.Ordinal) ||
            trimmed.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        var hasDigit = false;
        for (var index = 0; index < trimmed.Length; index++)
        {
            var ch = trimmed[index];
            if (char.IsDigit(ch))
            {
                hasDigit = true;
                continue;
            }

            if (ch == '.' || ch == ' ' || ch == '\u00A0')
            {
                continue;
            }

            if ((ch == '+' || ch == '-') && index == 0)
            {
                continue;
            }

            return false;
        }

        return hasDigit;
    }

    private static string BuildWhereClause(
        SqlCommand command,
        IReadOnlyList<ExchangeTableColumn> columns,
        IReadOnlyList<ExchangeTableFilter>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return string.Empty;
        }

        var allowed = columns.ToDictionary(col => col.Name, StringComparer.OrdinalIgnoreCase);
        var clauses = new List<string>();
        var paramIdx = 0;

        foreach (var filter in filters.Where(f => f.IsActive))
        {
            if (!allowed.TryGetValue(filter.ColumnName, out var column))
            {
                throw new InvalidOperationException($"Netinkamas filtro stulpelis: {filter.ColumnName}");
            }

            var connector = clauses.Count == 0 ? string.Empty
                : filter.Connector.Equals(ExchangeTableFilter.OrConnector, StringComparison.OrdinalIgnoreCase) ? "OR" : "AND";

            var colName = SqlName.QuoteIdentifier(column.Name);
            var op = NormalizeOperator(filter.Operator);
            string clause;

            if (op == ExchangeTableFilter.InOperator)
            {
                var parts = SplitValues(filter.Value)
                    .Select(v => AddFilterParam(command, column, ref paramIdx, v))
                    .ToArray();
                if (parts.Length == 0)
                {
                    throw new InvalidOperationException("IN filtrui reikia bent vienos reiksmes.");
                }

                clause = $"{colName} IN ({string.Join(", ", parts)})";
            }
            else if (op == ExchangeTableFilter.BetweenOperator)
            {
                var parts = SplitBetween(filter.Value);
                if (parts.Length != 2)
                {
                    throw new InvalidOperationException("BETWEEN filtrui reikia dvieju reiksmiu.");
                }

                clause = $"{colName} BETWEEN {AddFilterParam(command, column, ref paramIdx, parts[0])} AND {AddFilterParam(command, column, ref paramIdx, parts[1])}";
            }
            else
            {
                var isLike = op == ExchangeTableFilter.LikeOperator;
                var pName = AddFilterParam(command, column, ref paramIdx,
                    isLike ? NormalizeLike(filter.Value) : filter.Value,
                    convertValue: !isLike);
                var opSql = op switch
                {
                    ExchangeTableFilter.EqualsOperator => "=",
                    ExchangeTableFilter.NotEqualsOperator => "<>",
                    ExchangeTableFilter.LikeOperator => "LIKE",
                    ExchangeTableFilter.GreaterThanOperator => ">",
                    ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
                    ExchangeTableFilter.LessThanOperator => "<",
                    ExchangeTableFilter.LessThanOrEqualOperator => "<=",
                    _ => "="
                };
                clause = $"{colName} {opSql} {pName}";
            }

            clauses.Add(string.IsNullOrEmpty(connector) ? clause : $"{connector} {clause}");
        }

        return clauses.Count == 0 ? string.Empty : $" WHERE {string.Join(" ", clauses)}";
    }

    private static string AddFilterParam(
        SqlCommand command,
        ExchangeTableColumn column,
        ref int idx,
        string value,
        bool convertValue = true)
    {
        var name = $"@filter{idx++}";
        command.Parameters.AddWithValue(name, convertValue ? ConvertColumnValue(column, value.Trim()) : value.Trim());
        return name;
    }

    private static string NormalizeOperator(string op)
    {
        if (op.Equals("!=") || op.Equals("<>") || op.Equals(ExchangeTableFilter.NotEqualsOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.NotEqualsOperator;
        }

        if (op.Equals(">") || op.Equals(ExchangeTableFilter.GreaterThanOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.GreaterThanOperator;
        }

        if (op.Equals(">=") || op.Equals(ExchangeTableFilter.GreaterThanOrEqualOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.GreaterThanOrEqualOperator;
        }

        if (op.Equals("<") || op.Equals(ExchangeTableFilter.LessThanOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.LessThanOperator;
        }

        if (op.Equals("<=") || op.Equals(ExchangeTableFilter.LessThanOrEqualOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.LessThanOrEqualOperator;
        }

        if (op.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.LikeOperator;
        }

        if (op.Equals(ExchangeTableFilter.InOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.InOperator;
        }

        if (op.Equals(ExchangeTableFilter.BetweenOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.BetweenOperator;
        }

        return ExchangeTableFilter.EqualsOperator;
    }

    private static IReadOnlyList<string> SplitValues(string raw)
    {
        return raw.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(v => !string.IsNullOrWhiteSpace(v))
                  .ToArray();
    }

    private static string[] SplitBetween(string raw)
    {
        var sep = raw.Contains("..", StringComparison.Ordinal) ? new[] { ".." } : new[] { ",", ";" };
        return raw.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(v => !string.IsNullOrWhiteSpace(v))
                  .ToArray();
    }

    private static string NormalizeLike(string raw)
    {
        var v = raw.Trim();
        return v.IndexOfAny(['%', '_', '[']) >= 0 ? v : $"%{v}%";
    }

    private static string? SerializeChangedColumns(IEnumerable<string> cols)
    {
        var arr = cols.Where(c => !string.IsNullOrWhiteSpace(c))
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToArray();
        return arr.Length == 0 ? null : JsonSerializer.Serialize(arr);
    }

    private static IReadOnlyDictionary<string, string>? DeserializeRowValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static int NormalizeRowLimit(int rowLimit)
    {
        return rowLimit switch
        {
            10 or 100 or 1000 => rowLimit,
            _ => 10
        };
    }
}
