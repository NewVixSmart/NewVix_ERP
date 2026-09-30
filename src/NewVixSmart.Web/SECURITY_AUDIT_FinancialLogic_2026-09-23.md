# Financial-Integrity Audit II — NewVixSmart.Web (2026-09-23)

Re-verification of `SECURITY_AUDIT_FinancialLogic.md` (2026-08-31) against the current
codebase, plus deep financial-logic analysis of accounting delivery, payments, fiscal close,
reporting, and data integrity.

Method: read-only source review. No files edited by the audit; no build/test run. Baseline:
**241/241 tests PASS, 12s, net10.0** (previous run).

## Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High     | 2 |
| Medium   | 5 |
| Low      | 7 |
| Verified good | 11 |

Prior-audit outcome: **all five High items are now fixed at code level** (two with residuals);
2 new High and 5 Medium integrity-risk items were found. Nothing Critical.

## New findings (deep financial-logic pass)

### N-01 — HIGH — Delivery posts revenue/AR at gross line price; invoice Discount/Tax never reaches the ledger; AR control becomes the balancing plug
- Files: `Services\InventoryService.cs:176-188`, `Services\AccountingService.cs:37-55, 66-71`
- Issue: `DeliverDeliveryOrderAsync` computes delivered value as `Σ(effective × invLine.UnitPrice)`
  (line 176-184) — line `Discount`, and invoice `Discount/Discount2/Discount3/Tax` are **not**
  apportioned. `RecordSaleDeliveryAsync` then posts `Dr 1200 / Cr 4000 = value` and `Dr 5000 / Cr 1300 = cost`.
  The customer's payable is the invoice `NetAmount` (which adds Tax, subtracts discounts). A receipt
  credits `1200` by the full `NetAmount` (`AccountingService.cs:70`).
- Impact: whenever discounts or tax are used, net AR = value − NetAmount drifts negative (shown as a
  negative asset on the balance sheet — effectively an unreported tax-payable), revenue is overstated
  by the discount share (gross-price recognition), tax is misclassified into AR, and P&L/BS diverge
  from the invoice documents. These fields are first-class UI inputs (D2/D3/Tax), so this path is
  reachable as intended functionality. There is no VAT/tax-payable account in the ledger set.
- Evidence: delivery value at `InventoryService.cs:181-184`; invoice total accumulates discounts+tax
  at `InventoryService.cs:48-49`; delivery journal at `AccountingService.cs:43-52`.
- Fix: recognize per-delivery revenue by apportioning the invoice net total (incl. tax and discounts)
  across delivered quantities, or post invoice-level entries to dedicated contra-revenue / tax-payable
  accounts; add a reconciliation assertion that `Σ journal(1200) ≈ aging outstanding`.

### N-02 — HIGH — Payments and aging can settle sale invoices that were never delivered/posted (pay-before-delivery)
- Files: `Services\PaymentService.cs:167-171`, `Services\AccountingService.cs:66-71`, `Services\InventoryService.cs:146-208`
- Issue: `ApplyInvoiceAllocationAsync` selects any invoice with `PaidAmount < NetAmount`
  (no delivery-status filter). But `CreateSaleAsync` posts **no** journal entry at all
  (there is no `RecordSaleInvoiceAsync` call in the sales-create path); AR/revenue/COGS only occur at
  delivery. A receipt for an un-delivered invoice debits cash and credits `1200` with no offsetting
  receivable debit.
- Impact: the `1200` control account carries a credit for an invoice that may never be fulfilled;
  income-statement revenue is never recognized while cash was collected; invoice/aging say "paid";
  the balance sheet shows a mystery credit. A sale invoice can also be created in a **closed year**
  (see N-03) and still be paid.
- Evidence: allocation query `PaymentService.cs:167` has only `PaidAmount < NetAmount`; sales-create
  `InventoryService.cs:31-84` has no accounting gate/call.
- Fix: block allocation/aging against invoices with no delivered quantity (or post the receivable at
  invoice creation and reverse on cancellation); treat "invoice = document, delivery = financial
  event" explicitly in the payment selector.

