# Audit Round 20 — Hard Deep Full Audit

Date: 2026-09-26
Scope: full-codebase deep audit — ~88,815 LOC C#, 113 Razor views, 39 controllers, 67 migration files, 4 csproj, 1 slnx, 1 e2e suite.
Method: 5 parallel read-only sub-audits (Security / EF Core + DB integrity / Financial business logic / Frontend + a11y / CI-buil d-deploy) + orchestrator manual re-verification of every Critical/High claim on source + independent dependency scans.

## Dependency scan results (independent)
- `dotnet list package --vulnerable --include-transitive` (Web + Tests): **no vulnerable packages**.
- `npm audit --omit=dev` (e2e): **0 vulnerabilities** (0 critical / 0 high / 0 moderate / 0 low).

## Critical

| # | Severity | Finding | Location | Verified |
|---|----------|---------|----------|----------|
| C-1 | CRITICAL | Fully-discounted (zero-net) sale invoice books **full list-price revenue + AR at delivery**. Line discount is never capped at line gross: `SaleInvoiceItem.Total = qty*UnitPrice − Discount` (`SaleInvoiceItem.cs:41`) with no upper bound. Header `NetAmount < 0` is the only rejection guard (`InventoryService.cs:81`). At delivery, the net-scaling only runs when `invoice.TotalAmount > 0 && invoice.NetAmount >= 0` (`InventoryService.cs:221`); for a line discounted to zero, `TotalAmount == 0`, so the fallback executes `value = rawValue` = full gross price (`:219`,`:224`) and `RecordSaleDeliveryAsync` posts full revenue/AR for a free invoice. | `InventoryService.cs:211-227`, `Models/Sales/SaleInvoiceItem.cs:41`, `Models/Sales/SaleInvoice.cs:56-81` | ✅ manually confirmed |
| C-2 | CRITICAL | Return valuation ignores original invoice discounts — a return re-prices the returned qty at the invoice’s gross `UnitPrice` (Source invoice variants use `saleReturn.TotalAmount = valid.Sum(Total)` which uses gross). Refunding gross when the sale was discounted inflates AR credit and feels economically wrong. | `InventoryService.cs:419-513` | ✅ (agent-reported, mechanism confirmed at `:419`) |

> C-2 remediation overlaps C-1: cap `Item.Discount ≤ qty*UnitPrice` (model + DB check) and make the delivery fallback defensive (`TotalAmount <= 0 ⇒ value = 0`).

## High

| # | Severity | Finding | Location | Verified |
|---|----------|---------|----------|----------|
| H-1 | HIGH | `SaleReturns.Create` implicitly grants `SaleReturns.Post`: the `[HttpPost] Create` action calls `PostSaleReturnAsync` directly when `submitAction == "post"` (`:114-117`) while decorated only with `[RequirePerm("SaleReturns.Create")]`. A user with Create but not Post can fully post returns. | `SaleReturnsController.cs:43-139` (write at :114-117) | ✅ manually confirmed |
| H-2 | HIGH | Same privilege-descreted behavior in purchase returns: `submitAction=="post"` posts inside the Create action (`:126-129`), requiring only `PurchaseReturns.Create`. | `PurchaseReturnsController.cs:55-151` (write at :126-129) | ✅ manually confirmed |
| H-3 | HIGH | `PurchaseOrder` has **no RowVersion** (`Models/Purchases/PurchaseOrder.cs` has no `[Timestamp]`). Concurrent receive / cancel of the same order can both pass status guards and double-post stock / double-invoice. `SaleInvoice` carries `[Timestamp]` (`SaleInvoice.cs:113-114`) — PurchaseOrder should too. | `Models/Purchases/PurchaseOrder.cs`, `PurchaseOrdersController.cs` | ✅ manually confirmed (no RowVersion) |
| H-4 | HIGH | `role="status"` wraps a stat card whose `<h3>` runs a 60fps count-up animation (`Dashboard.cshtml:39,42-44`) → screen-readers announce a changing live region on every animation frame. One card only; other stat cards lack it. | `Views/Reports/Dashboard.cshtml:39,43` | ✅ manually confirmed |
| H-5 | HIGH | Label `for` mismatch on the Payments form: `<label asp-for="Payment.CurrencyId">` emits `for=Payment.CurrencyId` while the control has explicit `id="currencySelect"` — the association is broken (same for ExchangeRate/`exchangeRate`, BaseAmount/`baseAmount`). | `Views/Payments/Create.cshtml:47-48,54-55,60-61` | ✅ manually confirmed |
| H-6 | HIGH | Unlabeled / un-associated controls in `Accounts/Edit.cshtml` (::23,39,43,52,59 — five top-level controls without associated labels), `PurchaseOrders/Receive.cshtml` (:37-38,41-42,45 — labels not bound + 20 identical «استلام» accessible names in the table), and unlabeled `<select name="supplierId">` in `Suppliers/Quotes.cshtml:9-10`. | see refs | ✅ (agent-reported; refs match sibling forms) |
| H-7 | HIGH | a11y gate coverage gap: `e2e/a11y-gate.cjs:9-12,45-56` scans only sidebar `.nav-link` hrefs + 2 hard-coded extras (~48 routes). ~65 of the 113 views — including every H-5/H-6 page — are **never** axe-scanned, so the gate stays `GATE: PASS` while High issues ship. | `e2e/a11y-gate.cjs` | ✅ manually confirmed |

