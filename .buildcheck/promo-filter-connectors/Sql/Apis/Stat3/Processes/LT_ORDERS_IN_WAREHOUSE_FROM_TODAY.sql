-- SQL_DESCRIPTION: Orderiai pagal sandeli sios dienos ir ateinanciu dienu KPI (be taros!!!)
-- SQL_PARAM_MUST: @PREF|text||Sandelio kodo 2 raides pvz VN ar VH |regex=^[A-Za-z0-9]{2}$;message=@PREF turi buti tiksliai 2 raides arba skaiciai.


  SELECT 
        [ORDSTAT].[ORDSTATID],
        [ORDSTAT].[REFORDERNUM],
        DVAZNEW = CONVERT(VARCHAR, [ORDSTATTRP].DVAZNEW, 102),
        [ORDSTAT].[POSID],
        [ORGALL].ONAME,
        [ORGPOS].[ADDRESS],
		[OWNER].SHORTNAME as [OWNER],
  --      [ORDSTAT].[ORDSENDERID],
        [ORDSTATTRP].TRPGROUP,
        [ORDSTATROW].PREFSTOCK,
        BRUTTO_t = REPLACE(FORMAT(SUM([ORDSTATROW].BRUTTO_KG_ORDERED) / 1000, 'N2'), '.', ','),
        PLL = REPLACE(FORMAT(SUM([ORDSTATROW].PLL_BY_ORDER), 'N2'), '.', ','),
        FULL_PAL_DA = REPLACE(FORMAT(SUM([ORDSTATROW].FULL_PAL_DAL_ORDER), 'N2'), '.', ','),
        MIX_PAL_DA = REPLACE(FORMAT(SUM([ORDSTATROW].MIX_PAL_DAL_ORDER), 'N2'), '.', ',')
    FROM [SNTXDATA].[dbo].[ORDSTAT] AS [ORDSTAT] WITH (NOLOCK)
    LEFT JOIN 
    (
        SELECT DISTINCT
            rw.[ORDSTATID],
            voc.INF_PREK,
            rw.[PREFSTOCK],
            rw.[TRPGROUP],
            rw.[FIRMID],
			voc.TLIC,
            BRUTTO_KG_ORDERED = CASE
                WHEN voc.TVNT = 'K' AND voc.TLIC != 'D' THEN rw.AMOUNTORIG / voc.DNET * voc.BRUTTO
                WHEN voc.TLIC != 'D' THEN rw.AMOUNTORIG * voc.BRUTTO
                ELSE 0
            END,
            FULL_PAL_DAL_ORDER = CASE 
                WHEN voc.TLIC = 'D' THEN 0
                WHEN voc.TVNT = 'V' THEN CAST(rw.AMOUNTORIG / (voc.KIEKD * voc.KIEKP) AS INT)
                ELSE CAST(rw.AMOUNTORIG / voc.DNET / voc.KIEKD / voc.KIEKP AS INT)
            END,
            MIX_PAL_DAL_ORDER = CASE 
                WHEN voc.TLIC = 'D' THEN 0
                WHEN voc.TVNT = 'V' THEN (rw.AMOUNTORIG / (voc.KIEKD * voc.KIEKP)) - CAST(rw.AMOUNTORIG / (voc.KIEKD * voc.KIEKP) AS INT)
                ELSE (rw.AMOUNTORIG / voc.DNET / voc.KIEKD / voc.KIEKP) - CAST(rw.AMOUNTORIG / voc.DNET / voc.KIEKD / voc.KIEKP AS INT)
            END,
            PLL_BY_ORDER = CASE 
                WHEN voc.TLIC = 'D' THEN 0
                WHEN voc.TVNT = 'V' THEN rw.AMOUNTORIG / (voc.KIEKD * voc.KIEKP)
                ELSE rw.AMOUNTORIG / voc.DNET / voc.KIEKD / voc.KIEKP
            END
        FROM [SNTXDATA].[dbo].[ORDSTATROW] AS rw WITH (NOLOCK)
        LEFT JOIN SNTXDATA.dbo.VOCPREK AS voc WITH (NOLOCK) ON voc.PRODUCTID = rw.PRODUCTID AND voc.FIRMID = rw.FIRMID
  --      WHERE voc.TLIC  IN ('Z', 'D')
    ) AS [ORDSTATROW]
        ON [ORDSTATROW].ORDSTATID = [ORDSTAT].ORDSTATID  AND [ORDSTATROW].FIRMID = [ORDSTAT].FIRMID
    LEFT JOIN [SNTXDATA].[dbo].[ORDSTATTRP] AS [ORDSTATTRP] WITH (NOLOCK)  ON [ORDSTATTRP].ORDSTATID = [ORDSTATROW].ORDSTATID  AND [ORDSTATTRP].[TRPGROUP] = [ORDSTATROW].[TRPGROUP]
    LEFT JOIN [SNTXDATA].[dbo].[ORGPOS] AS [ORGPOS] WITH (NOLOCK) ON [ORGPOS].POSID = [ORDSTAT].[POSID]
	LEFT JOIN [SNTXDATA].[dbo].[ORGALL] AS [ORGALL] WITH (NOLOCK) ON [ORGALL].ORGID = [ORGPOS].ORGID AND [ORGALL].FIRMID = [ORDSTAT].FIRMID
	LEFT JOIN SNTXDATA.dbo.[OWNER] as [OWNER] WITH (NOLOCK) on [OWNER].ID=ORDSTAT.OWNERID and [OWNER].FIRMID=ORDSTAT.FIRMID
    WHERE 
        [ORDSTAT].FIRMID = 'SA'
        AND LEN([ORDSTATTRP].TRPGROUP) = 1
		AND LEFT([ORDSTATROW].PREFSTOCK,2) = @PREF
        AND CAST([ORDSTATTRP].DVAZNEW AS DATE) >= GETDATE()
 
    GROUP BY 
        [ORDSTAT].[ORDSTATID],
        [ORDSTAT].[REFORDERNUM],
        [ORDSTATTRP].DVAZNEW,
        [ORDSTAT].[POSID],
        [ORGALL].ONAME,
        [ORGPOS].[ADDRESS],
        [OWNER].SHORTNAME,
        [ORDSTATTRP].TRPGROUP,
        [ORDSTATROW].PREFSTOCK
    ORDER BY 
		[ORDSTATTRP].DVAZNEW,
        [ORDSTAT].[POSID],
        [ORDSTATROW].PREFSTOCK,
        [ORDSTATTRP].TRPGROUP,
        [OWNER].SHORTNAME;
