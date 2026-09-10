# Financial Transaction Logic Audit — Silk.Trading.Web

Scope: SalesController, PurchasesController, SaleReturnsController, PurchaseReturnsController, PaymentsController, InventoryAdjustmentsController, InventoryService/IInventoryService, StockController.

Date: 2026-08-31

## Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High     | 5 |
| Medium   | 4 |
| Low      | 4 |

The `InventoryService` correctly wraps its multi-step stock operations (stock update + stock movement + invoice creation) in explicit `BeginTransactionAsync` transactions and uses the `Item.RowVersion` concurrency token with a retry loop. This is the well-implemented core. The main weaknesses are in **payment allocation** (separate commits, no concurrency protection, not idempotent), **return-quantity validation** (TOCTOU race / not re-validated in the service), and **mass assignment of `IsPaid`**.

---

## High severity

### H-1 — Payment record and invoice allocation are committed separately; allocation is not transactional with the payment
File: `Controllers\PaymentsController.cs:85-146`

The payment is saved and committed at line 92, and only afterwards `ApplyInvoiceAllocationAsync` mutates invoice `PaidAmount`/`IsPaid` and commits them in a *separate* `SaveChangesAsync` at line 146. There is no shared transaction between the two.

```csharp
_db.Payments.Add(payment);
try
{
    await _db.SaveChangesAsync();                       // payment committed here
    await ApplyInvoiceAllocationAsync(payment);         // separate commit inside
    ...
}
```

```csharp
// ApplyInvoiceAllocationAsync (line 123)
if (inv.PaidAmount >= inv.NetAmount) inv.IsPaid = true;
...
await _db.SaveChangesAsync();   // line 146 — separate commit
```

Impact / risk:
- If allocation fails (line 169 catch), the payment is already committed and durable while the invoices are unchanged. The user was told "الدفعة محفوظة" but it was never applied — a permanent accounting discrepancy that the code only mitigates with a warning.
- There is no linkage from payment → applied invoices, so the operation is not idempotent and cannot be re-applied safely.

Recommendation: Create the payment and apply the allocation inside a single `BeginTransactionAsync`. Consider tracking per-invoice allocations (payment line-items) so the allocation is explicit and re-runnable.

### H-2 — Invoice `PaidAmount` update has no concurrency protection → lost update / over-allocation (double-spend)
File: `Controllers\PaymentsController.cs:123-167`

`SaleInvoice` and `PurchaseInvoice` have **no** `[Timestamp]`/`RowVersion` and the allocation reads `inv.PaidAmount`, computes `outstanding`, and writes `inv.PaidAmount += allocate`:

```csharp
var outstanding = inv.NetAmount - inv.PaidAmount;
if (outstanding > 0)
{
    var allocate = Math.Min(outstanding, remaining);
    inv.PaidAmount += allocate;
    remaining -= allocate;
}
```

Two payments applied concurrently against the same invoice will both read the same `PaidAmount`, both compute the same `outstanding`, and both `+=` — producing a cumulative `PaidAmount > NetAmount`. Stock changes use the concurrency token, but this money path does not.

Recommendation: Add a `RowVersion` (or use an atomic `UPDATE ... SET PaidAmount = PaidAmount + @amt WHERE ... AND PaidAmount + @amt <= NetAmount`) so allocation conflicts fail rather than silently over-pay.

### H-3 — Return-quantity validation is TOCTOU and is not re-checked in the service (can return more than sold → inventory inflation)
Files: `Controllers\SaleReturnsController.cs:62-93`, `Controllers\PurchaseReturnsController.cs:62-93`, `Services\InventoryService.cs:121-201`

The controller validates that returned quantities do not exceed the sold/purchased quantities *before* calling the service:

```csharp
decimal returnedCount = alreadyReturned.Where(r => r.ItemId == line.ItemId).Sum(r => r.Count);
...
if (line.Count + returnedCount > invLine.Count || line.Quantity + returnedQty > invLine.Quantity)
{
    ModelState.AddModelError("", "الكمية المرتجعة أكبر من ...");
}
```

Then, separately (line 95-98):

