---
goal: P4b — Chart-of-Accounts Management UI + Annual Budgets (Planned vs Actual) in Reports
version: 1.0
date_created: 2026-09-06
last_updated: 2026-09-06
owner: AI Agent
status: 'Shipped'
tags: ['feature', 'erp', 'accounts', 'budgets', 'reports']
---

# Introduction

![Status: In progress](https://img.shields.io/badge/status-In%20progress-yellow)

P4b milestone: Chart-of-Accounts CRUD admin UI + annual budget entities (BudgetYear/BudgetLine) + Budget vs Actual variance report with XLSX export + tests. No changes to posting engines or fiscal-close logic.

## 1. Requirements & Constraints

- **REQ-001**: AccountsController + Views (Index/Create/Edit/Deactivate) with full CRUD on GLAccount
- **REQ-002**: Guard rules: posted/system accounts cannot have Code changed or be deleted; system accounts cannot be deactivated
- **REQ-003**: BudgetYear + BudgetLine entities with unique (BudgetYearId, AccountId) constraint
- **REQ-004**: BudgetsController with Index (year list) + Manage(year) batch save
- **REQ-005**: BudgetVariance report page with Actual vs Budget + XLSX export
- **REQ-006**: Permissions: ChartOfAccounts (View/Create/Edit/Deactivate) + Budgets (View/Manage)
- **REQ-007**: Arabic UI/errors throughout; 0W/0E TreatWarningsAsErrors
- **REQ-008**: All 106 existing tests stay green; ≥114 total; migration applied
- **REQ-009**: Sign convention: Variance = Actual − Budget; favorable positive for both revenue and expense
- **CON-001**: GLAccount.ParentAccountId already exists in model — honor hierarchy
- **CON-002**: P&L budgetable accounts = codes 4000-5999 (Revenue + Expense types)
- **CON-003**: Budget for closed fiscal year: view-only, edit blocked with Arabic error
- **GUD-001**: Mirror existing SettingsController/CurrenciesController CRUD patterns
- **GUD-001**: Use ClosedXML for XLSX export (same as ReportService.cs)

## 2. Implementation Steps

### Phase 1: Entities + DB + Migration

| Task | Description |
|------|-------------|
| TASK-001 | Create `Models/Accounting/BudgetYear.cs` entity |
| TASK-002 | Create `Models/Accounting/BudgetLine.cs` entity |
| TASK-003 | Add DbSets + config to `AppDbContext.cs` |
| TASK-004 | Seed current-year BudgetYear in `SeedData.cs` (idempotent, before 1000 guard) |
| TASK-005 | Create + apply `AddBudgets` migration |

### Phase 2: Services

| Task | Description |
|------|-------------|
| TASK-006 | Create `Services/AccountsService.cs` — CRUD guards, running balance query |
| TASK-007 | Add `GetAccountYearlyActivityAsync(accountId, year)` to `FinancialReportService` |
| TASK-008 | Add `ExportBudgetVarianceXlsxAsync` to `ReportService` |

### Phase 3: Controllers + Views

| Task | Description |
|------|-------------|
| TASK-009 | Create `Controllers/AccountsController.cs` |
| TASK-010 | Create Views: Accounts/Index, Create, Edit |
| TASK-011 | Create `Controllers/BudgetsController.cs` |
| TASK-012 | Create Views: Budgets/Index, Manage |
| TASK-013 | Add BudgetVariance action + View to `ReportsController` |
| TASK-014 | Add BudgetVarianceXlsx export action to `ReportsController` |

### Phase 4: Permissions + Sidebar

| Task | Description |
|------|-------------|
| TASK-015 | Register ChartOfAccounts + Budgets modules in `PermissionCatalog.cs` |
| TASK-016 | Add defaults in `PermissionDefaults.cs` |
| TASK-017 | Add sidebar entries in `_Layout.cshtml` |

### Phase 5: Tests

| Task | Description |
|------|-------------|
| TASK-018 | Create `BudgetAndAccountsTests.cs` — all guard + variance tests |
| TASK-019 | Verify all 106 existing pass; total ≥114 |

### Phase 6: Docs + Smoke

| Task | Description |
|------|-------------|
| TASK-020 | P4b entry in `docs/BUILD-PLAN-README.md` |
| TASK-021 | Playwright smoke for /Accounts, /Budgets, /Reports/BudgetVariance |

## 3. Alternatives

- **ALT-001**: Flat list only (no parent display) — rejected because model already has ParentAccountId hierarchy
- **ALT-002**: MonthlyBudget JSON column — rejected; AnnualAmount is sufficient for budget variance reporting per spec

## 4. Dependencies

- **DEP-001**: ClosedXML 0.105.1 (already in project)
- **DEP-002**: SQLite in-memory for tests (already used)

## 5. Files

- **FILE-001**: `src/.../Models/Accounting/BudgetYear.cs` (new)
- **FILE-002**: `src/.../Models/Accounting/BudgetLine.cs` (new)
- **FILE-003**: `src/.../Data/AppDbContext.cs` (add DbSets + config)
- **FILE-004**: `src/.../Data/SeedData.cs` (add budget seed)
- **FILE-005**: `src/.../Services/AccountsService.cs` (new)
- **FILE-006**: `src/.../Services/FinancialReportService.cs` (add method)
- **FILE-007**: `src/.../Services/ReportService.cs` (add XLSX export)
- **FILE-008**: `src/.../Services/IReportService.cs` (add interface method)
- **FILE-009**: `src/.../Services/IFinancialReportService.cs` (add interface method)
- **FILE-010**: `src/.../Controllers/AccountsController.cs` (new)
- **FILE-011**: `src/.../Views/Accounts/Index.cshtml` (new)
- **FILE-012**: `src/.../Views/Accounts/Create.cshtml` (new)
- **FILE-013**: `src/.../Views/Accounts/Edit.cshtml` (new)
- **FILE-014**: `src/.../Controllers/BudgetsController.cs` (new)
- **FILE-015**: `src/.../Views/Budgets/Index.cshtml` (new)
- **FILE-016**: `src/.../Views/Budgets/Manage.cshtml` (new)
- **FILE-017**: `src/.../Controllers/ReportsController.cs` (add actions)
- **FILE-018**: `src/.../Views/Reports/BudgetVariance.cshtml` (new)
- **FILE-019**: `src/.../Services/PermissionCatalog.cs` (add modules)
- **FILE-020**: `src/.../Services/PermissionDefaults.cs` (add defaults)
- **FILE-021**: `src/.../Views/Shared/_Layout.cshtml` (add sidebar)
- **FILE-022**: `tests/.../BudgetAndAccountsTests.cs` (new)
- **FILE-023**: `docs/BUILD-PLAN-README.md` (add P4b entry)

## 6. Testing

- **TEST-001**: Account CRUD: create/edit zero-usage account OK
- **TEST-002**: Account CRUD: edit posted account — Code change rejected
- **TEST-003**: Account CRUD: delete posted account rejected; zero-usage delete OK
- **TEST-004**: Account CRUD: system account cannot be deactivated/deleted
- **TEST-005**: Account CRUD: duplicate Code rejected (case-insensitive)
- **TEST-006**: Budget: save persists annual amounts
- **TEST-007**: Budget: closed-year edit rejected
- **TEST-008**: Budget: only P&L accounts accepted
- **TEST-009**: BudgetVariance: exact math (Actual − Budget)
- **TEST-010**: All 106 existing tests stay green

## 7. Risks & Assumptions

- **RISK-001**: ParentAccountId usage in existing seed data may need review
- **ASSUMPTION-001**: Existing 106 tests unaffected by new entities/services

## 8. Related Specifications

- BUILD-PLAN-README.md (P0-P4e completion log)
