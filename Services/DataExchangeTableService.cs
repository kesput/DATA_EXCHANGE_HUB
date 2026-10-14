using System.Data;
using System.Globalization;
using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class DataExchangeTableService(
    IConfiguration configuration,
    IOptions<DataExchangeOptions> options) : IDataExchangeTableService
{
    private const string DefaultExchangeLogTableName = "BLS_LOGIT_STAT.dbo.BS_EXCHANGE_LOG";
    private const string UpdateTimeFormat = "yyyy.MM.dd HH:mm:ss";
    private static readonly CultureInfo LithuanianCulture = CultureInfo.GetCultureInfo("lt-LT");
    private readonly DataExchangeOptions _options = options.Value;

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

    public async Task<ExchangeTableData> GetTableDataAsync(
        ExchangeTableAccess tableAccess,
        int rowLimit,
        int pageNumber,
        bool includeRows = true,
        IReadOnlyList<ExchangeTableFilter>? filters = null,
        CancellationToken cancellationToken = default)
    {
        var tableName = NormalizeAllowedTableName(tableAccess);
        var normalizedRowLimit = NormalizeRowLimit(rowLimit);
        var normalizedPageNumber = Math.Max(1, pageNumber);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        var uniqueKeys = await LoadUniqueKeysAsync(connection, tableName, cancellationToken);
        var rowPage = includeRows
            ? await LoadRowsAsync(
                connection,
                tableName,
                columns,
                keyColumn,
                normalizedRowLimit,
                normalizedPageNumber,
                filters,
                cancellationToken)
            : new RowPage([], false);

        return new ExchangeTableData(
            tableAccess.TableName,
            columns,
            uniqueKeys,
            rowPage.Rows,
            keyColumn,
            normalizedRowLimit,
            normalizedPageNumber,
            rowPage.HasNextPage);
    }

    public async Task<int> CountRowsByFilterAsync(
        ExchangeTableAccess tableAccess,
        IReadOnlyList<ExchangeTableFilter> filters,
        CancellationToken cancellationToken = default)
    {
        if (!tableAccess.CanReceive || !tableAccess.CanUpload)
        {
            throw new InvalidOperationException("Blokinio trynimo perziurai reikia perziuros ir ikelimo teisiu.");
        }

        if (filters.Count == 0 || filters.All(filter => !filter.IsActive))
        {
            throw new InvalidOperationException("Blokinio trynimo perziurai privalomas bent vienas aktyvus filtras.");
        }

        var tableName = NormalizeAllowedTableName(tableAccess);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        await using var command = connection.CreateCommand();
        var whereSql = BuildWhereClause(command, columns, filters);
        command.CommandText = $"SELECT COUNT_BIG(1) FROM {tableName}{whereSql}";

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<ExchangeLogEntry>> ListExchangeLogsAsync(
        ExchangeTableAccess tableAccess,
        string actorUserName,
        bool canSeeAllLogs,
        int rowLimit = 25,
        CancellationToken cancellationToken = default)
    {
        var tableName = NormalizeAllowedTableName(tableAccess);

        await using var connection = new SqlConnection(GetConnectionString());
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
            FROM {NormalizeLogTableName()}
            WHERE TABLE_NAME = @TableName
                AND (@CanSeeAllLogs = 1
                    OR ACTOR_USER = @ActorUser
                    OR ACTOR_USER = @ShortActorUser)
            ORDER BY EVENT_AT DESC, LOG_ID DESC
            """;
        command.Parameters.Add("@RowLimit", SqlDbType.Int).Value = Math.Clamp(rowLimit, 1, 100);
        command.Parameters.Add("@TableName", SqlDbType.NVarChar, 776).Value = tableAccess.TableName;
        command.Parameters.Add("@CanSeeAllLogs", SqlDbType.Bit).Value = canSeeAllLogs;
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = NormalizeActorUserName(actorUserName);
        command.Parameters.Add("@ShortActorUser", SqlDbType.NVarChar, 256).Value = GetShortUserName(actorUserName);

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

    public async Task AddRowAsync(
        ExchangeTableAccess tableAccess,
        IFormCollection formData,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (!tableAccess.CanUpload)
        {
            throw new InvalidOperationException("Siai lentelei ikelimo teise nesuteikta.");
        }

        var tableName = NormalizeAllowedTableName(tableAccess);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        var userInsertableColumns = columns
            .Where(column => column.CanInsert &&
                             formData.ContainsKey(column.Name) &&
                             ShouldIncludeInsertValue(column, formData[column.Name].ToString()))
            .ToArray();

        if (userInsertableColumns.Length == 0)
        {
            throw new InvalidOperationException("Nera stulpeliu, kuriuos galima prideti.");
        }

        var columnValues = userInsertableColumns
            .Select(column => new ColumnValue(
                column,
                ToUserInputDbValue(formData[column.Name].ToString())))
            .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName, includeCreateColumns: true))
            .ToArray();
        var columnNames = columnValues
            .Select(columnValue => SqlName.QuoteIdentifier(columnValue.Column.Name))
            .ToArray();
        var parameterNames = columnValues
            .Select((_, index) => $"@p{index}")
            .ToArray();

        var batchId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {tableName}
                ({string.Join(", ", columnNames)})
            OUTPUT INSERTED.*
            VALUES
                ({string.Join(", ", parameterNames)})
            """;

        for (var index = 0; index < columnValues.Length; index++)
        {
            AddColumnParameter(command, parameterNames[index], columnValues[index]);
        }

        var insertedRow = await ExecuteInsertAndReadRowAsync(
            command,
            columns,
            keyColumn,
            cancellationToken);

        await InsertExchangeLogAsync(
            connection,
            transaction,
            batchId,
            "INSERT",
            "WEB_FORM",
            tableAccess.TableName,
            keyColumn,
            insertedRow?.KeyValue,
            actorUserName,
            beforeValues: null,
            afterValues: insertedRow?.Values,
            changedColumns: columnValues.Select(columnValue => columnValue.Column.Name),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> AddRowsAsync(
        ExchangeTableAccess tableAccess,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (!tableAccess.CanUpload)
        {
            throw new InvalidOperationException("Siai lentelei ikelimo teise nesuteikta.");
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        var tableName = NormalizeAllowedTableName(tableAccess);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        var uniqueKeys = await LoadUniqueKeysAsync(connection, tableName, cancellationToken);
        var allowedColumns = columns
            .ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var insertableColumns = columns
            .Where(column => column.CanInsert)
            .ToArray();
        var editableColumns = columns
            .Where(column => column.CanUpdate)
            .ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        if (insertableColumns.Length == 0)
        {
            throw new InvalidOperationException("Sioje lenteleje nera stulpeliu, kuriuos galima pildyti.");
        }

        var invalidColumns = rows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(columnName =>
                !allowedColumns.ContainsKey(columnName) &&
                !ExchangeTableColumn.IsSystemManagedColumnName(columnName))
            .ToArray();

        if (invalidColumns.Length > 0)
        {
            throw new InvalidOperationException(
                $"Faile yra lenteles neatitinkanciu stulpeliu: {string.Join(", ", invalidColumns)}.");
        }

        var insertedRows = 0;
        var batchId = Guid.NewGuid();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (insertableColumns.All(column =>
                    !row.TryGetValue(column.Name, out var value) ||
                    string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            var rowColumns = insertableColumns
                .Where(column =>
                {
                    row.TryGetValue(column.Name, out var value);
                    return ShouldIncludeInsertValue(column, value);
                })
                .ToArray();

            if (rowColumns.Length == 0)
            {
                continue;
            }

            var columnValues = rowColumns
                .Select(column =>
                {
                    row.TryGetValue(column.Name, out var rawValue);
                    return new ColumnValue(column, ToUserInputDbValue(rawValue));
                })
                .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName, includeCreateColumns: true))
                .ToArray();

            var upsertKey = SelectImportUpsertKey(uniqueKeys, allowedColumns, row);
            if (upsertKey is not null &&
                TryBuildUniqueKeyValues(upsertKey, allowedColumns, row, out var keyValues))
            {
                var existingRow = await LoadRowByUniqueKeyAsync(
                    connection,
                    (SqlTransaction)transaction,
                    tableName,
                    columns,
                    keyColumn,
                    keyValues,
                    cancellationToken);

                if (existingRow is not null)
                {
                    var updateColumnValues = row
                        .Where(item => editableColumns.ContainsKey(item.Key) &&
                                       !upsertKey.Columns.Contains(item.Key, StringComparer.OrdinalIgnoreCase) &&
                                       ShouldIncludeInsertValue(editableColumns[item.Key], item.Value))
                        .Select(item => new ColumnValue(editableColumns[item.Key], ToUserInputDbValue(item.Value)))
                        .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName)
                            .Where(columnValue => !upsertKey.Columns.Contains(
                                columnValue.Column.Name,
                                StringComparer.OrdinalIgnoreCase)))
                        .GroupBy(columnValue => columnValue.Column.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.Last())
                        .ToArray();

                    if (updateColumnValues.Length == 0)
                    {
                        continue;
                    }

                    var updatedRow = await ExecuteUpdateAndReadRowAsync(
                        connection,
                        (SqlTransaction)transaction,
                        tableName,
                        columns,
                        keyColumn,
                        keyValues,
                        updateColumnValues,
                        cancellationToken);

                    await InsertExchangeLogAsync(
                        connection,
                        (SqlTransaction)transaction,
                        batchId,
                        "UPDATE",
                        "EXCEL_IMPORT",
                        tableAccess.TableName,
                        keyColumn,
                        updatedRow?.KeyValue ?? existingRow.KeyValue,
                        actorUserName,
                        beforeValues: existingRow.Values,
                        afterValues: updatedRow?.Values,
                        changedColumns: updateColumnValues.Select(columnValue => columnValue.Column.Name),
                        cancellationToken);

                    insertedRows++;
                    continue;
                }
            }

            var columnNames = columnValues
                .Select(columnValue => SqlName.QuoteIdentifier(columnValue.Column.Name))
                .ToArray();
            var parameterNames = columnValues
                .Select((_, index) => $"@p{index}")
                .ToArray();

            await using var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction)transaction;
            command.CommandText = $"""
                INSERT INTO {tableName}
                    ({string.Join(", ", columnNames)})
                OUTPUT INSERTED.*
                VALUES
                    ({string.Join(", ", parameterNames)})
                """;

            for (var index = 0; index < columnValues.Length; index++)
            {
                AddColumnParameter(command, parameterNames[index], columnValues[index]);
            }

            var insertedRow = await ExecuteInsertAndReadRowAsync(
                command,
                columns,
                keyColumn,
                cancellationToken);

            await InsertExchangeLogAsync(
                connection,
                (SqlTransaction)transaction,
                batchId,
                "IMPORT_INSERT",
                "EXCEL_IMPORT",
                tableAccess.TableName,
                keyColumn,
                insertedRow?.KeyValue,
                actorUserName,
                beforeValues: null,
                afterValues: insertedRow?.Values,
                changedColumns: columnValues.Select(columnValue => columnValue.Column.Name),
                cancellationToken);

            insertedRows++;
        }

        await transaction.CommitAsync(cancellationToken);
        return insertedRows;
    }

    public async Task UpdateRowsAsync(
        ExchangeTableAccess tableAccess,
        IReadOnlyDictionary<string, Dictionary<string, string?>> updates,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (!tableAccess.CanUpload)
        {
            throw new InvalidOperationException("Siai lentelei koregavimo teise nesuteikta.");
        }

        if (updates.Count == 0)
        {
            return;
        }

        var tableName = NormalizeAllowedTableName(tableAccess);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        if (string.IsNullOrWhiteSpace(keyColumn))
        {
            throw new InvalidOperationException("Lentele neturi ID arba pirminio rakto stulpelio, todel koregavimas negalimas.");
        }

        var editableColumns = columns
            .Where(column => column.CanUpdate && !column.Name.Equals(keyColumn, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        var batchId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var (keyValue, rowUpdate) in updates)
        {
            var changes = rowUpdate
                .Where(change => editableColumns.ContainsKey(change.Key))
                .ToArray();

            if (changes.Length == 0)
            {
                continue;
            }

            var beforeRow = await LoadRowByKeyAsync(
                connection,
                transaction,
                tableName,
                columns,
                keyColumn,
                keyValue,
                cancellationToken);
            if (beforeRow is null)
            {
                throw new InvalidOperationException("Koreguojamas irasas nerastas arba jau pasalintas.");
            }

            var columnValues = changes
                .Select(change => new ColumnValue(
                    editableColumns[change.Key],
                    ToUserInputDbValue(change.Value)))
                .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName)
                    .Where(columnValue => !columnValue.Column.Name.Equals(
                        keyColumn,
                        StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var setClauses = columnValues
                .Select((columnValue, index) => $"{SqlName.QuoteIdentifier(columnValue.Column.Name)} = @p{index}")
                .ToArray();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE {tableName}
                SET {string.Join(", ", setClauses)}
                WHERE {SqlName.QuoteIdentifier(keyColumn)} = @keyValue
                """;

            for (var index = 0; index < columnValues.Length; index++)
            {
                AddColumnParameter(command, $"@p{index}", columnValues[index]);
            }

            command.Parameters.AddWithValue("@keyValue", keyValue);
            var updatedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (updatedRows == 0)
            {
                throw new InvalidOperationException("Koreguojamas irasas nerastas arba jau pasalintas.");
            }

            var afterRow = await LoadRowByKeyAsync(
                connection,
                transaction,
                tableName,
                columns,
                keyColumn,
                keyValue,
                cancellationToken);

            await InsertExchangeLogAsync(
                connection,
                transaction,
                batchId,
                "UPDATE",
                "WEB_EDIT",
                tableAccess.TableName,
                keyColumn,
                beforeRow.KeyValue,
                actorUserName,
                beforeValues: beforeRow.Values,
                afterValues: afterRow?.Values,
                changedColumns: columnValues.Select(columnValue => columnValue.Column.Name),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteRowAsync(
        ExchangeTableAccess tableAccess,
        string keyValue,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (!tableAccess.CanReceive || !tableAccess.CanUpload)
        {
            throw new InvalidOperationException("Siai lentelei salinimo teise nesuteikta.");
        }

        if (string.IsNullOrWhiteSpace(keyValue))
        {
            throw new InvalidOperationException("Nepavyko nustatyti salinamo iraso rakto.");
        }

        var tableName = NormalizeAllowedTableName(tableAccess);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        if (string.IsNullOrWhiteSpace(keyColumn))
        {
            throw new InvalidOperationException("Lentele neturi ID arba pirminio rakto stulpelio, todel salinimas negalimas.");
        }

        var batchId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var beforeRow = await LoadRowByKeyAsync(
            connection,
            transaction,
            tableName,
            columns,
            keyColumn,
            keyValue,
            cancellationToken);
        if (beforeRow is null)
        {
            throw new InvalidOperationException("Irasas nerastas arba jau pasalintas.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            DELETE FROM {tableName}
            WHERE {SqlName.QuoteIdentifier(keyColumn)} = @keyValue
            """;
        command.Parameters.AddWithValue("@keyValue", keyValue.Trim());

        var deletedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (deletedRows == 0)
        {
            throw new InvalidOperationException("Irasas nerastas arba jau pasalintas.");
        }

        await InsertExchangeLogAsync(
            connection,
            transaction,
            batchId,
            "DELETE",
            "WEB_DELETE",
            tableAccess.TableName,
            keyColumn,
            beforeRow.KeyValue,
            actorUserName,
            beforeValues: beforeRow.Values,
            afterValues: null,
            changedColumns: columns.Select(column => column.Name),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> DeleteRowsByFilterAsync(
        ExchangeTableAccess tableAccess,
        IReadOnlyList<ExchangeTableFilter> filters,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (!tableAccess.CanReceive || !tableAccess.CanUpload)
        {
            throw new InvalidOperationException("Trinti eilutes galima tik turint perziuros ir ikelimo teises.");
        }

        if (filters.Count == 0 || filters.All(filter => !filter.IsActive))
        {
            throw new InvalidOperationException("Blokiniam trynimui privalomas bent vienas aktyvus filtras.");
        }

        var tableName = NormalizeAllowedTableName(tableAccess);

        await using var connection = new SqlConnection(GetConnectionString());
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
                deletedRows.Add(ReadCurrentRow(reader, columns, keyColumn));
            }
        }

        foreach (var deletedRow in deletedRows)
        {
            await InsertExchangeLogAsync(
                connection,
                transaction,
                batchId,
                "DELETE",
                "WEB_FILTER_DELETE",
                tableAccess.TableName,
                keyColumn,
                deletedRow.KeyValue,
                actorUserName,
                beforeValues: deletedRow.Values,
                afterValues: null,
                changedColumns: columns.Select(column => column.Name),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return deletedRows.Count;
    }

    public async Task RevertLogEntryAsync(
        ExchangeTableAccess tableAccess,
        long logId,
        string actorUserName,
        bool canRevertAnyUser,
        CancellationToken cancellationToken = default)
    {
        if (!tableAccess.CanReceive || !tableAccess.CanUpload)
        {
            throw new InvalidOperationException("Atstatyti irasus galima tik turint perziuros ir ikelimo teises.");
        }

        var tableName = NormalizeAllowedTableName(tableAccess);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var columns = await LoadColumnsAsync(connection, tableName, cancellationToken);
        var keyColumn = await LoadKeyColumnAsync(connection, tableName, columns, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var log = await LoadLogDetailsAsync(connection, transaction, logId, cancellationToken);

        if (!SqlName.NormalizeTableName(log.TableName, string.Empty)
                .Equals(SqlName.NormalizeTableName(tableAccess.TableName, string.Empty), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Audit irasas nepriklauso pasirinktai lentelei.");
        }

        if (log.IsReverted)
        {
            throw new InvalidOperationException("Sis audit irasas jau atstatytas.");
        }

        if (!canRevertAnyUser && !IsSameActor(log.ActorUser, actorUserName))
        {
            throw new InvalidOperationException("Galite atstatyti tik savo atliktus pakeitimus.");
        }

        long revertSourceLogId;
        if (log.ActionType.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            revertSourceLogId = await RevertUpdatedRowAsync(
                connection,
                transaction,
                tableAccess,
                tableName,
                columns,
                keyColumn,
                log,
                actorUserName,
                cancellationToken);
        }
        else if (log.ActionType.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            revertSourceLogId = await RestoreDeletedRowAsync(
                connection,
                transaction,
                tableAccess,
                tableName,
                columns,
                keyColumn,
                log,
                actorUserName,
                cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("Atstatymas galimas tik UPDATE arba DELETE veiksmams.");
        }

        await MarkLogRevertedAsync(
            connection,
            transaction,
            log.LogId,
            revertSourceLogId,
            actorUserName,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
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
            SELECT
                LOG_ID,
                BATCH_ID,
                ACTION_TYPE,
                SOURCE,
                TABLE_NAME,
                KEY_COLUMN,
                KEY_VALUE,
                ACTOR_USER,
                ROW_DATA_BEFORE,
                ROW_DATA_AFTER,
                IS_REVERTED
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
            reader.GetInt64(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            DeserializeRowValues(reader.IsDBNull(8) ? null : reader.GetString(8)),
            DeserializeRowValues(reader.IsDBNull(9) ? null : reader.GetString(9)),
            reader.GetBoolean(10));
    }

    private async Task<long> RevertUpdatedRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ExchangeTableAccess tableAccess,
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
            .Where(column => column.CanUpdate && !column.Name.Equals(keyColumn, StringComparison.OrdinalIgnoreCase))
            .Where(column => log.BeforeValues.ContainsKey(column.Name))
            .Select(column => new ColumnValue(column, ToUserInputDbValue(log.BeforeValues[column.Name])))
            .ToArray();

        if (editableColumns.Length == 0)
        {
            throw new InvalidOperationException("Nera stulpeliu, kuriuos galima atstatyti.");
        }

        var beforeRow = await LoadRowByKeyAsync(
            connection,
            transaction,
            tableName,
            columns,
            keyColumn,
            log.KeyValue,
            cancellationToken);
        if (beforeRow is null)
        {
            throw new InvalidOperationException("Atstatomas irasas nerastas arba jau pasalintas.");
        }

        var columnValues = editableColumns
            .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName))
            .ToArray();
        var setClauses = columnValues
            .Select((columnValue, index) => $"{SqlName.QuoteIdentifier(columnValue.Column.Name)} = @p{index}")
            .ToArray();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {tableName}
            SET {string.Join(", ", setClauses)}
            WHERE {SqlName.QuoteIdentifier(keyColumn)} = @KeyValue
            """;

        for (var index = 0; index < columnValues.Length; index++)
        {
            AddColumnParameter(command, $"@p{index}", columnValues[index]);
        }

        command.Parameters.Add("@KeyValue", SqlDbType.NVarChar, 4000).Value = log.KeyValue;
        var updatedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updatedRows == 0)
        {
            throw new InvalidOperationException("Atstatomas irasas nerastas arba jau pasalintas.");
        }

        var afterRow = await LoadRowByKeyAsync(
            connection,
            transaction,
            tableName,
            columns,
            keyColumn,
            log.KeyValue,
            cancellationToken);

        return await InsertExchangeLogAsync(
            connection,
            transaction,
            Guid.NewGuid(),
            "UPDATE",
            "WEB_REVERT",
            tableAccess.TableName,
            keyColumn,
            beforeRow.KeyValue,
            actorUserName,
            beforeValues: beforeRow.Values,
            afterValues: afterRow?.Values,
            changedColumns: columnValues.Select(columnValue => columnValue.Column.Name),
            cancellationToken);
    }

    private async Task<long> RestoreDeletedRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ExchangeTableAccess tableAccess,
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
            .Where(column => column.CanInsert)
            .Where(column => log.BeforeValues.TryGetValue(column.Name, out var value) &&
                             ShouldIncludeInsertValue(column, value))
            .Select(column => new ColumnValue(column, ToUserInputDbValue(log.BeforeValues[column.Name])))
            .Concat(BuildAutomaticAuditColumnValues(columns, actorUserName, includeCreateColumns: true))
            .ToArray();

        if (restoredValues.Length == 0)
        {
            throw new InvalidOperationException("Nera stulpeliu, kuriuos galima atkurti.");
        }

        var columnNames = restoredValues
            .Select(columnValue => SqlName.QuoteIdentifier(columnValue.Column.Name))
            .ToArray();
        var parameterNames = restoredValues
            .Select((_, index) => $"@p{index}")
            .ToArray();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {tableName}
                ({string.Join(", ", columnNames)})
            OUTPUT INSERTED.*
            VALUES
                ({string.Join(", ", parameterNames)})
            """;

        for (var index = 0; index < restoredValues.Length; index++)
        {
            AddColumnParameter(command, parameterNames[index], restoredValues[index]);
        }

        var insertedRow = await ExecuteInsertAndReadRowAsync(
            command,
            columns,
            keyColumn,
            cancellationToken);

        return await InsertExchangeLogAsync(
            connection,
            transaction,
            Guid.NewGuid(),
            "INSERT",
            "WEB_REVERT",
            tableAccess.TableName,
            keyColumn,
            insertedRow?.KeyValue,
            actorUserName,
            beforeValues: null,
            afterValues: insertedRow?.Values,
            changedColumns: restoredValues.Select(columnValue => columnValue.Column.Name),
            cancellationToken);
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
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = NormalizeActorUserName(actorUserName);
        command.Parameters.Add("@RevertSourceLogId", SqlDbType.BigInt).Value = revertSourceLogId;
        command.Parameters.Add("@LogId", SqlDbType.BigInt).Value = logId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ExchangeTableRow?> ExecuteInsertAndReadRowAsync(
        SqlCommand command,
        IReadOnlyList<ExchangeTableColumn> columns,
        string? keyColumn,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadCurrentRow(reader, columns, keyColumn)
            : null;
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
        return await reader.ReadAsync(cancellationToken)
            ? ReadCurrentRow(reader, columns, keyColumn)
            : null;
    }

    private static async Task<ExchangeTableRow?> LoadRowByUniqueKeyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tableName,
        IReadOnlyList<ExchangeTableColumn> columns,
        string? keyColumn,
        IReadOnlyList<ColumnValue> keyValues,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var whereSql = BuildUniqueKeyWhereClause(command, keyValues, "@key");
        command.CommandText = $"""
            SELECT TOP (1) *
            FROM {tableName}
            WHERE {whereSql}
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadCurrentRow(reader, columns, keyColumn)
            : null;
    }

    private static async Task<ExchangeTableRow?> ExecuteUpdateAndReadRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tableName,
        IReadOnlyList<ExchangeTableColumn> columns,
        string? keyColumn,
        IReadOnlyList<ColumnValue> keyValues,
        IReadOnlyList<ColumnValue> updateValues,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var setSql = updateValues
            .Select((columnValue, index) =>
            {
                var parameterName = $"@p{index}";
                AddColumnParameter(command, parameterName, columnValue);
                return $"{SqlName.QuoteIdentifier(columnValue.Column.Name)} = {parameterName}";
            })
            .ToArray();
        var whereSql = BuildUniqueKeyWhereClause(command, keyValues, "@key");

        command.CommandText = $"""
            UPDATE {tableName}
            SET {string.Join(", ", setSql)}
            OUTPUT INSERTED.*
            WHERE {whereSql}
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadCurrentRow(reader, columns, keyColumn)
            : null;
    }

    private static string BuildUniqueKeyWhereClause(
        SqlCommand command,
        IReadOnlyList<ColumnValue> keyValues,
        string parameterPrefix)
    {
        return string.Join(" AND ", keyValues.Select((columnValue, index) =>
        {
            var parameterName = $"{parameterPrefix}{index}";
            AddColumnParameter(command, parameterName, columnValue);
            return $"{SqlName.QuoteIdentifier(columnValue.Column.Name)} = {parameterName}";
        }));
    }

    private static ExchangeTableRow ReadCurrentRow(
        SqlDataReader reader,
        IReadOnlyList<ExchangeTableColumn> columns,
        string? keyColumn)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            var rawValue = reader[column.Name];
            values[column.Name] = rawValue == DBNull.Value
                ? string.Empty
                : Convert.ToString(rawValue, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        }

        var keyValue = !string.IsNullOrWhiteSpace(keyColumn) && values.TryGetValue(keyColumn, out var value)
            ? value
            : null;

        return new ExchangeTableRow(values, keyValue);
    }

    private async Task<long> InsertExchangeLogAsync(
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
                (BATCH_ID,
                 ACTION_TYPE,
                 SOURCE,
                 TABLE_NAME,
                 KEY_COLUMN,
                 KEY_VALUE,
                 ACTOR_USER,
                 CHANGED_COLUMNS,
                 ROW_DATA_BEFORE,
                 ROW_DATA_AFTER)
            OUTPUT INSERTED.LOG_ID
            VALUES
                (@BatchId,
                 @ActionType,
                 @Source,
                 @TableName,
                 @KeyColumn,
                 @KeyValue,
                 @ActorUser,
                 @ChangedColumns,
                 @RowDataBefore,
                 @RowDataAfter)
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
            ToDbValue(SerializeRowValues(beforeValues));
        command.Parameters.Add("@RowDataAfter", SqlDbType.NVarChar, -1).Value =
            ToDbValue(SerializeRowValues(afterValues));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private string NormalizeLogTableName()
    {
        return SqlName.NormalizeTableName(_options.ExchangeLogTableName, DefaultExchangeLogTableName);
    }

    private static string? SerializeChangedColumns(IEnumerable<string> changedColumns)
    {
        var columns = changedColumns
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return columns.Length == 0 ? null : JsonSerializer.Serialize(columns);
    }

    private static string? SerializeRowValues(IReadOnlyDictionary<string, string>? values)
    {
        return values is null ? null : JsonSerializer.Serialize(values);
    }

    private static IReadOnlyDictionary<string, string>? DeserializeRowValues(string? values)
    {
        if (string.IsNullOrWhiteSpace(values))
        {
            return null;
        }

        var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(values)
            ?? [];
        return new Dictionary<string, string>(deserialized, StringComparer.OrdinalIgnoreCase);
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
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
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5)));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Lentele nerasta arba neturi stulpeliu: {tableName}");
        }

        return columns;
    }

    private async Task<string?> LoadKeyColumnAsync(
        SqlConnection connection,
        string tableName,
        IReadOnlyList<ExchangeTableColumn> columns,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) MAX(c.name)
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
            GROUP BY i.index_id, kc.type
            HAVING COUNT(*) = 1
            ORDER BY
                CASE WHEN kc.type = 'PK' THEN 0 ELSE 1 END,
                i.index_id
            """;
        command.Parameters.Add("@TableName", SqlDbType.NVarChar, 776).Value = tableName;

        var uniqueKey = await command.ExecuteScalarAsync(cancellationToken);
        if (uniqueKey is string uniqueKeyColumn && !string.IsNullOrWhiteSpace(uniqueKeyColumn))
        {
            return uniqueKeyColumn;
        }

        return columns.FirstOrDefault(column =>
            column.Name.Equals("ID", StringComparison.OrdinalIgnoreCase))?.Name;
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

    private static ExchangeUniqueKey? SelectImportUpsertKey(
        IReadOnlyList<ExchangeUniqueKey> uniqueKeys,
        IReadOnlyDictionary<string, ExchangeTableColumn> columns,
        IReadOnlyDictionary<string, string?> row)
    {
        return uniqueKeys
            .Where(key => key.Columns.Count > 0 &&
                          key.Columns.All(columnName =>
                              columns.ContainsKey(columnName) &&
                              row.TryGetValue(columnName, out var value) &&
                              !string.IsNullOrWhiteSpace(value)))
            .OrderBy(key => key.Type.Equals("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(key => key.Columns.Count)
            .FirstOrDefault();
    }

    private static bool TryBuildUniqueKeyValues(
        ExchangeUniqueKey key,
        IReadOnlyDictionary<string, ExchangeTableColumn> columns,
        IReadOnlyDictionary<string, string?> row,
        out IReadOnlyList<ColumnValue> keyValues)
    {
        var values = new List<ColumnValue>();
        foreach (var columnName in key.Columns)
        {
            if (!columns.TryGetValue(columnName, out var column) ||
                !row.TryGetValue(columnName, out var rawValue) ||
                string.IsNullOrWhiteSpace(rawValue))
            {
                keyValues = [];
                return false;
            }

            values.Add(new ColumnValue(column, ToUserInputDbValue(rawValue)));
        }

        keyValues = values;
        return true;
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
        var orderColumn = !string.IsNullOrWhiteSpace(keyColumn)
            ? keyColumn
            : columns.First().Name;

        await using var command = connection.CreateCommand();
        var whereSql = BuildWhereClause(command, columns, filters);
        if (rowLimit == int.MaxValue)
        {
            command.CommandText = $"""
                SELECT *
                FROM {tableName}
                {whereSql}
                ORDER BY {SqlName.QuoteIdentifier(orderColumn)} DESC
                """;
        }
        else
        {
            var offset = (Math.Max(1, pageNumber) - 1) * rowLimit;
            var fetch = rowLimit + 1;
            command.CommandText = $"""
                SELECT *
                FROM {tableName}
                {whereSql}
                ORDER BY {SqlName.QuoteIdentifier(orderColumn)} DESC
                OFFSET @Offset ROWS
                FETCH NEXT @Fetch ROWS ONLY
                """;
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            command.Parameters.Add("@Fetch", SqlDbType.Int).Value = fetch;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ExchangeTableRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns)
            {
                var rawValue = reader[column.Name];
                values[column.Name] = rawValue == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(rawValue, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
            }

            var keyValue = !string.IsNullOrWhiteSpace(keyColumn) && values.TryGetValue(keyColumn, out var value)
                ? value
                : null;

            rows.Add(new ExchangeTableRow(values, keyValue));
        }

        var hasNextPage = rowLimit != int.MaxValue && rows.Count > rowLimit;
        if (hasNextPage)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new RowPage(rows, hasNextPage);
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

        var allowedColumns = columns.ToDictionary(
            column => column.Name,
            StringComparer.OrdinalIgnoreCase);
        var clauses = new List<string>();
        var parameterIndex = 0;

        foreach (var filter in filters.Where(filter => filter.IsActive))
        {
            if (!allowedColumns.TryGetValue(filter.ColumnName, out var column))
            {
                throw new InvalidOperationException($"Netinkamas filtro stulpelis: {filter.ColumnName}");
            }

            var connectorSql = clauses.Count == 0
                ? string.Empty
                : GetFilterConnectorSql(filter.Connector);
            var clause = BuildFilterClause(command, column, filter, ref parameterIndex);

            clauses.Add(string.IsNullOrWhiteSpace(connectorSql)
                ? clause
                : $"{connectorSql} {clause}");
        }

        return clauses.Count == 0
            ? string.Empty
            : $" WHERE {string.Join(" ", clauses)}";
    }

    private static string BuildFilterClause(
        SqlCommand command,
        ExchangeTableColumn column,
        ExchangeTableFilter filter,
        ref int parameterIndex)
    {
        var columnName = SqlName.QuoteIdentifier(column.Name);
        var normalizedOperator = NormalizeFilterOperator(filter.Operator);

        if (normalizedOperator.Equals(ExchangeTableFilter.InOperator, StringComparison.OrdinalIgnoreCase))
        {
            var parameterNames = new List<string>();
            foreach (var value in SplitFilterValues(filter.Value))
            {
                parameterNames.Add(AddFilterParameter(command, column, ref parameterIndex, value));
            }

            if (parameterNames.Count == 0)
            {
                throw new InvalidOperationException("IN filtrui reikia bent vienos reiksmes.");
            }

            return $"{columnName} IN ({string.Join(", ", parameterNames)})";
        }

        if (normalizedOperator.Equals(ExchangeTableFilter.BetweenOperator, StringComparison.OrdinalIgnoreCase))
        {
            var values = SplitBetweenValues(filter.Value);
            if (values.Length != 2)
            {
                throw new InvalidOperationException("BETWEEN filtrui reikia dvieju reiksmiu, pvz. 10..20 arba 2026-01-01, 2026-01-31.");
            }

            var fromParameter = AddFilterParameter(command, column, ref parameterIndex, values[0]);
            var toParameter = AddFilterParameter(command, column, ref parameterIndex, values[1]);

            return $"{columnName} BETWEEN {fromParameter} AND {toParameter}";
        }

        var parameterName = AddFilterParameter(
            command,
            column,
            ref parameterIndex,
            normalizedOperator.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase)
                ? NormalizeLikeValue(filter.Value)
                : filter.Value,
            convertValue: !normalizedOperator.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase));

        var operatorSql = normalizedOperator switch
        {
            ExchangeTableFilter.EqualsOperator => "=",
            ExchangeTableFilter.NotEqualsOperator => "<>",
            ExchangeTableFilter.LikeOperator => "LIKE",
            ExchangeTableFilter.GreaterThanOperator => ">",
            ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
            ExchangeTableFilter.LessThanOperator => "<",
            ExchangeTableFilter.LessThanOrEqualOperator => "<=",
            _ => throw new InvalidOperationException("Netinkamas filtro operatorius.")
        };

        return $"{columnName} {operatorSql} {parameterName}";
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

        if (filterOperator.Equals(ExchangeTableFilter.BetweenOperator, StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.BetweenOperator;
        }

        if (filterOperator.Equals(ExchangeTableFilter.GreaterThanOperator, StringComparison.OrdinalIgnoreCase) ||
            filterOperator.Equals(">", StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.GreaterThanOperator;
        }

        if (filterOperator.Equals(ExchangeTableFilter.GreaterThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) ||
            filterOperator.Equals(">=", StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.GreaterThanOrEqualOperator;
        }

        if (filterOperator.Equals(ExchangeTableFilter.LessThanOperator, StringComparison.OrdinalIgnoreCase) ||
            filterOperator.Equals("<", StringComparison.OrdinalIgnoreCase))
        {
            return ExchangeTableFilter.LessThanOperator;
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
        ExchangeTableColumn column,
        ref int parameterIndex,
        string rawValue,
        bool convertValue = true)
    {
        var parameterName = $"@filter{parameterIndex++}";
        command.Parameters.AddWithValue(
            parameterName,
            convertValue ? ConvertColumnValue(column, rawValue) : rawValue.Trim());
        return parameterName;
    }

    private static IReadOnlyList<string> SplitFilterValues(string rawValue)
    {
        return rawValue
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string[] SplitBetweenValues(string rawValue)
    {
        var separator = rawValue.Contains("..", StringComparison.Ordinal)
            ? new[] { ".." }
            : new[] { ",", ";" };

        return rawValue
            .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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

    private string NormalizeAllowedTableName(ExchangeTableAccess tableAccess)
    {
        if (!tableAccess.CanReceive && !tableAccess.CanUpload)
        {
            throw new InvalidOperationException("Siai lentelei teises nesuteiktos.");
        }

        return SqlName.NormalizeTableName(tableAccess.TableName, string.Empty);
    }

    private static int NormalizeRowLimit(int rowLimit)
    {
        return rowLimit switch
        {
            10 or 100 or 1000 or int.MaxValue => rowLimit,
            _ => 10
        };
    }

    private static bool ShouldIncludeInsertValue(ExchangeTableColumn column, string? rawValue)
    {
        return !column.HasDefaultValue || !string.IsNullOrWhiteSpace(rawValue);
    }

    private static IReadOnlyList<ColumnValue> BuildAutomaticAuditColumnValues(
        IReadOnlyList<ExchangeTableColumn> columns,
        string actorUserName,
        bool includeCreateColumns = false)
    {
        var updateTime = DateTime.Now.ToString(UpdateTimeFormat, CultureInfo.InvariantCulture);
        var timestamp = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
        var normalizedActor = NormalizeActorUserName(actorUserName);

        return columns
            .Where(column => CanSetAutomaticAuditColumn(column, includeCreateColumns))
            .Select(column => new ColumnValue(
                column,
                GetAutomaticAuditColumnValue(column, updateTime, timestamp, normalizedActor)))
            .ToArray();
    }

    private static object GetAutomaticAuditColumnValue(
        ExchangeTableColumn column,
        string updateTime,
        string timestamp,
        string normalizedActor)
    {
        if (column.IsCreateTimeColumn || column.IsUpdateTimeColumn)
        {
            return column.IsUpdateTimeColumn && column.Name.Equals("UPDATE_TIME", StringComparison.OrdinalIgnoreCase)
                ? updateTime
                : timestamp;
        }

        return normalizedActor;
    }

    private static bool CanSetAutomaticAuditColumn(ExchangeTableColumn column, bool includeCreateColumns)
    {
        return !column.IsIdentity &&
               !column.IsComputed &&
               !column.IsRowVersion &&
               (column.IsUpdateTimeColumn ||
                column.IsUpdateUserColumn ||
                (includeCreateColumns && (column.IsCreateTimeColumn || column.IsCreateUserColumn)));
    }

    private static object ToUserInputDbValue(string? rawValue)
    {
        return string.IsNullOrWhiteSpace(rawValue) ? DBNull.Value : rawValue.Trim();
    }

    private static void AddColumnParameter(
        SqlCommand command,
        string parameterName,
        ColumnValue columnValue)
    {
        command.Parameters.AddWithValue(
            parameterName,
            ConvertColumnValue(columnValue.Column, columnValue.Value));
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

        var dataType = column.DataType.ToLowerInvariant();
        if (dataType == "bit")
        {
            return ParseBooleanValue(value);
        }

        if (IsIntegerSqlType(dataType))
        {
            return ParseIntegerValue(value, column.Name);
        }

        if (IsDecimalSqlType(dataType))
        {
            return ParseDecimalValue(value, column.Name);
        }

        if (dataType is "date" or "datetime" or "datetime2" or "smalldatetime")
        {
            return ParseDateTimeValue(value, column.Name);
        }

        if (dataType == "datetimeoffset")
        {
            return ParseDateTimeOffsetValue(value, column.Name);
        }

        return IsTextSqlType(dataType)
            ? NormalizeDottedNumericText(value)
            : value;
    }

    private static bool IsTextSqlType(string dataType)
    {
        return dataType is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";
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

    private static bool ParseBooleanValue(string value)
    {
        if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("taip", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("ne", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new InvalidOperationException($"Netinkama bit stulpelio reiksme: {value}");
    }

    private static long ParseIntegerValue(string value, string columnName)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var integerValue) ||
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue))
        {
            return integerValue;
        }

        var decimalValue = ParseDecimalValue(value, columnName);
        return decimal.ToInt64(decimalValue);
    }

    private static decimal ParseDecimalValue(string value, string columnName)
    {
        if (decimal.TryParse(value, NumberStyles.Number, LithuanianCulture, out var decimalValue) ||
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimalValue) ||
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out decimalValue) ||
            decimal.TryParse(
                NormalizeDecimalSeparators(value),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimalValue))
        {
            return decimalValue;
        }

        throw new InvalidOperationException($"Nepavyko konvertuoti stulpelio {columnName} reiksmes i skaiciu: {value}");
    }

    private static string NormalizeDecimalSeparators(string value)
    {
        var normalized = value
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Trim();
        var lastComma = normalized.LastIndexOf(',');
        var lastDot = normalized.LastIndexOf('.');

        if (lastComma >= 0 && lastComma > lastDot)
        {
            return normalized
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Replace(",", ".", StringComparison.Ordinal);
        }

        if (lastDot >= 0 && lastDot > lastComma)
        {
            return normalized.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        return normalized.Replace(",", ".", StringComparison.Ordinal);
    }

    private static DateTime ParseDateTimeValue(string value, string columnName)
    {
        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateTimeValue) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTimeValue))
        {
            return dateTimeValue;
        }

        if (TryParseExcelSerialDate(value, out dateTimeValue))
        {
            return dateTimeValue;
        }

        throw new InvalidOperationException($"Nepavyko konvertuoti stulpelio {columnName} reiksmes i data: {value}");
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

    private static DateTimeOffset ParseDateTimeOffsetValue(string value, string columnName)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateTimeOffsetValue) ||
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTimeOffsetValue))
        {
            return dateTimeOffsetValue;
        }

        return new DateTimeOffset(ParseDateTimeValue(value, columnName));
    }

    private static bool IsIntegerSqlType(string dataType)
    {
        return dataType is "tinyint" or "smallint" or "int" or "bigint";
    }

    private static bool IsDecimalSqlType(string dataType)
    {
        return dataType is "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real";
    }

    private static string NormalizeActorUserName(string actorUserName)
    {
        return string.IsNullOrWhiteSpace(actorUserName) ? "unknown" : actorUserName.Trim();
    }

    private static string GetShortUserName(string actorUserName)
    {
        var normalized = NormalizeActorUserName(actorUserName);
        return normalized.Contains('\\', StringComparison.Ordinal)
            ? normalized[(normalized.LastIndexOf('\\') + 1)..]
            : normalized;
    }

    private static bool IsSameActor(string leftActorUserName, string rightActorUserName)
    {
        var left = NormalizeActorUserName(leftActorUserName);
        var right = NormalizeActorUserName(rightActorUserName);

        return left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
               left.Equals(GetShortUserName(right), StringComparison.OrdinalIgnoreCase) ||
               GetShortUserName(left).Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    private string GetConnectionString()
    {
        return configuration.GetConnectionString(_options.ConnectionStringName)
            ?? throw new InvalidOperationException("SQL Server prisijungimo eilute nesukonfiguruota.");
    }
}


