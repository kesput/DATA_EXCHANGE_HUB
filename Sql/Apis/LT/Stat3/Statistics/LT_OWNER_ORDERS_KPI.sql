-- SQL_DESCRIPTION: Orderiu ataskaita kuri parodo uzsakyta ir atrinkta kieki
-- SQL_PARAM_MUST: @OWNER|text||Prekiu savininko kodas. Iveskite tiksliai 3 simbolius, pvz. SNX.|regex=^[A-Za-z0-9]{3}$;message=@OWNER turi buti tiksliai 3 raides arba skaiciai.
-- SQL_PARAM_MUST: @START_DATE|date||Laikotarpio pradzios data i STAT3 uzklausa perduodama YYYY-MM-DD formatu.|format=yyyy-MM-dd
-- SQL_PARAM_MUST: @END_DATE|date||Laikotarpio pabaigos data i STAT3 uzklausa perduodama YYYY-MM-DD formatu.|format=yyyy-MM-dd



SELECT
    CONVERT(date, trp.DVAZNEW) AS DVAZNEW,
    o.REFORDERNUM,
    o.ORDSTATID,
    rw.PCKORDERNO,
    o.POSID,
    oa.REGCODE,
    oa.ONAME,
    op.[ADDRESS],
    PRODUCT.SUPPPRCODE,
    PRODUCT.INF_PREK,
    PRODUCT.PRODNAME,
    rw.AMOUNTORIG,
    rw.AMOUNTSHIPPED
FROM SNTXDATA.dbo.ORDSTAT AS o WITH (NOLOCK)

LEFT JOIN SNTXDATA.dbo.ORDSTATROW AS rw WITH (NOLOCK)
    ON rw.ORDSTATID = o.ORDSTATID
    AND rw.FIRMID = o.FIRMID

LEFT JOIN SNTXDATA.dbo.PRODUCT AS PRODUCT WITH (NOLOCK)
    ON PRODUCT.PRODUCTID = rw.PRODUCTID
    AND PRODUCT.FIRMID = rw.FIRMID

LEFT JOIN SNTXDATA.dbo.DATVAZ AS dvz WITH (NOLOCK)
    ON dvz.ORDSTATID = o.ORDSTATID
    AND dvz.INF_PREK = PRODUCT.INF_PREK
    AND dvz.FIRMID = rw.FIRMID

LEFT JOIN SNTXDATA.dbo.V_VAZTAR AS vzt WITH (NOLOCK)
    ON vzt.NW = dvz.NW
    AND vzt.FIRMID = rw.FIRMID
    AND vzt.PREFSTOCK = rw.PREFSTOCK

LEFT JOIN SNTXDATA.dbo.ORGPOS AS op WITH (NOLOCK)
    ON op.POSID = o.POSID
    AND op.ACTIVE = 1

LEFT JOIN SNTXDATA.dbo.ORGALL AS oa WITH (NOLOCK)
    ON oa.ORGID = op.ORGID
    AND oa.ACTIVE = 1

LEFT JOIN SNTXDATA.dbo.MAINAGE AS a WITH (NOLOCK)
    ON a.MAINAGEID = o.AGENTID
    AND a.ACTIVE = 1

LEFT JOIN SNTXDATA.dbo.ORDSTATTRP AS trp WITH (NOLOCK)
    ON trp.ORDSTATID = rw.ORDSTATID
    AND trp.TRPGROUP = rw.TRPGROUP

LEFT JOIN SNTXDATA.dbo.ORGPOSTRP AS trp1
    ON trp1.TRPGROUP = rw.TRPGROUP
    AND trp1.POSID = o.POSID

LEFT JOIN SNTXDATA.dbo.[OWNER] AS own WITH (NOLOCK)
    ON own.ID = o.OWNERID
    AND own.FIRMID = o.FIRMID

WHERE
    o.FIRMID = 'SA'
	AND own.SHORTNAME = @OWNER
    AND trp.DVAZNEW between @START_DATE and @END_DATE;