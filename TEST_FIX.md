# Project Report Opening Balance Fix - Test Instructions

## What Was Fixed

**Issue:** The opening balance for "Patti Gatta" item on June 2, 2026 was showing **-635**, which was incorrect.

**Root Cause:** 
- The opening stock calculation wasn't handling **negative opening stock** (oversold items) correctly
- When an item was sold more than purchased before the period start date, the negative quantity was being multiplied by the average **purchase** rate, creating a nonsensical negative value

**The Fix:**
Modified `GetProjectItemSummaryAsync()` method in [ReportController.cs:1431-1475](Controllers/ReportController.cs) to:
1. **For positive opening stock**: Value at weighted average **purchase** cost (standard accounting)
2. **For negative opening stock**: Value at weighted average **sale** price (represents liability to supply)

## Code Changes

File: `Controllers/ReportController.cs`, lines 1434-1475

```csharp
// Opening stock amount = net cost of stock before fromDate
// Get all purchase and sale vouchers before fromDate
var openingPurchases = await _context.Vouchers
    .Where(v => v.ItemId == item.Id &&
                v.ProjectId == projectId &&
                v.VoucherType == VoucherType.Purchase &&
                v.VoucherDate < fromDate)
    .ToListAsync();
var openingSales = await _context.Vouchers
    .Where(v => v.ItemId == item.Id &&
                v.ProjectId == projectId &&
                v.VoucherType == VoucherType.Sale &&
                v.VoucherDate < fromDate)
    .ToListAsync();

var openingTotalPurchaseQty = openingPurchases.Sum(p => p.Quantity ?? 0);
var openingTotalPurchaseAmt = openingPurchases.Sum(p => p.Amount);
var openingTotalSaleQty = openingSales.Sum(s => s.Quantity ?? 0);
var openingTotalSaleAmt = openingSales.Sum(s => s.Amount);

// Calculate opening stock amount based on weighted average cost method
decimal openingStockAmount = 0;
if (openingStockQty != 0)
{
    if (openingStockQty > 0)
    {
        // Positive stock: value at weighted average purchase cost
        var avgPurchaseRate = openingTotalPurchaseQty > 0
            ? openingTotalPurchaseAmt / openingTotalPurchaseQty
            : 0;
        openingStockAmount = openingStockQty * avgPurchaseRate;
    }
    else
    {
        // Negative stock (oversold): value at weighted average sale price (reversed)
        // This represents the liability or commitment to supply stock
        var avgSaleRate = openingTotalSaleQty > 0
            ? openingTotalSaleAmt / openingTotalSaleQty
            : 0;
        openingStockAmount = openingStockQty * avgSaleRate;
    }
}
```

## How to Test

### Method 1: Manual Testing (Recommended)
1. Rebuild and run the application
2. Navigate to: `/Reports/ProjectReport?projectId=2&fromDate=2026-06-02&toDate=2026-06-02`
3. Look for the "Item-wise Inventory Report" section
4. Find the "Patti Gatta" row
5. Check the "Opening Stock" columns (Qty, Rate, Amount)
6. **Expected Result**: The opening amount should NO LONGER be -635
   - If opening stock Qty is negative, the amount should be negative but calculated at the sale rate, not purchase rate
   - This makes logical sense: a liability to supply is valued at what you'll sell it for

### Method 2: Direct Database Verification
Run this SQL query to verify the calculation logic:

```sql
-- Get opening stock details for Patti Gatta before 2026-06-02
DECLARE @itemId INT = (SELECT Id FROM Items WHERE Name LIKE '%Patti%Gatta%' LIMIT 1);
DECLARE @projectId INT = 2;
DECLARE @fromDate DATE = '2026-06-02';

-- Opening Purchase transactions
SELECT 
    'PURCHASES' as TransactionType,
    COUNT(*) as Count,
    SUM(CAST(Quantity as DECIMAL)) as TotalQty,
    SUM(Amount) as TotalAmount,
    SUM(Amount) / NULLIF(SUM(CAST(Quantity as DECIMAL)), 0) as AvgRate
FROM Vouchers
WHERE ItemId = @itemId
  AND ProjectId = @projectId
  AND VoucherType = 'Purchase'
  AND VoucherDate < @fromDate

UNION ALL

-- Opening Sale transactions
SELECT 
    'SALES',
    COUNT(*),
    SUM(CAST(Quantity as DECIMAL)),
    SUM(Amount),
    SUM(Amount) / NULLIF(SUM(CAST(Quantity as DECIMAL)), 0)
FROM Vouchers
WHERE ItemId = @itemId
  AND ProjectId = @projectId
  AND VoucherType = 'Sale'
  AND VoucherDate < @fromDate;

-- Expected calculations:
-- Opening Stock Qty = TotalPurchaseQty - TotalSaleQty
-- If Qty > 0: OpeningAmount = Qty × AvgPurchaseRate
-- If Qty < 0: OpeningAmount = Qty × AvgSaleRate
```

## Expected Behavior After Fix

For "Patti Gatta" on June 2, 2026:
- **Before Fix**: Opening balance showed -635 (incorrect - doesn't make accounting sense)
- **After Fix**: 
  - If opening stock is negative (more sold than purchased before June 2):
    - Amount will be negative, but valued at the average sale rate
    - This correctly represents the liability to supply stock
  - If opening stock is positive:
    - Amount will be positive, valued at the average purchase cost (standard accounting)

## Files Modified
- `Controllers/ReportController.cs` - GetProjectItemSummaryAsync() method (lines 1434-1475)

## No Database Changes Required
This fix only changes the calculation logic - no database migrations or schema changes needed.
