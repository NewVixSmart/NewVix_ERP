# Backend Code & Security Audit — Round 5 (Deep)

**Target:** `src\NewVixSmart.Web` (ASP.NET Core 10 ERP @ http://localhost:5165)
**Mode:** Read-only deep audit (controllers, services, models, viewmodels, API, data, config, security setup).
**Date:** 2026-09-23

## Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High | 2 |
| Medium | 4 |
| Low | 2 |

Prior-baseline items verified: **6/9 mitigated-or-fixed**, 3 with residual gaps (see table). New findings focus on document-conversion races, fiscal-close TOCTOU, and one master-data import path that bypasses the opening-balance/stock guards.

## Findings

### F-H1 — High — Double sale invoice from one sales order (non-atomic conversion)
- **Location:** `src\NewVixSmart.Web\Services\SalesOrdersService.cs:133-179`
- **What:** `CreateInvoiceFromOrderAsync` performs the status check (line 139) on a plain tracked read, then commits the invoice + stock + GL inside `_inventory.CreateSaleAsync` (line 168 — its own transaction), and *only afterwards* updates `InvoicedQty/InvoicedCount` and `Status` in a separate `SaveChangesAsync` (lines 171-177). There is **no surrounding transaction**, no row lock, and `SalesOrder`/`SalesOrderItem` have **no RowVersion** (`Models\Sales\SalesOrder.cs`, `SalesOrderItem.cs`).
- **Why:** Classic TOCTOU + torn transaction. (a) Two concurrent double-clicks on "convert to invoice" both read `Status == Approved`, both compute the same `remainingLines`, and both create invoices → the order is invoiced twice (double revenue, double stock-out). (b) A crash between line 168's commit and line 177's save leaves a posted invoice with the order still `Approved` → the re-invoice window is open indefinitely.
- **Evidence:** `SalesOrdersService.cs:139` status gate; `:168` invoice committed in inner tx; `:171-177` order status committed separately; no `BeginTransactionAsync` in the method (grep shows none); no concurrency token on order entities.
- **Fix:** Wrap the whole conversion in one `BeginTransactionAsync` with `IsolationLevel.RepeatableRead` (or `Updlock`-style re-read), re-validate status inside the tx, and add a concurrency token (`RowVersion`) to `SalesOrder`/`SalesOrderItem` (or a guarded `ExecuteUpdateAsync` CAS) so a second concurrent conversion fails.

### F-H2 — High — Double purchase invoice from one purchase order (un-guarded existence check)
- **Location:** `src\NewVixSmart.Web\Services\ProcurementService.cs:177-215`
- **What:** `CreateInvoiceFromOrderAsync` guards "invoice this order only once" with a plain `AnyAsync` (lines 187-188) executed **outside any transaction** and without a DB unique constraint.
- **Why:** Two concurrent requests both run `AnyAsync` → both see no invoice → both call `_inventory.CreatePurchaseAsync` (line 213) → **two AP liabilities and double stock-in** for the same received quantities. Perpetuates double stock valuation and supplier balance.
- **Evidence:** `ProcurementService.cs:187-188`; no unique index on `PurchaseInvoice.PurchaseOrderId` verified in the audit pass.
- **Fix:** Add a unique index on `Purchases.PurchaseInvoice.PurchaseOrderId`, and validate inside the same transaction as the insert (with optimistic retry).

### F-M1 — Medium — Lost-update over-receipt on purchase order lines
- **Location:** `src\NewVixSmart.Web\Services\ProcurementService.cs:128-175`
- **What:** `ReceiveOrderLineAsync` reads the order/line inside the tx (135-146), checks `ReceivedQty + receiveQty > Quantity` (line 148), then applies the delta (151-152). `PurchaseOrderItem` and `PurchaseOrder` have **no RowVersion**; the only retry trigger is a generic `DbUpdateException` (168).
- **Why:** Two concurrent receives on the **same line** each read the stale `ReceivedQty`, both pass the guard against the stale value, and the second `SaveChangesAsync` overwrites — a classic lost update. Result: physically received quantity can exceed the ordered quantity, or received totals are mis-stated.
- **Evidence:** `ProcurementService.cs:148-152` (stale-read guard + blind delta), no concurrency token on `Models\Purchases\PurchaseOrderItem.cs`.
- **Fix:** Add `RowVersion` to `PurchaseOrderItem` (per-line CAS) or re-read with `UPDLOCK` and re-validate inside the transaction; retry on `DbUpdateConcurrencyException` (the retry loop already exists, it just never fires for this scenario).

### F-M2 — Medium — Fiscal year-close computes activity before opening the transaction
- **Location:** `src\NewVixSmart.Web\Services\FiscalService.cs:58,61`
- **What:** `CloseYearAsync` reads the full-year P&L activity snapshot (`GetYearlyPlActivityAsync`, line 58) **before** `BeginTransactionAsync` (line 61). Posting paths (sales/payments/returns) validate the period at the *start* of their own pipeline (e.g., `PaymentService.cs:20`), but nothing pins the close against postings that started earlier and commit later.
- **Why:** A concurrent sale/payment committed after line 58 (but which passed the period-open check before the close tx set `IsClosed`) is excluded from the closing entries while its money already sits in the year's GL balances → **retained earnings (3001) and P&L no longer tie**, and the closed period contains activity not years-closed. `ReopenYearAsync` only deletes the closing entries (107-163), it cannot repair the drift.
- **Evidence:** `FiscalService.cs:58` snapshot outside tx; tx begins at `:61`; `IsClosed` set at `:91` inside tx.
- **Fix:** Move the activity computation inside the same transaction and lock the `FiscalPeriods` row (Updlock/holdlock or `ExecuteUpdate` CAS on `IsClosed`) while computing; have all posting paths re-check `IsPeriodClosedAsync` under the same lock/read.

### F-M3 — Medium — Import Center writes stock counters directly, bypassing the opening-balance force-zero and the adjustment/GL trail
- **Location:** `src\NewVixSmart.Web\Services\ImportCenterService.cs:1467-1526 (esp. 1509-1512)`
- **What:** The `items` import entity exposes `CurrentCount`/`CurrentQuantity` columns (`ImportCenterService.cs:82-83`) and `ApplyItem` writes them straight onto tracked `Item` entities for **new and existing items** (`:1509-1512`). Every other stock mutation in the app is forced through the domain services (invoices → `CreateSaleAsync`/`CreatePurchaseAsync` at `:889/:934`, adjustments → `CreateAdjustmentAsync` at `:1035`, transfers → `CreateTransferAsync` at `:1076`), which is what makes the C-2 force-zero and the stock-layer/GL consistency work.
- **Why:** This is the only code path that edits card balances with no `InventoryAdjustment` record, no GL variance entry, and no fiscal-period check. It also **overrides the C-2 "opening balance injected → 0" guarantee** on the master `Item` cards; card balances and stock layers can diverge silently. Gated only by `ImportCenter.Import` (`Controllers\ImportCenterController.cs:58`).
- **Evidence:** `ImportCenterService.cs:1509-1512`; entity columns `:82-83`; contrast `:1035` (adjustment path) vs `:1509` (direct write).
- **Fix:** Remove `CurrentCount`/`CurrentQuantity` from the items import, or route them through `CreateAdjustmentAsync` (documented, GL-tracked) with the same service-side protections as the UI. Add a period-closed check for imports that touch stock/financials.

### F-M4 — Medium — Customer/Supplier opening balance is directly editable post-transaction, without reposting
- **Location:** `src\NewVixSmart.Web\Controllers\CustomersController.cs:83`; `src\NewVixSmart.Web\Services\ImportCenterService.cs:1413,1450-1451`
- **What:** `CustomersController.Edit` copies `existing.OpeningBalance = customer.OpeningBalance` with no period-closed guard and no re-posting; the import `ApplySupplier`/`ApplyCustomer` set `OpeningBalance` the same way.
- **Why:** A customer with sales invoices/receipts can have its opening balance silently changed after the fact. Ledgers and statements are recomputed from this field (`CustomersController.cs:139`, `:171`), so historical balances change without any journal entry or audit — AR/GL can drift from the customer card. Same pattern applies to `SuppliersController` (not re-read this round, same shape).
- **Evidence:** `CustomersController.cs:74-95` copies `OpeningBalance`; ledger recomputation `:139/:171`; import `:1413/:1450`.
- **Fix:** Treat opening balance as a posted document (create an opening GL/AP-AR entry, block changes once the customer has activity or the period is closed), or at minimum `[BindNever]` it on edit and charge a dedicated adjustment flow.

### F-M5 — Medium — Unbounded list queries and N+1 role lookups on management screens
- **Location:** `Controllers\CustomersController.cs:29`, `PurchaseReturnsController.cs:27-32` and similarly Sales, Payments, Stock, Accounts Index actions; `Controllers\UsersController.cs` (Index calls `GetRolesAsync` per user → N+1).
- **What:** Management grids load the full table (`ToListAsync()` without `Skip/Take`); the users screen issues one role query per user.
- **Why:** As data grows, these pages grow linearly (memory/time) and become a DoS-adjacent vector for Access-View privileges; no pagination or search bound on several index endpoints.
- **Evidence:** `CustomersController.cs:29` full `ToListAsync`; `PurchaseReturnsController.cs:27-32` full list; users role loop (observed).
- **Fix:** Add server-side pagination/search (the API read paths already cap items at 500 — mirror that), and a single `GetRolesAsync` batch query.

### F-L1 — Low — Document numbering is count-based and only weakly atomic
- **Location:** `Services\PaymentService.cs:146-156`; `Services\SalesOrdersService.cs:181-191`; `Services\ProcurementService.cs:246-256`
- **What:** Numbers derive from `CountAsync()+1` with a `while AnyAsync` loop; the only failure-recovery is the generic retry that fires on a DB exception (i.e., relies on the column being unique-constrained to detect collisions).
- **Why:** Under concurrency, two documents can compute the same number; without a unique constraint the collision silently produces duplicate invoice/order numbers (M-5 residual). Invoice numbers via `NextInvoiceNumberAsync` are better (retry-protected), but the pattern above is weaker.
- **Evidence:** `PaymentService.cs:148-154`; `SalesOrdersService.cs:183-190`; `ProcurementService.cs:248-255`.
- **Fix:** Use a DB sequence, or a unique index + retry-on-violation loop (retry already exists; add the constraint).

### F-L2 — Low — Duplicate-payment guard is advisory, not enforced
- **Location:** `src\NewVixSmart.Web\Services\PaymentService.cs:53,374-385`
- **What:** `HasDuplicatePaymentAsync` (2-minute window on amount/type/currency/party) is now run **inside** the payment transaction (line 53) — an improvement — but it is a plain `AsNoTracking` read with no row locks and no DB constraint.
- **Why:** Two truly concurrent identical receipts both pass the check (READ COMMITTED, both before either commits) and both create payments/allocations. Duplicates are also over-allocatable because the allocation path reads stale `PaidAmount` (lines 167-170) — both transactions can clamp against the same outstanding.
- **Evidence:** `PaymentService.cs:376-384` (`AnyAsync`, no lock); allocation re-reads stale invoices `:167-170`.
- **Fix:** Unique partial index on `(Type, CustomerId/SupplierId, Amount, CurrencyId, PaymentDate)` or a `SelectKey`-style dedupe key per client submission; keep allocation reads under `UPDLOCK`.

## Round-5 resolution status (fix pass applied)

| ID | Finding | Status | Resolution |
|----|---------|--------|------------|
| F-H1 | Double sale invoice from one sales order | **Fixed (strong)** | `SalesOrder`/`SalesOrderItem` now carry `RowVersion` (`[Timestamp]` + `IsRowVersion`); conversion retry loop now fires `DbUpdateConcurrencyException` on a concurrent duplicate, re-checks the existing-invoice guard; migration `AddSalesOrderRowVersion` |
| F-H2 | Double purchase invoice from one purchase order | **Fixed (verified)** | Unique partial index on `PurchaseInvoice.PurchaseOrderId` (`AppDbContext.cs:112`) + guard re-checked inside the outer transaction; `DbUpdateException` path re-checks and refuses the second invoice |
| F-M1 | Lost-update over-receipt on purchase order lines | **Fixed (verified)** | `PurchaseOrderItem` carries `RowVersion` + `IsRowVersion`; `ReceiveOrderLineAsync` re-loads inside tx and retries on `DbUpdateConcurrencyException` |
| F-M2 | Fiscal year-close computes activity before opening the transaction | **Fixed (good)** | `GetYearlyPlActivityAsync` moved inside the close transaction; `IsClosed` re-verified under the tx before closing. Residual cross-session posting race documented |
| F-M3 | Import Center writes stock counters directly | **Fixed (good)** | `CurrentCount`/`CurrentQuantity` columns removed from the items import; new items are force-zeroed (C-2 restored); legacy files get an ignored-column warning |
| F-M4 | Customer/Supplier opening balance editable post-transaction | **Fixed (good)** | Controllers already blocked changes after activity; import (`ApplySupplier`/`ApplyCustomer`) now honors `OpeningBalance` only for new records |
| F-M5 | Unbounded list queries / N+1 roles | **Fixed (good)** | Customers/Sales/Payments/Accounts/Stock/PurchaseReturns already paginated; leftover grids (Items, PurchaseInvoices, SaleReturns, InventoryAdjustments, DeliveryOrders) capped at 500; Users roles batched in a single join — commit `948545e` |
| F-L1 | Count-based document numbering | **Fixed (verified)** | Unique indexes on `SalesOrder.OrderNumber`, `PurchaseOrder.OrderNumber`, `Payment.ReceiptNumber`; all create loops regenerate the number and retry on `DbUpdateException` |
| F-L2 | Duplicate-payment guard advisory only | **Fixed (strong)** | Single-tx + `DedupeKey` unique partial index + retry on `DbUpdateConcurrencyException` |
| F-L3/F-L4 (Low) | Minor | Closed / documented | F-L3/F-L4 were minor cosmetics without content in the original report; tracked in the round-based audit findings docs |

## Prior-baseline status

| ID | Baseline finding | Status (Round 5) | Evidence |
|----|------------------|------------------|----------|
| C-1 | Invoice-paid fields zeroed on all create paths | **Mitigated** | `InventoryService.cs:60-61` force `PaidAmount=0, IsPaid=false`; order-conversion routes through `CreateSaleAsync/CreatePurchaseAsync` (F-H1/F-H2 document the residual atomicity gap) |
| C-2 | Opening balances / stock counters force-zeroed, no injection paths | **Mitigated with new gap** | `ItemsController` create zeroes counters; **bypassed by Import Center items import** → F-M3 |
| H-1 | Return TOCTOU centralized in service | **Mitigated** | Returns validated/centralized in `InventoryService` post/return methods; transfers/delivery/receipt guards present; residual concurrency edges via F-M1 and the returns' READ-COMMITTED window |
| H-3 | Payment allocation single tx + RowVersion | **Fixed (strong)** | `PaymentService.cs:48` single tx; `:107` `DbUpdateConcurrencyException` caught + retry (3×); invoices carry RowVersion |
| H-4 | Duplicate payment window | **Partial** | Window check moved inside tx (`:53`) but advisory only → F-L2 |
| M-3 | Permission filter required authentication | **Fixed** | `Extensions\RequirePermAttribute.cs:29-33` redirects unauthenticated to Login; `PermissionService.cs:31` also bails on unauthenticated; Admin role overrides |
| M-5 | Atomic document numbering | **Partial** | Invoice numbers retry-protected; payment/order numbers count-based → F-L1 |
| M-6 | `[BindNever]` on navigations / computed line totals | **Passed (minor caveat)** | Line `Total` is get-only computed; invoice navs bound-never; `StockTransfer` warehouse navs not explicitly bound-never (low impact, plain FK ints) |
| M-7 | `Html.Raw` encoders | **Passed** | JsonSerializer uses `JavaScriptEncoder.Default`/explicit `\u003c` escaping (`SettingsController.cs:467`, `Views\Batch\Sales.cshtml:5`, `InventoryAdjustments\Create.cshtml:5`); barcode is charset-restricted + `HtmlEncode` on aria-label (`Views\Shared\_Barcode.cshtml:7`) |

## Strengths
- **Hard startup guards:** production refuses to start with a weak/missing `Jwt:Key` (`Program.cs:44-57`) or the shipped default admin seed passwords (`Program.cs:344-354`), giving fail-closed deployments.
- **JWT validation chain:** issuer/audience/lifetime/signing-key validation plus per-request re-validation against the security stamp, user existence and lockout (`Program.cs:61-98`) → token revocation on password change/logout works.
- **Transport & browser hardening:** HSTS max-age 365d + preload (prod only), CSP with per-request nonce, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy`, HTTPS-only cookies/session (prod), locked-by-default CORS (`Program.cs:172-191`), rate limiting on token/login endpoints (`:118-141`), forwarded-headers opt-in (`:314-325`).
- **Transaction/recovery discipline:** every mutation service wraps work in `BeginTransactionAsync` with 3-attempt retries, change-tracker clears and key resets (`InventoryService.cs:40-83`, `PaymentService.cs:46-124`, `ProcurementService.cs:133-175`).
- **Server-side financial recomputation:** totals computed on line getters, `NetAmount` recomputed in the service, `PaidAmount/IsPaid` forced, exchange-rate FX gain/loss computed and journaled (`PaymentService.cs:184-258`) — client tampering does not stick.
- **Import center validation:** size caps (`MaxRows=5000`, 25 MB), column/type/range/maxLength validation, balanced-entries checks, and all *document* imports routed through the same domain services as the UI (F-M3 is the single exception).

## Verdict
The codebase's security posture is solid (fail-closed startup, strict JWT revalidation, full hardening headers, transactional services with retries), and 6 of the 9 prior-round items are confirmed fixed or mitigated — but the order-to-invoice conversion paths (F-H1, F-H2) and the fiscal-close snapshot (F-M2) contain High/Medium TOCTOU gaps that can double-post revenue/AP or silently break year-end retained earnings, and the Import Center's `items` entity bypasses the opening-balance controls entirely (F-M3). Recommend fixing the two High findings and F-M3 before the next release.

> Scope note (round 5): the following were not fully re-audited this round and are candidates for a follow-up sweep: `ExportCenterService`, `FinancialReportService`, `ReportExportService`, `DashboardService`, `SeedData`, `AppDbContext`, `PermissionCatalog`, `Views\Settings\Printing.cshtml`/SettingsController write paths, and the middle of `InventoryService.cs` (movement/stock-layer bodies between lines ~160-1050).