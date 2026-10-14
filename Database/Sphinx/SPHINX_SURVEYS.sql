-- SPHINX: Survey · Platform for · Human · Insights · Networked · Xchange
-- Schema: apklausu sudarymui, atsakymų saugojimui ir šakotajai logikai.
-- Saugus paleisti kelis kartus (idempotentinis).

-- 1. Apklausų antraštė ─────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE name = 'SPHINX_SURVEYS' AND schema_id = SCHEMA_ID('dbo')
)
BEGIN
    CREATE TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS] (
        [ID]            INT            IDENTITY(1,1) NOT NULL,
        [TITLE]         NVARCHAR(500)  NOT NULL,
        [DESCRIPTION]   NVARCHAR(2000) NULL,
        [CREATED_BY]    NVARCHAR(100)  NOT NULL,
        [CREATED_AT]    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        [STATUS]        NVARCHAR(50)   NOT NULL DEFAULT N'OPEN',
        [MAX_RESPONSES] INT            NULL,
        [EXPIRES_AT]    DATETIMEOFFSET NULL,
        [IS_ANONYMOUS]  BIT            NOT NULL DEFAULT 1,
        [SURVEY_KIND]   NVARCHAR(20)   NOT NULL DEFAULT N'SURVEY',
        CONSTRAINT [PK_SPHINX_SURVEYS] PRIMARY KEY ([ID])
    );
END

-- 1a. Migracija: MAX_RESPONSES stulpelis
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('[BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]')
      AND name = 'MAX_RESPONSES'
)
BEGIN
    ALTER TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]
    ADD [MAX_RESPONSES] INT NULL;
END

-- 1d. Migracija: SURVEY_KIND stulpelis (SURVEY arba POLL)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('[BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]')
      AND name = 'SURVEY_KIND'
)
BEGIN
    ALTER TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]
    ADD [SURVEY_KIND] NVARCHAR(20) NOT NULL CONSTRAINT [DF_SPHINX_SURVEYS_SURVEY_KIND] DEFAULT N'SURVEY';
END

-- 1b. Migracija: EXPIRES_AT stulpelis
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('[BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]')
      AND name = 'EXPIRES_AT'
)
BEGIN
    ALTER TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]
    ADD [EXPIRES_AT] DATETIMEOFFSET NULL;
END

-- 1c. Migracija: IS_ANONYMOUS stulpelis
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('[BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]')
      AND name = 'IS_ANONYMOUS'
)
BEGIN
    ALTER TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]
    ADD [IS_ANONYMOUS] BIT NOT NULL CONSTRAINT [DF_SPHINX_SURVEYS_IS_ANONYMOUS] DEFAULT 1;
END

-- 2. Klausimai ────────────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE name = 'SPHINX_SURVEY_QUESTIONS' AND schema_id = SCHEMA_ID('dbo')
)
BEGIN
    CREATE TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_QUESTIONS] (
        [ID]               INT            IDENTITY(1,1) NOT NULL,
        [SURVEY_ID]        INT            NOT NULL,
        [PARENT_OPTION_ID] INT            NULL,
        [QUESTION_TEXT]    NVARCHAR(1000) NOT NULL,
        [QUESTION_TYPE]    NVARCHAR(50)   NOT NULL DEFAULT N'TEXT',
        [TEXT_VALIDATION]  NVARCHAR(20)   NOT NULL DEFAULT N'NONE',
        [QUESTION_ORDER]   INT            NOT NULL DEFAULT 0,
        CONSTRAINT [PK_SPHINX_QUESTIONS] PRIMARY KEY ([ID]),
        CONSTRAINT [FK_SPHINX_Q_SURVEY]
            FOREIGN KEY ([SURVEY_ID])
            REFERENCES [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]([ID])
    );
END

-- 2a. Migracija: PARENT_OPTION_ID stulpelis (jei lentelė sukurta senu skriptu)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('[BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_QUESTIONS]')
      AND name = 'PARENT_OPTION_ID'
)
BEGIN
    ALTER TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_QUESTIONS]
    ADD [PARENT_OPTION_ID] INT NULL;
END