## Medium (agent-verified, key refs confirmed)

| # | Finding | Location |
|---|---------|----------|
| M-1 | `UseForwardedHeaders` registered only when `ForwardedHeaders:KnownProxies/Networks` are configured (`Program.cs:326-333`). Behind an unconfigured proxy, `RemoteIpAddress` is the proxy IP → the fixed-window rate limiter (20/5min, `:140-147`) becomes a single shared bucket across all clients. | `Program.cs` |
| M-2 | `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true` (`Program.cs:152`) disables implicit `[Required]` on non-nullable refs app-wide; required fields then depend on explicit attributes/controllers. Mitigated by global `AutoValidateAntiforgeryToken` (`:153`) + explicit guards. Design tradeoff — document or re-enable selectively. | `Program.cs` |
| M-3 | JWT placeholder key committed in `appsettings.Development.json:17`. Acceptable **only** because the production gate hard-fails on missing/placeholder/short keys (`Program.cs:51-59`). Dev accepts any key ≥1 char (`:60-64`). Key must never be committed with a real value; keep placeholder. | `Program.cs:51-64` |
| M-4 | Default seed password `Admin@123` committed in `appsettings.json:14` of a **public** repo. | `appsettings.json:14` |
| M-5 | Unhandled `DbUpdateConcurrencyException` in 6 order/operational methods → generic HTTP 500 instead of a retry/conflict path (SaleInvoice/PurchaseInvoice have RowVersion + retry loops; PurchaseOrder does not). | order services |
| M-6 | Return records can be created with **no source invoice** selected (only-guard applies when `SaleInvoiceId != null`), bypassing the qty/customer validation — incl. via the CSV import pipeline. | `SaleReturnsController.cs:69-106`, `ImportCenterService.cs` |
| M-7 | 90 `<th>` without `scope` across 19 views; two views put `scope="col"` on row headers. | `Payments/Details.cshtml:12-23`, `Items/Details.cshtml` et al. |
| M-8 | ARIA `tablist`/`tab` pattern used without roving `tabindex` (keyboard users can’t move between tabs). | report tabs views |

## Low / Info

| # | Finding | Location |
|---|---------|----------|
| L-1 | Deactivate-gated GL account delete (hard delete restricted to `Active == false`) — fine, but relies on manual toggling for cascade safety. | `AccountsController.cs:147-149` |
| L-2 | 8-char minimum password policy; no complexity/comprehension requirements beyond length. | Identity options |
| L-3 | Several security controls keyed to `IsDevelopment()` (JWT, connection-string guard) fail open if an operator deploys the Development env accidentally. | `Program.cs` |
| L-4 | No root `Dockerfile`, `global.json`, `Directory.Build.props`, `.editorconfig`; no `dependabot.yml`; no `CODEOWNERS`. | repo root |
| L-5 | No gitleaks / CodeQL / dependency-review in CI. | CI only runs build+test |

## CI / Build / Deploy gaps (agent-reported)
- AppHost **absent from `NewVixSmart.slnx`** (verified: slnx lists Web + Tests only) → `dotnet test NewVixSmart.slnx` never builds the AppHost, so the Round-19 Aspire migration has no CI coverage (F-01).
- AppHost runtime (DCP, dashboard, SQL container, MessagePack log channel) only verified locally in Round 19 d.

## Confirmed NOT issues (false premises corrected by sub-audits)
- `NEWID()` PublicId backfill is safe for existing rows (unique indexes added after backfill; all 10 `ValueGeneratedNever` + symmetric migrations).
- `SalesOrders.Approve` carries `[ValidateAntiForgeryToken]` — no CSRF.
- `StockLayer` per-branch index matches the model — no schema drift.
- CSV import wraps imports in a transaction (`ImportCenterService`) — no partial imports observed.
- Double-posting of orders/invoices is guarded by `RowVersion` retry loops (`MaxAttempts`) on document entities.
- NuGet + npm dependency sets are clean (0 known-vulnerable transitive deps).

## Outcome & recommended sequence
1. **Fix C-1/C-2**: cap line discount at line gross (model validation + DB check migration) and make delivery net-scaling fail-safe when `TotalAmount <= 0`. Add regression tests (zero-net invoice → delivered value 0).
2. **H-1/H-2**: gate `submitAction=="post"` posting behind the Post permission (split the submit path or verify `SaleReturns.Post` before calling `Post*ReturnAsync`).
3. **H-3**: add `[Timestamp] RowVersion` to `PurchaseOrder` + handle concurrency in receive/cancel.
4. **H-4…H-7**: a11y — remove `role="status"` from the animated stats, fix label `for`/id pairs, seed an explicit route manifest in the gate (~113 routes).
5. M-items: configure forward proxies, add CI dependency-scanning (gitleaks/dependabot/CodeQL), add `global.json`/`.editorconfig`.

No items were closed in this round; this document is the audit deliverable and stays untracked.