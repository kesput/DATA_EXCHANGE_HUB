-- SQL_DESCRIPTION: Likuciai pakavimo (perpakavimo) sandeliuose: visi *4 (pakavimo zona) ir isoriniai VBB, VBF, VBG, VBU


SELECT
    s.PREFSTOCK,
    WAREHOUSE_TYPE = CASE
                         WHEN RIGHT(s.PREFSTOCK, 1) = '4' THEN 'REPACK ZONE (*4)'
                         ELSE 'EXTERNAL REPACK'
                     END,
    WAREHOUSE_NAME = pd.DESCRIPTION,
    s.INF_PREK,
    p.PRODNAMELT,
    p.KORG,
    s.STOCK,
    s.ORDSTOCK,
    s.RESERVE,
    FREE_QTY = s.STOCK - s.ORDSTOCK - s.RESERVE
FROM SNTXDATA.dbo.STOCK AS s WITH (NOLOCK)
LEFT JOIN SNTXDATA.dbo.PREFDESC AS pd WITH (NOLOCK)
       ON pd.FIRMID = s.FIRMID
      AND pd.PREFMAIN = s.PREFMAIN
      AND pd.PREFSTOCK = LEFT(s.PREFSTOCK, 2)
      AND pd.ACTIVE = 1
      AND pd.PREF IN ('VBM', 'VBL')
LEFT JOIN QLIKDATA.dbo.PRODUCTH AS p WITH (NOLOCK)
       ON p.FIRMID = s.FIRMID
      AND p.INF_PREK = s.INF_PREK
      AND p.ACTIVE = 1
      AND p.[SID] IN ('SNT', 'LOG')
WHERE s.FIRMID = 'SA'
  AND (RIGHT(s.PREFSTOCK, 1) = '4' OR s.PREFSTOCK IN ('VBB', 'VBF', 'VBG', 'VBU'))
  AND s.STOCK <> 0
ORDER BY s.PREFSTOCK, p.KORG, s.INF_PREK;