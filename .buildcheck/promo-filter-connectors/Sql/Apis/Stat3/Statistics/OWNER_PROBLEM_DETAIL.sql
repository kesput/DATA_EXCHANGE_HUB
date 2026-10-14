-- SQL_PROCEDURE: [SNTXDATA].[dbo].[OWNER_PROBLEM_DETAIL]
-- SQL_DESCRIPTION: Detalus savininko problemu sarasas pagal laikotarpi. Rezultate matomi konkretus dokumentai, klientai, adresai, komentarai ir problemu tipai.
-- SQL_PARAM_MUST: @OWNER|text||Prekiu savininko kodas. Iveskite tiksliai 3 simbolius, pvz. KGR.|regex=^[A-Za-z0-9]{3}$;message=@OWNER turi buti tiksliai 3 raides arba skaiciai.
-- SQL_PARAM_MUST: @PERIOD_NAME|text|w|KPI laikotarpis: w - savaite, m - menuo.|values=w,m
-- SQL_PARAM_MUST: @LAST_PERIOD_STEP|int|0|Kiek savaiciu ar menesiu atgal pateikti skaiciavima. 0 - einamas laikotarpis, 1 - vienas laikotarpis anksciau ir t.t.
-- SQL_PARAM: @EXCLUDE|text|NULL|Problemos tipas, kurio nevertinti. NULL - nieko neismesti, DELAY - nevertinti velavimo problemu.|values=NULL,DELAY
 