```csharp
if (ModelState.IsValid)
{
    var (ok, error) = await _inventory.CreateSaleReturnAsync(saleReturn, items, ...);
```

Problems:
1. The validation query and the insert in `CreateSaleReturnAsync` are not in the same transaction. Two concurrent (or double-click) submissions both observe the same cumulative returned quantity, both pass validation, and both add stock back — returning more units than were sold. This is a classic TOCTOU double-spend.
2. `CreateSaleReturnAsync` / `CreatePurchaseReturnAsync` do **not** re-validate quantities against the invoice or against cumulative prior returns at all — they apply stock unconditionally (`Services\InventoryService.cs:140-147` and `181-188`).

Recommendation: Move the "returned ≤ sold" check inside the service, inside the existing transaction, with a serializable/locked or atomically-updated cumulative-return counter so concurrent returns cannot both pass.

### H-4 — Mass assignment: client can forge `IsPaid` to mark invoices fully paid without payment
Files: `Controllers\SalesController.cs:43` (`invoice.PaidAmount = invoice.IsPaid ? invoice.NetAmount : 0`), `Controllers\PurchasesController.cs:99`, `Models\Sales\SaleInvoice.cs:46-47`, `Models\Purchases\PurchaseInvoice.cs:46-47`

`SaleInvoice.IsPaid` and `PurchaseInvoice.IsPaid` are plain `bool` properties with no `[BindNever]`, and `SaleInvoiceViewModel`/`PurchaseInvoiceViewModel` bind the entire `SaleInvoice`/`PurchaseInvoice` entity from the form. The service trusts it to compute the paid amount:

```csharp
invoice.PaidAmount = invoice.IsPaid ? invoice.NetAmount : 0;
```

A crafted POST with `Invoice.IsPaid=true` (or via nested property binding) produces an invoice recorded as fully paid (`IsPaid=true`, `PaidAmount=NetAmount`) even though no payment was captured. This corrupts revenue and accounts-receivable tracking.

Recommendation: Mark `IsPaid`/`PaidAmount` with `[BindNever]` (and set them from actual payment records only), or use a DTO that does not bind these fields. All money fields are recomputed server-side — good — but `IsPaid` is the gap.

### H-5 — No idempotency protection on payment creation (double-submit creates duplicate payments)
File: `Controllers\PaymentsController.cs:61-113`

There is no idempotency key or uniqueness constraint distinguishing legitimate duplicate submissions (double-click, network retry). Each request adds a new `Payment` record and each re-runs `ApplyInvoiceAllocationAsync`, with the concurrency danger from H-2.

```csharp
for (int attempt = 1; attempt <= 3; attempt++)
{
    ...
    _db.Payments.Add(payment);
    await _db.SaveChangesAsync();
    await ApplyInvoiceAllocationAsync(payment);
```

The retry loop only guards receipt-number uniqueness; it does nothing for accidental duplicate whole payments.

Recommendation: Require an idempotency key (e.g., a client-supplied nonce or a unique `(CustomerId/SupplierId, Amount, PaymentDate)` check) and reject repeats.

---

## Medium severity

### M-1 — Client-supplied line quantities are trusted for stock movement
File: `Services\InventoryService.cs:312-322`, `19-20`

`ToStockLines` copies `Count`/`Quantity` straight from posted line items into stock movements, and the `valid` filter accepts any line where *either* quantity or count is `> 0`:

```csharp
var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
```

Stock is only protected because `[Range(0, 999999999)]` on `Item.Quantity`/`Item.Count` rejects negatives during model binding — but the service itself does no defensive check and will happily move client-supplied quantities. If binding is bypassed or a validation gap appears, stock could be manipulated. The *money* totals are recomputed server-side (good); stock *quantities* are not.

Recommendation: Validate quantities in the service (reject `Count < 0 || Quantity < 0`).

### M-2 — StockController.Report aggregates over the filtered (not total) stock set
File: `Controllers\StockController.cs:46-83`

`TotalCount`, `TotalQuantity`, and `TotalValue` are computed on the `query` that already includes `search`, `categoryId`, and `lowOnly` filters (applied at lines 52-59):

