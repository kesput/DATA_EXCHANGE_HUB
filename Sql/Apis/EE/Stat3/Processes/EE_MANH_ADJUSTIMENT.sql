-- SQL_DESCRIPTION: Pagal data atvedama ADJUSTIMENT informacija, kurioje nurodoma kokiu budu buvo atliktas likuciu koregavimas (status change arba adjustment).
-- SQL_PARAM_MUST: @DATE|date||Koregavimo data. Rodomi pasirinktos dienos irasai.

-- created_at is ISO 8601 text with up to nine fractional-second digits.
-- Keep both range bounds as text: implicit datetime conversion rejects these values.
DECLARE @DateFrom nvarchar(10) = CONVERT(nvarchar(10), @DATE, 23);
DECLARE @DateTo nvarchar(10) = CONVERT(nvarchar(10), DATEADD(DAY, 1, @DATE), 23);

SELECT
	ACTION_DATE	   = CAST(M.created_at AS DATE)
   ,warehouse	   = M.warehouse_id
   ,ERP_WHS_FROM   = M.from_erp_warehouse_code
   ,ERP_WHS_TO	   = M.to_erp_warehouse_code
   ,ITEM		   = M.item_code
   ,ITEM_DESCRIPTION = M.item_description
   ,[LOCATION]	   = M.from_location_code
   ,QTY			   = TRY_CAST(REPLACE(CAST(M.moved_quantity AS NVARCHAR(50)), ',', '.') AS DECIMAL(18, 3))
   ,StatusFrom	   = M.from_inventory_status
   ,StatusTo	   = M.to_inventory_status
   ,USERNAME	   = M.created_by_user_id
   ,WorkB		   = 'Status Change'
   ,OWNER_CODE	   = M.owner_item_code
   ,WAREHOUSE_AREA = ''
   ,BBD			   = M.from_expiration_date
   ,PROJECT		   = M.company_id
   ,reason		   = M.reason_code
   ,DOCUMENT_TYPE  = A.document_type
   ,DOCUMENT_NO	   = A.atlas_movement_number
   ,[SOURCE]	   = 'STATUS_CHANGE'
FROM [BLSTXSTAT1].BLS_LOGIT_STAT.dbo.EE_movements_STATUS_CHANGE AS M WITH (NOLOCK)
LEFT JOIN [BLSTXSTAT1].BLS_LOGIT_STAT.dbo.EE_movement_mex AS A WITH (NOLOCK)
	 ON A.message_id = M.message_id
WHERE M.created_at >= @DateFrom
  AND M.created_at < @DateTo
UNION
SELECT
	ACTION_DATE	   = CAST(M.created_at AS DATE)
   ,warehouse	   = M.warehouse_id
   ,ERP_WHS_FROM   = M.erp_warehouse_code
   ,ERP_WHS_TO	   = M.erp_warehouse_code
   ,ITEM		   = M.item_code
   ,ITEM_DESCRIPTION = M.item_description
   ,[LOCATION]	   = M.location_code
   ,QTY			   = TRY_CAST(M.quantity_after AS DECIMAL(18, 3)) - TRY_CAST(M.quantity_before AS DECIMAL(18, 3))
   ,StatusFrom	   = M.inventory_status
   ,StatusTo	   = M.inventory_status
   ,USERNAME	   = M.created_by_user_id
   ,WorkB		   = 'adjustment'
   ,OWNER_CODE	   = M.owner_item_code
   ,WAREHOUSE_AREA = ''
   ,BBD			   = M.expiration_date
   ,PROJECT		   = M.company_id
   ,reason		   = M.reason_code
   ,DOCUMENT_TYPE  = A.document_type
   ,DOCUMENT_NO	   = A.atlas_adjustment_number
   ,[SOURCE]	   = 'ADJUSTIMENT'
FROM [BLSTXSTAT1].BLS_LOGIT_STAT.dbo.EE_adjustment_internal AS M WITH (NOLOCK)
LEFT JOIN [BLSTXSTAT1].BLS_LOGIT_STAT.dbo.EE_adjustments_mex AS A WITH (NOLOCK)
	 ON A.message_id = M.message_id
WHERE M.created_at >= @DateFrom
  AND M.created_at < @DateTo;
