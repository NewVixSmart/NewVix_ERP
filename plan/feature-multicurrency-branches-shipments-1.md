---
goal: M8a - Add multi-currency + branches + shipments to New Vix Smart MVC
version: 1.0
date_created: 2026-09-03
owner: Senior ERP Engineer
status: 'Shipped'
tags: feature, multicurrency, branches, shipments, accounting
---

# Introduction

Add light-weight multi-currency and multi-branch support on top of the existing single-currency double-entry GL, plus a shipments (delivery notes) log. Keep pragmatic and non-breaking. GL math remains base-currency only; foreign amounts are informational snapshots.

## 1. Requirements & Constraints

- **REQ-001**: New entities `Currency` (code unique, base flag, decimal(18,6) exchange rate), `Branch` (code unique), `Shipment` (unique shipment number) + `ShipmentStatus` enum.
- **REQ-002**: `CurrencyId` optional FK on Customer/Supplier/SaleInvoice/PurchaseInvoice (default = base currency).
- **REQ-003**: `SaleInvoice`/`PurchaseInvoice` get optional `CurrencyId` + informational `ExchangeRate` decimal(18,6) snapshot at booking; base-currency NetAmount NOT rewritten.
- **REQ-004**: GL posting math untouched; always base currency. Reports stay base currency.
- **REQ-005**: `BranchId` int? FK Restrict added to: GLAccount, JournalEntry, JournalEntryLine, SaleInvoice, PurchaseInvoice, Payment, Item. All nullable.
- **REQ-006**: `AccountingService.PostAsync` central path stamps `BranchId` on entry + lines. All public method signatures source-compatible, new `branchId: int? = null` / currency defaults.
- **REQ-007**: `Shipments.{View,Create,Edit}` permission module following M4/M5 pattern; default to Accountant + Warehouse roles.
- **REQ-008**: SettingsController manages currencies CRUD (set base, edit rates) + branches CRUD + "current branch" selector.
- **REQ-009**: Seed base `SDG` + `USD` idempotently; seed 2 branches if `!db.Branches.Any()`. PLACED BEFORE the GL early-return, do not disturb warehouse/PO/quote seed order.
- **REQ-010**: ONE migration `AddMultiCurrencyBranchesShipments` for all new tables + nullable columns; applied to LocalDB.
- **CON-001**: `dotnet build` 0W/0E; 54 existing tests stay green; new tests reach 60+.
- **CON-002**: Nullable `BranchId`/`CurrencyId` everywhere so old seeded data stays valid; existing reports still render (base currency).
- **GUD-001**: Arabic labels, idempotent seed, no junk comments, no secrets.
- **PAT-001**: Mirror existing warehouse/PO seed guard pattern; extend `AccountingService` centrally, do NOT duplicate GL math.

## 2. Implementation Steps

### Implementation Phase 1 — Models + DbContext

