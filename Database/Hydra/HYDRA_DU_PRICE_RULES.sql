IF OBJECT_ID(N'BLS_LOGIT_STAT.dbo.HYDRA_DU_PRICE_RULES', N'U') IS NULL
BEGIN
    CREATE TABLE [BLS_LOGIT_STAT].[dbo].[HYDRA_DU_PRICE_RULES]
    (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_HYDRA_DU_PRICE_RULES PRIMARY KEY,
        BASE decimal(18,6) NOT NULL,
        YEAR_OF_SET_BASE int NOT NULL,
        BRUTTO_RULE nvarchar(50) NOT NULL,
        DU_BRUTTO_RULE nvarchar(50) NOT NULL,
        NOMINAL_QTY_PER_H decimal(18,6) NOT NULL,
        STICKER_COUNT int NOT NULL,
        COMPLEXITY_INDEX_DEFOULT decimal(18,6) NOT NULL,
        PRICE_FOR_EMPLOYEES decimal(19,12) NOT NULL,
        IS_ACTIVE bit NOT NULL CONSTRAINT DF_HYDRA_DU_PRICE_RULES_IS_ACTIVE DEFAULT 1,
        UPDATED_AT datetimeoffset(7) NOT NULL CONSTRAINT DF_HYDRA_DU_PRICE_RULES_UPDATED_AT DEFAULT SYSDATETIMEOFFSET(),
        UPDATED_BY nvarchar(256) NULL,
        CONSTRAINT UX_HYDRA_DU_PRICE_RULES_YEAR_RULE UNIQUE (YEAR_OF_SET_BASE, BRUTTO_RULE)
    );
END;
GO

MERGE [BLS_LOGIT_STAT].[dbo].[HYDRA_DU_PRICE_RULES] AS target
USING
(
    VALUES
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,08', N'<=0,08', CAST(1400 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.004071428571 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,1', N'<=0,1', CAST(1300 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.004384615385 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,2', N'<=0,2', CAST(1200 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.004750000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,3', N'<=0,3', CAST(1100 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.005181818182 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,4', N'<=0,4', CAST(1000 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.005700000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,5', N'<=0,5', CAST(900 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.006333333333 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,6', N'<=0,6', CAST(800 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.007125000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,7', N'<=0,7', CAST(700 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.008142857143 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,8', N'<=0,8', CAST(600 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.009500000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_0,9', N'<=0,9', CAST(500 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.011400000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_1', N'<=1', CAST(400 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.014250000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_4', N'<=4', CAST(300 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.019000000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_6', N'<=6', CAST(250 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.022800000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_8', N'<=8', CAST(200 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.028500000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_10', N'<=10', CAST(150 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.038000000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'LEQ_20', N'<=20', CAST(100 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.057000000000 AS decimal(19,12))),
        (CAST(5.7 AS decimal(18,6)), 2024, N'GTR_20', N'>20', CAST(50 AS decimal(18,6)), 1, CAST(0 AS decimal(18,6)), CAST(0.114000000000 AS decimal(19,12)))
) AS source
(
    BASE,
    YEAR_OF_SET_BASE,
    BRUTTO_RULE,
    DU_BRUTTO_RULE,
    NOMINAL_QTY_PER_H,
    STICKER_COUNT,
    COMPLEXITY_INDEX_DEFOULT,
    PRICE_FOR_EMPLOYEES
)
ON target.YEAR_OF_SET_BASE = source.YEAR_OF_SET_BASE
   AND target.BRUTTO_RULE = source.BRUTTO_RULE
WHEN MATCHED THEN
    UPDATE SET
        BASE = source.BASE,
        DU_BRUTTO_RULE = source.DU_BRUTTO_RULE,
        NOMINAL_QTY_PER_H = source.NOMINAL_QTY_PER_H,
        STICKER_COUNT = source.STICKER_COUNT,
        COMPLEXITY_INDEX_DEFOULT = source.COMPLEXITY_INDEX_DEFOULT,
        PRICE_FOR_EMPLOYEES = source.PRICE_FOR_EMPLOYEES,
        IS_ACTIVE = 1,
        UPDATED_AT = SYSDATETIMEOFFSET(),
        UPDATED_BY = SUSER_SNAME()
WHEN NOT MATCHED BY TARGET THEN
    INSERT
    (
        BASE,
        YEAR_OF_SET_BASE,
        BRUTTO_RULE,
        DU_BRUTTO_RULE,
        NOMINAL_QTY_PER_H,
        STICKER_COUNT,
        COMPLEXITY_INDEX_DEFOULT,
        PRICE_FOR_EMPLOYEES,
        UPDATED_BY
    )
    VALUES
    (
        source.BASE,
        source.YEAR_OF_SET_BASE,
        source.BRUTTO_RULE,
        source.DU_BRUTTO_RULE,
        source.NOMINAL_QTY_PER_H,
        source.STICKER_COUNT,
        source.COMPLEXITY_INDEX_DEFOULT,
        source.PRICE_FOR_EMPLOYEES,
        SUSER_SNAME()
    );
GO