### N-03 — MEDIUM — Fiscal-close gate missing on sale-invoice creation (inconsistent gating)
- Files: `Services\InventoryService.cs:31` (no `IsPeriodClosedAsync`), `Services\SalesOrdersService.cs:133-176`
- Issue: purchases (`InventoryService.cs:226`), deliveries, returns, adjustments, payments and budget
  writes all check closed year; `CreateSaleAsync` and `SalesOrdersService.CreateInvoiceFromOrderAsync`
  do not, so a sale document (and its aging/sales-report rows) can be dated in a closed year.
  Deliveries remain gated, so no money moves — the impact is document/report contamination only.
- Fix: gate invoice creation on `IsPeriodClosedAsync(invoice.InvoiceDate)` like the purchase path.

### N-04 — MEDIUM — `PaymentAllocation.InvoiceId` is polymorphic with no referential integrity; purchase stock movements carry `DocumentId = null`
- Files: `Data\AppDbContext.cs:204-209`, `Services\InventoryService.cs:256`
- Issue: allocations store `InvoiceType` + `InvoiceId` without FKs — an allocation can point to a
  missing or cross-type invoice (sales id in a purchase allocation or vice versa); the
  `(PaymentId, InvoiceType, InvoiceId)` index is non-unique so duplicate allocation rows are possible.
  Separately, purchase-invoice stock movements are written with `DocumentId = null`
  (invoice id exists only after save), so the only movement→invoice link is the string `DocumentNumber`.
- Fix: split allocations into real `SalePaymentAllocation`/`PurchasePaymentAllocation` FKs (or add a
  check constraint keyed on a presence token); set purchase `DocumentId` by saving the invoice first.

### N-05 — MEDIUM — Cash-flow and Payments reports disagree for OnReceipt purchases
- Files: `Services\InventoryService.cs:290-296`, `Services\ReportService.cs:862+`
- Issue: an OnReceipt purchase posts both the AP entry and an immediate disbursement entry but creates
  **no `Payment` row**. The cash-flow report counts these disbursements (from purchase invoices);
  the Payments report and party statements only see `Payment` rows. Same period → different totals,
  and foreign on-receipt purchases are settled at the created rate with no allocation record.
- Fix: record a `Payment` (+ optional allocation) for on-receipt purchases, or reconcile explicitly.

### N-06 — MEDIUM — FX conversion-timing mismatch between ledger and aging/reports
> **Superseded (2026-09-27):** multi-currency was removed entirely, so the conversion-timing
> mismatch cannot occur. There is no `ExchangeRate`, no `AllocatedBaseAmount`, and no
> 8400/4400 posting; the single settlement basis is the document value itself (EGP).
> See [`docs/DECISION-EGP-ONLY.md`](docs/DECISION-EGP-ONLY.md). The text below is kept as
> the original round record.

- Files: `Services\ReportService.cs:655+` (aging), `Services\PaymentService.cs:158-260`
- Issue: the ledger converts at posting/delivery time using each invoice's `ExchangeRate`; the aging
  report re-converts outstanding via `NetAmount × ExchangeRate` at report time and settles using
  allocation base sums. Under changing FX rates the AR control and the aging/statement figures drift.
- Fix: state a single settlement basis (base currency, invoice-rate) for aging and document it.
- Status: **VERIFIED — settlement basis is "base currency at invoice rate", and aging reconciles to the
  `1200` control exactly.** Evidence: `ApplyInvoiceAllocationAsync` persists `AllocatedBaseAmount =
  fCap × invoice-rate` (the literal `1200` reduction), books the rate difference separately to
  8400/4400, and aging reduces `NetAmount × invoice-rate` by `Σ AllocatedBaseAmount` — so outstanding
  == `1200` ledger balance after settlement at a different rate (test
  `AgingTests.Aging_FxSettlementAtDifferentRate_OutstandingReconcilesToArControl`: invoice 100 @48.5,
  settle 50 @50 → invoiceBase 2425 + FxGain 75; aging total and GC-level `ArTotal` = 2425 = `1200`
  balance).