- GOAL-001: Add all new entities, extend existing entities, wire fluent config + DbSets.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Create `Models/Accounting/Currency.cs` (Id, Code unique, Name, Symbol, ExchangeRate decimal(18,6)=1, IsBase, IsActive) | | |
| TASK-002 | Create `Models/Core/Branch.cs` (Id, Code unique, Name, Address, Phone, IsActive, CreatedAt) | | |
| TASK-003 | Create `Models/Accounting/ShipmentStatus.cs` enum (Preparing=0, Shipped=1, Delivered=2, Cancelled=3) + `ShipmentInvoiceType` enum (Sales/Purchase) | | |
| TASK-004 | Create `Models/Accounting/Shipment.cs` (Id, ShipmentNumber, InvoiceType, SaleInvoiceId?, PurchaseInvoiceId?, CustomerId?, SupplierId?, Carrier, TrackingNumber, ShipDate?, Status, Notes, CreatedBy, CreatedAt) | | |
| TASK-005 | Extend `Models/Sales/Customer.cs` with `int? CurrencyId` + `Currency? Currency` nav | | |
| TASK-006 | Extend `Models/Purchases/Supplier.cs` with `int? CurrencyId` + `Currency? Currency` nav | | |
| TASK-007 | Extend `Models/Sales/SaleInvoice.cs` with `int? CurrencyId`, `decimal? ExchangeRate decimal(18,6)`, `int? BranchId` + navs | | |
| TASK-008 | Extend `Models/Purchases/PurchaseInvoice.cs` with `int? CurrencyId`, `decimal? ExchangeRate decimal(18,6)`, `int? BranchId` + navs | | |
| TASK-009 | Extend `Models/Accounting/Payment.cs` with `int? BranchId` | | |
| TASK-010 | Extend `Models/Accounting/GLAccount.cs` with `int? BranchId` | | |
| TASK-011 | Extend `Models/Accounting/JournalEntry.cs` with `int? BranchId` | | |
| TASK-012 | Extend `Models/Accounting/JournalEntryLine.cs` with `int? BranchId` | | |
| TASK-013 | Extend `Models/Core/Item.cs` with `int? BranchId` | | |
| TASK-014 | `Data/AppDbContext.cs`: DbSets Currency, Branch, Shipment + fluent config (unique codes/numbers, Restrict FKs, nullable BranchId/CurrencyId) | | |

### Implementation Phase 2 — Services

- GOAL-002: Extend AccountingService + InventoryService + PaymentService centrally; branch + currency honored.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-015 | `Services/IAccountingService.cs`: add `branchId: int? = null` to all 7 methods (source-compatible) | | |
| TASK-016 | `Services/AccountingService.cs`: thread `branchId` through every method into `PostAsync`; stamp `BranchId` on JournalEntry + each JournalEntryLine | | |
| TASK-017 | `Services/InventoryService.cs`: `CreateSaleAsync`/`CreatePurchaseAsync` accept optional `branchId`, set `invoice.BranchId` | | |
| TASK-018 | `Services/PaymentService.cs`: `CreatePaymentAsync` accept optional `branchId`, set `payment.BranchId` | | |

### Implementation Phase 3 — Permissions

- GOAL-003: Add Shipments permission module + role defaults.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-019 | `Services/PermissionCatalog.cs`: add `["Shipments"] = [View, Create, Edit]` + `Shipments` module entry | | |
| TASK-020 | `Services/PermissionDefaults.cs`: add `Shipments.View/Create/Edit` to Accountant + Warehouse arrays | | |

### Implementation Phase 4 — ViewModels + Controllers + Views

- GOAL-004: CRUD UI for currencies/branches, current-branch selector, shipments controller/views, sale/purchase currency selection, sidebar link.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-021 | `ViewModels/Core/SettingsViewModel.cs`: add `Currencies`, `Branches`, `CurrentBranchId` | | |
| TASK-022 | `ViewModels/Sales/SaleInvoiceViewModel.cs` + `ViewModels/Purchases/PurchaseInvoiceViewModel.cs`: add `SelectList? Currencies` | | |
| TASK-023 | Create `ViewModels/Accounting/ShipmentViewModel.cs` for form/listing | | |
| TASK-024 | `Controllers/SettingsController.cs`: currencies CRUD (Add/Update/Delete/SetBase), branches CRUD, SetCurrentBranch | | |
| TASK-025 | Create `Controllers/ShipmentsController.cs` (Index/Create/Edit) gated by `RequirePerm("Shipments.*")` | | |
| TASK-026 | `Controllers/SalesController.cs` + `Controllers/PurchasesController.cs`: currencies SelectList + pass branch/currency | | |
| TASK-027 | `Views/Settings/Index.cshtml`: currencies table + CRUD + branches table + CRUD + current branch selector | | |
| TASK-028 | Create `Views/Shipments/Index.cshtml`, `Create.cshtml`, `Edit.cshtml` | | |
| TASK-029 | `Views/Shared/_Layout.cshtml`: "الشحنات" link under العمليات | | |
| TASK-030 | Update Sale/Purchase `Create.cshtml` to include currency dropdown + row totals | | |

