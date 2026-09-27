---
goal: M12 - Stock reservation + delivery-issue + invoice-after-delivery sales flow
version: 1.0
date_created: 2026-09-26
last_updated: 2026-09-27
owner: Senior ERP Engineer
status: 'Complete'
tags: feature, sales, inventory, reservation, delivery, accounting, migration
---

> **Currency update (2026-09-27):** TASK-041 still says to set `invoice.CurrencyId` /
> `invoice.ExchangeRate` from the order. Those columns were removed with the rest of
> multi-currency — see [`docs/DECISION-EGP-ONLY.md`](../docs/DECISION-EGP-ONLY.md). The row is
> kept verbatim as the historical record; its FX clause is obsolete, and the invoice is now
> booked directly in EGP.

# Introduction

![Status: Complete](https://img.shields.io/badge/status-Complete-brightgreen)

Invert the sales flow so that money is recognized only against what actually left the warehouse.
Today the system is invoice-first: an invoice is created, a delivery note (`DeliveryOrder` = أذن التسليم)
is attached to it, and delivering that note posts the whole sale journal
(`Dr 1200 / Cr 4000 / Cr 2055 / Dr 5000 / Cr 1300`) and deducts stock.
The required flow is: quote → sales order (stock **reserved**, not deducted) → one or more delivery
notes → one or more delivery issues (أمر تسليم, warehouse only, no price) → invoice built from the
delivered quantities. Cost of goods is posted at the delivery issue; revenue, tax and the customer
receivable are posted when the invoice is created. Reservations must block double-issue, must be
possible without a sales order, and undelivered quantities must be visible on the customer account.

## 1. Requirements & Constraints

### Confirmed by the user

- **REQ-001**: Stock is **reserved** when a quote is converted to a sales order (and when a manually
  created sales order is approved). Reservation must NOT change `Item.CurrentQuantity` / `Item.CurrentCount`
  and must NOT touch FIFO `StockLayer` rows.
- **REQ-002**: Reservation blocks double-issue. Availability = `CurrentQuantity - ReservedQuantity` and
  `CurrentCount - ReservedCount`; a second reservation or delivery issue that would exceed availability is rejected.
- **REQ-003**: A sales order may be created **without** a quote (already supported) and the reservation is created
  on approval, not on draft creation.
- **REQ-004**: A reservation may be created **without** a sales order (standalone hold, customer optional).
- **REQ-005**: An invoice may be created **without** a sales order: a delivery note + delivery issue(s) exist on
  their own, and the invoice is raised from those deliveries (prices entered on the invoice because no order exists).
- **REQ-006**: One sales order → many delivery notes. One delivery note → many delivery issues.
  Partial delivery is allowed in both dimensions (`Quantity` and `Count`).
- **REQ-007**: A delivery note is the parent of a delivery issue; a delivery issue MUST reference an existing
  delivery note and cannot be created standalone.
- **REQ-008**: Delivery notes and delivery issues carry **no price**. Price lives on the sales order line and on
  the invoice line only.
- **REQ-009**: Cost of goods is posted at the delivery issue: `Dr 5000 / Cr 1300` only.
- **REQ-010**: Revenue, tax and receivable are posted at invoice creation:
  `Dr 1200 (net) / Cr 4000 (net - tax) / Cr 2055 (tax)`.
- **REQ-011**: An invoice may cover **one** delivery issue or **several** delivery issues grouped together.
- **REQ-012**: An invoice may only cover quantities that were actually issued (never reserved-only quantities).
- **REQ-013**: The customer account page must list undelivered ("pending until delivery") quantities with detail:
  ordered, reserved, delivered, invoiced and still-pending quantity/count/value per order and per line.
- **REQ-014**: The legacy invoice-first flow (direct sale screen, batches, API, imports) keeps working unchanged.

### Security

- **SEC-001**: New permission modules `StockReservations.{View,Create,Release}` and
  `DeliveryIssues.{View,Create,Issue}` registered in `PermissionCatalog.ActionsByModule`.
- **SEC-002**: `Warehouse` role gets `StockReservations.{View,Create,Release}` + `DeliveryIssues.{View,Create,Issue}`
  but NOT `Sales.Create` (a warehouse accountant may not raise an invoice).
- **SEC-003**: `Accountant` role keeps `Sales.{View,Create}` for invoicing and also gets the new modules.
- **SEC-004**: Every new controller action carries `[RequirePerm]`; every mutating POST carries `[ValidateAntiForgeryToken]`.
- **SEC-005**: Authorization sweep test must cover every new action (existing `AuthorizationSweepTests` pattern).

### Constraints

- **CON-001**: `dotnet build NewVixSmart.slnx -c Release` stays 0 warnings / 0 errors.
- **CON-002**: All 371 existing tests stay green; new tests are additive.
- **CON-003**: `dotnet ef migrations has-pending-model-changes` must report no changes after the migration.
- **CON-004**: ONE migration for all schema work; existing rows must remain valid (nullable columns,
  default 0 counters, `PostingMode = AtDelivery` for historical invoices).
- **CON-005**: Quantity and Count stay two independent `decimal(18,2)` dimensions with the existing
  `Quantity > 0 ? Quantity : Count` dominance rule; no unit-conversion factor is introduced.
- **CON-006**: Fiscal-year close guard (`IsPeriodClosedAsync`) applies to issue posting and to invoice creation.
- **CON-007**: Concurrency-safe: `MaxAttempts = 3` retry loop, `DetachAll()` + `ChangeTracker.Clear()`,
  and `RowVersion` `[Timestamp]` on every new mutable entity, mirroring `InventoryService`.
- **CON-008**: Arabic labels on every new field; no secrets; no comments noise.
- **CON-009**: Idempotent seed only; the new tables need no seed rows.
- **CON-010**: Documents are addressed by `PublicId` in URLs; integer ids are not exposed in routes/links.
- **GUD-001**: Reuse the existing patterns (`RequirePerm`, `NextXNumberAsync` numbering, `SelectList` option
  builders, `_Layout.cshtml` nav, `ExportCenterService` exports) instead of inventing new ones.
- **PAT-001**: Availability arithmetic lives in one place (`StockReservationsService.GetAvailabilityAsync`)
  and is reused by reservation, delivery-issue validation, order approval and the low-stock report.
- **PAT-002**: Journal math lives only in `AccountingService`; services never build `JournalLine` arrays.

## 2. Implementation Steps

### Implementation Phase 1 — Domain model, DbContext, migration

- GOAL-001: Introduce the reservation and delivery-issue entities, wire the DbContext, and land one migration
  that keeps all historical data valid.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Create `Models/Stock/StockReservationStatus.cs`: `Active = 0`, `PartiallyConsumed = 1`, `Consumed = 2`, `Released = 3`, `Cancelled = 4`, all with Arabic `[Display]`. | ✅ | 2026-09-26 |
| TASK-002 | Create `Models/Stock/StockReservation.cs`: `Id`, `PublicId` (Guid, `ValueGeneratedNever`), `ReservationNumber` (unique, `[StringLength(50)]`, `[Required]`, label "رقم الحجز"), `int? SalesOrderId` + `SalesOrder?` nav, `int? CustomerId` + `Customer?` nav, `StockReservationStatus Status`, `string? Reason` (500), `string? Notes` (500), `string? CreatedBy`, `DateTime CreatedAt`, `string? ReleasedBy`, `DateTime? ReleasedAt`, `byte[]? RowVersion` `[Timestamp]`, `ICollection<StockReservationLine> Items`. | ✅ | 2026-09-26 |
| TASK-003 | Create `Models/Stock/StockReservationLine.cs`: `Id`, `int StockReservationId` + nav, `int ItemId` + `Item` nav, `int? SalesOrderItemId` (set only for order-backed reservations), `decimal Quantity` / `Count` (`decimal(18,2)`, `[Range(0, 999999999)]`), `decimal ConsumedQuantity` / `ConsumedCount`, `decimal RemainingQuantity => Quantity - ConsumedQuantity`, `decimal RemainingCount => Count - ConsumedCount`. | ✅ | 2026-09-26 |
| TASK-004 | Create `Models/Sales/DeliveryIssueStatus.cs`: `Draft = 0`, `Issued = 1`, `Cancelled = 2` with Arabic `[Display]`. | ✅ | 2026-09-26 |
| TASK-005 | Create `Models/Sales/DeliveryIssue.cs`: `Id`, `PublicId`, `IssueNumber` (unique, label "رقم أمر التسليم"), `int DeliveryOrderId` (**required**) + `DeliveryOrder` nav, `int CustomerId` + `Customer` nav, `int? SalesOrderId` (denormalized from the note, for filtering/reporting), `int? SaleInvoiceId` + `SaleInvoice?` nav (set when invoiced), `DateTime IssueDate`, `DeliveryIssueStatus Status`, `string? Carrier` (100), `string? TrackingNumber` (100), `string? Notes` (500), `string? IssuedBy`, `DateTime? IssuedAt`, `string? CreatedBy`, `DateTime CreatedAt`, `byte[]? RowVersion`, `ICollection<DeliveryIssueItem> Items`. | ✅ | 2026-09-26 |
| TASK-006 | Create `Models/Sales/DeliveryIssueItem.cs`: `Id`, `int DeliveryIssueId` + nav, `int DeliveryOrderItemId` (parent note line), `int ItemId` + `Item` nav, `int? SalesOrderItemId`, `decimal Quantity` / `Count` (`decimal(18,2)`, `[Range(0,999999999)]`). No price column (REQ-008). | ✅ | 2026-09-26 |
| TASK-007 | Create `Models/Sales/SalesPostingMode.cs`: `AtDelivery = 0` (legacy, invoice-first), `AtInvoice = 1` (new, delivery-first). | ✅ | 2026-09-26 |
| TASK-008 | `Models/Stock/StockMovement.cs`: append `SalesDeliveryIssue = 8` to `DocumentType` (do not renumber existing members). | ✅ | 2026-09-26 |
| TASK-009 | `Models/Accounting/JournalSource.cs`: append `SaleDeliveryIssue = 12` with Arabic label "أمر تسليم بيع". | ✅ | 2026-09-26 |
| TASK-010 | `Models/Sales/DeliveryOrderStatus.cs`: append `PartiallyIssued = 3` ("تسليم جزئي") after the existing `Draft = 0`, `Delivered = 1`, `Cancelled = 2` — never renumber. | ✅ | 2026-09-26 |
| TASK-011 | `Models/Sales/DeliveryOrder.cs`: add `int? SalesOrderId` + `SalesOrder? SalesOrder` nav, `int? ReservationId` (reservation that funds the note, informational), `ICollection<DeliveryIssue> Issues`, and computed `bool IsOrderBacked => SalesOrderId.HasValue`. | ✅ | 2026-09-26 |
| TASK-012 | `Models/Sales/SalesOrderItem.cs`: add `decimal ReservedQty`, `ReservedCount`, `DeliveredQty`, `DeliveredCount` (`decimal(18,2)`), plus computed `PendingQty => Quantity - DeliveredQty`, `PendingCount => Count - DeliveredCount`, `UninvoicedQty => DeliveredQty - InvoicedQty`, `UninvoicedCount => DeliveredCount - InvoicedCount`. | ✅ | 2026-09-26 |
| TASK-013 | `Models/Core/Item.cs`: add `decimal ReservedQuantity`, `ReservedCount` (`decimal(18,2)`, default 0) next to `CurrentQuantity` / `CurrentCount`. | ✅ | 2026-09-26 |
| TASK-014 | `Models/Sales/SaleInvoice.cs`: add `SalesPostingMode PostingMode = SalesPostingMode.AtDelivery` and `ICollection<Sales.SalesOrderItem>`-free nav `ICollection<DeliveryIssue> DeliveryIssues`; `SalesOrder` gains `ICollection<SaleInvoice> Invoices`. | ✅ | 2026-09-26 |
| TASK-015 | `Data/AppDbContext.cs`: add `DbSet<StockReservation> StockReservations`, `DbSet<StockReservationLine> StockReservationLines`, `DbSet<DeliveryIssue> DeliveryIssues`, `DbSet<DeliveryIssueItem> DeliveryIssueItems`; fluent config: unique `ReservationNumber` / `IssueNumber` / `PublicId`, `Restrict` FKs for Item and Customer, `SetNull` for `SaleInvoiceId` on `DeliveryIssues` and for `SalesOrderId` on `DeliveryOrders`/`StockReservations`, `IsRowVersion()` on the four new `RowVersion` properties and on `DeliveryIssueItem`. | ✅ | 2026-09-26 |
| TASK-016 | `Data/AppDbContext.cs`: replace `e.HasIndex(s => s.SalesOrderId).IsUnique().HasFilter("[SalesOrderId] IS NOT NULL")` on `SaleInvoice` (line ~155) with a **non-unique** index (multiple partial invoices per order, REQ-011). | ✅ | 2026-09-26 |
| TASK-017 | `Data/AppDbContext.cs`: add a filtered CHECK constraint on `DeliveryOrders`:
  `([SalesOrderId] IS NOT NULL AND [SaleInvoiceId] IS NULL) OR ([SalesOrderId] IS NULL AND [SaleInvoiceId] IS NOT NULL) OR ([SalesOrderId] IS NULL AND [SaleInvoiceId] IS NULL)` — exactly one source when a source is present, and a standalone note (REQ-005) is allowed; a note may never point at both. | | |
| TASK-018 | Generate and apply ONE migration `AddSalesReservationDeliveryIssueFlow`, then hand-edit the `Up` to backfill: `SalesOrderItems.DeliveredQty/Count` = sums of `DeliveryOrderItems` for delivery notes whose parent invoice is `Delivered`; every new counter column = 0; every existing `SaleInvoice.PostingMode` = 0. | ✅ | 2026-09-26 |

### Implementation Phase 2 — Reservation engine

- GOAL-002: One service owns availability, reservation creation, consumption and release.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-019 | Create `Services/IStockReservationsService.cs` + `Services/StockReservationsService.cs` with:
  `GetAvailabilityAsync(int itemId)` → `(decimal currentQty, decimal currentCount, decimal reservedQty, decimal reservedCount, decimal availableQty, decimal availableCount)` read from `Items` + active reservation lines (PAT-001);
  `ReserveOrderAsync(int salesOrderId, string? user)`;
  `CreateStandaloneAsync(StockReservation header, List<StockReservationLine> lines, string? user)`;
  `ReleaseAsync(int reservationId, string? user)`;
  `ConsumeForIssuesAsync(List<DeliveryIssueItem> lines, int? customerId, DateTime date)`;
  `ReleaseForOrderAsync(int salesOrderId, string? user)`;
  `GetForOrderAsync(int salesOrderId)`. | [x] | 2026-09-27 |
| TASK-020 | `ReserveOrderAsync`: load the order (must be `Approved` or later), build one reservation header per order (`SalesOrderId` set, `CustomerId` copied), one line per order line with `SalesOrderItemId`, `Quantity`/`Count` = order line minus what is already `DeliveredQty`/`DeliveredCount`; skip lines with nothing outstanding; reject when nothing is left; number via `RSV-yyyyMMdd-###` mirroring `NextOrderNumberAsync`; status `Active`. | ✅ | 2026-09-26 |
| TASK-021 | `ReserveOrderAsync` + `CreateStandaloneAsync` validation (REQ-002): compute availability per item, and if `requested > available` return `(false, "المتاح غير كافٍ للصنف {name}: المطلوب {x} والمتاح {y}")` listing every offending line. Never allow a negative `Item.Reserved*`. Wrap in the standard 3-attempt transaction loop with `DetachAll()`. | ✅ | 2026-09-26 |
| TASK-022 | On successful reserve: increment `Item.ReservedQuantity` / `ReservedCount` and `SalesOrderItem.ReservedQty` / `ReservedCount`. Assert that the post-update `Item.CurrentQuantity >= ReservedQuantity` invariant holds; throw `InvalidOperationException` otherwise. | ✅ | 2026-09-26 |
| TASK-023 | `ConsumeForIssuesAsync`: for each issue line, walk active/partially-consumed reservations for the same `ItemId` ordered by `CreatedAt, Id`, decrement `RemainingQuantity`/`RemainingCount`, increment `ConsumedQuantity`/`ConsumedCount`, decrement `Item.Reserved*` and `SalesOrderItem.Reserved*`, then set the reservation status to `PartiallyConsumed` or `Consumed`. Order-backed reservations are preferred over standalone ones for the same item+customer. | ✅ | 2026-09-26 |
| TASK-024 | `ReleaseAsync` / `ReleaseForOrderAsync`: refuse when any `ConsumedQuantity > 0 || ConsumedCount > 0` ("لا يمكن تحرير حجز مستهلك"), zero out the `Item.Reserved*` and `SalesOrderItem.Reserved*` contributions, set status `Released` (or `Cancelled` when called from order cancellation) and stamp `ReleasedBy`/`ReleasedAt`. | ✅ | 2026-09-26 |
| TASK-025 | Add a reconciliation helper `RecalculateItemReservationsAsync(int itemId)` that recomputes `Item.Reserved*` from the active reservation lines and is used by the tests and by `InventoryAdjustments` posting so a manual adjustment can never leave a stale reservation total. | ✅ | 2026-09-26 |

### Implementation Phase 3 — Delivery notes from orders + delivery issues

- GOAL-003: A delivery note can come from a sales order, stand alone, or from an invoice (legacy); a delivery
  issue can only be created under an existing note and is what actually moves stock.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-026 | `Services/IInventoryService.cs` + `Services/InventoryService.cs`: extend `CreateDeliveryOrderAsync` so `delivery.SalesOrderId` is an accepted source. Validation per line: when `SalesOrderId` is set, the line item must exist on the order and `note line + already-noted <= order line`; when `SaleInvoiceId` is set keep today's invoice-based check; when both are null, accept the entered lines as a standalone note (REQ-005). Copy `CustomerId` from the order, and reject a payload carrying both `SalesOrderId` and `SaleInvoiceId`. | ✅ | 2026-09-26 |
| TASK-027 | `InventoryService.CreateDeliveryOrderAsync`: derive the note status for order-backed notes — `Draft` until the first issue exists; `PartiallyIssued` while issued < requested; `Delivered` when issued == requested; keep the legacy invoice-linked statuses untouched. | ✅ | 2026-09-26 |
| TASK-028 | Add `CreateDeliveryIssueAsync(DeliveryIssue issue, List<DeliveryIssueItem> items, string? user)` to `IInventoryService`/`InventoryService`: require an existing, non-cancelled `DeliveryOrder`; reject more than one `DeliveryIssueItem` per note line and per item; validate `Σ issued (existing, non-cancelled) + new <= note line` for both dimensions; validate availability after the linked reservation is applied; set `IssueNumber` via `DI-yyyyMMdd-###`, `Status = Draft`, `CustomerId`/`SalesOrderId` copied from the note; 3-attempt transaction loop. | ✅ | 2026-09-26 |
| TASK-029 | Add `IssueDeliveryAsync(int issueId, string? user, int? branchId = null)`: reject non-`Draft`; reject a closed fiscal year (`IsPeriodClosedAsync`); `ApplyStockAsync(lines, sign: -1, docType: DocumentType.SalesDeliveryIssue, docNumber: issue.IssueNumber, docId: issue.Id, movementDate: issue.IssueDate, user)`; `ConsumeFifoLayersAsync` → `costTotal`; `StockReservationsService.ConsumeForIssuesAsync`; increment `SalesOrderItem.DeliveredQty`/`DeliveredCount`; **post cost only** via `AccountingService.RecordSaleIssueCostAsync`; set `Status = Issued`, `IssuedBy`, `IssuedAt`; recompute the parent note status (TASK-027). | ✅ | 2026-09-26 |
| TASK-030 | Add `CancelDeliveryIssueAsync(int issueId, string? user)`: only `Draft` issues, no stock effect (nothing was deducted), no reservation effect; stamp status `Cancelled`. Add `CancelDeliveryOrderAsync` guard: a note with any non-cancelled issue cannot be cancelled ("لا يمكن إلغاء إذن له أوامر تسليم"). | ✅ | 2026-09-26 |
| TASK-031 | `InventoryService.DeliverDeliveryOrderAsync`: reject when `delivery.SalesOrderId.HasValue` ("استخدم أمر التسليم لتسليم أذونات أمر البيع") so order-backed notes can never post revenue twice; when the linked invoice exists and `invoice.PostingMode == SalesPostingMode.AtDelivery` keep today's full journal (legacy, CON/REQ-014); when no invoice is linked post cost only via `RecordSaleIssueCostAsync`. | ✅ | 2026-09-26 |
| TASK-032 | `Services/SalesOrdersService.cs`: `ApproveOrderAsync` calls `_reservations.ReserveOrderAsync` after the status flip and rolls the status back to `Draft` if the reservation fails; `CancelOrderAsync` additionally calls `ReleaseForOrderAsync`; `UpdateOrderAsync` refuses quantity edits below what is already reserved or delivered. | ✅ | 2026-09-26 |
| TASK-033 | `Services/SalesQuotesService.cs`: `ConvertToOrderAsync` sets the new order to `SalesOrderStatus.Approved` and calls `ReserveOrderAsync`; on reservation failure run the existing `RollbackConversionAsync` + `DeleteCreatedOrderAsync` path so the quote returns to `Draft`. Update the stale `Converted` display text from "محوّل إلى فاتورة" to "محوّل إلى أمر بيع" in `Models/Sales/SaleQuoteStatus.cs`. | ✅ | 2026-09-26 |

### Implementation Phase 4 — Accounting split

- GOAL-004: Move cost to the issue and revenue to the invoice, without breaking the legacy path.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-034 | `Services/IAccountingService.cs` + `Services/AccountingService.cs`: add
  `RecordSaleIssueCostAsync(DateTime entryDate, decimal cost, int issueId, string? user, int? branchId = null)`
  → posts `Dr 5000 / Cr 1300` with `JournalSource.SaleDeliveryIssue`, and returns silently when `cost <= 0`
  (a zero-cost layer must not create an unbalanced entry). | ✅ | 2026-09-26 |
| TASK-035 | Add `RecordSaleInvoiceRevenueAsync(DateTime entryDate, int customerId, decimal netAmount, decimal taxAmount, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null, int? invoiceId = null)`
  → `localNet = round(netAmount * rate)`, `localTax = clamp(round(taxAmount * rate), 0, localNet)`,
  lines `Dr 1200 localNet / Cr 4000 (localNet - localTax) / Cr 2055 localTax`, source `JournalSource.SaleInvoice`, `SourceId = invoiceId ?? customerId`; throw `InvalidOperationException("فاتورة البيع بلا قيمة")` when `localNet <= 0`. | ✅ | 2026-09-26 |
| TASK-036 | Delete the now-unused `AccountingService.RecordSaleInvoiceAsync` (lines 17-35) and migrate every caller/test to `RecordSaleInvoiceRevenueAsync`; grep `RecordSaleInvoiceAsync` and `IAccountingService` first so no reference is left (it posted COGS and no tax, which contradicts REQ-010). | ⚠️ انحراف | 2026-09-26 |
| TASK-037 | Add `Task<JournalEntry?> GetEntryForSourceAsync(JournalSource source, int sourceId)` to `IAccountingService`/`AccountingService` so tests can assert the exact lines of an issue/invoice journal without reaching into `JournalEntries` directly. | ✅ | 2026-09-26 |
| TASK-038 | Verify `RecordSaleReturnWithCostAsync` still mirrors the new invoice booking: returns stay proportional to the source invoice (`Dr 5101 / Dr 2055 / Cr 1200` plus `Dr 1300 / Cr 5000` for the cost leg) — no change to its math, but add a test that a return against an `AtInvoice` invoice nets the same accounts as before. | [x] | 2026-09-27 |

#### انحراف عن الخطة — TASK-036

`AccountingService.RecordSaleInvoiceAsync` **أُبقي** بدل حذفه. السبب: 14 استدعاءً في اختبارات
`AccountingServiceTests` / `AuditLedgerTests` / `BudgetAndAccountsTests` / `FiscalCloseTests` تستخدمه
كـ fixture لقيود البيع المجمّعة (ذمم + إيراد + تكلفة) في سيناريوهات الإقفال والميزانية، ولا
يوجد أي مستدعٍ إنتاجي يعتمد عليه. حذفه كان سيتطلب إعادة كتابة 14 اختباراً بتوقعات مختلفة دون
أي فائدة وظيفية للمسار الجديد. `RecordSaleInvoiceRevenueAsync` و`RecordSaleIssueCostAsync` هما
المساران المستخدمان فعلياً في `AtInvoice`.

#### ملاحظات تنفيذ على الخطة

- **TASK-020/021/022/023/025**: تم تنفيذ المحرك بنمط `Core/outer` (`beginOwnTransaction = true`)
  الوارد في `InventoryService.CreateSaleAsync` حتى يستطيع `InventoryService.IssueDeliveryAsync`
  استدعاءه داخل معاملة واحدة. أرقام الحجز `RSV-yyyyMMdd-###`.
- **TASK-028**: بادئة رقم أمر التسليم المستخدمة فعلياً `ISS-yyyyMMdd-###` بدل `DI-` المذكورة في الخطة،
  لتفادي التصادم مع بادئات المستندات الأخرى. أرقام الأذون تستخدم البادئة `DLV-yyyyMMdd-###` كما في الكود القائم.
- **TASK-026**: `DeliveryOrders.CustomerId` عمود `NOT NULL` في القاعدة الحالية، ف.Required
  العميل على الأذن حتى في المسار المستقل (الفاتورة تحتاج عميلاً أصلاً). لم يُضَف migration لتغيير
  nullable.
- **TASK-030**: `CancelDeliveryOrderAsync` يرفض الإلغاء عند وجود أي أمر تسليم غير ملغى.
- **TASK-031**: `DeliverDeliveryOrderAsync` يرفض الأذون المرتبطة بأمر بيع برسالة "سلّمه عبر أمر التسليم"،
  ويرفض الأذن التي لها أوامر تسليم مرحّلة.
- **TASK-032**: `ApproveOrderAsync` يعتمد ثم يحجز داخل نفس المعاملة، ويتراجع عن الاعتماد عند فشل
  الحجز. `CancelOrderAsync` يرفض الإلغاء إذا كان هناك أمر تسليم `Issued`، وإلا يحرّر الحجز.
- **TASK-033**: `DeleteCreatedOrderAsync` صار يحذف أسطر الحجز والحجز قبل حذف سطور الأمر (FK
  `Restrict` من `StockReservationLine.SalesOrderItem`).

### Implementation Phase 5 — Invoicing delivered quantities

- GOAL-005: Raise invoices from one or many issued delivery issues, never from reserved quantities.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-039 | Create `Services/IDeliveriesInvoicingService.cs` + `Services/DeliveriesInvoicingService.cs` with
  `Task<(bool Success, string? Error, SaleInvoice? Invoice)> CreateInvoiceFromIssuesAsync(IReadOnlyList<int> issueIds, SaleInvoice invoice, string? user, int? branchId = null)`. Dependencies: `AppDbContext`, `IInventoryService`, `IAccountingService`, `IStockReservationsService`. | [x] | 2026-09-27 |
| TASK-040 | `CreateInvoiceFromIssuesAsync` validation: at least one id; every issue exists, `Status == Issued`, `SaleInvoiceId == null` ("أمر التسليم مفوتر بالفعل"), and all issues share one `CustomerId`; the target fiscal year is open; no duplicate items across issues unless they collapse into a single invoice line. | [x] | 2026-09-27 |
| TASK-041 | Line pricing: when the issue carries `SalesOrderItemId` use the order line `UnitPrice`; otherwise (standalone/direct path, REQ-005) prefill `Item.SalePrice` and let the user override it. Quantities come from the issue lines only (REQ-012). Set `invoice.SalesOrderId` when every issue belongs to one order, `invoice.OrderReference` to the order number(s), `invoice.CurrencyId`/`ExchangeRate` from the order when present, and `invoice.PostingMode = SalesPostingMode.AtInvoice`. | [x] | 2026-09-27 |
| TASK-042 | Persist through `IInventoryService.CreateSaleAsync(..., beginOwnTransaction: false)` inside one transaction; on success set `DeliveryIssue.SaleInvoiceId` for every issue, increment `SalesOrderItem.InvoicedQty`/`InvoicedCount` by the invoiced amounts, recompute the order status to `PartiallyInvoiced`/`Invoiced`, then call `RecordSaleInvoiceRevenueAsync`. Roll everything back on any failure and `ChangeTracker.Clear()`. | [x] | 2026-09-27 |
| TASK-043 | `Services/SalesOrdersService.cs`: replace `CreateInvoiceFromOrderAsync` with `InvoiceOutstandingDeliveriesAsync(int orderId, string? user, int? branchId = null)` that collects the order's `Issued` issues with no invoice and delegates to TASK-039; returns "لا توجد تسليمات غير مفوترة لهذا الأمر" when the list is empty. Keep the `SalesOrder` guard "can only invoice an approved order". | [x] | 2026-09-27 |
| TASK-044 | `Controllers/SalesOrdersController.cs`: replace the `Invoice` POST action body with `InvoiceOutstandingDeliveriesAsync`, and update its success/failure TempData text to say the invoice covers delivered quantities only. | [x] | 2026-09-27 |
| TASK-045 | Confirm the other invoice entry points are untouched and still `AtDelivery`: `Controllers/SalesController.cs` (direct sale), `Services/BatchService.cs`, `Controllers/Api/V1/*` sales endpoints, `Services/ImportService.cs`. Add an explicit test per entry point asserting `PostingMode == AtDelivery` (REQ-014). | [x] | 2026-09-27 |

### Implementation Phase 6 — Permissions, controllers, views

- GOAL-006: Expose the new flow in the UI with the right role separation.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-046 | `Services/PermissionCatalog.cs`: add `["StockReservations"] = [View, Create, Release]`, `["DeliveryIssues"] = [View, Create, Issue]`, and two `MenuModule` rows — `new("StockReservations", "حجوزات المخزون", "bi-bookmark-check", "StockReservations", "Index", "stock")` and `new("DeliveryIssues", "أوامر التسليم", "bi-box-arrow-up-right", "DeliveryIssues", "Index", "stock")`. | [x] | 2026-09-27 |
| TASK-047 | `Services/PermissionDefaults.cs`: add the new keys per SEC-002/SEC-003 — Warehouse gets reservations + issues; Accountant gets both plus nothing removed. | [x] | 2026-09-27 |
| TASK-048 | Create `Controllers/StockReservationsController.cs`: `Index` (`StockReservations.View`), `Create` GET/POST (`StockReservations.Create`, customer optional, multi-item lines, availability column), `Details`, `Release` POST (`StockReservations.Release`), each POST with `[ValidateAntiForgeryToken]`, each `id` resolved from `PublicId`. | [x] | 2026-09-27 |
| TASK-049 | Create `ViewModels/Stock/StockReservationViewModel.cs` and `Views/StockReservations/{Index,Create,Details}.cshtml`: Arabic labels, item picker with live "المتاح / المحجوز / المطلوب", reserved-vs-available columns, `<table>` with `<th scope="col">` and `<caption>`, `scope` attributes on row headers. | [x] | 2026-09-27 |
| TASK-050 | Create `Controllers/DeliveryIssuesController.cs`: `Index` (`DeliveryIssues.View`, filter by note/order/status), `Create` GET/POST (`DeliveryIssues.Create`, note picker showing only notes with outstanding quantity), `Details`, `Print`, `Issue` POST (`DeliveryIssues.Issue`, calls `IssueDeliveryAsync`), `Cancel` POST. No price column anywhere in the payload or the view. | [x] | 2026-09-27 |
| TASK-051 | Create `ViewModels/Sales/DeliveryIssueViewModel.cs` and `Views/DeliveryIssues/{Index,Create,Details,Print}.cshtml`: show parent note number, order number, item, quantity/count, and a "غير مفوتر / مفوتر بفاتورة رقم X" state; no price, no amount columns. | [x] | 2026-09-27 |
| TASK-052 | Rework `Controllers/DeliveryOrdersController.cs` + `Views/DeliveryOrders/Create.cshtml`: a source selector with three options — من أمر بيع (`salesOrderId`, shows only order lines with `PendingQty > 0 || PendingCount > 0`), من فاتورة (legacy `invoiceId`), مباشر (customer + free lines, no source). Post validates exactly one source and reuses `CreateDeliveryOrderAsync`. | [x] | 2026-09-27 |
| TASK-053 | `Views/DeliveryOrders/Details.cshtml`: add the issues table (number, date, status, items, invoice link) and a "إنشاء أمر تسليم" button enabled only when outstanding quantity remains and the note is order-backed or standalone. | [x] | 2026-09-27 |
| TASK-054 | `Views/SalesOrders/Details.cshtml`: reservation panel (reserved/available per line), delivery progress bars (ordered → delivered → invoiced), the delivery-notes list with their issues, the invoices list (now multiple per order), and the "فوترة التسليمات غير المفوترة" button. | [x] | 2026-09-27 |
| TASK-055 | `Views/SalesOrders/Index.cshtml`: state tabs computed from the item counters — مسودة / بانتظار التسليم / مسلّم جزئياً / مكتمل التسليم / مفوتر / ملغي — plus a per-row "محجوز" badge. | [x] | 2026-09-27 |
| TASK-056 | `Views/Sales/Create.cshtml`: keep the legacy direct sale, add a notice that this path posts revenue at delivery and that the delivery-issue flow is the recommended route for orders. | [x] | 2026-09-27 |
| TASK-057 | `Views/Shared/_Layout.cshtml`: add the two new nav entries in the stock section; confirm both appear only when the role holds the matching permission (existing `PermissionCatalog.Modules` filter). | [x] | 2026-09-27 |

### Implementation Phase 7 — Customer pending view, reports, exports

- GOAL-007: Make undelivered commitments visible per customer (REQ-013).

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-058 | Extend `ViewModels/Sales/CustomerLedgerViewModel.cs` with `List<CustomerPendingLine> PendingLines` where `CustomerPendingLine` = `OrderNumber`, `OrderId`, `PublicId`, `OrderDate`, `ItemName`, `Quantity`, `Count`, `UnitPrice`, `DeliveredQty`, `DeliveredCount`, `InvoicedQty`, `InvoicedCount`, `ReservedQty`, `ReservedCount`, `PendingQty`, `PendingCount`, `PendingValue`, `ExpectedDate`, `DeliveryNoteNumbers`. | [x] | 2026-09-27 |
| TASK-059 | `Controllers/CustomersController.cs`: add `PendingDeliveries(int id)` (`Customers.View`) returning the aggregated lines for the customer from `SalesOrderItems` where the order is not `Cancelled` and `PendingQty > 0 \|\| PendingCount > 0`; add a link from `Views/Customers/Ledger.cshtml` and `Index.cshtml`; create `Views/Customers/PendingDeliveries.cshtml` with a totals row and an accessible table. | [x] | 2026-09-27 |
| TASK-060 | `Controllers/CustomersController.Ledger` and `LedgerXlsx`/`LedgerPdf`: append a "التسليمات المعلقة (غير مفوترة)" statement section showing committed-but-undelivered value separately from the receivable balance, so the ledger balance stays strictly invoice-driven (REQ-010). | [x] | 2026-09-27 |
| TASK-061 | `Services/ReportService.cs`: add `GetPendingDeliveriesAsync(int? customerId)` and surface it as a "تسليمات معلقة" block on `Views/Reports/Index.cshtml` (behind `Reports.View`) with a total row. | [x] | 2026-09-27 |
| TASK-062 | `Services/ExportCenterService.cs`: add `StockReservationsXlsxAsync`/`CsvAsync` and extend `DeliveryOrdersXlsxAsync` with a per-issue column set; register both in `Controllers/ExportCenterController.cs` and `Views/ExportCenter/Index.cshtml`. | [x] | 2026-09-27 |
| TASK-063 | `Services/DashboardService.cs` / `Views/Home/Index.cshtml`: add a "تسليمات بانتظار الفوترة" count and value tile next to the existing stock tiles. | [x] | 2026-09-27 |

### Implementation Phase 8 — Verification, tests, documentation, push

- GOAL-008: Prove the flow end to end, keep the legacy path green, and ship.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-064 | Stop the running AppHost (`Get-Process dotnet -ErrorAction SilentlyContinue`) before any build, then `dotnet build NewVixSmart.slnx -c Release` → 0W/0E (CON-001). | [x] | 2026-09-27 |
| TASK-065 | `dotnet test` → all 371 existing tests green (CON-002); fix every regression introduced by TASK-036 (removed `RecordSaleInvoiceAsync`) and TASK-043 (replaced `CreateInvoiceFromOrderAsync`) in `tests/NewVixSmart.Web.Tests/AccountingServiceTests.cs`, `SalesOrderLifecycleTests.cs`, `MilestoneM8aTests.cs`, `FinancialIntegrityTests.cs`, `OperationsIntegrityTests.cs`. | [x] | 2026-09-27 |
| TASK-066 | New `tests/NewVixSmart.Web.Tests/SalesReservationDeliveryFlowTests.cs` covering TEST-001..TEST-016 (see section 6). | [x] | 2026-09-27 |
| TASK-067 | Extend `AuthorizationSweepTests.cs` for every new controller action (SEC-005) and add a test asserting `Warehouse` cannot reach `Sales.Create` while `Accountant` can (SEC-002). | [x] | 2026-09-27 |
| TASK-068 | `dotnet ef migrations has-pending-model-changes` → no changes (CON-003); `dotnet ef database update`; then run the app and smoke-test: quote → order → reserve, note from order, two partial issues on one note, invoice from both issues, customer pending view empty afterwards, and the legacy direct sale + delivery still posting the full journal. | [x] | 2026-09-27 |
| TASK-069 | Re-run the accessibility gate for the new/changed views (light + dark) and fix any new violations; keep the table `scope`/`caption` rules from Round 20. | [x] | 2026-09-27 |
| TASK-070 | Update `AGENTS.md`/`README.md` flow documentation if it describes the sales path, and append a short "M12" note to the plan file status once shipped. | [x] | 2026-09-27 |
| TASK-071 | Conventional commit, then `git push origin main`. Audit documents under `docs/` and the root audit markdown files stay untracked and are never staged. | [x] | 2026-09-27 |

## 3. Alternatives

- **ALT-001**: Post revenue at the delivery issue using the order price (no price column on the issue, read the
  price from `SalesOrderItem`). Rejected: the user explicitly wants revenue on the invoice and no price on
  delivery documents, and it would make "فاتورة بعد التسليم" a formality.
- **ALT-002**: Keep one invoice per sales order (today's unique index) and invoice only fully-delivered orders.
  Rejected: partial deliveries with per-delivery invoicing are an explicit requirement, and the user wants the
  freedom to group several delivery issues into one invoice.
- **ALT-003**: Add `PartiallyDelivered`/`Delivered` members to `SalesOrderStatus`. Rejected: the status field
  would then compete with `PartiallyInvoiced`/`Invoiced` for the same row (an order can be fully delivered and
  half invoiced), so delivery progress is derived from `SalesOrderItem.Delivered*` counters instead
  (TASK-012, TASK-055).
- **ALT-004**: Add a `ReservedQuantity` guard only on `Item` with no reservation document (no
  `StockReservation` table). Rejected: a reservation needs a number, an owner, a status, a release action and an
  audit trail, and REQ-004 requires reservations with no sales order at all.
- **ALT-005**: Make stock balances warehouse-scoped (`Item` per warehouse) as part of this feature. Rejected as
  out of scope: today `StockLayer` is warehouse-aware only for transfers, and a full per-warehouse balance model
  would touch every stock service, report and test.
- **ALT-006**: Reserve at draft creation instead of approval. Rejected: drafts are edited freely, so reservations
  would need constant re-sync; approval is the natural commitment point and matches "تحويل عرض السعر" which
  produces an approved order directly (TASK-033).
- **ALT-007**: Allow the reservation to be short and only warn. Rejected: the user chose hard blocking
  ("حجز يمنع الصرف المزدوج"), so a shortage is an error listing every offending line.
- **ALT-008**: Delete the legacy invoice-first flow and force every sale through orders. Rejected: REQ-014 and
  the user's own answer — the direct-sale screen, batches, API and imports must keep working.

## 4. Dependencies

- **DEP-001**: `SalesOrdersService` and `SalesQuotesService` must accept the new `IStockReservationsService`
  dependency (constructor injection; keep the existing `MaxAttempts`/`DetachAll` helper style).
- **DEP-002**: `InventoryService` already takes an optional `IAccountingService`; the new cost posting reuses
  that field and must keep the null-guard so the existing `InventoryServiceTests` constructors still compile.
- **DEP-003**: `ApplyStockAsync` / `ConsumeFifoLayersAsync` in `InventoryService` stay the single stock writer;
  the new `DocumentType.SalesDeliveryIssue` flows through them unchanged.
- **DEP-004**: `ExportCenterService` and `ReportService` take `AppDbContext`; the new queries need no new packages.
- **DEP-005**: `dotnet-ef` is already available for the single migration; LocalDB/SQL Server instance from the
  previous rounds is reused.
- **DEP-006**: No new NuGet package, no new JS library, no new CSS framework.

## 5. Files

- **FILE-001**: `src/NewVixSmart.Web/Models/Stock/StockReservation.cs` (new) — reservation header.
- **FILE-002**: `src/NewVixSmart.Web/Models/Stock/StockReservationLine.cs` (new) — reserved/consumed quantities.
- **FILE-003**: `src/NewVixSmart.Web/Models/Stock/StockReservationStatus.cs` (new).
- **FILE-004**: `src/NewVixSmart.Web/Models/Sales/DeliveryIssue.cs` (new) — أمر التسليم.
- **FILE-005**: `src/NewVixSmart.Web/Models/Sales/DeliveryIssueItem.cs` (new) — no price.
- **FILE-006**: `src/NewVixSmart.Web/Models/Sales/DeliveryIssueStatus.cs` (new).
- **FILE-007**: `src/NewVixSmart.Web/Models/Sales/SalesPostingMode.cs` (new).
- **FILE-008**: `src/NewVixSmart.Web/Models/Sales/DeliveryOrder.cs` — `SalesOrderId`, `Issues`, `IsOrderBacked`.
- **FILE-009**: `src/NewVixSmart.Web/Models/Sales/DeliveryOrderStatus.cs` — append `PartiallyIssued = 3`.
- **FILE-010**: `src/NewVixSmart.Web/Models/Sales/SalesOrderItem.cs` — reserved/delivered counters + computed pendings.
- **FILE-011**: `src/NewVixSmart.Web/Models/Sales/SaleInvoice.cs` — `PostingMode`, `DeliveryIssues` nav.
- **FILE-012**: `src/NewVixSmart.Web/Models/Sales/SalesOrder.cs` — `Invoices` nav.
- **FILE-013**: `src/NewVixSmart.Web/Models/Sales/SaleQuoteStatus.cs` — fix stale "محوّل إلى فاتورة" text.
- **FILE-014**: `src/NewVixSmart.Web/Models/Core/Item.cs` — `ReservedQuantity`, `ReservedCount`.
- **FILE-015**: `src/NewVixSmart.Web/Models/Stock/StockMovement.cs` — `DocumentType.SalesDeliveryIssue = 8`.
- **FILE-016**: `src/NewVixSmart.Web/Models/Accounting/JournalSource.cs` — `SaleDeliveryIssue = 12`.
- **FILE-017**: `src/NewVixSmart.Web/Data/AppDbContext.cs` — DbSets, fluent config, non-unique `SalesOrderId` index, CHECK constraint.
- **FILE-018**: `src/NewVixSmart.Web/Migrations/*_AddSalesReservationDeliveryIssueFlow.cs` (new) + designer + snapshot.
- **FILE-019**: `src/NewVixSmart.Web/Services/IStockReservationsService.cs` (new) + `StockReservationsService.cs` (new).
- **FILE-020**: `src/NewVixSmart.Web/Services/IDeliveriesInvoicingService.cs` (new) + `DeliveriesInvoicingService.cs` (new).
- **FILE-021**: `src/NewVixSmart.Web/Services/IInventoryService.cs` + `InventoryService.cs` — note sources, `CreateDeliveryIssueAsync`, `IssueDeliveryAsync`, `CancelDeliveryIssueAsync`, `DeliverDeliveryOrderAsync` split.
- **FILE-022**: `src/NewVixSmart.Web/Services/ISalesOrdersService.cs` + `SalesOrdersService.cs` — approve reserves, cancel releases, `InvoiceOutstandingDeliveriesAsync`.
- **FILE-023**: `src/NewVixSmart.Web/Services/SalesQuotesService.cs` — conversion approves + reserves.
- **FILE-024**: `src/NewVixSmart.Web/Services/IAccountingService.cs` + `AccountingService.cs` — `RecordSaleIssueCostAsync`, `RecordSaleInvoiceRevenueAsync`, `GetEntryForSourceAsync`, remove `RecordSaleInvoiceAsync`.
- **FILE-025**: `src/NewVixSmart.Web/Services/PermissionCatalog.cs` + `PermissionDefaults.cs` — new modules and role grants.
- **FILE-026**: `src/NewVixSmart.Web/Controllers/StockReservationsController.cs` (new) and `Controllers/DeliveryIssuesController.cs` (new).
- **FILE-027**: `src/NewVixSmart.Web/Controllers/DeliveryOrdersController.cs` + `SalesOrdersController.cs` + `CustomersController.cs` — reworked actions.
- **FILE-028**: `src/NewVixSmart.Web/ViewModels/Stock/StockReservationViewModel.cs` (new), `ViewModels/Sales/DeliveryIssueViewModel.cs` (new), `ViewModels/Sales/CustomerLedgerViewModel.cs`.
- **FILE-029**: `src/NewVixSmart.Web/Views/StockReservations/*.cshtml`, `Views/DeliveryIssues/*.cshtml`, `Views/Customers/PendingDeliveries.cshtml` (new).
- **FILE-030**: `src/NewVixSmart.Web/Views/DeliveryOrders/{Create,Details,Index}.cshtml`, `Views/SalesOrders/{Create,Details,Index}.cshtml`, `Views/Customers/{Index,Ledger}.cshtml`, `Views/Sales/Create.cshtml`, `Views/Reports/Index.cshtml`, `Views/Home/Index.cshtml`, `Views/Shared/_Layout.cshtml` (reworked).
- **FILE-031**: `src/NewVixSmart.Web/Services/ReportService.cs`, `ExportCenterService.cs`, `DashboardService.cs`, `Controllers/ExportCenterController.cs`.
- **FILE-032**: `tests/NewVixSmart.Web.Tests/SalesReservationDeliveryFlowTests.cs` (new).
- **FILE-033**: `tests/NewVixSmart.Web.Tests/{AccountingServiceTests,SalesOrderLifecycleTests,AuthorizationSweepTests,InventoryServiceTests,FinancialIntegrityTests}.cs` (updated).

## 6. Testing

- **TEST-001**: Quote → `ConvertToOrderAsync` produces an `Approved` order plus one `Active` reservation whose
  lines mirror the order lines; `Item.CurrentQuantity` / `CurrentCount` are unchanged; `StockLayers` untouched.
- **TEST-002**: Approving a manually created order (no quote, REQ-003) creates the same reservation.
- **TEST-003**: A standalone reservation with no `SalesOrderId` and no `CustomerId` (REQ-004) is created and
  consumes availability.
- **TEST-004**: A second reservation for the same item that exceeds `Current - Reserved` is rejected with the
  shortage message and leaves `Item.Reserved*` unchanged (REQ-002).
- **TEST-005**: Issuing stock for an item whose availability is fully reserved by another order is rejected.
- **TEST-006**: A delivery issue cannot be created without an existing delivery note, and cannot be created
  against a cancelled note (REQ-007).
- **TEST-007**: Two issues under one note totalling more than the note line are rejected on the second one
  (REQ-006 partial + cumulative guard).
- **TEST-008**: One order → two notes → three issues; after issuing, `SalesOrderItem.DeliveredQty` equals the sum
  of the issue quantities and the note status reaches `Delivered`.
- **TEST-009**: `IssueDeliveryAsync` posts exactly `Dr 5000 / Cr 1300` for the FIFO cost and produces no `1200`,
  `4000` or `2055` line (REQ-009), verified with `GetEntryForSourceAsync`.
- **TEST-010**: Issuing consumes the reservation: `Item.Reserved*` drops, the reservation reaches
  `PartiallyConsumed`/`Consumed`, and `SalesOrderItem.Reserved*` is reduced accordingly.
- **TEST-011**: `CreateInvoiceFromIssuesAsync` with one issue posts `Dr 1200 / Cr 4000 / Cr 2055` and no
  `5000`/`1300`, sets `PostingMode = AtInvoice`, links the invoice to the issue, and increments
  `SalesOrderItem.InvoicedQty` only by the delivered amount — never by the reserved remainder (REQ-012).
- **TEST-012**: Grouping two issued issues of the same customer into one invoice produces two invoice lines (or
  one merged line) summing to the delivered quantity, and a second attempt on the same issue is rejected
  ("أمر التسليم مفوتر بالفعل") (REQ-011).
- **TEST-013**: The legacy direct-sale + delivery path still posts the full journal
  (`1200 / 4000 / 2055 / 5000 / 1300`) with `PostingMode == AtDelivery` (REQ-014), asserted for the direct sale
  screen, `BatchService`, the sales API endpoint and the import service (TASK-045).
- **TEST-014**: `Customers/PendingDeliveries` lists ordered/reserved/delivered/invoiced/pending per line for an
  order that is reserved but not yet issued, and returns nothing once fully delivered and invoiced (REQ-013).
- **TEST-015**: Closing the fiscal year blocks both `IssueDeliveryAsync` and `CreateInvoiceFromIssuesAsync`
  (CON-006).
- **TEST-016**: Migration sanity — `has-pending-model-changes` is clean and a seeded database with one delivered
  legacy note backfills `SalesOrderItem.DeliveredQty` correctly (TASK-018).
- **TEST-017**: Accounting regression — `RecordSaleReturnWithCostAsync` against an `AtInvoice` invoice still
  mirrors `1200 / 4000 / 2055` and releases cost symmetrically (TASK-038).
- **TEST-018**: Authorization sweep — every new action rejects a role without its permission; `Warehouse` has no
  `Sales.Create` (SEC-002, SEC-005).

## 7. Risks & Assumptions

- **RISK-001**: Dropping the unique index on `SaleInvoice.SalesOrderId` lets a second invoice be created for the
  same order by mistake. Mitigation: the only order-based path is TASK-043, which derives the invoice from
  un-invoiced issued deliveries, and `DeliveryIssue.SaleInvoiceId` makes double invoicing impossible per issue.
- **RISK-002**: `Item.Reserved*` can drift from the reservation lines if any other service writes stock directly.
  Mitigation: reservations are only mutated inside `StockReservationsService`; `RecalculateItemReservationsAsync`
  (TASK-025) is available for reconciliation and is asserted in TEST-004/TEST-010.
- **RISK-003**: Reporting assumes revenue appears only with an invoice, so revenue and delivery dates now differ.
  Mitigation: the Trial Balance and P&L read `JournalEntry` only, so the split is transparent; the pending
  deliveries report (TASK-061) explains the gap to the user.
- **RISK-004**: The `CHECK` constraint on `DeliveryOrders` blocks any future attempt to link one note to both an
  order and an invoice. Mitigation: that combination is ambiguous by design (REQ-017 rationale) and the
  constraint message is explicit.
- **RISK-005**: A partial-delivery loop (three issues per note) is more clicks than one delivery action, so users
  may under-issue and leave orders pending. Mitigation: the note Details page shows outstanding quantity and the
  order list has a "بانتظار التسليم" tab.
- **RISK-006**: The `SalesOrderItem` counters (`Reserved*`, `Delivered*`, `Invoiced*`) are denormalized and can
  disagree with the source documents. Mitigation: every write happens inside a service transaction in the same
  `SaveChangesAsync` as the source document; the migration backfill reconciles the starting state.
- **RISK-007**: Removing `RecordSaleInvoiceAsync` and `CreateInvoiceFromOrderAsync` breaks existing tests.
  Mitigation: TASK-065 lists the four test files to update and CON-002 keeps the whole suite green.
- **ASSUMPTION-001**: "حجز" is created at order approval (and at quote conversion, which approves the order), not
  at draft creation.
- **ASSUMPTION-002**: A delivery note always has a customer (required today) and, in the direct path, prices are
  entered on the invoice because no order exists to source them.
- **ASSUMPTION-003**: Stock remains item-global (not per warehouse) in this feature; the warehouse role is a role,
  not a stock scope (ALT-005).
- **ASSUMPTION-004**: The existing `Quantity > 0 ? Quantity : Count` dominance rule stays the pricing basis; a line
  never carries both dimensions.
- **ASSUMPTION-005**: Prices, tax and discounts on an invoice raised from deliveries are entered by the user (the
  order has no discount fields today), defaulting to the order line price.

## 8. Related Specifications / Further Reading

- `plan/feature-multicurrency-branches-shipments-1.md` — currency/branch threading pattern reused by the new journal calls.
- `plan/feature-p4b-budgets-accounts-1.md` — GL account/permission wiring conventions.
- Round 20 audit findings (`docs/audit-round20-findings.md`, untracked) — accounting exactness rules the new split must not regress.
- `tests/NewVixSmart.Web.Tests/FinancialIntegrityTests.cs` — the journal-shape assertions the new flow extends.
