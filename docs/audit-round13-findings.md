# Audit Round 13 — Aging credit remainders + Stock report totals + CSV truncation

Scope: three leftover findings from the financial-review rounds — aging drops credit remaining
(customer/supplier) parties, stock-report totals sum a filtered subset, and CSV exports silently
truncate at 50k rows. Build: 0 warnings, 0 errors. Tests: **338/338 pass** (+8). `/healthz` = 200.
Untracked audit doc (kept out of commit).

## R13-1 [MEDIUM] Aging report drops credit-remainder parties — FIXED

`ReportService.AgingAsync` built receivable/payable lines from open invoices (positive) plus
returns and opening balances (positive or negative), then ran `.Where(r => r.Total > 0.005m)` —
negative totals were the party's credit position (returns that outweigh open invoices). A customer
with only a standalone posted return (pure credit to the customer) therefore vanished entirely,
making the aging report disagree with statements.

Fix (`ReportService.cs:724,785`): keep every line whose net is non-zero by filtering on the
**absolute** figure:

```csharp
.Where(r => Math.Abs(r.Total) > 0.005m)
```

Behavior now:
- Credit remainder — negative totals are surfaced in the correct due bucket
  (`BuildAgingRow` adds signed amounts, so a return 3 days ago lands in `Days1To30` as `-120`).
- A zero-net party (invoice 100 delivered + linked posted return 100) is still excluded, because
  `outstanding = 100 - 0 - 100 = 0` never becomes a line and the pure-credit remainder rows net to 0.
- `ArTotal`/`ApTotal` sum the signed rows, so the report stays self-consistent (e.g. `-120`).

## R13-2 [MEDIUM] Stock report totals computed on the filtered subset — FIXED

`StockController.Report` (`SECURITY_AUDIT_FinancialLogic.md` M-2) computed `TotalCount`,
`TotalQuantity`, and `TotalValue` with `query.SumAsync(...)` — the IQueryable already constrained
by `search` / `categoryId` / `lowOnly`. A category filter therefore reported category-only totals
labeled "company total", and the value footer changed when the user searched. That is misleading:
totals must describe the whole active item base.

Fix (`StockController.cs:77-91`): totals now read from an unfiltered base query over active items:

```csharp
var baseQuery = _db.Items.AsNoTracking().Where(i => i.IsActive);
// TotalCount  = baseQuery.SumAsync(i => i.CurrentCount)
// TotalQuantity = baseQuery.SumAsync(i => i.CurrentQuantity)
// TotalValue   = baseQuery.SumAsync(i => (i.QuantityUnitId.HasValue || i.CurrentQuantity > 0)
//                   ? i.CurrentQuantity * i.PurchasePrice
//                   : i.CurrentCount * i.PurchasePrice)
```

`TotalItems` and `LowItems` keep describing the visible (filtered) table, which is the correct split:
the grid header says "ن", the footer states the whole-base aggregates.

## R13-3 [MEDIUM] CSV exports silently truncate at 50,000 rows — FIXED

`ReportExportService` capped every export with `Take(MaxExportRows)` (`MaxExportRows = 50_000`) and
returned the partial file with no signal, so a range with more matches downloaded an incomplete CSV
that looked complete. The controllers could not detect the cut because the service returned
`byte[]`.

Fix — the service now returns a result record and reports truncation explicitly:

```csharp
public sealed record CsvExportResult(byte[] Bytes, bool Truncated, int TotalRows);
```

- `SalesToCsv` / `PurchasesToCsv` / `PaymentsToCsv` count total rows first, `Take(maxRows)` (new
  optional `maxRows` parameter, default `MaxExportRows`), and return the record.
- The three export actions in `ReportsController` refuse partial files: on `Truncated` they set
  `TempData["Error"]` ("…يتجاوز حد التصدير (50,000 سجل)؛ ضيّق نطاق التاريخ ثم أعد التصدير") and
  redirect back to the matching report page with the same range. The user gets an actionable error
  instead of a silently short file.
- Redirect targets (`Sales` / `Purchases` / `Payments`) share the class-level `Reports.View`
  permission, so the redirect never loses access for an exporting user.

## Tests added (+8)

- `AgingTests.Aging_StandaloneReturn_CreditRemainder_IsSurfacedNotDropped` — standalone posted
  customer return 120 → one receivable row `Total = -120`, `Days1To30 = -120`, `ArTotal = -120`.
- `AgingTests.Aging_ZeroNetPosition_Excluded_ButPureCreditRemainderSurfaced` — zero-net customer
  (invoice 100 + linked return 100) excluded; pure supplier credit 60 surfaced as `-60`.
- `StockReportTests.StockReport_Totals_AreCompanyWide_NotFilteredSubset` — 3 items in 2 categories;
  `Report(catA.Id, false)` → `TotalItems == 2` but `TotalCount/TotalQuantity == 10`, `TotalValue == 310`.
- `StockReportTests.StockReport_LowOnlyFilter_StillReportsCompanyWideTotals` — `lowOnly` shows one
  row yet totals still cover the whole base (`TotalCount == 102`, `TotalValue == 180`).
- `ReportExportServiceTests` (new) — untruncated sales/purchases/payments CSV (header + rows,
  `Truncated == false`) and `maxRows: 2` over 3 seeded sales → `Truncated == true`, `TotalRows == 3`,
  exactly 2 data rows emitted.

## Notes

- No new physical checks/enums; record `CsvExportResult` is a plain type. `ImportExportCenterTests`
  adapted to the new returns (two assertions now read `.Bytes`).
- Deferred (unchanged): QuestPDF Arabic font registration, date culture/locale configuration,
  cash-flow detail-vs-totals layout.