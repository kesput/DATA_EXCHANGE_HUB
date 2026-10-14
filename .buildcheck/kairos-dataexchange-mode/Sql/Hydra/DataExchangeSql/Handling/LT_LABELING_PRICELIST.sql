-- HYDRA_FILTER_MUST: DATE_FROM | DATE_FROM | date | =,>=,<=
-- HYDRA_FILTER: DATE_TILL | DATE_TILL | date | <=
-- HYDRA_FILTER: OWNER | OWNER | varchar | =,like,in,!=
-- HYDRA_FILTER: KORG | KORG | varchar | =,like,in,!=
-- HYDRA_FILTER: INF_PREK | INF_PREK | varchar | =,like,in,!=
-- HYDRA_FILTER: CATEGORY | CATEGORY | varchar | =,like,in,!=
-- HYDRA_FILTER: MEASURE | MEASURE | varchar | =,like,in,!=
-- HYDRA_FILTER: MATERIALS | MATERIALS | varchar | =,like,in,!=

SELECT 
	   [FIRMID]
      ,[DATE_FROM]
      ,[DATE_TILL]
      ,[OWNER]
      ,[KORG]
      ,[INF_PREK]
      ,[PRODNAMELT]
      ,[CATEGORY]
      ,[MEASURE]
      ,[MATERIALS]
      ,[DU_NETTO]
      ,[SELL_PRICE]
      ,[OUTPUT_QTY_PER_HOUR]
  FROM [BLS_LOGIT_STAT].[dbo].[LABELING_PRICELIST]
  WHERE 1=1