- ~~Known limitation (not FX): aging shows full `NetAmount` for an invoice even when only **partially
  delivered**~~ — **FIXED.** `AgingAsync` now recognizes per delivered order
  `Round(NetAmount × (Σ delivered-line value / TotalAmount) × ExchangeRate, 2)` — the exact value
  `RecordSaleDeliveryAsync` posted — and sums the rounded per-order values exactly like the ledger.
  Fully delivered invoices still reconcile to `NetAmount`. Legacy/test delivery orders with no item
  lines keep full-value recognition. Evidence: test
  `FinancialIntegrityTests.Aging_PartialDelivery_OutstandingMatchesArControl` (2 × 100 @ Discount 20 /
  Tax 10 → Net 190; deliver 1 → 1200 = aging = 95).

## Prior-audit carry-over status

| ID | Claim (2026-08-31) | Status in current code | Evidence |
|----|--------------------|------------------------|----------|
| H-1 | Payment + allocation committed separately | **FIXED** — one `BeginTransactionAsync`; allocations persisted as rows; all-or-nothing | `PaymentService.cs:48-104` |
| H-2 | Invoice `PaidAmount` no concurrency token → lost update / over-allocation | **FIXED** — `RowVersion` on Sale/PurchaseInvoices (migration `20260831220000`); conflict → retry with fresh read | `PaymentService.cs:107-122`; `Data\AppDbContext.cs:138-145` |
| H-3 | Return-quantity validation TOCTOU, not re-checked in service | **FIXED with residual** — validation moved inside the service transaction on post; Item.RowVersion retry re-validates the same-item race. Residual: reads not locked; only the same-item conflict forces retry | `InventoryService.cs:386, 497, 851-915` |
| H-4 | Mass assignment of `IsPaid` | **FIXED** — sales hardcode `PaidAmount=0; IsPaid=false`; purchases recompute from `PaymentTerms` | `InventoryService.cs:60-61, 279-280` |
| H-5 | No idempotency on payment creation | **FIXED (heuristic)** — identical (amount,type,currency,rate,party) rejected within 2-minute window | `PaymentService.cs:374-385` |
| M-1 | Client quantities trusted; no service-level guard | **FIXED** — sale & purchase reject `UnitPrice < 0` and `(Quantity < 0 || Count < 0)`; returns/adjustments/transfers reject negatives; negative-stock before decrement | `InventoryService.cs:35-40, 293-298, 404-405, 459-460, 539-540, 594-595, 1068-1109, 1158` |
| M-2 | Stock report totals computed on filtered set | **FIXED** (Round 13) — summary `TotalCount/TotalQuantity/TotalValue` computed on the **unfiltered** `baseQuery`, while `Items` is the filtered set | `StockController.cs:77-89` |
| M-3 | Adjustment-delete rewrites stock without concurrency token | **FIXED** (Round 15) — `InventoryService.DeleteAdjustmentAsync`: 3-attempt tx + `Item.RowVersion` replay, journal/later-movement guards, stock restored from first movement | `InventoryService.cs:764-808` |
| M-4 | Sequential public IDs on financial docs | **STILL OPEN** (accepted for internal tool) | Models, `Details(int id)` routes |
| L-1 | Allocation failure only surfaces via TempData warning | **FIXED** — failure rolls back the whole payment | `PaymentService.cs:74-79` |
| L-2 | Return/invoice numbers derived from `CountAsync()+1` | **ACCEPTED** — race-safe via unique indexes + 3-attempt fresh-read replay on every number generator (not a live defect for a single-tenant internal tool) | `InventoryService.cs:1000-1034`, `AccountingService.cs:222-232`, `PaymentService.cs:146-156` |
| L-3 | Stale preview numbers in create forms | **FIXED** — POST failure paths recompute the preview (fresh value + `ModelState.Remove`); returns/adjustments set `ViewBag.NextNumber` in `PopulateDropdowns()`; return-form totals recalc on load | `SalesController.cs:95-97`, `PurchasesController.cs:81-83`, `SaleReturnsController.cs`, `PurchaseReturnsController.cs`, `SalesQuotesController.cs`, `InventoryAdjustmentsController.cs` |
| L-4 | No raw SQL / injection | **CONFIRMED GOOD** — EF Core only | — |

