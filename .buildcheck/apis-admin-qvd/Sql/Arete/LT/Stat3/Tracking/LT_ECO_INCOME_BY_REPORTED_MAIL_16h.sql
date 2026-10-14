-- SQL_DESCRIPTION: ECO prekiu pajamavimo detales pagal parinkta diena
-- SQL_PARAM_MUST: @NDATE|date||Pajamavimo data. Pasirinkta data i STAT3 uzklausa perduodama YYYYMMDD formatu.|format=yyyyMMdd


DECLARE @PREV_NDATE int;
SET @PREV_NDATE = CONVERT(int, CONVERT(char(8), DATEADD(day, -1, CONVERT(date, CONVERT(char(8), @NDATE), 112)), 112));

WITH
atlas AS (
    SELECT
         INVOICE.NDATE AS NDORD
        ,CONVERT(varchar(8), INVOICE.ADDTIME, 108) AS [TIME]
        ,ORGBRIEF = CASE
            WHEN INVOICE.OPERTYPE = 'Purchase' THEN LTRIM(RTRIM(ISNULL(SUPPLIER.NAME, '')))
            WHEN INVOICE.OPERTYPE = 'Movement' THEN INVOICE.INVPREFSTOCK
            ELSE ''
         END
        ,ERP = CASE
            WHEN INVOICE.OPERTYPE = 'Purchase' THEN INVOICE.INVPREFSTOCK
            WHEN INVOICE.OPERTYPE = 'Movement' THEN INVOICE.PREFSTOCKTO
            ELSE ''
         END
        ,VAZTIP = CASE
            WHEN INVOICE.OPERTYPE = 'Purchase' THEN 'kSav'
            WHEN INVOICE.OPERTYPE = 'Movement' THEN 'Vid'
            ELSE ''
         END
        ,INVOICE.INVOICENO AS DOCUMENT
        ,ECO_ALCO = CASE
            WHEN VOCPREK.TLICSUBTYPE = 'ECO' AND TLICTYPE.ALCO = 1 THEN 'ECO/ALCO'
            WHEN VOCPREK.TLICSUBTYPE = 'ECO' THEN 'ECO'
            WHEN TLICTYPE.ALCO = 1 THEN 'ALCO'
            ELSE ''
         END
        ,[OWNER].SHORTNAME AS [OWNER]
        ,SUPPLIER.CODE AS KORG
        ,SUPPLIER.[NAME] AS SUPPLIER_NAME
        ,INVOICELINE.INF_PREK
        ,PRODUCT.PRODNAME
        ,CAST(NULL AS decimal(18,3)) AS Quantity
        ,x.p.value('(Quantity/text())[1]', 'decimal(18,3)') AS AMOUNT
        ,PRODUCT.ALCOCONC
        ,y.c.value('(CertNumber/text())[1]', 'nvarchar(100)') AS CERT_NO
        ,y.c.value('(CertIssuedBy/text())[1]', 'nvarchar(100)') AS CERT_ISSUED_BY
        ,x.p.value('(LotNumber/text())[1]', 'nvarchar(100)') AS LOT_NUMBER
        ,x.p.value('(LPN/text())[1]', 'nvarchar(100)') AS PaletID
        ,x.p.value('(ExpDate/text())[1]', 'date') AS validuntil
        ,RepackByLabel = CASE WHEN PRODLABELN.[COMMENT] IS NOT NULL THEN 1 ELSE 0 END
        ,ITEMDOC.CHTIME AS LabelFileTime
        ,PRODLABELN.CHTIME AS LabelCommentTime
        ,PRODLABELN.[COMMENT] AS LabelComment
        ,PRODUCTH.BARCODE
        ,CAST('ATLAS' AS varchar(20)) AS DATA_SOURCE
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
    LEFT JOIN SNTXDATA.dbo.PRODUCT AS PRODUCT WITH (NOLOCK)
        ON PRODUCT.FIRMID = VOCPREK.FIRMID
       AND PRODUCT.INF_PREK = VOCPREK.INF_PREK
       AND PRODUCT.ACTIVE = 1
    LEFT JOIN QLIKDATA.dbo.PRODUCTH AS PRODUCTH WITH (NOLOCK)
        ON PRODUCTH.FIRMID = VOCPREK.FIRMID
       AND PRODUCTH.INF_PREK = VOCPREK.INF_PREK
       AND PRODUCTH.[SID] = PRODUCT.[SID]
       AND PRODUCTH.ACTIVE = 1
    LEFT JOIN QLIKDATA.dbo.[GROUPS] AS [GROUPS] WITH (NOLOCK)
        ON [GROUPS].GROUP_ID = PRODUCTH.GROUPID
    LEFT JOIN WM0.SNTXDB.dbo.TLICTYPE AS TLICTYPE WITH (NOLOCK)
        ON TLICTYPE.FIRMID = VOCPREK.FIRMID
       AND TLICTYPE.[TYPE] = VOCPREK.TLIC
    LEFT JOIN SNTXDATA.dbo.SUPPLIER AS SUPPLIER WITH (NOLOCK)
        ON SUPPLIER.ID = INVOICE.SUPPLIERID
    LEFT JOIN WM0.SNTXDB.dbo.PRODLABELN AS PRODLABELN WITH (NOLOCK)
        ON PRODLABELN.PRODUCTID = VOCPREK.PRODUCTID
       AND LEN(CASE
                    WHEN PRODLABELN.[COMMENT] LIKE '%#%'
                    THEN SUBSTRING(PRODLABELN.[COMMENT], 0, CHARINDEX('#', PRODLABELN.[COMMENT]))
                    ELSE PRODLABELN.[COMMENT]
               END) > 1
       AND PRODLABELN.[COMMENT] NOT LIKE '%NEKLIJUOJAM%'
    LEFT JOIN WM0.SNTXDB.dbo.ITEMDOC AS ITEMDOC WITH (NOLOCK)
        ON ITEMDOC.ITEMCODE = VOCPREK.INF_PREK
       AND ITEMDOC.FIRMID = VOCPREK.FIRMID
       AND ITEMDOC.ACTIVE = 1
       AND ITEMDOC.DOCTYPE = 'L'
    OUTER APPLY (
        SELECT TRY_CAST('<Root>' + CAST(INVOICELINEPROP.PALETS AS nvarchar(max)) + '</Root>' AS xml) AS PaletsXml
    ) AS p
    OUTER APPLY p.PaletsXml.nodes('/Root/Container') AS x(p)
    OUTER APPLY (
        SELECT TRY_CAST('<Root>' + CAST(INVOICELINEPROP.CERTINFO AS nvarchar(max)) + '</Root>' AS xml) AS CertinfoXml
    ) AS c
    OUTER APPLY c.CertinfoXml.nodes('/Root/Certificate') AS y(c)
    WHERE INVOICE.TEST = 0
      AND INVOICE.CANCELLED = 0
      AND INVOICE.ACTIVE = 1
      AND (
            INVOICE.OPERTYPE = 'Purchase'
            OR (
                INVOICE.OPERTYPE = 'Movement'
                AND LEFT(INVOICE.INVPREFSTOCK, 2) = 'VH'
                AND LEFT(INVOICE.PREFSTOCKTO, 2) = 'VN'
            )
          )
      AND LEFT(INVOICE.INVOICEOWNER, 5) = 'ATLAS'
      AND INVOICE.NDATE IN (@PREV_NDATE, @NDATE)
      AND INVOICE.FIRMID = 'SA'
      AND VOCPREK.INF_PREK NOT IN ('V932AC9', 'V932ACM')
      AND (
            VOCPREK.TLICSUBTYPE = 'ECO'
            OR PRODUCT.PRODNAME LIKE '%EKO-%'
            OR PRODUCT.PRODNAME LIKE '%ekolog%'
            OR PRODUCT.PRODNAME LIKE '% organic%'
            OR (
                PRODUCT.PRODNAME LIKE '% org%'
                AND PRODUCT.PRODNAME NOT LIKE '% orginal%'
                AND PRODUCT.PRODNAME NOT LIKE '%vidaus org%'
            )
            OR SUPPLIER.CODE='NSP'
          )
      AND (
            ([GROUPS].GRP_ID1 = 75240 AND [GROUPS].GRP_ID2 IN (74961,74962,74963,72514,72516,72521,72522,73754,74969))
            OR PRODUCT.FOOD = 1
            OR [OWNER].SHORTNAME <> 'SNX'
          )
),
dv AS (
    SELECT *
    FROM SNTXDATA.dbo.DATVAZ WITH (NOLOCK)
    WHERE FIRMID = 'SA'
      AND AMOUNT < 0
      AND [PREF] IN ('VBL','VBM')
      AND PREFSTOCK IN ('VB1','VD1','VT2','VF1','VHU','VN1','VR1','VV1')
      AND INF_PREK NOT IN ('V932AC9','V932ACM')
      AND NDORD IN (@PREV_NDATE, @NDATE)
),
vzt AS (
    SELECT *
    FROM SNTXDATA.dbo.VAZTAR WITH (NOLOCK)
    WHERE CANC = 'N'
      AND VTYP <> 'C'
      AND TPER IN ('V','P','D')
),
prekes AS (
    SELECT
         dv.NDORD
       -- ,CAST(NULL AS bigint) AS INVOICEID
       -- ,CAST(NULL AS int) AS LINENUM
        ,CONVERT(varchar(8), t.EffectiveTime, 108) AS [TIME]
        ,ORGBRIEF = CASE
            WHEN (VAZTAR.TORG = 'Y' OR VAZTAR.TPRE = 'V')
                 AND VAZTAR.RELPREFSTOCK <> ''
                 AND VOCORG.SALETYPE <> 'V'
            THEN VAZTAR.RELPREFSTOCK
            ELSE LTRIM(RTRIM(ISNULL(OrgPos.OrgBrief, VOCORG.ORGBRIEF)))
         END
        ,ERP = dv.PREFSTOCK
        ,VAZTIP = CASE
            WHEN VAZTAR.TCUS = 'W' THEN
                CASE
                    WHEN VAZTAR.TORG = 'U' THEN 'kSav'
                    WHEN VAZTAR.TORG = 'Y' THEN 'Vid.'
                    ELSE 'Paj.'
                END
            ELSE 'Isl.'
         END
        ,DOCUMENT = CASE
            WHEN RTRIM(VAZTAR.NSAD) <> '' THEN RTRIM(VAZTAR.NSAD)
            WHEN VAZTAR2.NW IS NOT NULL THEN RTRIM(VAZTAR2.NSAD)
            WHEN RTRIM(VAZTAR.NKRO) <> '' THEN RTRIM(VAZTAR.NKRO)
            WHEN VAZTAR2.NW IS NOT NULL THEN RTRIM(VAZTAR2.NKRO)
            ELSE ''
         END
        ,ECO_ALCO = CASE
            WHEN VOCPREK.TLICSUBTYPE = 'ECO' AND TLICTYPE.ALCO = 1 THEN 'ECO/ALCO'
            WHEN VOCPREK.TLICSUBTYPE = 'ECO' THEN 'ECO'
            WHEN TLICTYPE.ALCO = 1 THEN 'ALCO'
            ELSE ''
         END
        ,[OWNER].SHORTNAME AS [OWNER]
        ,SUPPLIER.CODE AS KORG
        ,SUPPLIER.[NAME] AS SUPPLIER_NAME
        ,dv.INF_PREK
        ,PRODUCT.PRODNAME
        ,TRY_CONVERT(decimal(18,3), INVOICELINEPROP.Quantity) AS Quantity
        ,CONVERT(decimal(18,3), ISNULL(
            TRY_CONVERT(decimal(18,3), INVOICELINEPROP.Quantity),
            CASE
                WHEN DATVAZPROP.Amount IS NOT NULL THEN DATVAZPROP.Amount
                ELSE ABS(dv.AMOUNT)
            END
         )) AS AMOUNT
        ,PRODUCT.ALCOCONC
        ,CAST(DATVAZPROP.CERT_NO AS nvarchar(100)) AS CERT_NO
        ,CAST(DATVAZPROP.CERT_ISSUED_BY AS nvarchar(100)) AS CERT_ISSUED_BY
        ,CAST(CASE
            WHEN DATVAZPROP.LotNumber IS NOT NULL THEN DATVAZPROP.LotNumber
            ELSE DATVAZPROP.LOT
         END AS nvarchar(100)) AS LOT_NUMBER
        ,CAST(DATVAZPROP.PaletID AS nvarchar(100)) AS PaletID
        ,CONVERT(date, ISNULL(
            TRY_CONVERT(date, INVOICELINEPROP.ExpDate),
            DATVAZPROP.ValidUntil
         )) AS validuntil
        ,RepackByLabel = CASE WHEN PRODLABELN.[COMMENT] IS NOT NULL THEN 1 ELSE 0 END
        ,ITEMDOC.CHTIME AS LabelFileTime
        ,PRODLABELN.CHTIME AS LabelCommentTime
        ,PRODLABELN.[COMMENT] AS LabelComment
        ,PRODUCTH.BARCODE
        ,CAST('PREKES' AS varchar(20)) AS DATA_SOURCE
    FROM dv
    LEFT JOIN SNTXDATA.dbo.VOCPREK AS VOCPREK WITH (NOLOCK)
        ON VOCPREK.FIRMID = dv.FIRMID
       AND VOCPREK.INF_PREK = dv.INF_PREK
    LEFT JOIN WM0.SNTXDB.dbo.TLICTYPE AS TLICTYPE WITH (NOLOCK)
        ON TLICTYPE.FIRMID = VOCPREK.FIRMID
       AND TLICTYPE.[TYPE] = VOCPREK.TLIC
    LEFT JOIN SNTXDATA.dbo.PRODUCT AS PRODUCT WITH (NOLOCK)
        ON PRODUCT.FIRMID = dv.FIRMID
       AND PRODUCT.INF_PREK = dv.INF_PREK
       AND PRODUCT.ACTIVE = 1
    LEFT JOIN QLIKDATA.dbo.PRODUCTH AS PRODUCTH WITH (NOLOCK)
        ON PRODUCTH.FIRMID = dv.FIRMID
       AND PRODUCTH.INF_PREK = dv.INF_PREK
       AND PRODUCTH.[SID] = PRODUCT.[SID]
       AND PRODUCTH.ACTIVE = 1
    LEFT JOIN QLIKDATA.dbo.[GROUPS] AS [GROUPS] WITH (NOLOCK)
        ON [GROUPS].GROUP_ID = PRODUCTH.GROUPID
    LEFT JOIN SNTXDATA.dbo.[OWNER] AS [OWNER] WITH (NOLOCK)
        ON [OWNER].ID = PRODUCT.OWNERID
       AND [OWNER].FIRMID = dv.FIRMID
    LEFT JOIN SNTXDATA.dbo.SUPPLIER AS SUPPLIER WITH (NOLOCK)
        ON SUPPLIER.FIRMID = dv.FIRMID
       AND SUPPLIER.CODE = PRODUCT.KORG
       AND SUPPLIER.ACTIVE = 1
       AND SUPPLIER.[SID] = PRODUCT.[SID]
    INNER JOIN vzt AS VAZTAR
        ON VAZTAR.FIRMID = dv.FIRMID
       AND VAZTAR.PREFSTOCK = dv.PREFSTOCK
       AND VAZTAR.NW = dv.NW
    LEFT JOIN SNTXDATA.dbo.VAZTAR AS VAZTAR2 WITH (NOLOCK)
        ON VAZTAR2.FIRMID = VAZTAR.FIRMID
       AND VAZTAR2.PREFSTOCK = VAZTAR.PREFSTOCK
       AND VAZTAR2.NW = VAZTAR.NWMA
    CROSS APPLY (
        SELECT EffectiveTime = CAST(VAZTAR.INVOICECREATETIME as time)
    ) AS t
    LEFT JOIN (
        SELECT
             v.NDORD
            ,v.FIRMID
            ,v.PREFSTOCK
            ,v.NW
            ,v.NUM
            ,p.value('@paletid', 'varchar(30)') AS PaletID
            ,p.value('@amount', 'decimal(18,3)') AS Amount
            ,p.value('@validuntil', 'date') AS ValidUntil
            ,p.value('@gs1_10_lotnumber', 'varchar(50)') AS LotNumber
            ,vp.CERTINFO.value('CertInfo[1]/Cert[1]/@Nr', 'varchar(30)') AS CERT_NO
            ,vp.CERTINFO.value('CertInfo[1]/Cert[1]/@IssuedBy', 'varchar(30)') AS CERT_ISSUED_BY
            ,vp.CERTINFO.value('CertInfo[1]/Cert[1]/@LOTNumber', 'varchar(30)') AS LOT
        FROM SNTXDATA.dbo.DATVAZ AS v
        INNER JOIN SNTXDATA.dbo.DATVAZPROP AS vp
            ON v.NDORD = vp.NDORD
           AND v.FIRMID = vp.FIRMID
           AND v.PREFSTOCK = vp.PREFSTOCK
           AND v.NW = vp.NW
           AND v.NUM = vp.NUM
        CROSS APPLY vp.PALETS.nodes('/Palets/palet') AS X(p)
    ) AS DATVAZPROP
        ON DATVAZPROP.NDORD = dv.NDORD
       AND DATVAZPROP.FIRMID = dv.FIRMID
       AND DATVAZPROP.NW = dv.NW
       AND DATVAZPROP.NUM = dv.NUM
       AND DATVAZPROP.PREFSTOCK = dv.PREFSTOCK
    LEFT JOIN SNTXDATA.dbo.VOCORG AS VOCORG WITH (NOLOCK)
        ON VOCORG.CODEORG = VAZTAR.CORG
       AND VOCORG.PREF = dv.[PREF]
       AND VOCORG.FIRMID = dv.FIRMID
    LEFT JOIN SNTXDATA.dbo.ORGPOS AS OrgPos WITH (NOLOCK)
        ON OrgPos.POSID = VOCORG.POSID
    LEFT JOIN SNTXDATA.dbo.INVOICE AS INVOICE WITH (NOLOCK)
        ON LEFT(INVOICE.ERPWAREHFROM, 2) = 'VH'
       AND INVOICE.FIRMID = dv.FIRMID
       AND INVOICE.INVOICENO = CASE
            WHEN RTRIM(VAZTAR.NSAD) <> '' THEN RTRIM(VAZTAR.NSAD)
            WHEN VAZTAR2.NW IS NOT NULL THEN RTRIM(VAZTAR2.NSAD)
            WHEN RTRIM(VAZTAR.NKRO) <> '' THEN RTRIM(VAZTAR.NKRO)
            WHEN VAZTAR2.NW IS NOT NULL THEN RTRIM(VAZTAR2.NKRO)
            ELSE ''
         END
    LEFT JOIN SNTXDATA.dbo.INVOICELINE AS INVOICELINE WITH (NOLOCK)
        ON INVOICELINE.INVOICEID = INVOICE.INVOICEID
       AND INVOICELINE.INF_PREK = dv.INF_PREK
    LEFT JOIN WM0.SNTXDB.dbo.PRODLABELN AS PRODLABELN WITH (NOLOCK)
        ON PRODLABELN.PRODUCTID = VOCPREK.PRODUCTID
       AND LEN(CASE
                    WHEN PRODLABELN.[COMMENT] LIKE '%#%'
                    THEN SUBSTRING(PRODLABELN.[COMMENT], 0, CHARINDEX('#', PRODLABELN.[COMMENT]))
                    ELSE PRODLABELN.[COMMENT]
               END) > 1
       AND PRODLABELN.[COMMENT] NOT LIKE '%NEKLIJUOJAM%'
    LEFT JOIN WM0.SNTXDB.dbo.ITEMDOC AS ITEMDOC WITH (NOLOCK)
        ON ITEMDOC.ITEMCODE = dv.INF_PREK
       AND ITEMDOC.FIRMID = dv.FIRMID
       AND ITEMDOC.ACTIVE = 1
       AND ITEMDOC.DOCTYPE = 'L'
    LEFT JOIN (
        SELECT
             ilp.INVOICEID
            ,ilp.LINENUM
            ,X.value('(ExpDate)[1]', 'varchar(50)') AS ExpDate
            ,X.value('(Quantity)[1]', 'varchar(50)') AS Quantity
        FROM SNTXDATA.dbo.INVOICELINEPROP AS ilp WITH (NOLOCK)
        CROSS APPLY ilp.PALETS.nodes('/Container') AS T(X)
    ) AS INVOICELINEPROP
        ON INVOICELINEPROP.INVOICEID = INVOICE.INVOICEID
       AND INVOICELINEPROP.LINENUM = INVOICELINE.LINENUM
    WHERE (
            VOCPREK.TLICSUBTYPE = 'ECO'
            OR PRODUCT.PRODNAME LIKE '%EKO-%'
            OR PRODUCT.PRODNAME LIKE '%ekolog%'
            OR PRODUCT.PRODNAME LIKE '% organic%'
            OR (
                PRODUCT.PRODNAME LIKE '% org%'
                AND PRODUCT.PRODNAME NOT LIKE '% orginal%'
                AND PRODUCT.PRODNAME NOT LIKE '%vidaus org%'
            )
          )
      AND NOT (
            CASE
                WHEN (VAZTAR.TORG = 'Y' OR VAZTAR.TPRE = 'V')
                     AND VAZTAR.RELPREFSTOCK <> ''
                     AND VOCORG.SALETYPE <> 'V'
                THEN VAZTAR.RELPREFSTOCK
                ELSE LTRIM(RTRIM(ISNULL(OrgPos.OrgBrief, VOCORG.ORGBRIEF)))
            END
          ) IN ('VB4','VT4','VD4','VF4','VH4','VN4','VR4','WBLSO,SANDELIO PERTE')
      AND (
            ([GROUPS].GRP_ID1 = 75240 AND [GROUPS].GRP_ID2 IN (74961,74962,74963,72514,72516,72521,72522,73754,74969))
            OR PRODUCT.FOOD = 1
            OR [OWNER].SHORTNAME <> 'SNX'
          )
)
SELECT
     result.NDORD
    ,result.[TIME]
    ,result.ORGBRIEF
    ,result.ERP
    ,result.VAZTIP
    ,result.DOCUMENT
    ,result.ECO_ALCO
    ,result.[OWNER]
    ,result.KORG
    ,result.SUPPLIER_NAME
    ,result.INF_PREK
    ,result.PRODNAME
    ,result.Quantity
    ,result.AMOUNT
 -- ,result.ALCOCONC
    ,result.CERT_NO
    ,result.CERT_ISSUED_BY
    ,result.LOT_NUMBER
    ,result.PaletID
    ,result.validuntil
    ,result.RepackByLabel
    ,result.LabelFileTime
    ,result.LabelCommentTime
    ,result.LabelComment
    ,result.BARCODE
    ,result.DATA_SOURCE
FROM (
    SELECT *
    FROM atlas

    UNION ALL

    SELECT *
    FROM prekes
) AS result
WHERE (
        result.NDORD = @PREV_NDATE
        AND TRY_CONVERT(time(0), result.[TIME]) >= CAST('16:00:00' AS time(0))
      )
   OR (
        result.NDORD = @NDATE
        AND TRY_CONVERT(time(0), result.[TIME]) <= CAST('16:00:00' AS time(0))
      )
ORDER BY result.NDORD, result.[TIME], result.ECO_ALCO;