-- 2b. Migracija: TEXT_VALIDATION stulpelis
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('[BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_QUESTIONS]')
      AND name = 'TEXT_VALIDATION'
)
BEGIN
    ALTER TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_QUESTIONS]
    ADD [TEXT_VALIDATION] NVARCHAR(20) NOT NULL CONSTRAINT [DF_SPHINX_QUESTIONS_TEXT_VALIDATION] DEFAULT N'NONE';
END

-- 3. Atsakymų variantai ────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE name = 'SPHINX_SURVEY_OPTIONS' AND schema_id = SCHEMA_ID('dbo')
)
BEGIN
    CREATE TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_OPTIONS] (
        [ID]                  INT           IDENTITY(1,1) NOT NULL,
        [QUESTION_ID]         INT           NOT NULL,
        [OPTION_TEXT]         NVARCHAR(500) NOT NULL,
        [OPTION_ORDER]        INT           NOT NULL DEFAULT 0,
        [SKIP_ACTION]         NVARCHAR(20)  NOT NULL DEFAULT N'NEXT',
        [SKIP_TO_QUESTION_ID] INT           NULL,
        CONSTRAINT [PK_SPHINX_OPTIONS] PRIMARY KEY ([ID]),
        CONSTRAINT [FK_SPHINX_OPT_QUESTION]
            FOREIGN KEY ([QUESTION_ID])
            REFERENCES [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_QUESTIONS]([ID])
    );
END

-- 4. FK: QUESTIONS.PARENT_OPTION_ID → OPTIONS (po abu lenteliu sukurimo)
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_SPHINX_Q_PARENT_OPT'
)
BEGIN
    ALTER TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_QUESTIONS]
    ADD CONSTRAINT [FK_SPHINX_Q_PARENT_OPT]
        FOREIGN KEY ([PARENT_OPTION_ID])
        REFERENCES [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_OPTIONS]([ID]);
END

-- 5. FK: OPTIONS.SKIP_TO_QUESTION_ID → QUESTIONS
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_SPHINX_OPT_SKIP_TO'
)
BEGIN
    ALTER TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_OPTIONS]
    ADD CONSTRAINT [FK_SPHINX_OPT_SKIP_TO]
        FOREIGN KEY ([SKIP_TO_QUESTION_ID])
        REFERENCES [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_QUESTIONS]([ID]);
END

-- 6. Respondentų atsakymų antraštės ────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE name = 'SPHINX_SURVEY_RESPONSES' AND schema_id = SCHEMA_ID('dbo')
)
BEGIN
    CREATE TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_RESPONSES] (
        [ID]          INT            IDENTITY(1,1) NOT NULL,
        [SURVEY_ID]   INT            NOT NULL,
        [RESPONDENT]  NVARCHAR(100)  NOT NULL,
        [ANSWERED_AT] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT [PK_SPHINX_RESPONSES] PRIMARY KEY ([ID]),
        CONSTRAINT [FK_SPHINX_RESP_SURVEY]
            FOREIGN KEY ([SURVEY_ID])
            REFERENCES [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEYS]([ID]),
        CONSTRAINT [UQ_SPHINX_RESP_USER]
            UNIQUE ([SURVEY_ID], [RESPONDENT])
    );
END

-- 7. Atskiri atsakymai (viena eilutė = vienas klausimas arba vienas variantas) ─
IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE name = 'SPHINX_SURVEY_ANSWERS' AND schema_id = SCHEMA_ID('dbo')
)
BEGIN
    CREATE TABLE [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_ANSWERS] (
        [ID]          INT            IDENTITY(1,1) NOT NULL,
        [RESPONSE_ID] INT            NOT NULL,
        [QUESTION_ID] INT            NOT NULL,
        [ANSWER_TEXT] NVARCHAR(MAX)  NULL,
        [OPTION_ID]   INT            NULL,
        CONSTRAINT [PK_SPHINX_ANSWERS] PRIMARY KEY ([ID]),
        CONSTRAINT [FK_SPHINX_ANS_RESPONSE]
            FOREIGN KEY ([RESPONSE_ID])
            REFERENCES [BLS_LOGIT_STAT].[dbo].[SPHINX_SURVEY_RESPONSES]([ID])
    );
END
