# Round 16 — Closing L-3 (stale previews) and the deferred date culture/locale item

Build: 0 warnings, 0 errors. Tests: 348/348 green (no behavioural changes to accounting services).
App restart: `/healthz` 200 after migration-free startup.

## R16-1 [LOW] L-3 stale/blank "next document number" previews — FIXED

The GET-only preview (next invoice/return/adjustment/quote number) was never refreshed on POST
re-render: Sales/Purchases re-rendered the stale snapshot, returns/adjustments blanked it
(`ViewBag` is per-request and was only set in GET), and return forms never ran `calculateTotals()`
on load (totals stayed 0 until the first keystroke). The service regenerates the real document
number on save (L-2 unique-index + fresh-read replay), so this was cosmetic — but it is now
deterministic.

- `SalesController.cs:95-97`, `PurchasesController.cs:81-83` — POST failure path recomputes
  `Invoice.InvoiceNumber` (`lastInvoice.Id + 1`) and `ModelState.Remove(...)` so the tag helper
  renders the fresh value (posted stale ModelState would otherwise win).
- `SaleReturnsController.cs`, `PurchaseReturnsController.cs` — `ViewBag.NextNumber` moved into
  `PopulateDropdowns()` (GET + POST both set it); POST additionally sets the model's `ReturnNumber`
  and `ModelState.Remove("ReturnNumber")`.
- `SalesQuotesController.cs:76-78` — POST recomputes `vm.Quote.QuoteNumber = await NextNumberPreviewAsync()`
  + `ModelState.Remove("Quote.QuoteNumber")`.
- `InventoryAdjustmentsController.cs` — `ViewBag.NextNumber` moved into `PopulateDropdowns()`.
  `ReferenceNumber` is a user-editable field, so POST intentionally never touches ModelState/model.
- `Views\SaleReturns\Create.cshtml`, `Views\PurchaseReturns\Create.cshtml` — `calculateTotals();`
  now invoked once on load (matches Sales/Purchases behaviour).

## R16-2 [LOW] Date culture / locale configuration — CLOSED (was deferred across R12-R15)

Foundational fact: the app had **no** culture configuration — no `UseRequestLocalization`, no
`DefaultThreadCurrentCulture`, no `InvariantGlobalization` in any csproj. All rendering followed the
**host OS default culture**. On an `ar-SA`-locale host this is a real correctness hazard, not just
cosmetics: `ar-SA` defaults to the **UmAlQura (Hijri) calendar**, so `DateTime.ToString("yyyy...")`
would render the Hijri year (1448) and Arabic-Indic digits in printed/exported dates and in
`yyyyMMdd` document numbers (`SRTN-20260924`-style stamps), while views show Western digits on the
typical en-US host — non-deterministic output across hosts.

- `Program.cs` — pins `CultureInfo.InvariantCulture` for `DefaultThreadCurrentCulture/UICulture` and
  `CurrentCulture/UICulture` at startup (before `WebApplication.CreateBuilder`). Effect: Gregorian
  calendar, Western digits, invariant separators for every thread/request/background service and all
  culture-less `ToString` calls in views. No visual change on an en-US host (Invariant `N2` == `en-US`
  `N2`), matching existing print/PDF/CSV behaviour.
- `ExportCenterService.cs` (42 call sites) + `ReportExportService.cs` (3 call sites) — export **date**
  cells now pass `CultureInfo.InvariantCulture` explicitly, closing the gap where export *numbers*
  were already invariant but *dates* were not (rounds 14/15 claim made true everywhere).
- Intentionally left for the pin: `ViewBag.From/To` filter values in `ReportsController.cs` and
  generated file names (`$"newvixsmart_{key}_{DateTime.Today:yyyyMMdd}.xlsx"`), which are not export
  cell content. No `.cshtml` views modified (the pin covers all 356 `ToString` call sites).
- Not added: `UseRequestLocalization` middleware (single-locale internal tool), `InvariantGlobalization`
  csproj flag (would also strip ICU calendars app-wide; the process pin is sufficient and testable).

## Audit-driver status

| ID | Verdict |
| --- | --- |
| L-3 | **FIXED** |
| Date culture/locale (deferred R12-R15) | **CLOSED** |
| M-4 | STILL OPEN (accepted for internal tool) |
| L-2 | ACCEPTED (unique indexes + 3-attempt fresh-read replay) |

## Evidence

- `dotnet build NewVixSmart.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test`: 348/348 passed.
- Commits: `584306b` (L-3), `a977524` (culture pinning + invariant export dates).