```csharp
TotalCount = await query.SumAsync(i => i.CurrentCount),
TotalQuantity = await query.SumAsync(i => i.CurrentQuantity),
TotalValue = await query.SumAsync(i => ... PurchasePrice)
```

When the user searches or filters by category, the "totals" silently become subset totals rather than company-wide stock value, which can mislead financial reporting.

Recommendation: Compute report totals on the unfiltered active-items query; use the filtered query only for the paged item list.

### M-3 — InventoryAdjustmentsController.Delete restores stock without a concurrency check
File: `Controllers\InventoryAdjustmentsController.cs:77-99`

The delete path reads the movement and rewrites `item.CurrentCount`/`CurrentQuantity` inside a transaction but never compares against a fresh concurrency token:

```csharp
var item = await _db.Items.FindAsync(stock.ItemId);
if (item != null)
{
    item.CurrentCount = stock.CountBefore;
    item.CurrentQuantity = stock.BalanceBefore;
}
```

It does guard against later movements (`hasLaterMovements`, line 81), but the `Item` itself carries no optimistic-concurrency protection here, so a concurrent stock change on the same item between load and save could be lost.

Recommendation: Consume `item.RowVersion` and handle `DbUpdateConcurrencyException`.

### M-4 — Incrementing public IDs exposed for financial documents
Files: `Models` (all `Id = int` identity), URL routes `Details(int id)`, etc.

All financial documents use small auto-increment integer IDs. Leaks document volume via sequential numbering and is part of the H-3/H-4 attack surface (guessable/iterable). Appropriate for an internal admin tool, but worth noting if this is ever exposed beyond staff.

Recommendation: Keep internal identity IDs but expose opaque UUIDs/denormalized document numbers in URLs if reachable by customers.

---

## Low severity

### L-1 — Payment allocation failure is only reported via TempData warning
File: `Controllers\PaymentsController.cs:169-173`

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "فشل تطبيق دفعة {ReceiptNumber} ...", payment.ReceiptNumber);
    TempData["Warning"] = "تم حفظ الدفعة لكن تعذر تطبيقها ...";
}
```

No persistent audit record that a payment exists without an allocation. Acceptable UX fallback, but the inconsistency is silent beyond a transient flash message.

### L-2 — Return-number generation uses `CountAsync() + 1` (not max)
File: `Services\InventoryService.cs:339-352`

Return numbers are derived from row count rather than the highest existing number. With deletions or gaps, numbers can collide-or-repeat across date boundaries; the unique index + retry loop prevents corruption but may raise confusing validation errors. Invoice numbers share this pattern (`NextInvoiceNumberAsync`, line 324-337).

### L-3 — SalesController/PurchasesController Create preview numbers can be stale
Files: `Controllers\SalesController.cs:40-41`, `Controllers\PurchasesController.cs:40-41`

The initial form number uses `lastInvoice.Id + 1`, which can collide under concurrency. The service re-generates and retries, so this is cosmetic, not a correctness bug.

### L-4 — No direct-SQL or obvious injection issues found
The data layer is entirely EF Core; no raw SQL, no string-concatenated queries. Model validation uses `[Range]`/`[Required]`. Permission checks (`RequirePerm`) are consistently applied on mutating actions, and cross-entity ownership is validated in returns/payments controllers.

---

## What is done well (confirmations)

- **Explicit transactions**: `InventoryService` wraps stock + movement + invoice/return + adjustment writes in `BeginTransactionAsync` and rolls back on error (`Services\InventoryService.cs:24,72,128,169,207`).
- **Concurrency token on stock**: `Item.RowVersion` (`Models\Core\Item.cs:89-90`) + `DbUpdateConcurrencyException` retry loop in every service method prevents lost stock updates on concurrent sales.
- **Negative stock guard**: `ApplyStockAsync` rejects any decrement that would drive `CurrentCount`/`CurrentQuantity` negative (`Services\InventoryService.cs:276-281`).
- **Money recomputed server-side**: `TotalAmount`/`NetAmount`/`PaidAmount` are overwritten from items, not trusted from the form (`Services\InventoryService.cs:36-43,92-99`).
- **Cross-type payment validation** (receipt requires customer, disbursement requires supplier) and ownership checks on returns invoices are present at the controller layer.
```
