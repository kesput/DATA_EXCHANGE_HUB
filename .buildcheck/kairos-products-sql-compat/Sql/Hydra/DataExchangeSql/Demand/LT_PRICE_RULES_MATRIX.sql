-- HYDRA_FILTER: YYYYMM_FROM | prm.YYYYMM_FROM | int | =,>=,<=,in,!=
-- HYDRA_FILTER: YYYYMM_TILL | prm.YYYYMM_TILL | text | =,like,in,!=
-- HYDRA_FILTER: OWNER | prm.OWNER | text | =,like,in,!=
-- HYDRA_FILTER: GROUP_NAME2 | prm.GROUP_NAME2 | text | =,like,in,!=
-- HYDRA_FILTER: GROUP_NAME3 | prm.GROUP_NAME3 | text | =,like,in,!=
-- HYDRA_FILTER: GROUP_NAME4 | prm.GROUP_NAME4 | text | =,like,in,!=
-- HYDRA_FILTER: GROUP_NAME5 | prm.GROUP_NAME5 | text | =,like,in,!=
-- HYDRA_FILTER: GROUP_NAME6 | prm.GROUP_NAME6 | text | =,like,in,!=
-- HYDRA_FILTER: KORG_LIST | prm.KORG_LIST | text | =,like,in,!=
-- HYDRA_FILTER: BRANDNAME_LIST | prm.BRANDNAME_LIST | text | =,like,in,!=
-- HYDRA_FILTER: BRUTTO_RULE | prm.BRUTTO_RULE | text | =,like,in,!=
-- HYDRA_FILTER: STICKER_COUNT | prm.STICKER_COUNT | text | =,like,in,!=
-- HYDRA_FILTER: LABELLING_TYPE | prm.LABELLING_TYPE | text | =,like,in,!=
-- HYDRA_FILTER: TLICSUBTYPE | prm.TLICSUBTYPE | text | =,like,in,!=
-- HYDRA_FILTER: CATEGORY | prm.CATEGORY | text | =,like,in,!=
-- HYDRA_FILTER: OWNER_GR | prm.OWNER_GR | text | =,like,in,!=

SELECT
    prm.ID,
    prm.YYYYMM_FROM,
    prm.YYYYMM_TILL,
    prm.OWNER,
    prm.GROUP_NAME2,
    prm.GROUP_NAME3,
    prm.GROUP_NAME4,
    prm.GROUP_NAME5,
    prm.GROUP_NAME6,
    prm.STORTMP1,
    prm.TNET,
    prm.KORG_LIST,
    prm.BRANDNAME_LIST,
    prm.BRUTTO_RULE,
    prm.STICKER_COUNT,
    prm.LABELLING_TYPE,
    prm.TLICSUBTYPE,
    prm.SALES_PRICE,
    prm.LABELLING_ACT,
    prm.CATEGORY,
    prm.OWNER_GR,
    prm.IS_ACTIVE,
    prm.UPDATED_AT,
    prm.UPDATED_BY
FROM [BLS_LOGIT_STAT].[dbo].[HYDRA_PRICE_RULES_MATRIX] AS prm
WHERE 1=1
