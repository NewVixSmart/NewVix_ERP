# Audit Round 9 — Bootstrap/Seeding, Service-Layer Guards & API Surface

Date: 2026-09-24 · Baseline: 291/291 (Round-8 `37c33f4`) → Result: **302/302** passing, 0 warnings, `/healthz` 200.

Scope: unexplored domains — bootstrap/seeding robustness, service-layer validation bypass via batch/import, JWT API surface (antiforgery, pagination, DTO exposure).

---

## Closed this round

### R9-1 [HIGH] Seeder pair-guards can permanently skip accounts that postings depend on
`SeedData.cs` guarded account pairs as one block (`4400`+`8400`, `5101`+`5102`, and the whole default chart) by checking **one** code. A partially-seeded DB whose pivot code exists (but its partner does not) skipped the block forever, then posting threw at runtime for the missing partner:
- `AccountingService.cs:108` purchase returns → `5102`
- `AccountingService.cs:89-90, 96-97` FX settlement → `4400`/`8400`
- Default-chart `AnyAsync(1000)` guard skipped chart repair for any DB that already had `1000`

**Fix:** every seed account is now guarded per-code (fills gaps independently); the demo-data gate moved to a `hasChart` snapshot taken *before* the chart loop, preserving “fresh DB seeds demo once” semantics while repairing missing accounts on existing DBs.

**Tests:** `InitializeAsync_RepairsMissing5102_OnPartialSeed`, `InitializeAsync_RepairsMissing8400_OnPartialSeed`, `InitializeAsync_RepairsMissingChartAccount_WithoutDuplicatingDemoData`.

### R9-2 [HIGH] Demo-seed dedupe by Code collides with Round-7 unique Name indexes
Round-7 added unique indexes on `Items.Name`, `Items.Barcode`, `Suppliers.Name`, `Customers.Name`. Demo seeding deduped by **Code** only, so a DB with a same-name/different-code record crashed on startup with a unique-index violation.

**Fix:** demo suppliers/customers/items now skip when either Code **or** Name already exists.

**Test:** `InitializeAsync_SameNameDifferentCode_DemoSupplier_DoesNotCrash`.

### R9-3 [HIGH] Batch/import bypassed negative-value guards
`.Where(qty/count > 0)` silently *dropped* negative quantities instead of rejecting, and batch adjustment (`RunAdjustmentBatchAsync`) wrote negative stock directly via `CreateAdjustmentAsync` — none of the MVC/API annotations apply to batch/import callers.

**Fix (service layer):**
- `CreateAdjustmentAsync`: rejects `NewCount < 0 || NewQuantity < 0` (`لا يمكن أن يكون الرصيد بعد الجرد سالباً`). Write-off to zero remains allowed.
- `CreateSaleAsync` / `CreatePurchaseAsync`: explicitly reject negative `UnitPrice`, negative `Quantity`, or negative `Count` lines (returning an Arabic error) before the happy-path filter.

**Tests:** `CreateSale_NegativeUnitPrice_IsRejected`, `CreateSale_NegativeQuantity_IsRejected_Exactly`, `CreatePurchase_NegativeUnitPrice_IsRejected`, `CreateAdjustment_NegativeQuantity_IsRejected` (stock + doc count unchanged).

### R9-4 [MEDIUM] Global antiforgery filter broke the documented JWT API contract
`Program.cs:143-147` registers a global `AutoValidateAntiforgeryToken` that applies to **all** POST actions, including the `api/*` controllers, which authenticate via `[Authorize(JwtBearer)]` only — no cookie, so no CSRF vector, but every API POST (including `POST api/auth/token`) was being CSRF-blocked.

**Fix:** `[IgnoreAntiforgeryToken]` on all 8 API controllers. JWT-bearer remains the sole auth scheme, so no CSRF exposure is introduced.

**Tests:** `ApiControllerSecurityTests` — all 8 controllers carry the attribute, protected controllers authorize via JwtBearer only, token endpoint stays anonymous, all are `[ApiController]`.

### R9-5 [MEDIUM] Unbounded result sets when `page` is omitted
`GET api/items|customers|suppliers` returned the **entire** active result set when `page` was omitted (pagination was opt-in), a DoS/data-volume risk on the authenticated surface.

**Fix:** always paginate — `page ?? 1`, `Math.Clamp(pageSize ?? 100, 1, 500)`.

### R9-6 [MEDIUM] `GET/POST api/payments` serialized the full `Payment` entity
Included internal `DedupeKey` (dedup fingerprint), `CreatedBy`, and full navigation graphs. New `PaymentResponse` DTO in `Api/Dtos.cs` trims to presentation fields only; used for both list and create responses.

**Tests updated:** `ApiCreatePayment_ReceiptNumber_AssignedByService`, `ApiCreatePayment_WithCurrency_MapsAndSettles` now assert `ApiResponse<PaymentResponse>`.

> **Superseded (2026-09-27):** `ApiCreatePayment_WithCurrency_MapsAndSettles` was removed with
> the multi-currency feature; the settlement is covered by
> `PaymentServiceSingleCurrencyTests`. See [`DECISION-EGP-ONLY.md`](DECISION-EGP-ONLY.md).

---

## Deferred (with rationale)
- **GL opening-balance postings for AR/AP on party create** — feature-level accounting behavior, not a defect.
- **FX year-end revaluation** — product decision, needs scope agreement.
- **Branch-isolation (data RPM) for GL/budgets** — large design change, out of round scope.
- **API POST `api/payments` amount cap vs MVC** — MVC checks `99999999.99m`; API `ValidateCreatePayment` already does; parity confirmed, nothing to change.

---

## Artifacts
- `src/NewVixSmart.Web/Data/SeedData.cs`, `Services/InventoryService.cs`, `Api/*` (8 controllers), `Api/Dtos.cs`
- `tests/NewVixSmart.Web.Tests/*`: `ApiControllerSecurityTests.cs` (new), `BackupServiceTests.cs`, `InventoryServiceTests.cs`, `MilestoneM9Tests.cs`, `OperationsIntegrityTests.cs`
- No migrations required.