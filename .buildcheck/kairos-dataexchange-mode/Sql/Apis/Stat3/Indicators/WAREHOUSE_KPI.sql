-- SQL_DESCRIPTION: Sandeliu KPI pagal planuojama transportavimo data. Auto indikatoriu vaizdas APIS skiltyje Indikatoriai.
-- APIS_VIEW: auto


--DECLARE @DateFrom date = '2026-07-16';
--DECLARE @DateTo   date = DATEADD(DAY, 1, @DateFrom);



DECLARE @Today date = CAST(GETDATE() AS date);

-- 0 = pirmadienis, 1 = antradienis, ..., 6 = sekmadienis
DECLARE @WeekdayNo int = DATEDIFF(DAY, 0, @Today) % 7;

DECLARE @DateFrom date = DATEADD(DAY, 1, @Today);

DECLARE @DateTo date =
    CASE
        -- Pirmadienis–ketvirtadienis:
        -- imama tik rytojaus diena
        WHEN @WeekdayNo BETWEEN 0 AND 3
            THEN DATEADD(DAY, 2, @Today)

        -- Penktadienis–sekmadienis:
        -- imama nuo rytojaus iki antradienio pradžios,
        -- todėl įtraukiamos visos likusios dienos ir pirmadienis
        WHEN @WeekdayNo BETWEEN 4 AND 6
            THEN DATEADD(DAY, (7 - @WeekdayNo) + 1, @Today)
    END;



DROP TABLE IF EXISTS #FilteredVoc;
DROP TABLE IF EXISTS #FilteredRows;
DROP TABLE IF EXISTS #FilteredBase;

------------------------------------------------------------
-- 1. Data + transporto grupė + užsakymas viename etape
------------------------------------------------------------

SELECT
    trp.ORDSTATID,
    trp.TRPGROUP,
    trp.DVAZNEW,
    os.FIRMID,
    os.ADDTIME,

    ORG =
        CASE
            WHEN op.ORGID = 47 THEN 'PCC'
            ELSE 'CUST'
        END
INTO #FilteredBase
FROM SNTXDATA.dbo.ORDSTATTRP AS trp WITH (NOLOCK)
INNER JOIN SNTXDATA.dbo.ORDSTAT AS os WITH (NOLOCK) ON os.ORDSTATID = trp.ORDSTATID  AND os.FIRMID = 'SA'
LEFT JOIN SNTXDATA.dbo.ORGPOS AS op WITH (NOLOCK) ON op.POSID = os.POSID
WHERE trp.DVAZNEW >= @DateFrom
  AND trp.DVAZNEW <  @DateTo
  AND (
      os.ORDERSTATUS <> 9
      OR os.ORDERSTATUS IS NULL
  )
OPTION (RECOMPILE);
CREATE CLUSTERED INDEX IX_FilteredBase
ON #FilteredBase
(
    ORDSTATID,
    TRPGROUP,
    FIRMID
);
------------------------------------------------------------
-- 2. Tik reikalingos užsakymo eilutės
------------------------------------------------------------
SELECT
    rw.ORDSTATID,
    rw.TRPGROUP,
    rw.FIRMID,
    rw.PRODUCTID,
    rw.PREFSTOCK,
    rw.AMOUNTORIG
INTO #FilteredRows
FROM #FilteredBase AS fb
INNER JOIN SNTXDATA.dbo.ORDSTATROW AS rw WITH (NOLOCK)
    ON rw.ORDSTATID = fb.ORDSTATID
   AND rw.TRPGROUP  = fb.TRPGROUP
   AND rw.FIRMID    = fb.FIRMID
WHERE rw.AMOUNT <> 0
  AND rw.PREFSTOCK LIKE 'V%';
CREATE CLUSTERED INDEX IX_FilteredRows
ON #FilteredRows
(
    ORDSTATID,
    TRPGROUP,
    FIRMID
);
CREATE NONCLUSTERED INDEX IX_FilteredRows_Product
ON #FilteredRows
(
    PRODUCTID
);
------------------------------------------------------------
-- 3. Tik reikalingos VOCPREK prekės
------------------------------------------------------------
SELECT
    voc.PRODUCTID,
    voc.INF_PREK,
    voc.TLIC,
    voc.TVNT,
    voc.DNET,
    voc.BRUTTO,
    voc.KIEKD,
    voc.KIEKP
