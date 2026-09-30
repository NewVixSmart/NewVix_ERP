# Backend Audit — Round 6 Findings


> **Currency update (2026-09-27):** the fields and rules described below (`CurrencyId`,
> `ExchangeRate`, `BaseAmount`, `ExchangeRateAtSettlement`, `FxGain`/`FxLoss`, foreign-currency
> allocation and rate-based posting) no longer exist. Multi-currency was removed entirely —
> see [`DECISION-EGP-ONLY.md`](DECISION-EGP-ONLY.md). The findings are kept verbatim as the
> historical record of that round; they are resolved by removal, not by the fixes proposed here.

**Date:** 2026-09-23
**Scope:** Follow-ups from Backend-Audit-Report-Round5.md lines 123-124, plus the three known
suspicious areas in `InventoryService` (mixed-direction FIFO, movement dates, dual-unit valuation),
and the scoped services/controllers (Export/Report/Dashboard/Financial reports, permissions stack,
Settings, Program/SeedData).

**Method:** Static analysis + exploratory agent reads. No runtime interaction with port 5165.

**Result:** 3 High · 9 Medium · 7 Low

**Already fixed (Round-6 pass):** L-1 (CSV row caps applied to all 25 CSV exporters), M-2
(purchase movement now uses `invoice.InvoiceDate`).

**Fixed in Round-6 close-out (2026-09-23):** H-1, **H-2**, H-3, M-1, M-3, M-4, M-5, M-6, M-7,
M-8, M-9, L-2, L-3, L-4, L-5, L-6, L-7. **All Round-6 findings are CLOSED.** 271 unit tests
pass (266 baseline + 5 new regression tests), the a11y gate passes (46 light + 11 dark), and
migration `20260923125320_AddDeliveryOrderRowVersion` is applied. H-2 resolved with the
**quantity-primary, no-ratio** convention (see its section).

---

## HIGH

### H-1 — Stock transfers are unusable for any layer created by purchases or opening stock

- **File:** `src/NewVixSmart.Web/Services/InventoryService.cs:1168` (consumption), `:859-883`
  (`CreateOrTopUpLayer`), `:1278` (balance snapshot)
- **What:** Purchases and top-ups create `StockLayer` rows with `WarehouseId = null`
  (`CreateOrTopUpLayer` never stamps a warehouse). `CreateTransferAsync` consumes only
  `sl.WarehouseId == SourceWarehouseId`, while the balance pre-check at `:1278` counts
  `WarehouseId == null` layers as being *in* the source warehouse. The snapshot therefore says
  "sufficient balance", then consumption finds zero eligible layers and throws
  "الرصيد غير كافٍ".
- **Why it matters:** The first stock transfer from any warehouse against any purchased or
  opening-balance stock always fails. Transfers only work for layers already carrying a
  `WarehouseId` — which the tests create manually (`InventoryServiceTests.cs:601`), masking the
  bug in CI while production is broken.
- **Fix:** Stamp `WarehouseId` on every ingest path (add a warehouse parameter to
  `CreateOrTopUpLayer` and thread it through purchase posting, replenishment, and adjustment —
  opening stock already knows its warehouse), and make the `:1278` snapshot use the identical
  predicate as `:1168` (`sl.WarehouseId == SourceWarehouseId`, nulls excluded) so the check and
the consumption can never disagree. Add a transfer integration test that starts from a posted
   purchase invoice.
- **Status:** CLOSED (2026-09-23). Consumption now includes `sl.WarehouseId == null` layers and
  **claims** them (`layer.WarehouseId = transfer.SourceWarehouseId`) at consumption time, so
  purchased/opening stock is transferable; the snapshot's null = global-pool semantics are
  retained. Purchases carry no warehouse field, so ingest can't stamp at source — claiming at
  transfer is the safe equivalent, and `StockLayer.RowVersion` protects concurrent claims.
  Regression covered by `Transfer_ConsumesNullWarehouseLayers_And_ClaimsThem`.

### H-2 — Dual-unit valuation is internally inconsistent: revenue is quantity-dominant, COGS sums both dimensions, and `CountCost` is unscaled

