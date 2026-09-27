# Audit Round 15 — Adjustment-delete concurrency (M-3) + tax-exclusive revenue (N-01) + FK payment allocations (N-04)

Scope: the user-selected "full" round from `SECURITY_AUDIT_FinancialLogic_2026-09-23.md` —
M-3, N-01, N-04. Build: 0 warnings, 0 errors. Tests: **346/346 pass** (+6).
`/healthz` = 200 after restart (EF migration applied at startup). Untracked audit doc
(kept out of commit).

## M-3 [HIGH] Adjustment delete had no concurrency/atomicity guard — FIXED

`InventoryAdjustmentsController.Delete` previously orchestrated the deletion itself
(check → stock restore → remove) with no transaction, no retry, and no guard against a
concurrent stock movement or `Item.RowVersion` conflict — a lost update could silently
restore a stale `CurrentQuantity`, and a mid-delete crash could leave the record gone
with the movement (or vice versa).

Fix — the logic moved into `InventoryService.DeleteAdjustmentAsync(int, string?)`
(mirrors the H-2/RowVersion retry pattern):

- `MaxAttempts = 3` transaction retry on `DbUpdateConcurrencyException` / `DbUpdateException`
  with `DetachAll()` between attempts.
- Guard 1: reject when a journal entry exists (`Source == OpeningStock`, `SourceId == ItemId`)
  — the adjustment was posted to the GL; the user must post a new adjustment instead.
- Guard 2: reject when a later stock movement exists on the same item (`m.Id > last movement id`)
  — deleting would rewind history under subsequent movements.
- Guard 3: missing item → reject.
- Restore: `CurrentCount` / `CurrentQuantity` are set from the **first** movement's
  `CountBefore` / `BalanceBefore` (the pre-adjustment state), and **all** movements of the
  adjustment document are removed.

Note discovered by test: one adjustment can create **two** stock movements (an `In` for a
quantity delta and an `Out` for a count delta when `NewCount < oldCount` while
`NewQuantity` rises). The first implementation only inspected the first movement and
falsely saw its sibling as "later"; delete now groups movements by
`(DocumentType.Adjustment, DocumentNumber)` and checks against the last one.

Controller (`InventoryAdjustmentsController.Delete`) now only delegates and reports
`TempData` Success/Error.

## N-01 [HIGH] Delivery/revenue posted gross of tax — FIXED (new account 2055)

`RecordSaleDeliveryAsync` booked the full delivered value (tax included) to `4000` —
revenue overstated by the VAT share, and the liability was never credited.

Fix (purchase path intentionally untouched — inventory stays at tax-inclusive cost):

- New optional parameter `taxAmount = 0m` on `IAccountingService.RecordSaleDeliveryAsync`
  and `RecordSaleReturnWithCostAsync`.
- Delivery posting: `Dr 1200 = value` / `Cr 4000 = value − tax` / `Cr 2055 = tax`.
- Sale-return posting (mirror): `Dr 5101 = value − tax` / `Dr 2055 = tax` / `Cr 1200 = value`,
  plus the existing cost lines (`Dr 1300` / `Cr 5000`).
- Guards: `localTax` zeroed if `< 0.005` or `> value − 0.005` (defends against degenerate
  tax/discount inputs so a journal line is never degenerate or unbalanced).
- `InventoryService.DeliverDeliveryOrderAsync` computes the tax share per delivered line
  (`share = rawValue / TotalAmount`; `taxShare = Tax * share`) and passes the rounded
  exchange-rate-adjusted local tax; `PostSaleReturnAsync` derives
  `returnTax = round(TotalAmount * Tax/TotalAmount_of_invoice)`.
- `SeedData` idempotently seeds GL account **`2055` "الضريبة مستحقة (VAT)"**
  (Liability/Credit) on startup.

Impact: trial-balance/statement figures change — `4000` now nets the VAT out, and `2055`
carries the payable. P&L revenue is unaffected in total (previously `4000` was inflated
only if tax was included there).

## N-04 [HIGH] Payment allocations had no real invoice FKs; purchase movements lacked DocumentId — FIXED

Problems from the audit:

1. Polymorphic `PaymentAllocation` (`InvoiceId` + `InvoiceType` enum) had **no FK** to
   `SaleInvoices`/`PurchaseInvoices` — orphaned allocations were possible and the DB could
   not enforce referential integrity (audit N-04).
2. `CreatePurchaseCoreAsync` saved the invoice **after** applying stock, so the purchase
   `StockMovements.DocumentId` was written as `null` (sale/delivery/return paths already
   passed the document id).

Fix:

- Deleted `Models/Accounting/PaymentAllocation.cs` (incl. `PaymentAllocationInvoiceType`).
- Added typed entities `SalePaymentAllocation` (FK `SaleInvoiceId`) and
  `PurchasePaymentAllocation` (FK `PurchaseInvoiceId`), each with FK to `Payments`
  (`OnDelete(Restrict)`) and a **unique index `(PaymentId, <typed>InvoiceId)`**.
- `Payment` navigations, `AppDbContext` DbSets/configurations, and
  `PaymentService` (`ApplyInvoiceAllocationAsync`, `GetPaymentAsync` includes) updated.
- Consumers migrated: `ReportService.AgingAsync` (sale/purchase allocation queries),
  `Views/Payments/Details.cshtml` (renders both typed collections with invoice numbers
  and Arabic type labels).
- `CreatePurchaseCoreAsync` reordered: totals/validation → supplier quotes → paid/items →
  `Add` + `SaveChanges` (invoice inserted first) → `ApplyStockAsync(..., docId: invoice.Id)`
  → `ReplenishFifoLayersAsync`. Purchase stock movements now carry `DocumentId`.

Migration: `20260924142033_AddTypedPaymentAllocations` — drops `PaymentAllocations` and
creates the two typed tables with FKs + unique indexes. **Note: dropping the table means
existing allocation rows are lost on upgrade** (schema-only rebuild; re-derivable from
`PaidAmount` if needed). `Down()` rebuilds the old polymorphic table. An earlier
`migrations add` produced an empty migration because `dotnet ef` defaults to Debug while
the build was Release — regenerated with the current model.

## Tests added (+6)

- `FinancialIntegrityTests.SaleReturn_WithTax_PostsValueAndTaxReversal` — delivered taxed
  invoice, 1-qty return → `5101 Dr 95` + `2055 Dr 5` = `1200 Cr 100`, cost `1300 Dr 40` /
  `5000 Cr 40`, entry balances.
- `FinancialIntegrityTests.Purchase_PostsStockMovement_WithInvoiceDocumentId` — purchase
  movement has `DocumentId == invoice.Id` and the invoice number (N-04).
- `FinancialIntegrityTests.Adjustment_Delete_WithPostedOpeningJournal_IsRejected` —
  increase adjustment posts `OpeningStock`; delete rejected, stock stays at 25 (M-3 guard 1).
- `InventoryServiceTests.DeleteAdjustment_RestoresStockAndRemovesMovement` — 100→150 then
  delete → back to 100, record + all its movements gone (M-3 restore path).
- `InventoryServiceTests.DeleteAdjustment_WithLaterMovementOnSameItem_IsRejected` — later
  purchase blocks the delete; stock stays 160 (M-3 guard 2).
- `InventoryServiceTests.DeleteAdjustment_MissingRecord_ReturnsFalse` — missing id → clean
  error, no throw.

Existing `FinancialIntegrityTests.Delivery_Posts_ApportionedNet_NotGross_Revenue` was
updated for the tax split (95 / 90 / 5) and now seeds `2055` + `5101`.
All `PaymentAllocation`-typed call sites across `MilestoneM9Tests`, `AuditN15FxTests`,
`OperationsIntegrityTests`, and `FinancialIntegrityTests` migrated to the typed DbSets.

## Audit-driver status (from SECURITY_AUDIT_FinancialLogic_2026-09-23.md)

- **N-01** fixed this round (above).
- **N-02** fixed (receipt requires `DeliveryOrderStatus.Delivered`, `PaymentService.cs:180-183`).
- **N-03** fixed (`CreateSaleCoreAsync` gates `IsPeriodClosedAsync`).
- **N-04** fixed this round (above).
- **N-05** substantially closed by Rounds 12+14 — on-receipt purchases deliberately have no
  `Payment` row; cash-flow `Details` now foots to totals. Documented as design, not a defect.
- **N-06** FX-basis documentation item — allocations carry `ExchangeRateAtSettlement` and
  aging/`AgingAsync` use base-converted amounts; still a verify/document task, no code gap found.
- **M-1** fixed (sale + purchase reject negative qty/price/duplicate lines).
- **M-2** fixed in Round 13.
- **M-3** fixed this round (above).
- **M-4 / L-2** accepted; **L-3** cosmetic.

## Notes

- Remaining deferred across rounds: date culture/locale request-localization (exports and
  views already format `dd/MM/yyyy` explicitly / `InvariantCulture`).
- Untracked and intentionally never committed: this file, prior round docs, and
  `src/NewVixSmart.Web/SECURITY_AUDIT_FinancialLogic_2026-09-23.md`.
