-- SQL_DESCRIPTION: Invoice irasu tarukimas pagal saskaitos data (be taros!!!)
-- SQL_PARAM_MUST: @OWNER|text||Prekiu savininko kodas. Galima tiksliai SNX, LIKE fragmentas arba IN sarasas per kableli.|regex=^[A-Za-z0-9_, %_-]+$;message=@OWNER gali tureti raides, skaicius, kablelius, tarpus, %, _ arba minusus.;operators==,like,in
-- SQL_PARAM_MUST: @SNDATE|date||Laikotarpio pradzios data i STAT3 uzklausa perduodama YYYYMMDD formatu.|format=yyyyMMdd
-- SQL_PARAM_MUST: @ENDATE|date||Laikotarpio pabaigos data i STAT3 uzklausa perduodama YYYYMMDD formatu.|format=yyyyMMdd
-- SQL_PARAM: @DOCTYPE|text||Papildomas dokumento tipo filtras.|operators==,like,in;expr=I.[DOCTYPE]

SELECT      
	O.SHORTNAME AS [OWNER]
	,I.INVOICENO
	,I.[OPERTYPE]
	,I.[DOCTYPE]
	,I.[SUBOPERTYPE]
	,I.NDATE
	,IL.ORDSTATID
,PLL_BY_INVOICE =SUM(CASE 
						WHEN ISNULL(IL.QUANTITY, 0) = 0 THEN CONVERT(decimal(18,6), 0)
						WHEN V.TLIC = 'D' THEN CONVERT(decimal(18,6), 0)
						WHEN V.TVNT = 'V' THEN CONVERT(decimal(18,6), IL.QUANTITY)/NULLIF(CONVERT(decimal(18,6), V.KIEKD)*CONVERT(decimal(18,6), V.KIEKP),0) 
						ELSE CONVERT(decimal(18,6), IL.QUANTITY)/NULLIF(CONVERT(decimal(18,6), V.DNET), 0)/NULLIF(CONVERT(decimal(18,6), V.KIEKD), 0)/NULLIF(CONVERT(decimal(18,6), V.KIEKP), 0) END )
,SKU_BY_INVOICE = COUNT(DISTINCT IL.PRODUCTID)
FROM SNTXDATA.dbo.INVOICE AS I WITH (NOLOCK)  
	LEFT JOIN SNTXDATA.dbo.INVOICELINE AS IL WITH (NOLOCK)  ON IL.INVOICEID = I.INVOICEID 
	LEFT JOIN SNTXDATA.dbo.VOCPREK AS V WITH (NOLOCK)  ON V.PRODUCTID = IL.PRODUCTID  AND V.FIRMID = I.FIRMID
	LEFT JOIN SNTXDATA.dbo.[OWNER] AS O ON O.ID = I.OWNERID AND O.FIRMID = I.FIRMID
WHERE
	I.FIRMID = 'SA'
	AND I.NDATE BETWEEN @SNDATE AND @ENDATE
	AND I.CANCELLED = 0
	AND I.ACTIVE = 1
	AND I.TEST = 0
	AND IL.TARE = 'N'
	AND I.[OPERTYPE] = 'Sales'
	AND O.SHORTNAME = @OWNER
	/* SQL_PARAM_FILTERS */
GROUP BY           
	O.SHORTNAME
	,I.INVOICENO
	,I.NDATE
	,I.[OPERTYPE]
	,I.[DOCTYPE]
	,I.[SUBOPERTYPE]
	,IL.ORDSTATID