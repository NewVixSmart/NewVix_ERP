# Audit Round 12 — Statements (FX / on-receipt) + Print & Settings


> **Currency update (2026-09-27):** the fields and rules described below (`CurrencyId`,
> `ExchangeRate`, `BaseAmount`, `ExchangeRateAtSettlement`, `FxGain`/`FxLoss`, foreign-currency
> allocation and rate-based posting) no longer exist. Multi-currency was removed entirely —
> see [`DECISION-EGP-ONLY.md`](DECISION-EGP-ONLY.md). The findings are kept verbatim as the
> historical record of that round; they are resolved by removal, not by the fixes proposed here.

Scope: financial-statement HIGHs (XLSX / PDF / web ledger) and print & settings MEDIUMs, per
user instruction to run both streams in parallel. Build: 0 warnings, 0 errors.
Tests: **330/330 pass** (+7). `/healthz` = 200. Untracked audit doc (kept out of commit).

## Financial statements (XLSX / PDF / web ledger)

### F1 [HIGH] Supplier statement overstated closing for on-receipt invoices — FIXED
On-receipt purchase invoices are recorded fully paid at posting time
(`InventoryService.cs:361-362` set `PaidAmount = NetAmount`, `IsPaid = true`) but post **no
`Payment` row** — the accounting `RecordDisbursementAsync` only writes a journal
(`InventoryService.cs:375-378`). Statements that sum invoice debits minus payment rows
therefore overstated the supplier balance by the full auto-paid amount.

Fix: statements now emit an offsetting credit line **"مدفوع عند الاستلام"** for every invoice
with `PaymentTerms == OnReceipt && PaidAmount > 0`, converted to base at
`round(PaidAmount * (ExchangeRate ?? 1), 2)`:

- `ReportService.ExportSupplierStatementXlsxAsync` / `ExportCustomerStatementXlsxAsync`
  (`ReportService.cs:1051+`) — customer side added defensively (sale invoices always start
  `PaidAmount = 0`, but the guard keeps both statements correct if that ever changes).
- `SuppliersController.LedgerPdf` / `CustomersController.LedgerPdf`.
- Web ledger `Balance` (`SuppliersController.Ledger`, `CustomersController.Ledger`) now
  subtracts the native on-receipt `PaidAmount` sum before returns/payments.

Regression risk: `OnReceipt == 0` is the default enum, but no existing test sets `PaidAmount`,
so existing statement tests are unaffected (verified, all pass).

### F2 [HIGH] PDF party statements ignored FX — FIXED
`LedgerPdf` passed native amounts straight into `StatementLine`. It now converts invoice
debits (`NetAmount * (ExchangeRate ?? 1)`), return credits (`TotalAmount * (ExchangeRate ?? 1)`),
and payment credits (`BaseAmount > 0 ? BaseAmount : Amount`) exactly like the XLSX builders,
so PDF and XLSX statements show the same base-currency numbers.

### F3 [LOW] PDF ledger grouped by type, not date-sorted — FIXED
PDF lines are now `OrderBy(l.Date).ThenBy(l.Description)` before computing
from/to/closing and rendering, matching the XLSX.

### F4 [MEDIUM] Income statement silently broken for fully-future ranges — FIXED
`FinancialReportService.IncomeStatementAsync` swapped an inverted range then clamped `toDate`
to today, leaving `fromDate > toDate` (a range that matched nothing while the view printed it).
Fix: after clamping, also clamp `fromDate = toDate` when needed (consistent with the
`TrialBalanceAsync` convention of clamping future dates to today). Range is now always valid.

### Cash-flow items from agent report — verified, no code change
- `p.BaseAmount > 0 ? p.BaseAmount : p.Amount` fallback (`ReportService.cs:908, 920, ...`) is a
  no-op in practice: base-currency payments always set `BaseAmount = Amount`; foreign payments
  with missing rate are rejected earlier or rate defaults to 1. Not a defect.
- On-receipt double count cannot occur: a manual disbursement to an on-receipt supplier is
  rejected because allocation caps at zero remaining
  (`PaymentService` allocates only to `PaidAmount < NetAmount` invoices, then rejects
  "المبلغ أكبر من إجمالي المستحق لهذا الطرف" when a residual remains).

## Print & settings

### P1 [MEDIUM] `AmountInWords` dropped the negative sign — FIXED
`PrintPdfBuilder.AmountInWords` (255+) used only magnitudes, so −500 printed like +500.
Now negative whole parts are prefixed with "ناقص ". Values with zero whole (e.g. −0.40) keep
"صفر" (no "ناقص صفر" nonsense). Positive output is byte-identical.

### P2 [MEDIUM] `SaveBranding` could persist partial theme data — FIXED
Preset validation sat *after* `SetSettingAsync("Theme.Primary", ...)` writes, so an invalid
preset id already persisted the four theme colors before erroring
(`SettingsController.SaveBranding`). The preset check now runs **before** the settings writes
(no partial persist), and the reject path invalidates the branding cache as defense.

### P3 [LOW] `SavePrinting(applyToAll)` cache invalidation inside uncommitted transaction — FIXED
Each `SaveLayoutAsync` invalidates the cache mid-transaction; a concurrent reader could re-cache
old committed values right before commit and serve them for up to 60 min. An explicit
`_printSettings.Invalidate()` now runs immediately after `tx.CommitAsync()`.

### P4 [LOW] `SavePrinting` no validation guard — FIXED
`SavePrinting` now rejects `!ModelState.IsValid` (catches malformed numbers / invalid enum
bindings) with a clear TempData error instead of silently clamping and saving.

### P5 [LOW] Unbounded footer/signature text — FIXED
`PrintLayoutOptions.FooterNoteText` → `[StringLength(500)]`, `SignatureOne`/`SignatureTwo` →
`[StringLength(200)]`; `PrintSettingsService.Clamp` truncates via the extended `AsText(...,
maxLength)` overload (defense in depth for preview/JSON state paths that bypass MVC binding).

### Deferred (documented, not in this round)
- Arabic font registration in QuestPDF: UNVERIFIED risk on Linux renders; needs font asset +
  bundle decision, not a code fix.
- Date culture/locale for numbers in PDFs (independent config decision).
- Aging credit-remainder policy, stock-value export double, single CSV 50k truncation
  (`ReportExportService`), cash-flow detail-rows vs totals presentation — carry over.

## Tests added (7)
- `CashFlowTests.SupplierStatement_OnReceiptInvoice_AutoPaidLine_KeepsClosingEqualToOpening`
  (XLSX closing stays at opening; credit line present).
- `PrintPdfBuilderTests.AmountInWords_Negative_PrefixedWithMinusAndKeepsMagnitude` (2 cases).
- `PrintPdfBuilderTests.AmountInWords_AlwaysEndsWithFaqat` (3 cases incl. negative & zero).
- `FinancialStatementsCorrectnessTests.IncomeStatement_EntirelyFutureRange_ClampsToToday_ValidRange_ZeroTotals`.