### Implementation Phase 5 — Seed + Migration + Tests

- GOAL-005: Idempotent seed, single migration, new tests; build + test + apply + run.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-031 | `Data/SeedData.cs`: seed SDG(base)+USD idempotently + 2 branches, BEFORE GL guard | | |
| TASK-032 | Generate migration `AddMultiCurrencyBranchesShipments` | | |
| TASK-033 | Create `tests/.../MilestoneM8aTests.cs` (Squash): currency seed, sale with foreign currency+branch asserts BranchId+currency+unchanged base NetAmount, branch CRUD, shipment CRUD | | |
| TASK-034 | `dotnet build` 0W/0E | | |
| TASK-035 | `dotnet test` all green (54 existing + new = 60+) | | |
| TASK-036 | `dotnet ef database update` apply migration to LocalDB | | |
| TASK-037 | Start app so it runs | | |

## 3. Alternatives

- **ALT-001**: Rewrite GL in foreign currency recursively. Rejected: high risk, breaks base-currency reports/balances, out of pragmatic scope.
- **ALT-002**: Per-user BranchId stored in Identity claim. Rejected: complexity; global "current branch" from Settings is simplest sound approach and documented.

## 4. Dependencies

- **DEP-001**: EF Core 10 + SQL Server LocalDB (existing).
- **DEP-002**: Existing `AccountingService` central posting path to reuse.

## 5. Files

- **FILE-001**: `src/NewVixSmart.Web/Models/Accounting/Currency.cs` (new)
- **FILE-002**: `src/NewVixSmart.Web/Models/Core/Branch.cs` (new)
- **FILE-003**: `src/NewVixSmart.Web/Models/Accounting/Shipment.cs` + `ShipmentStatus.cs` (new)
- **FILE-004**: Customer.cs, Supplier.cs, SaleInvoice.cs, PurchaseInvoice.cs, Payment.cs, GLAccount.cs, JournalEntry.cs, JournalEntryLine.cs, Item.cs (extended)
- **FILE-005**: AppDbContext.cs, IAccountingService.cs, AccountingService.cs, InventoryService.cs, PaymentService.cs
- **FILE-006**: PermissionCatalog.cs, PermissionDefaults.cs
- **FILE-007**: SettingsController.cs, ShipmentsController.cs, SalesController.cs, PurchasesController.cs
- **FILE-008**: SettingsViewModel.cs, SaleInvoiceViewModel.cs, PurchaseInvoiceViewModel.cs, ShipmentViewModel.cs
- **FILE-009**: Views (Settings/Index, Shipments/*, _Layout, Sales/Purchases Create)
- **FILE-010**: SeedData.cs
- **FILE-011**: Migration `AddMultiCurrencyBranchesShipments`
- **FILE-012**: tests/.../MilestoneM8aTests.cs

## 6. Testing

- **TEST-001**: Seed base+foreign currency; assert rates and base flag.
- **TEST-002**: Create sale with foreign currency + branch; assert GL entries got BranchId stamped and currency stored; base-currency NetAmount unchanged.
- **TEST-003**: Branch CRUD persistence.
- **TEST-004**: Shipment CRUD.
- **TEST-005**: Setting currency as base works.
- **TEST-006**: Existing-style sale (no currency/branch) still posts correctly (regression).

## 7. Risks & Assumptions

- **RISK-001**: Adding `BranchId` to Item/GLAccount adds nullable FK columns; existing data unaffected (nullable). RESTRICT FK prevents accidental deletes.
- **ASSUMPTION-001**: Existing 54 tests map to entities affected; adding nullable columns is additive and non-breaking under SQLite EnsureCreated (tests).
- **ASSUMPTION-002**: SQL Server LocalDB reachable for `dotnet ef database update`.

## 8. Related Specifications / Further Reading

- `docs/BUILD-PLAN-README.md` section P2 / M8