INTO #FilteredVoc
FROM SNTXDATA.dbo.VOCPREK AS voc WITH (NOLOCK)
INNER JOIN
(
    SELECT DISTINCT
        fr.PRODUCTID
    FROM #FilteredRows AS fr
) AS p ON p.PRODUCTID = voc.PRODUCTID
WHERE voc.FIRMID = 'SA'
AND voc.TLIC NOT IN ('Z', 'D');
CREATE CLUSTERED INDEX IX_FilteredVoc
ON #FilteredVoc
(
    PRODUCTID
);
------------------------------------------------------------
-- 4. Galutinis skaičiavimas
------------------------------------------------------------
;WITH CalculatedRows AS
(
    SELECT DISTINCT
        rw.ORDSTATID,
        rw.TRPGROUP,
        rw.FIRMID,
        rw.PREFSTOCK,
        voc.INF_PREK,
        BRUTTO_KG_ORDERED = CASE WHEN voc.TVNT = 'K' AND voc.TLIC <> 'D' THEN rw.AMOUNTORIG / NULLIF(voc.DNET, 0) * voc.BRUTTO
								 WHEN voc.TLIC <> 'D' THEN rw.AMOUNTORIG * voc.BRUTTO 
						    ELSE 0 END,
        FULL_PAL_DAL_ORDER = CASE WHEN voc.TLIC = 'D' THEN 0  
								  WHEN voc.TVNT = 'V' THEN CAST(rw.AMOUNTORIG / NULLIF(voc.KIEKD * voc.KIEKP, 0) AS int)		
							ELSE CAST (rw.AMOUNTORIG / NULLIF(voc.DNET * voc.KIEKD * voc.KIEKP, 0) AS int) END,
        MIX_PAL_DAL_ORDER = CASE WHEN voc.TLIC = 'D' THEN 0 
								 WHEN voc.TVNT = 'V' THEN rw.AMOUNTORIG / NULLIF(voc.KIEKD * voc.KIEKP, 0) - CAST(rw.AMOUNTORIG / NULLIF(voc.KIEKD * voc.KIEKP, 0) AS int)
			                ELSE rw.AMOUNTORIG / NULLIF(voc.DNET * voc.KIEKD * voc.KIEKP,0) - CAST(rw.AMOUNTORIG / NULLIF(voc.DNET * voc.KIEKD * voc.KIEKP, 0) AS int) END,
        PLL_BY_ORDER = CASE WHEN voc.TLIC = 'D' THEN 0
							WHEN voc.TVNT = 'V' THEN rw.AMOUNTORIG / NULLIF(voc.KIEKD * voc.KIEKP, 0) ELSE rw.AMOUNTORIG / NULLIF (voc.DNET * voc.KIEKD * voc.KIEKP, 0) END
    FROM #FilteredRows AS rw
    INNER JOIN #FilteredVoc AS voc ON voc.PRODUCTID = rw.PRODUCTID),
PreparedData AS
(  SELECT
        fb.DVAZNEW,
        WEEKDAY_NO = (DATEDIFF( DAY, '19000101', CAST(fb.DVAZNEW AS date)) % 7) + 1,
        REGIME = CASE WHEN LEFT(cr.PREFSTOCK, 2) IN ('VT', 'VF')THEN 'FROZEN'
					  WHEN LEFT(cr.PREFSTOCK, 2) = 'VN' THEN 'DRY (VN)'
					  WHEN LEFT(cr.PREFSTOCK, 2) = 'VH' THEN 'DRY (VH)'
	                  WHEN LEFT(cr.PREFSTOCK, 2) IN ('VR', 'VU') THEN 'THERMO (VR,VU)'
	                  WHEN LEFT(cr.PREFSTOCK, 2) IN ('VD', 'VV') THEN 'THERMO (VV,VD)'
	                  WHEN LEFT(cr.PREFSTOCK, 2) IN ('VA', 'VO', 'VQ', 'VS') THEN 'TOBACCO'
                ELSE '???' END,
        PREFDESK = LEFT(cr.PREFSTOCK, 2),
		TODAY_ORDER = CASE WHEN fb.ADDTIME >= CAST(fb.DVAZNEW AS date) AND fb.ADDTIME <  DATEADD(DAY, 1, CAST(fb.DVAZNEW AS date)) THEN 'SAME DAY'
						   WHEN fb.ADDTIME >= DATEADD(DAY, -1, CAST(fb.DVAZNEW AS date)) AND fb.ADDTIME <  CAST(fb.DVAZNEW AS date) THEN 'PICK_DAY'
					  ELSE 'EARLIER' END,
        SENDTIME_HOUR = DATEADD(HOUR,DATEDIFF(HOUR, 0, fb.ADDTIME),0),
        fb.ORG,
        cr.INF_PREK,
        cr.BRUTTO_KG_ORDERED,
        cr.FULL_PAL_DAL_ORDER,
        cr.MIX_PAL_DAL_ORDER,
        cr.PLL_BY_ORDER

    FROM #FilteredBase AS fb

    INNER JOIN CalculatedRows AS cr
        ON cr.ORDSTATID = fb.ORDSTATID
       AND cr.TRPGROUP  = fb.TRPGROUP
       AND cr.FIRMID    = fb.FIRMID
)
SELECT
    CONVERT(varchar(8), pd.DVAZNEW, 112) AS DVAZNEW,
    pd.WEEKDAY_NO,
    pd.REGIME,
    pd.PREFDESK,
    pd.TODAY_ORDER,
    pd.SENDTIME_HOUR,
    pd.ORG,

    COUNT(pd.INF_PREK) AS Lines,

    CAST(SUM(pd.BRUTTO_KG_ORDERED) / 1000.0 AS decimal(18, 2)) AS Brutto_t,
    CAST(SUM(pd.FULL_PAL_DAL_ORDER) AS decimal(18, 2)) AS F_pll,
    CAST(SUM(pd.MIX_PAL_DAL_ORDER) AS decimal(18, 2)) AS M_pll
FROM PreparedData AS pd

GROUP BY
    pd.DVAZNEW,
    pd.WEEKDAY_NO,
    pd.REGIME,
    pd.PREFDESK,
    pd.TODAY_ORDER,
    pd.SENDTIME_HOUR,
    pd.ORG

ORDER BY
    pd.DVAZNEW,
    pd.PREFDESK,
    pd.SENDTIME_HOUR,
    pd.ORG

OPTION (RECOMPILE);

------------------------------------------------------------
-- 5. Valymas
------------------------------------------------------------

DROP TABLE IF EXISTS #FilteredVoc;
DROP TABLE IF EXISTS #FilteredRows;
DROP TABLE IF EXISTS #FilteredBase;

