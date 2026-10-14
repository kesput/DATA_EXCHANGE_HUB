-- SQL_DESCRIPTION: ProblemBook'o problemines eilutes kur Department parinktas ne Owner tarukimas pagal saskaitos data (be taros!!!)
-- SQL_PARAM_MUST: @OWNER|text||Prekiu savininko kodas. Iveskite tiksliai 3 simbolius, pvz. SNX.|regex=^[A-Za-z0-9]{3}$;message=@OWNER turi buti tiksliai 3 raides arba skaiciai.
-- SQL_PARAM_MUST: @SNDATE|date||Laikotarpio pradzios data i STAT3 uzklausa perduodama YYYYMMDD formatu.|format=yyyyMMdd
-- SQL_PARAM_MUST: @ENDATE|date||Laikotarpio pabaigos data i STAT3 uzklausa perduodama YYYYMMDD formatu.|format=yyyyMMdd

SELECT
        PRB.[Owner],
        PRB.[SerialNo],
        PRB.[ProdCode],
        PRB.[DocNo],
        INV.[INVOICEDATE],
        PRB.[Department],
        PRB.[MismatchType],
        PRB.[NotCountable]
    FROM BLSTXSTA3.PROBLEMBOOK.dbo.v_ProblemAndReturnProducts AS PRB WITH (NOLOCK)

    LEFT JOIN
    (
        SELECT
            I.[INVOICENO],
            I.[INVOICEDATE],
            I.[NDATE],
            O.[SHORTNAME]
        FROM SNTXDATA.dbo.[INVOICE] AS I WITH (NOLOCK)

        LEFT JOIN SNTXDATA.dbo.[OWNER] AS O WITH (NOLOCK)
            ON O.[ID] = I.[OWNERID]
            AND O.[FIRMID] = I.[FIRMID]

        WHERE
            I.[CANCELLED] = 0
            AND I.[ACTIVE] = 1
            AND I.[TEST] = 0
            AND I.[OPERTYPE] = 'Sales'
    ) AS INV
        ON INV.[INVOICENO] = PRB.[DocNo]
        AND INV.[SHORTNAME] = PRB.[Owner]

    LEFT JOIN SNTXDATA.dbo.[VOCPREK] AS V WITH (NOLOCK)
        ON V.[INF_PREK] = PRB.[ProdCode]
        AND V.[FIRMID] = 'SA'

    WHERE
        PRB.[Owner] = @OWNER
        AND PRB.[MismatchType] = 'Problem'
        AND PRB.[NotCountable] = 0
        AND PRB.[Department] <> 'Owner'
        AND LEN(PRB.[DocNo]) > 0
        AND (V.[TLIC] NOT IN ('D', 'Z') OR V.[TLIC] IS NULL)
        AND INV.[NDATE] between @SNDATE and @ENDATE