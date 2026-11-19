internal static class EeAdjustmentChecks
{
    // Execute the report's real SELECT / UNION / WHERE using inline source rows.
    // No SQL tables are created or changed by this regression fixture.
    internal static string WithInlineSources(string sql)
    {
        const string movements = """
(SELECT sample.CreatedAt AS created_at, sample.Item AS item_code,
    N'test-message' AS message_id, N'W1' AS warehouse_id,
    N'FROM' AS from_erp_warehouse_code, N'TO' AS to_erp_warehouse_code,
    N'W1' AS erp_warehouse_code, N'Fixture' AS item_description,
    N'LOCATION' AS from_location_code, N'LOCATION' AS location_code,
    N'1,500' AS moved_quantity, N'3.500' AS quantity_after, N'2.000' AS quantity_before,
    N'GOOD' AS from_inventory_status, N'GOOD' AS to_inventory_status, N'GOOD' AS inventory_status,
    N'fixture' AS created_by_user_id, N'OWNER' AS owner_item_code,
    CAST(NULL AS date) AS from_expiration_date, CAST(NULL AS date) AS expiration_date,
    N'EE' AS company_id, N'TEST' AS reason_code
 FROM (VALUES
    (N'before', N'2026-11-18T23:59:59.999999999'),
    (N'start',  N'2026-11-19T00:00:00.000000000'),
    (N'middle', N'2026-11-19T12:34:56.123456789'),
    (N'last',   N'2026-11-19T23:59:59.999999999'),
    (N'after',  N'2026-11-20T00:00:00.000000000')
 ) AS sample(Item, CreatedAt))
""";
        const string documents = """
(SELECT N'test-message' AS message_id, N'FIXTURE' AS document_type,
    N'FIXTURE' AS atlas_movement_number, N'FIXTURE' AS atlas_adjustment_number)
""";
        return sql
            .Replace("[BLSTXSTAT1].BLS_LOGIT_STAT.dbo.EE_movements_STATUS_CHANGE AS M WITH (NOLOCK)", movements + " AS M", StringComparison.OrdinalIgnoreCase)
            .Replace("[BLSTXSTAT1].BLS_LOGIT_STAT.dbo.EE_adjustment_internal AS M WITH (NOLOCK)", movements + " AS M", StringComparison.OrdinalIgnoreCase)
            .Replace("[BLSTXSTAT1].BLS_LOGIT_STAT.dbo.EE_movement_mex AS A WITH (NOLOCK)", documents + " AS A", StringComparison.OrdinalIgnoreCase)
            .Replace("[BLSTXSTAT1].BLS_LOGIT_STAT.dbo.EE_adjustments_mex AS A WITH (NOLOCK)", documents + " AS A", StringComparison.OrdinalIgnoreCase);
    }
}
