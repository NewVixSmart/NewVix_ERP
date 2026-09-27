# Audit Round 11 — Procurement & Order-Lifecycle (Agent-Assisted Audit)

Date: 2026-09-24 · Baseline: 308/308 (Round-10 `303f2cb`) → Result: **323/323** passing, 0 warnings, `/healthz` 200.

Method: 3 explore agents re-run successfully this round (they were offline in Round 10):
- **Procurement & orders** → full report received.
- **Financial-report math** → full report received (see "Deferred" below).
- **Print & settings** → agent resolved but could not reproduce its prior report (session context lost); the re-audit is queued as a follow-up round.
- Identity/backup-security was self-audited directly in Round 10 (already closed).

---

## Closed this round

### R11-1 [HIGH] Purchase order could be cancelled after partial receipt AND after invoicing
`CancelOrderAsync` only blocked `Received`/`Cancelled`. A partially-received PO was cancel-eligible, allowing a **posted** `PurchaseInvoice` (GL + stock/FIFO effects) to reference a Cancelled order, and stranding goods-receipt quantities with no reversal path.

**Fix** (`ProcurementService.cs:135-146`): cancel is now allowed only for `Draft`/`Approved`; blocked for `PartiallyReceived`/`Received` and for any order that already has a `PurchaseInvoice`. Two independent guards (status + invoice existence).

**Tests:** `CancelOrder_Approved_Succeeds`, `CancelOrder_PartiallyReceived_IsRejected`, `CancelOrder_AfterInvoice_IsRejected`.

### R11-2 [HIGH] Duplicate item lines bricked purchase-order invoicing
A PO with the same `ItemId` on two lines could be received but never invoiced: `CreateInvoiceFromOrderAsync` maps both lines into the invoice and `InventoryService.CreatePurchaseAsync` rejects the duplicate item, leaving the PO permanently stuck (`ProcurementService.cs:43-46, 90-93`).

**Fix:** `CreateOrderAsync` and `UpdateOrderAsync` reject a repeated `ItemId` before saving ("لا يمكن إضافة الصنف نفسه في أكثر من سطر").

### R11-3 [HIGH] Same brick for sales quotes/orders via duplicate items
`sales order`/quote creation had no duplicate-item rule while invoicing rejects duplicates — a quote with the same item on two lines converted to an order that could never be invoiced.

**Fix:** duplicate-`ItemId` rejection added to `SalesOrdersService.CreateOrderAsync`/`UpdateOrderAsync` and `SalesQuotesService.CreateAsync`; `ConvertToOrderAsync` also rejects duplicate lines (defense for pre-existing quotes).

**Note:** the existing `CreateQuote_ComputesTotals_LineQtyVsCount` fixture deliberately used the same item twice; updated to two distinct items (totals unchanged). This is now an actionable error instead of a silent brick.

### R11-4 [MEDIUM] Supplier-quote delete was a GET with only View permission, no antiforgery
`PurchaseRequestsController.Remove` ran a destructive delete on GET with `RequirePerm("PurchaseRequests.View")` (CSRF/browser-prefetch risk; view users could delete).

**Fix:** action is now `[HttpPost, ValidateAntiForgeryToken]` gated by the new **`PurchaseRequests.Delete`** permission (added to `PermissionCatalog` and to the Accountant + Warehouse default grants); the view uses a small POST form with antiforgery token + confirm, shown only to users with the Delete permission.

### R11-5 [MEDIUM] `SalesQuotesService.CreateAsync` accepted negative quantities/prices
A quote line with negative `Quantity`/`Count` could produce totals that diverge from the quoted `NetAmount` after conversion.

**Fix:** negative quantity and negative unit-price lines are rejected before saving (`SalesQuotesService.cs:35-38`), mirroring the `InventoryService` guards.

**Tests:** `CreateQuote_NegativeQuantity_IsRejected`, `CreateQuote_NegativeUnitPrice_IsRejected`, `CreateQuote_DuplicateItem_IsRejected`, `ConvertQuote_DuplicateItem_IsRejected`.

### R11-6 [MEDIUM] Update-order paths didn't validate supplier/customer existence
Draft update with a bogus `SupplierId`/`CustomerId` threw an unhandled FK `DbUpdateException` (HTTP 500).

**Fix:** existence checks added to `ProcurementService.UpdateOrderAsync` (supplier) and `SalesOrdersService.UpdateOrderAsync` (customer), before any mutation.

**Tests:** `UpdateOrder_MissingSupplier_IsRejected`, `UpdateOrder_MissingCustomer_IsRejected`, plus sales `UpdateOrder_DuplicateItem_IsRejected`, `Cancel_AfterInvoiced_IsRejected`.

---

## Deferred to a later round (from the financial-report agent; needs design decision)
- [HIGH] Party statements overstate closing for on-receipt (auto-paid) invoices — `ReportService.cs:1039-1041, 1058-1060`; shared with web ledger `CustomersController.cs:163`, `SuppliersController.cs:148` (PaidAmount never subtracted). Aging excludes them correctly, so statement vs aging disagree.
- [HIGH] PDF party statements ignore FX and disagree with the XLSX statement (`CustomersController.cs:189-196`, `SuppliersController.cs:174-181`).
- [MEDIUM] Cash-flow FX fallback `BaseAmount > 0 ? BaseAmount : Amount` can mix currencies (`ReportService.cs:908, 920, 934-935, 1011, 1041, 1060`).
- [MEDIUM] On-receipt purchases in cash flow can be double-counted if an unlinked manual payment is also recorded (`ReportService.cs:911-914, 924-927, 939-956`); cash-flow detail table doesn't include on-receipt rows (`916-917`).
- [MEDIUM] Income statement silently empty when both `from`/`to` are in the future (`FinancialReportService.cs:73-74`).
- [LOW] Ledger/PDF statements grouped by type, not date-sorted (`CustomersController.cs:188-191`, `SuppliersController.cs:173-176`); aging drops negative/credit remainders (`ReportService.cs:711-712`); stock-value export writes raw `double` (`284-285`); CSV truncates silently at 50k rows (`ReportExportService.cs:11, 23, 55, 88`).

## Verified (from the financial-report agent; no defect)
- Trial balance recomputes live from posted entries — no cached/out-of-sync opening balances (`FinancialReportService.cs:17-67`). Debit/Credit equal by construction (`AccountingService.cs:179-182`).
- Income statement contra-revenue/contra-expense classification is arithmetic-correct; YEC entries excluded (`FinancialReportService.cs:274`).
- Balance sheet: closed retained earnings (`3001`, `Source == YearEndClose`) counted once; NetIncome = P&L − closed retained; loss years work (`FinancialReportService.cs:180-211`).
- Cash-flow ordering safe (`OrderBy(PaymentDate).ThenBy(Id)`, `ReportService.cs:889-892`).

## Follow-up
- Re-run the print/settings audit (session context was lost) — HTML-injection in PDF branding fields, print-settings cache invalidation, layout validation.

## Artifacts
- `Services/ProcurementService.cs`, `Services/SalesOrdersService.cs`, `Services/SalesQuotesService.cs`, `Controllers/PurchaseRequestsController.cs`, `Services/PermissionCatalog.cs`, `Services/PermissionDefaults.cs`, `Views/PurchaseRequests/Index.cshtml`.
- Tests: `ProcurementServiceTests.cs`, `SalesQuoteTests.cs`, new `SalesOrderLifecycleTests.cs` (15 new tests total, 1 fixture updated). No migrations.