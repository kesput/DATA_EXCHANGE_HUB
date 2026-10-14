-- SQL_PROCEDURE: [SNTXDATA].[dbo].[OWNER_PROBLEM_SL]
-- SQL_DESCRIPTION: Savininko problemu KPI suvestine pagal pasirinkta laikotarpi. Rezultate matomi problemu kiekiai ir KPI pjuviai pagal pasirinktus parametrus.
-- SQL_PARAM_MUST: @OWNER|text||Prekiu savininko kodas. Iveskite tiksliai 3 simbolius, pvz. KGR.|regex=^[A-Za-z0-9]{3}$;message=@OWNER turi buti tiksliai 3 raides arba skaiciai.
-- SQL_PARAM_MUST: @PERIOD_NAME|text|w|KPI laikotarpis: w - savaite, m - menuo.|values=w,m
-- SQL_PARAM_MUST: @LAST_PERIOD_STEP|int|0|Kiek savaiciu ar menesiu atgal pateikti skaiciavima. 0 - einamas laikotarpis, 1 - vienas laikotarpis anksciau ir t.t.
-- SQL_PARAM_MUST: @TIME_TOLERANCE_MINUTES|int|0|Tolerancija minutemis velavimams skaiciuoti. 0 - be papildomos tolerancijos.
-- SQL_PARAM: @EXCLUDE|text|NULL|Problemos tipas, kurio nevertinti. NULL - nieko neismesti, DELAY - nevertinti velavimo problemu.|values=NULL,DELAY
-- SQL_PARAM: @DISPLAY_TYPE|text|NULL|Atvaizdavimas. NULL - suminis vaizdas, DAILY - skaidyti dienomis.|values=NULL,DAILY
-- SQL_PARAM_MUST: @PLUS_KPI|text|ALL|Kokius KPI atvaizduoti: ALL, LINE, SKU arba PLL.|values=ALL,LINE,SKU,PLL

