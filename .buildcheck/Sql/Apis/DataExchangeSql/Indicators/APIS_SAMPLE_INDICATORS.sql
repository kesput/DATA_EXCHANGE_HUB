SELECT
    CAST(N'Orders' AS nvarchar(100)) AS Metric,
    CAST(128 AS int) AS Value
UNION ALL
SELECT
    CAST(N'Lines' AS nvarchar(100)) AS Metric,
    CAST(842 AS int) AS Value
UNION ALL
SELECT
    CAST(N'Errors' AS nvarchar(100)) AS Metric,
    CAST(7 AS int) AS Value;
