-- SQL_DESCRIPTION:(ATLAS) Pajamavimo detales pagal savininka, firma ir pasirinkta pajamavimo data. Rezultate matomos invoice eilutes, prekes, LPN, galiojimo datos, lotai, kiekiai ir svoriai.
-- SQL_PARAM_MUST: @FIRMID|text||Firmos kodas arba keli kodai per kableli, pvz. SA, LV, EE.|regex=^[A-Za-z0-9_, -]+$;message=@FIRMID gali tureti tik raides, skaicius, tarpus, kablelius, pabraukimus arba minusus.
-- SQL_PARAM_MUST: @OWNER|text||Prekiu savininko kodas. Iveskite tiksliai 3 simbolius, pvz. SNX.|regex=^[A-Za-z0-9]{3}$;message=@OWNER turi buti tiksliai 3 raides arba skaiciai.
-- SQL_PARAM_MUST: @NDATE|date||Pajamavimo data. Pasirinkta data i STAT3 uzklausa perduodama YYYYMMDD formatu.|format=yyyyMMdd

SELECT 
    INVOICE.NDATE,
    INVOICE.[ERPID],
    INVOICE.INVOICENO,
    INVOICE.INVPREFSTOCK,
    INVOICE.INVOICEDATE,
    INVOICE.CREATEDTIME,
    INVOICE.ADDTIME,
    INVOICE.OPERTYPE,
    INVOICE.DOCTYPE,
    INVOICE.EXTERNALINVOICENO,
    INVOICELINE.INF_PREK,
    VOCPREK.INF_PAVADL,
    LINE_ID = TRY_CONVERT(decimal(18,3), x.c.value('(LineId/text())[1]', 'nvarchar(50)')),
    LPN = x.c.value('(LPN/text())[1]', 'nvarchar(100)'),
    EXP_DATE = x.c.value('(ExpDate/text())[1]', 'date'),
    LOT_NUMBER = x.c.value('(LotNumber/text())[1]', 'nvarchar(100)'),
    QTY = x.c.value('(Quantity/text())[1]', 'decimal(18,3)'),
    BRUTTO_KG = CASE
        WHEN VOCPREK.TVNT = 'K' AND VOCPREK.TLIC != 'D' THEN x.c.value('(Quantity/text())[1]', 'decimal(18,3)') / VOCPREK.DNET * VOCPREK.BRUTTO
        WHEN VOCPREK.TLIC != 'D' THEN x.c.value('(Quantity/text())[1]', 'decimal(18,3)') * VOCPREK.BRUTTO
        ELSE 0
    END,
    NETTO_KG = CASE
        WHEN VOCPREK.TVNT = 'K' AND VOCPREK.TLIC != 'D' THEN x.c.value('(Quantity/text())[1]', 'decimal(18,3)') / VOCPREK.NETTO
        WHEN VOCPREK.TLIC != 'D' THEN x.c.value('(Quantity/text())[1]', 'decimal(18,3)') * VOCPREK.NETTO
        ELSE 0
    END
FROM SNTXDATA.dbo.INVOICE AS INVOICE WITH (NOLOCK)
LEFT JOIN SNTXDATA.dbo.[OWNER] AS [OWNER] WITH (NOLOCK)
    ON [OWNER].ID = INVOICE.OWNERID
    AND [OWNER].FIRMID = INVOICE.FIRMID
LEFT JOIN SNTXDATA.dbo.INVOICELINE AS INVOICELINE WITH (NOLOCK)
    ON INVOICELINE.INVOICEID = INVOICE.INVOICEID
LEFT JOIN SNTXDATA.dbo.INVOICELINEPROP AS INVOICELINEPROP WITH (NOLOCK)
    ON INVOICELINEPROP.INVOICEID = INVOICELINE.INVOICEID
    AND INVOICELINEPROP.LINENUM = INVOICELINE.LINENUM
LEFT JOIN SNTXDATA.dbo.VOCPREK AS VOCPREK WITH (NOLOCK)
    ON VOCPREK.PRODUCTID = INVOICELINE.PRODUCTID
    AND VOCPREK.FIRMID = INVOICE.FIRMID
OUTER APPLY (
    SELECT TRY_CAST('<Root>' + CAST(INVOICELINEPROP.PALETS AS nvarchar(max)) + '</Root>' AS xml) AS PaletsXml
) p
OUTER APPLY p.PaletsXml.nodes('/Root/Container') AS x(c)
WHERE INVOICE.TEST = 0
    AND INVOICE.CANCELLED = 0
    AND INVOICE.ACTIVE = 1
    AND INVOICE.OPERTYPE = 'Purchase'
    AND LEFT(INVOICE.INVOICEOWNER,5) ='ATLAS'
    AND INVOICE.NDATE = @NDATE
    AND [OWNER].SHORTNAME = @OWNER
    AND INVOICE.FIRMID IN (
        SELECT LTRIM(RTRIM([value]))
        FROM STRING_SPLIT(@FIRMID, ',')
        WHERE LTRIM(RTRIM([value])) <> ''
    );