- **Files:**
  - `src/NewVixSmart.Web/Models/Sales/SaleInvoiceItem.cs:41` — `Total => (Quantity > 0 ? Quantity : Count) * UnitPrice - Discount`
  - `src/NewVixSmart.Web/Services/InventoryService.cs:187`, `:532` — COGS = `qtyCost + countCost`
  - `src/NewVixSmart.Web/Services/InventoryService.cs:788-789` — `ReplenishFifoLayersAsync` passes `CountCost = baseUnitCost` unscaled
  - `src/NewVixSmart.Web/Services/InventoryService.cs:610-611` — `CreateAdjustmentAsync` passes `CountCost` unscaled
  - **Correct pattern for contrast:** `src/NewVixSmart.Web/Data/SeedData.cs:342-344` —
    `CountCost = PurchasePrice * (CurrentQuantity / CurrentCount)` (scales per-unit cost by the conversion ratio)
- **What:** Three mutually inconsistent conventions coexist:
  1. **Revenue** picks `Quantity` when `Quantity > 0`, ignoring `Count`.
  2. **COGS** adds both dimensions' cost together.
  3. **Layer ingest** (replenish, adjustment) stores the *base-unit* cost into `CountCost`
     without dividing by the unit conversion ratio, while seeded data does scale it.
- **Why it matters:** For a line with both `Quantity = 10` and `Count = 2`: revenue bills
  `10 × UnitPrice`, but COGS charges the cost of 10 units *plus* the cost of 2 units — margin is
  understated and inventory is relieved with wrong totals. Independently, any dual-unit item
  restocked via replenishment or adjustment gets a `CountCost` equal to the full box cost stored
  per each unit counted, so FIFO cost layers drift from seed data within days of go-live,
  corrupting every downstream COGS figure.