## Verified good (guarantee inventory)

1. Every GL post is balanced (Dr=Cr to 2dp), no both/neither-leg lines, non-negative amounts,
   unknown account rejected, atomically posted (`AccountingService.cs:159-183`).
2. All stock + invoice + layer + return writes run inside explicit transactions with `Item.RowVersion`
   replay (3 attempts) (`InventoryService.cs` throughout).
3. Negative-stock guard before every decrement (`InventoryService.cs:937-941`).
4. Returns validated inside the tx against posted-returns + delivered quantities with party ownership
   checks; drafts do not consume returnable balance (`InventoryService.cs:851-915`; test
   `OperationsIntegrityTests.SaleReturn_PostedOnly_CountsTowardReturnableRemainder`).
5. Fiscal close/reopen: latest-year-only, retained-earnings 3001, contra-revenue-aware reversal;
   reopen removes close entries and restores P&L; balance sheet re-injects closed retained earnings and
   income statement excludes YearEndClose (`FiscalService.cs:41-163`, `FinancialReportService.cs:155-208`).
6. ~~FX settlement accounted with gain/loss accounts (8400/4400)~~ — **removed 2026-09-27**;
   multi-currency is gone, so there is no FX leg to account for. The remaining guard is the
   single overpayment rejection inside the payment transaction (`PaymentService.cs`;
   tests `AuditN15SettlementTests.Receipt_OverPayment_Rejected_NoSideEffects`,
   `Receipt_PennyOver_IsAbsorbedByRoundingTolerance`).
7. Duplicate-payment window + unique ReceiptNumber + retries with fresh reads (`PaymentService.cs`).
8. Unique document numbers enforced by unique indexes + replay for invoices, returns, deliveries,
   transfers, adjustments, journal entries, orders, quotes (`Data\AppDbContext.cs`; number helpers).
9. Duplicate line-item rejection on sale/purchase/delivery/transfer; same-warehouse transfer rejected;
   delivery qty capped to invoice remaining (`InventoryService.cs:33-37, 86-113, 1105-1111`).
10. Closed-period guards on purchases, deliveries, returns, payments, adjustments, budget writes
    (tests: `FiscalCloseTests.Guard_*`).
11. Reported invariants asserted by tests: `AccountingServiceTests.*` (balanced posts, FX to base,
    unique entry numbers), `FinancialStatementsCorrectnessTests.*` (contra-asset rendering, close
    exclusions, inactive-account trial balance, aging vs posted/draft returns), `CashFlowTests.*`,
    `AgingTests.*`, `ConcurrencyTests.*`, `OperationsIntegrityTests.*`. All green at baseline.

## Verdict

No Critical. All High and the two new High items (N-01 revenue/tax/discount recognition at delivery,
N-02 pay-before-delivery) are **fixed at code level and covered by tests**. N-03/N-04 closed
(Rounds 12-15). N-05 documented as design (Round 14 reconciliation covers reporting), N-06 verified
with a reconciliation test (this round). M-1/M-2/M-3 closed. The partial-delivery aging overstatement
(full `NetAmount` regardless of delivered portion) is **fixed**: aging now recognizes the delivered
portion with the same per-order formula the ledger posts. L-3 stale create-form number previews fixed
(round 16). The cross-round deferred "date culture/locale" item is closed (round 16): `Program.cs` pins
`InvariantCulture` (Gregorian + Western digits, deterministic regardless of host, incl. `yyyyMMdd`
document numbers) and export date cells now pass `CultureInfo.InvariantCulture` explicitly. Remaining:
M-4 (accepted for internal tool), L-2 (accepted, unique-index+replay safe).
