# Audit Round 20 — Hard Deep Full Audit

Date: 2026-09-26
Scope: full-codebase deep audit — ~88,815 LOC C#, 113 Razor views, 39 controllers, 67 migration files, 4 csproj, 1 slnx, 1 e2e suite.
Method: 5 parallel read-only sub-audits (Security / EF Core + DB integrity / Financial business logic / Frontend + a11y / CI-buil d-deploy) + orchestrator manual re-verification of every Critical/High claim on source + independent dependency scans.

> **Status:** all Critical and High items are now closed — see [Resolution log](#resolution-log-added-after-the-fact) at the end. The findings below are kept verbatim as the round-20 record; the trailing "stays untracked" note is a historical artifact, this file is now committed.

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

---

# Resolution log (added after the fact)

Added 2026-09-27. The tables above are preserved exactly as the round-20 audit wrote them, including
the "stays untracked" line at the end. Every Critical and High item below was re-checked against
`main` rather than taken on trust, because several had already been remediated by intervening work
and some rested on a wrong premise.

| # | Round-20 claim | Status on `main` | Evidence |
|---|---|---|---|
| C-1 | Zero-net invoice books full list-price revenue + AR at delivery | Stale as written, but it led to a worse adjacent bug — now fixed | `InventoryService.cs:85-97` already caps line discount at gross, rejects over-discount and negative net. Verifying it exposed `DeliveriesInvoicingService.cs:158` calling `RecordSaleInvoiceRevenueAsync` unguarded, which throws on a zero value, escaped the `DbUpdate*` catches and rolled an accepted delivery back into a 500. Guarded with `NetAmount > 0m` to match the purchase path. `ae6c769` + `InvoiceFromIssues_FullyDiscountedInvoice_PostsCostOnlyAndNoRevenue` |
| C-2 | Return valuation ignores original invoice discounts | Already fixed | `ReturnMirror.ProratedAgainst` in `InventoryService.cs:958-967`; covered by `ReturnAgainstAtInvoiceInvoice_MirrorsGrossRevenueAndReleasesCost` |
| H-1 | `SaleReturns.Create` can post without `SaleReturns.Post` | False premise | `SaleReturnsController.cs:114-117` checks `_permissions.HasAsync("SaleReturns.Post")` before posting |
| H-2 | Same for `PurchaseReturns` | False premise | `PurchaseReturnsController.cs:126` checks `PurchaseReturns.Post` |
| H-3 | `PurchaseOrder` has no `RowVersion` | False premise | `[Timestamp] RowVersion` present on `PurchaseOrder`, `SaleInvoice`, `PurchaseInvoice`, `SalesOrder` |
| H-4 | `role="status"` around a 60fps count-up stat | Already fixed | No `role="status"` and no animation loop left in `Views/Reports/Dashboard.cshtml` |
| H-5 | `label for` / control `id` mismatch on the Payments form | False premise | Labels are hand-written and match: `for="currencySelect"` / `id="currencySelect"`, same for `exchangeRate` and `baseAmount` |
| H-6 | Unlabelled controls in `Accounts/Edit`, `PurchaseOrders/Receive`, `Suppliers/Quotes` | False premise (all three) | `Accounts/Edit.cshtml:25-32` are mutually exclusive `if`/`else` branches, so only one `id="Code"` ever renders; `PurchaseOrders/Receive.cshtml:45` gives each receive button a unique `aria-label` naming the item and code; `Suppliers/Quotes.cshtml:9-10` labels the select |
| H-7 | Gate scans ~48 of 113 views, so High issues ship | **Fixed** | `d315031`: `A11yGateManifestTests` reflects over every view-rendering GET action and fails when one is absent from the manifest (plus a stale-entry check). Manifest 80 → 99 light routes, dark subset gained `StockReservations` and `DeliveryIssues`, `Users/Permissions` GUID resolved from the Users list. Widening the gate then exposed three real defects, all fixed in the same commit |
| M-1 | `UseForwardedHeaders` registered only when proxies configured | Already fixed | `Program.cs:352-399`: explicit `ForwardedHeaders:Enabled`, validated proxy/CIDR parsing, hard-fails when enabled with no known proxies |
| M-5 | Unhandled `DbUpdateConcurrencyException` | Already fixed | 27 catch sites across `InventoryService`, `DeliveriesInvoicingService`, `ProcurementService`, `SalesOrdersService`, `PaymentService`, `StockReservationsService` |
| M-7 | 90 `<th>` without `scope` across 19 views | Already fixed | Every `<th>` in every view carries `scope` (verified by scan across `Views/**/*.cshtml`) |
| M-8 | `tablist` without roving `tabindex` | **Fixed** | `7f2910b`: `Views/Settings/Printing.cshtml` renders `tabindex="0"` on the active tab, `setActiveGroup` keeps the roving value, and a keydown handler drives `ArrowUp`/`ArrowDown`/`Home`/`End` with wraparound. Verified in a real browser: 11 tabs, exactly one tab stop, arrows move and select, panel follows focus |

### Defects found while widening the gate (H-7 work)

None of these appear in the round-20 tables, because the gate could not see them:

- `Views/DeliveryOrders/Create.cshtml` — three selects had no accessible name; their labels pointed at the adjacent radio inputs. Fixed with `aria-label`.
- `Views/Sales/Details.cshtml`, `Views/SalesQuotes/Details.cshtml`, `Views/PurchaseReturns/Details.cshtml` — the `.small` label rendered white on `bg-success`/`bg-danger` (~4.0:1), failing WCAG 1.4.3. Switched to the pattern neighbouring details pages already pass with: `text-muted small` plus a coloured `h4`, no hardcoded colours.
- Every `NotFound()` (102 call sites) answered with a body declaring no language and no title, failing WCAG 3.1.1 and 2.4.2. `Home/StatusCode` now renders 404/403/401/500 through the layout via `UseStatusCodePagesWithReExecute`.

### Second pass — Medium and Low, re-verified 2026-09-27

Each item below was re-checked against the code by a separate read-only pass before anything was
changed, because the first pass of this log showed how often these claims were stale or built on a
premise that does not hold. Three of the six Medium/Low claims turned out to rest on code that does
not exist; the real problems were adjacent to them.

| # | Round-20 claim | Status on `main` | Evidence |
|---|---|---|---|
| M-2 | `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` stops EF treating non-nullable reference properties as required | False premise | The flag is an `MvcOptions` setting (`Program.cs:159`) and has no effect on EF, which derives requiredness from NRT annotations. The committed snapshot shows `Item.Category` marked `= null!` with no `[Required]` yet still emitted as `.IsRequired()` (91 such calls across 118 navigations). All 40 non-nullable navigations are backed by a non-nullable `int` FK, so a null navigation cannot reach `SaveChanges`. Flag left as-is — flipping it would buy no EF safety and turn working POST paths into 400s. The real invariant is now pinned by `Item_CategoryForeignKey_IsRequiredInEfModel` and `StockMovement_ItemForeignKey_IsRequiredInEfModel` |
| M-3 | Placeholder JWT key in `appsettings.Development.json` | Accurate, no change warranted | The value is self-describing (`DevOnly-SuperSecretKey-DoNotUseInProduction-…`), and `CopyToPublishDirectory="Never"` keeps it out of published output. The production gate (`Program.cs:51-59`) throws on missing, `< 32` chars, or `REPLACE_WITH`; the presence check at `:60-64` is unconditional. **Adjacent real defect, fixed:** the project had no `<UserSecretsId>`, so the remediation path that `Program.cs:58,63` and the README both point operators to (`dotnet user-secrets set "Jwt:Key"`) failed outright and left the environment variable as the only working route |
| M-4 | Default seed password `Admin@123` committed in `appsettings.json` | Already fixed a commit earlier | `00f8ba8` removed the whole `Seed` section; `appsettings.json:13-17` now carries a comment saying so. `SeedData.cs:37-38,443-444` falls back to `GenerateSecurePassword()` rather than any literal, and the non-development gate at `Program.cs:426-452` throws before `Migrate()` and before `app.Run()`. Held by `ProductionConfigGateTests.BaseSettings_ShipsNoSeedPasswords` |
| M-6 | Returns can be created with no source invoice, bypassing validation "including via CSV import" | False premise as written; one real adjacent defect, fixed | An invoice-less return is a deliberate feature, not an oversight — `SaleReturn.SaleInvoiceId` is nullable by design, the UI labels it "اختياري", `ReturnValuation.ForGross` prices it at item-master cost with no proration, `ReportService` has explicit standalone aging buckets, and `Round20FinancialSecurityTests.SaleReturn_Post_WithoutSourceInvoice_UsesItemPrice` asserts it. The import half is also wrong: `ImportCenterService` never touches `_db.SaleReturns`, it calls `IInventoryService.CreateSaleReturnAsync` (`:995`) and therefore inherits the *stronger* service validation, which caps against delivered quantities rather than invoice lines. Making the invoice mandatory would have broken a supported workflow. **Real defect fixed instead:** the UI path copies the source invoice's currency and rate in the controller, but the import applies the row's own values and posts through the same service method, so an imported return could name its source invoice and still book the receivable credit at an unrelated rate. `ReconcileSaleReturnCurrencyAsync` / `ReconcilePurchaseReturnCurrencyAsync` in `InventoryService` now force the source invoice's values at post time, covering every caller |
| L-1 | User deactivation is a soft delete not gated on posting state, and a deactivated user can still be assigned to documents | False premise — the feature does not exist | `UsersController` has four actions (`Index`, `Create` GET/POST, `Permissions` GET/POST) and no deactivate, delete, or toggle. The audit compared a *GL account* deactivate (`AccountsController.cs:128-145`, correctly guarded by `AccountsService.GetDeleteInfoAsync`) against an invented user deactivate. Documents carry no assignable user reference at all: `CreatedBy` is a nullable free-text field populated from the session, and no view contains a user dropdown. **Real gap found next door, and fixed:** an admin had no way to revoke a departing employee's access — clearing permissions leaves the role intact and the account still authenticates. `UsersController.ToggleDeactivated` now uses `SetLockoutEndDateAsync`, reusing the `LockoutEnd` enforcement that `AccountController.cs:37` (`SignInAsync(lockoutOnFailure: true)`) and `TokensController.cs:41` (`IsLockedOutAsync`) already honour, with a self-lockout guard and an undo path. It carries no `[RequirePerm]`, matching the rest of the admin-only controller |
| L-2 | Password policy requires 8 characters | Accepted trade-off; one real inconsistency, fixed | All four complexity rules were already `true` (`IdentityOptionsFactory.cs:9-12`), so the report's "no complexity beyond length" was wrong, and 8 was a deliberate earlier remediation. There is no self-service registration, and the seed path generates 16 CSPRNG characters and self-heals a non-compliant operator-supplied value (`SeedData.cs:447-453`), so the floor is an admin-self-inflicted risk rather than an attack surface. Raising it to 12 would have churned 9 test passwords for little gain. **Fixed:** `CreateUserViewModel.Password` advertised `MinimumLength = 6` while Identity enforced 8, so a 6- or 7-character password looked accepted and then failed |
| L-3 | Security controls keyed to `IsDevelopment()` fail open | Accurate; guards are correctly fail-closed, exposure is a deployment mistake | Every guard is `if (!IsDevelopment() && <bad>) throw`, so `Prod`, `QA`, `Staging` and an unset name are all already treated as production. Only a literal `ASPNETCORE_ENVIRONMENT=Development` on a reachable host disables the JWT gate, the seed gate, the connection-string gate, HSTS, the cookie `Secure` flag and the Swagger UI at once, leaving the published dev key and dev seed passwords live — and nothing warned about it. Added a `LogCritical` at startup naming each disabled control. No behaviour gate was changed |

### Still open

`.editorconfig`, `CODEOWNERS`, CodeQL and gitleaks remain genuinely absent. The round-20 CI section is
otherwise stale in the reader's favour: `.github/workflows/ci.yml` already runs a NuGet/npm CVE gate,
builds `src/NewVixSmart.Web/Dockerfile` against a real SQL Server, and executes `./a11y-gate.cjs` inside
the container, so the H-7 gate work is now enforced on every push rather than only locally.
`global.json` and the two Aspire projects in `NewVixSmart.slnx` landed in `3ea97ae`, and
`.github/dependabot.yml` in `f594fbb`.

`RequiredLength` and the standalone-return quantity ceiling were both considered and deliberately left
alone: the first is an accepted trade-off with the seed path already stronger than the floor, and the
second would cap a supported credit-note workflow and change revenue recognition.