- **Fix:** Pick one dual-unit convention (the codebase's existing intent is "quantity is primary,
  count is a secondary display dimension", per `SaleInvoiceItem.Total`) and apply it everywhere:
  make FIFO consumption, `RecordSaleDeliveryAsync`, and the adjustment/replenishment cost math
  use the same rule as `Total` — and scale `CountCost` at ingest exactly the way
  `SeedData.cs:342-344` does (`baseUnitCost * (quantity / count)`), never the raw base-unit cost.
- **Decision needed:** There is no `QuantityPerUnit` model property, and regression tests pin
  `CountCost == UnitCost` for count-only purchases. Resolving H-2 therefore requires a product
  decision on the dual-unit convention (recommended: quantity-primary per `SaleInvoiceItem.Total`,
  with count conversions enforced via a unit ratio) before the mechanical fix. Blocking M-1.
- **Status:** CLOSED (2026-09-23) — **quantity-primary, no ratio** (product decision). COGS/GL
  valuation now never sums the two dimensions: every valuation point follows `Total`'s rule
  (`Quantity > 0` prices quantity only; `Count` is display-only):
  - `ConsumeFifoLayersAsync` / `ConsumeAdjustmentLayersAsync` / `GetConsumedCostAsync` return a
    per-line **`DominantTotal`** (qty-drive if `Quantity > 0`, else count-drive) instead of the
    caller adding `qtyCost + countCost`; sale-delivery COGS and purchase-return reversal cost now
    use it.
  - `RestoreSaleReturnLayersAsync` values `Quantity` when present (`quantityDriven`), else `Count`.
  - `RecordOpeningStockAsync` / `RecordStockWriteDownAsync` price `(Quantity > 0 ? Quantity : Count) × Cost`.
  - Physical layer relief stays both-dimensional (each dimension remains consistent with its own
    layer) — only the *dollarized* COGS/GL side is quantity-driving. `CountCost == UnitCost`
    on ingest is preserved (no ratio exists; count-only lines still consume at `CountCost`).
  - Regression: `SaleDelivery_DualDimensionLine_ValuesQuantityOnly_NotQuantityPlusCount` pins
    COGS = 5 × 40 = 200 (not 200 + 5 × 80) with distinct per-dimension layer costs.
  - Full suite: 271 passed.

### H-3 — Permission editor silently deletes every custom action grant on save, and mislabels those actions as "حذف"

- **Files:**
  - `src/NewVixSmart.Web/ViewModels/Users/UserManagementViewModels.cs:26-39` — VM holds only
    `View/Create/Edit/Delete` bools; `HasAction` returns `false` for every other action
  - `src/NewVixSmart.Web/Controllers/UsersController.cs:110-122` — GET maps only those four keys
    into the model; `:166-173` — POST does `RemoveRange(existing)` then `AddRange(selected)`
  - `src/NewVixSmart.Web/Views/Users/Permissions.cshtml:42-61` — renders a checkbox for
    *every* action in `PermissionCatalog.ActionsFor(module)`, checked state driven by the broken
    `HasAction`; `:45-53` — label switch maps anything that isn't View/Create/Edit/SalesCreate/
    AdjustmentCreate to `"حذف"`
  - `src/NewVixSmart.Web/Services/PermissionCatalog.cs:21-52` — custom actions that exist:
    `Approve`, `Receive`, `Post`, `Convert`, `Deliver`, `Deactivate`, `Manage`, `Dashboard`,
    `Export`, `Import`, `Close`, `Reopen`, `SalesCreate`, `AdjustmentCreate`
  - `src/NewVixSmart.Web/Services/PermissionDefaults.cs:8-51` — these custom keys are granted by
    default to Accountant and Warehouse roles
- **What:** Two defects in one flow:
  1. **Silent grant loss.** The GET action only populates View/Create/Edit/Delete, so every
     custom-action checkbox renders unchecked regardless of what the DB holds. The POST handler
     then **replaces the user's entire permission set** with whatever was submitted. The moment
     an admin opens any non-admin user's permissions page and presses "حفظ الصلاحيات", every
     custom grant (`SalesOrders.Approve`, `SaleReturns.Post`, `DeliveryOrders.Deliver`,
     `PurchaseOrders.Receive`, `Reports.Export`, `Budgets.Manage`, `FiscalClose.Close`, …) is
     wiped — including defaults the roles were seeded with.
  2. **Mislabeled UI.** All fourteen custom actions display under the column header "حذف",
     so an admin thinks they are granting/removing delete rights when they are actually
     toggling Approve/Post/Convert/Deliver.
- **Why it matters:** One routine admin action silently downgrades users — approvers can no
  longer approve, deliverers can no longer deliver, the accountant loses Reports.Export — with a
  green "تم تحديث صلاحيات" success message. Recovery requires re-checking boxes the admin
  cannot identify because they are all labeled "حذف". This is a privilege-loss bug that
  presents as a success.
- **Fix:** Store the full granted key set on `ModulePermissionViewModel`
  (e.g. `HashSet<string> Granted` populated from the same `granted` set in
  `UsersController.Permissions` GET at `:105-108`), make `HasAction` check set membership, and
  give each custom action its real Arabic label in the `switch` at
  `Permissions.cshtml:45-53` (اعتماد / استلام / تحويل / تسليم / إقفال / إعادة فتح / تصدير / …).
Add a regression test that seeding `PermissionDefaults.DefaultsFor("Accountant")`, round-tripping
   through GET+POST, preserves every key.
- **Status:** CLOSED (2026-09-23). `ModulePermissionViewModel` now carries the full granted set
  (`HashSet<string> Granted`), GET populates it from `UserPermissions`, `HasAction` checks set
  membership, and `Permissions.cshtml` renders real Arabic labels (اعتماد / استلام / تحويل /
  تسليم / إقفال / إعادة فتح / تصدير / …) with a two-column الوحدة | الإجراءات header. POST keeps
  the full-set replace semantics, so saving no longer wipes custom grants. Covered by
  `Permissions_Get_SurfacesCustomGrants_AsActions` and `Permissions_Post_ReplacesAllCustomGrants`.

---

## MEDIUM

### M-1 — General-ledger postings double-count dual-unit stock movements
- **File:** `src/NewVixSmart.Web/Services/AccountingService.cs:140` and `:148`
- **What:** `RecordOpeningStockAsync` and `RecordStockWriteDownAsync` compute
  `amount = (Quantity + Count) * Cost` — adding two different dimensions into one scalar before
  pricing. Callers: `InventoryService.cs:630` (adjustment write-down), `SeedData.cs:349`.
- **Why it matters:** Any dual-unit adjustment posts a GL amount that equals
  `(base units + boxes) × cost` — an economically meaningless number, so inventory asset and the
  offsetting adjustment expense are both wrong whenever count-based stock moves.
- **Fix:** Apply the same dimension rule chosen in H-2 (quantity-dominant): price `Quantity ×
  Cost` and treat `Count` as display-only, or convert count to base units with the item's unit
  ratio before summing. Never add `Quantity + Count` directly.
- **Status:** CLOSED (2026-09-23, with H-2). Both methods now price
  `(Quantity > 0 ? Quantity : Count) * Cost`; the adjustment write-down passes layer-consumed
  costs with `1m` (dominant-picked) or `writtenQty/writtenCount` with `PurchasePrice`.

### M-2 — Purchase-invoice stock movements are dated `DateTime.UtcNow`, not the invoice date
- **File:** `src/NewVixSmart.Web/Services/InventoryService.cs:294`
- **Status:** FIXED in Round-6 pass — `movementDate: invoice.InvoiceDate`.

### M-3 — `CreateTransferAsync` skips the fiscal period-close check that every other stock mutation performs
- **File:** `src/NewVixSmart.Web/Services/InventoryService.cs:1140-1272`
- **What:** Purchase posting, sale delivery, adjustments, and write-downs all gate on
  `IsPeriodClosedAsync` for their document date; `CreateTransferAsync` never calls it.
- **Why it matters:** After a period is closed, users cannot post a sale or adjustment dated in
  it — but they can execute a stock transfer whose movement rows (`:1224`) are dated inside the
  closed period, silently shifting quantities across a sealed fiscal boundary.
- **Fix:** Add `IsPeriodClosedAsync(transfer.Date)` (or the effective movement date) at the top
  of `CreateTransferAsync`, inside the transaction, consistent with the other flows.

### M-4 — Delivery-limit check runs outside the transaction (TOCTOU)
- **File:** `src/NewVixSmart.Web/Services/InventoryService.cs:112-126` vs transaction opened at `:128`
- **What:** The "delivery would exceed invoice quantity" validation reads the database before
  `BeginTransactionAsync` at `:128`.
- **Why it matters:** Two concurrent "Deliver" submissions both pass the pre-check, then both
  execute — over-delivering stock past the invoiced quantity and deducting inventory that was
  never sold. This is the established F-H1/F-M1/F-M2 TOCTOU pattern from Round5.
- **Fix:** Move the limit query inside the transaction and re-read the invoice items with
  `FOR UPDATE`-style tracking (or optimistic `RowVersion` retry) so check and mutation are
  atomic.

### M-5 — `CancelDeliveryOrderAsync` mutates `DeliveryOrder` with no transaction and no concurrency token
- **File:** `src/NewVixSmart.Web/Services/InventoryService.cs:226-237`; entity
  `src/NewVixSmart.Web/Models/Sales/DeliveryOrder.cs` (no `RowVersion`)
- **What:** Cancellation flips status and reverses stock/GL without an ambient transaction, and
  the entity has no optimistic-concurrency token. (`StockLayer` *does* have `RowVersion` —
  `Models/Stock/StockLayer.cs:49` — so layer writes inside this path are protected, but the
  order document itself is not.)
- **Why it matters:** Cancel racing Deliver: Cancel reads `Draft`, Deliver transitions to
  `Delivered` and posts COGS + GL, Cancel then overwrites status to `Cancelled` — leaving a
  cancelled document whose stock was deducted and whose journal entries remain posted.
- **Fix:** Wrap the cancel in a transaction, add `public byte[]? RowVersion` to `DeliveryOrder`
  (with a migration), and bail with a concurrency error if the row changed since read —
  matching the house pattern used for invoices.

### M-6 — Sales invoice line defaults `Quantity = 1` alongside an editable `Count`
- **File:** `src/NewVixSmart.Web/Views/Sales/Create.cshtml:81` (initial row), `:144` (dynamically appended rows `value="1"`)
- **What:** New invoice lines ship with `Quantity` prefilled to `1` while `Count` is editable.
  Combined with H-2's `Total` rule (`Quantity > 0` wins), a user entering only a count still
  bills `1 × UnitPrice`.
- **Why it matters:** A count-only sale (e.g. "5 boxes", entered only in the count field) is
  charged for 1 base unit, and stock relief consumes the inconsistent quantity-dimension —
  under-charged revenue plus wrong inventory decrement on every dual-unit line.
- **Fix:** Default `Quantity` to `0` in both places and let `Total` fall through to `Count`
  (per `SaleInvoiceItem.cs:41`'s existing ternary), or hide/derive `Quantity` from `Count` via
  the item's unit ratio so both fields are never independently editable.

### M-7 — Switching base currency does not rescale existing exchange rates
- **File:** `src/NewVixSmart.Web/Controllers/SettingsController.cs:148-158` (`SetBaseCurrency`)
- **What:** The endpoint changes which currency is the system base but does not recompute any
  other currency's `ExchangeRate`, which are all quoted relative to the old base.
- **Why it matters:** Switching base from USD to SAR leaves EUR at `0.92` (was USD-quoted) while
  the system now interprets rates as SAR-quoted — every FX conversion, multi-currency report, and
  FX settlement after the switch is wrong by the USD/SAR ratio.
- **Fix:** In `SetBaseCurrency`, before saving, requote every active currency:
  `rate_new = rate_old × (1 / oldBaseToNewBaseRate)`, inside a transaction.

### M-8 — Production seeding runs demo data, gated by a check that also skips unrelated master-data seeding
- **Files:** `src/NewVixSmart.Web/Program.cs:361-367` (unconditional `SeedData.InitializeAsync`;
  production only gates the insecure-password check at `:345-359`), `src/NewVixSmart.Web/Data/SeedData.cs:161-162`
  (demo gate = `GLAccounts.Code == "1000"`), `:281-299` (demo stock/master data injected into
  `CurrentCount`/`CurrentQuantity`), `:178-225` (unit hierarchy/category seeding sharing the gate)
- **What:** Demo data (GL capital entries, injected demo stock, demo master data) seeds in
  production. The only guard — absence of GL account `1000` — is a proxy for "empty database",
  and because unit-hierarchy and category seeding sit *behind the same gate*, a partial failure
  or a database that already has a COA skips that unrelated bootstrap entirely.
- **Why it matters:** New production deployments can receive fictional stock quantities and
  capital journal entries (distorting opening balances), while simultaneously missing the unit
  conversion hierarchy that H-2's fix will depend on.
- **Fix:** Split the method: seed structural master data (units, categories, permission
  defaults) unconditionally and idempotently; gate **only** demo transactions behind
  `IHostEnvironment.IsDevelopment()` (injected into `SeedData`), not behind COA content.
- **Status:** CLOSED (2026-09-23). The entire demo method (`EnsureDemoDataAsync` — warehouses,
  currencies, catalog samples, suppliers/customers/items, stock layers, demo PO) is gated behind
  `env.IsDevelopment()` at the `InitializeAsync` call site; structural seeding (roles, users,
  COA, permissions) stays unconditional and idempotent. Production now starts with an empty
  catalog and no fictional stock; tests keep the demo data because their host environment is
  `Development`.

### M-9 — Reopening a fiscal period requires both `FiscalClose.Close` and `FiscalClose.Reopen`, but the UI checks only one
- **Files:** `src/NewVixSmart.Web/Controllers/FiscalController.cs:12-13` (class-level
  `RequirePerm("FiscalClose.Close")`) + `:107` (action-level `RequirePerm("FiscalClose.Reopen")`);
  `src/NewVixSmart.Web/Extensions/RequirePermAttribute.cs` (each attribute runs its own filter;
  both must pass); view gate
  `src/NewVixSmart.Web/Views/Fiscal/Index.cshtml:70` checks only `FiscalClose.Reopen`
- **What:** The class-level filter stacks with the action filter, so `Reopen` is reachable only
  with **both** keys. The button visibility logic and the permission editor (H-3 aside) present
  Reopen as a standalone grant.
- **Why it matters:** An admin who grants only `FiscalClose.Reopen` produces a user who sees
  the reopen button (`Index.cshtml:70`) and is then bounced to AccessDenied on click — a
  confusing half-granted state on a year-end accounting control.
- **Fix:** Remove the class-level `[RequirePerm("FiscalClose.Close")]` from `FiscalController`
  and annotate `Index`/`Close`/`Preview` actions individually with `Close`, leaving `:107`'s
  `Reopen` as the sole requirement for reopen; keep the view gate as-is.

---

## LOW

### L-1 — CSV exporters in ExportCenterService ignore `MaxExportRows` (XLSX path caps correctly)
- **File:** `src/NewVixSmart.Web/Services/ExportCenterService.cs` — the `.Take(MaxExportRows)`
  cap exists on the sheet path (`:1557`, `WriteSheet`); the CSV writer loops have no equivalent
  cap. (`ReportExportService` caps all three paths — `:23`, `:55`, `:88`.)
- **Status:** FIXED in Round-6 pass — `.Take(MaxExportRows)` added to all 25 CSV top-level queries.

### L-2 — Dashboard endpoint is authorized only `[Authorize]`, not `Reports.Dashboard`
- **File:** `src/NewVixSmart.Web/Controllers/HomeController.cs:9`, `:15-18`; contrast menu gate
  `src/NewVixSmart.Web/Views/Shared/_Layout.cshtml:272`
- **What:** Any authenticated user (e.g. a Warehouse-role user with no Reports grants) can hit
  `/Home/Index`; `GetDashboardAsync()` loads full KPIs unconditionally, and view-level
  `Perm.HasAsync` checks (`Home/Index.cshtml:37,51,72,109,152,179`) hide some cards but the data
  is already materialized and partially rendered.
- **Why it matters:** The menu treats the dashboard as a Reports-granted screen; the server does
  not — inconsistent access model and mild financial-data exposure to unauthorized roles.
- **Fix:** Add `[RequirePerm("Reports.Dashboard")]` to `HomeController.Index` (keeping
  `Error()` anonymous), matching the `_Layout` gate.

### L-3 — `GetConsumedCostAsync` aggregates need across all items but consumes against one item's layers
- **File:** `src/NewVixSmart.Web/Services/InventoryService.cs:647-687` — needed qty/count summed
  for *all* items (`:658-664` region) while FIFO consumption is scoped to the passed `itemId`.
  Sole caller is a test (`InventoryServiceTests.cs:555`).
- **Why it matters:** Dead-ish production API with wrong-by-construction math; anyone wiring it
  into a real write-down will understate cost for multi-item operations.
- **Fix:** Scope the aggregation `Where(l => l.ItemId == itemId)` (and warehouse), or delete the
  method until a real caller exists.

### L-4 — Adjustment movement `Type` cannot represent mixed-sign quantity deltas
- **File:** `src/NewVixSmart.Web/Services/InventoryService.cs:589-592`
- **What:** A single `MovementType` flag is chosen for a movement row that can carry positive
  `QuantityDelta` and negative `CountDelta` simultaneously (or vice versa).
- **Why it matters:** Stock-movement history and any type-filtered report misread one of the two
  dimensions' direction for mixed adjustments.
- **Fix:** Emit one movement row per dimension (split positive/negative deltas into separate
  rows), or normalize the delta into a single signed base-unit quantity before assigning `Type`.

### L-5 — `SetSettingAsync` duplicate-key recovery re-inserts a still-tracked Added entity
- **File:** `src/NewVixSmart.Web/Controllers/SettingsController.cs:559-570`
- **What:** After `SaveChangesAsync` throws `DbUpdateException`, the failed `SystemSetting`
  instance remains tracked as `Added`; `FindAsync(key)` returns that tracked instance rather
  than `null`, so the recovery path takes the update branch — but the failed insert is still
  queued, and the retry `SaveChangesAsync` hits the same duplicate key and throws out of the
  handler.
- **Why it matters:** The race the try/catch was written to survive (concurrent settings writes)
  still surfaces as an unhandled 500 instead of recovering.
- **Fix:** In the catch, detach the failed entry first
  (`_db.Entry(existing).State = EntityState.Detached;`) before `FindAsync`, or reload with
  `AsNoTracking` + explicit re-attach as `Modified`.

### L-6 — Dashboard queries load unbounded data and count sales/purchases asymmetrically
- **File:** `src/NewVixSmart.Web/Services/DashboardService.cs:121-137`
  (`LoadDueAlertsAsync` materializes *all* unpaid invoices), `:46-65` (sales counted only when
  delivered; purchases counted regardless of status)
- **Why it matters:** Dashboard render time degrades with open-invoice volume; and the sales-vs-
  purchases KPI cards use different inclusion rules, so the two numbers are not comparable —
  misleading at a glance.
- **Fix:** Filter/paginate due alerts server-side (`Where(...).Take(n)` with projected fields),
  and align the status predicate between the sales and purchases counters.
- **Status:** CLOSED (partial, 2026-09-23). Due-alert counts/totals are now computed server-side
  with `GroupBy(1)` aggregates instead of materializing every unpaid invoice. The sales-vs-
  purchases KPI asymmetry is **retained by design**: sales are recognized on delivery, and
  `PurchaseInvoice` has no status field to compare against — documented here as the intended
  semantics rather than a defect.

### L-7 — Client-supplied `StockTransferItem.UnitCost` is trusted and persisted
- **File:** `src/NewVixSmart.Web/Services/InventoryService.cs:1252` (stored from the bound
  model), flows into exports at `src/NewVixSmart.Web/Services/ExportCenterService.cs:593`
- **Why it matters:** Transfer cost shown on exported documents comes from the request body, not
  from the consumed FIFO layers — a client can submit an arbitrary cost that then appears in
  official exports (integrity, not a valuation driver, but exported numbers should be server truth).
- **Fix:** Recompute `UnitCost` server-side from the FIFO consumption results at `:1252`
  (the layer costs are already being read in the same method) and ignore the posted value.

---

## Summary table

| # | Severity | File:line | One-line fix | Status |
|---|----------|-----------|--------------|--------|
| H-1 | High | InventoryService.cs:1168, 859-883, 1278 | Stamp `WarehouseId` on every layer ingest; make snapshot use the same predicate as consumption | CLOSED |
| H-2 | High | SaleInvoiceItem.cs:41; InventoryService.cs:187, 532, 788-789, 610-611 | One dimension rule everywhere; scale `CountCost` like SeedData.cs:342-344 | CLOSED — quantity-primary, no ratio |
| H-3 | High | UserManagementViewModels.cs:26-39; UsersController.cs:110-122, 166-173; Permissions.cshtml:42-53 | Carry full granted-key set into the VM; real labels for custom actions | CLOSED |
| M-1 | Medium | AccountingService.cs:140, 148 | Never sum `Quantity + Count`; convert via unit ratio first | CLOSED — quantity-primary rule |
| M-2 | Medium | InventoryService.cs:294 | Use `invoice.InvoiceDate` for movement date | FIXED |
| M-3 | Medium | InventoryService.cs:1140-1272 | Add `IsPeriodClosedAsync` to `CreateTransferAsync` | CLOSED |
| M-4 | Medium | InventoryService.cs:112-126 vs :128 | Move delivery-limit check inside the transaction | CLOSED |
| M-5 | Medium | InventoryService.cs:226-237; DeliveryOrder.cs | Transaction + `RowVersion` on cancel | CLOSED (migration applied) |
| M-6 | Medium | Sales/Create.cshtml:81, :144 | Default Quantity to 0 or derive it from Count | CLOSED |
| M-7 | Medium | SettingsController.cs:148-158 | Requote all FX rates inside `SetBaseCurrency` | CLOSED |
| M-8 | Medium | Program.cs:361-367; SeedData.cs:161-162, 281-299, 178-225 | Gate demo data on `IsDevelopment()`, not COA content | CLOSED |
| M-9 | Medium | FiscalController.cs:12-13, :107; Fiscal/Index.cshtml:70 | Per-action RequirePerm instead of class-level Close | CLOSED |
| L-1 | Low | ExportCenterService.cs (CSV paths) | `.Take(MaxExportRows)` on CSV loops | FIXED |
| L-2 | Low | HomeController.cs:9 | Add `[RequirePerm("Reports.Dashboard")]` to Index | CLOSED |
| L-3 | Low | InventoryService.cs:647-687 | Scope need-aggregation to `itemId`, or remove | CLOSED |
| L-4 | Low | InventoryService.cs:589-592 | Split mixed-sign deltas into separate movement rows | CLOSED |
| L-5 | Low | SettingsController.cs:559-570 | Detach failed Added entity before recovery `FindAsync` | CLOSED |
| L-6 | Low | DashboardService.cs:121-137, :46-65 | Server-side filter due alerts; align KPI status predicates | CLOSED (partial) |
| L-7 | Low | InventoryService.cs:1252 → ExportCenterService.cs:593 | Recompute `UnitCost` from consumed FIFO layers server-side | CLOSED |
