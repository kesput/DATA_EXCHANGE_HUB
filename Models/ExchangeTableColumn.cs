namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeTableColumn(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool HasDefaultValue)
{
    public bool IsIdColumn => IsSystemManagedColumnName(Name, "ID");

    public bool IsCreateTimeColumn =>
        IsSystemManagedColumnName(Name, "CREATE_TIME") ||
        IsSystemManagedColumnName(Name, "CREATED_AT") ||
        IsSystemManagedColumnName(Name, "UPLOADED_AT");

    public bool IsCreateUserColumn =>
        IsSystemManagedColumnName(Name, "CREATE_BY") ||
        IsSystemManagedColumnName(Name, "CREATED_BY") ||
        IsSystemManagedColumnName(Name, "UPLOADED_BY");

    public bool IsUpdateTimeColumn =>
        IsSystemManagedColumnName(Name, "UPDATE_TIME") ||
        IsSystemManagedColumnName(Name, "UPDATED_AT");

    public bool IsUpdateUserColumn =>
        IsSystemManagedColumnName(Name, "UPDATE_BY") ||
        IsSystemManagedColumnName(Name, "UPDATED_BY");

    public bool IsSystemManaged => IsIdColumn || IsCreateTimeColumn || IsCreateUserColumn || IsUpdateTimeColumn || IsUpdateUserColumn;

    public bool IsRowVersion => DataType.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
                                DataType.Equals("rowversion", StringComparison.OrdinalIgnoreCase);

    public bool CanInsert => !IsIdentity && !IsComputed && !IsRowVersion && !IsSystemManaged;

    public bool CanUpdate => !IsIdentity && !IsComputed && !IsRowVersion && !IsSystemManaged;

    public static bool IsSystemManagedColumnName(string? columnName)
    {
        return IsSystemManagedColumnName(columnName, "ID") ||
               IsSystemManagedColumnName(columnName, "CREATE_TIME") ||
               IsSystemManagedColumnName(columnName, "CREATE_BY") ||
               IsSystemManagedColumnName(columnName, "CREATED_AT") ||
               IsSystemManagedColumnName(columnName, "CREATED_BY") ||
               IsSystemManagedColumnName(columnName, "UPLOADED_AT") ||
               IsSystemManagedColumnName(columnName, "UPLOADED_BY") ||
               IsSystemManagedColumnName(columnName, "UPDATE_TIME") ||
               IsSystemManagedColumnName(columnName, "UPDATE_BY") ||
               IsSystemManagedColumnName(columnName, "UPDATED_AT") ||
               IsSystemManagedColumnName(columnName, "UPDATED_BY");
    }

    private static bool IsSystemManagedColumnName(string? columnName, string expectedColumnName)
    {
        return columnName?.Equals(expectedColumnName, StringComparison.OrdinalIgnoreCase) == true;
    }
}


