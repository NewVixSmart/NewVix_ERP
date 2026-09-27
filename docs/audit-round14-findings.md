# Audit Round 14 — Cash-flow detail reconciliation + PDF Arabic font verification

Scope: the two remaining "deferred" items with real user impact — the cash-flow report's detail
table not reconciling to its own totals, and QuestPDF Arabic rendering. Build: 0 warnings, 0 errors.
Tests: **340/340 pass** (+2). `/healthz` = 200. Untracked audit doc (kept out of commit).

## R14-1 [MEDIUM] Cash-flow detail table omits on-receipt auto-paid purchases — FIXED

On-receipt purchase invoices are paid at posting time without a `Payment` row
(`InventoryService.cs:361-362`; the cash-flow totals already fold them in via
`PurchaseInvoices WHERE PaymentTerms == OnReceipt && PaidAmount > 0`). The report's detail table
(web + XLSX) iterated only `vm.Payments`, so whenever such purchases existed in the period the
detail rows **summed to less than `TotalDisbursements`** — the table could not be footed to the
totals, and the "auto-paid" cash left no trace in the detail view.

Fix — the report now carries a reconciled, merged line list:

- New `CashFlowDetailLine(Date, Doc, Type, Party, Method, Reference, Amount)` record and
  `List<CashFlowDetailLine> Details` on `CashFlowReportViewModel`.
- `ReportService.CashFlowAsync` builds `Details` from the period's `Payment` rows **plus** a
  synthetic disbursement line per period on-receipt purchase (`Doc = InvoiceNumber`, `Party =
  supplier name`, `Method = Cash`, base-converted amount), then sorts them together by date.
- `Views/Reports/CashFlow.cshtml` renders `Details` and adds a footer row showing
  `مجموع التفاصيل` = `قبض / صرف` (always equal to the summary cards, by construction). Empty-state
  check now uses `Details`.
- `ReportService.ExportCashFlowXlsxAsync` uses the same `Details` (adds a `المرجع` column) and
  writes a bold totals row that round-trips against the top summary.

Behavior: `Sum(Details Receipt) == TotalReceipts` and `Sum(Details Disbursement) ==
TotalDisbursements` in every case, including periods with on-receipt purchases. The synthetic line
is date-ordered among real payments, so the ledger story stays chronological.

## R14-2 [INFO] QuestPDF Arabic glyphs — VERIFIED (no code change needed)

QuestPDF ≥ 2024.3 removed the manual `.Fallback(...)` API (obsolete-invaldated in 2026.8; the build
rejects it) and replaced it with an **automated glyph fallback**: text is split per codepoint and
glyphs missing from the primary font (bundled Lato: Latin/Greek/Cyrillic only) are matched against
the runtime-installed fonts via `SKFontManager.MatchCharacter`. On Windows this resolves Arabic to
Segoe UI / Tahoma / Arial automatically, no font bundling or registration required.

Verification: all PDF entry points funnel through `PrintPdfBuilder.Render` (invoices, purchase
orders, sale/purchase returns, stock transfers, customer/supplier statements, quotes, financial-
report PDFs), and the existing `PrintPdfBuilderTests` render Arabic invoices/orders end-to-end with
no missing-glyph exception (all 340 tests pass against QuestPDF 2026.8.0).

Note for non-Windows deployments: the automated mechanism depends on an Arabic-capable font being
installed in the runtime image (same as the existing behavior); a slim Linux/Docker image would need
`fonts-noto-core`/`fonts-noto-arabic` installed — a deployment concern, not a code defect. No
external font files (Cairo/Noto) were bundled because the bundled `cairo-*.woff2` cannot be consumed
by QuestPDF/Skia (WOFF2 compression) and adding a licensed font binary is out of scope.

## Tests added (+2)

- `CashFlowTests.CashFlow_Details_ReconcileToTotals_WithOnReceiptPurchases` — on-receipt purchase
  250 + receipt 200 + disbursement 80 → `Details` = 3 lines; detail sums equal
  `TotalReceipts`/`TotalDisbursements` (200/330); the synthetic line is `Disbursement`, `Cash`,
  250, supplier name.
- `CashFlowTests.CashFlow_OnReceiptPurchase_BeforePeriod_CountsInOpeningBalance` — on-receipt
  purchase dated before the period lowers `OpeningBalance` to `-400`, contributes no period
  disbursement, and appears in no detail row.

## Notes

- `vm.Payments` kept for compatibility (existing assertions); web view + XLSX now render `Details`.
- Still deferred (cosmetic, no correctness impact): date culture/locale request-localization — all
  exports already use explicit `dd/MM/yyyy` + `InvariantCulture`, and views format explicitly.