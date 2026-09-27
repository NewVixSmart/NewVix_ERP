# Round 17 — Cross-domain cleanup: CSP/a11y views, CI & secret hygiene, stale docs/e2e

Build: 0 warnings, 0 errors. Tests: 348/348 (unchanged — no accounting logic touched). App restart:
`/healthz` 200. Executed via three parallel agents (views / CI+secrets / docs+e2e) orchestrated centrally.

## R17-1 [LOW] frontend-audit A-03 residual — delete-confirm silent under CSP — FIXED

The app's CSP (`script-src 'self' 'nonce-...'`, no `'unsafe-inline'`) blocked the last remaining
inline `onsubmit` handler on `PurchaseRequests\Index.cshtml` — the delete confirmation dialog never
fired. Removed the inline attribute; bound a `submit` listener in a nonce'd `@section Scripts` block
(nonce via `@Context.GetCspNonce()`, mirroring `DeliveryOrders\Create.cshtml:88-101`). `rg` now shows
**zero** inline `on*=` attribute handlers anywhere in `Views`.

## R17-2 [LOW] frontend-audit a11y sweep — FIXED

- `scope="col"` on `Payments\Details.cshtml` (6 th), `Warehouses\Index.cshtml` (4 th, incl. the
  adjacent إجراءات header found on inspection); `scope="colgroup"` on `Reports\Dashboard.cshtml`
  section rows (cols 6/6/7).
- Second `<h1>` on Settings pages removed: `Printing.cshtml:20` and `Branding.cshtml:6` → `<h2 class="h3 mb-0">`
  (layout already provides the SR-only `<h1>`).
- `Home\Error.cshtml` `<h1>` had an empty accessible name (icon-only) → added "خطأ" text kept beside
  the `aria-hidden` icon.
- `rel="noopener noreferrer"` added to the 3 `target="_blank"` anchors missing it (Items/Purchases/Sales Details).
- Invalid `<span><h6>` heading nesting in `_Layout.cshtml` topbar → `<span class="mb-0">` (styles kept).

## R17-3 [MEDIUM] CI hardening — ci.yml

- Top-level `permissions: contents: read` (least privilege).
- NuGet cache (`actions/cache@v4`, key on `**/*.csproj`) added to `build-and-test` and
  `package-vulnerability`.
- EF snapshot gate: `dotnet tool install --global dotnet-ef` + `dotnet ef migrations has-pending-model-changes
  --project src/NewVixSmart.Web/NewVixSmart.Web.csproj ... --configuration Release` between Build and Test
  (closes I-01/M-03 "no automated snapshot↔model gate" — full suite still seeds via `EnsureCreated`, per
  the audit's "at minimum" recommendation).
- SHA-pinning of actions deferred: exact SHAs not derivable here (task documented for maintainer).

## R17-4 [MEDIUM] secret/deployment hygiene

- `docker-compose.yml`: removed deprecated `version:`.
- `.env.example`: now documents ALL compose-required vars (`SQL_SA_PASSWORD`, `JWT_KEY`,
  `Jwt__Key`, `SEED_ADMIN/ACCOUNTANT/WAREHOUSE_PASSWORD`) and every placeholder starts with
  `REPLACE_WITH_` so it can never pass the JWT production guard.
- `Program.cs` seed guard extended: Production also rejects any seed value containing
  `REPLACE_WITH` (ordinal, ignore case) — closing the hole where a copied `.env.example` placeholder
  would otherwise seed an admin password.
- `Api\TokensController.cs`: `[ResponseCache(NoStore = true, Location = None)]` on the JWT token
  endpoint (BREACH-class hardening). Global compression left intact (too broad to disable).
- `MapDefaultEndpoints()` NOT wired: it maps `/healthz` (collides with `app.MapHealthChecks("/healthz")`)
  and a `/liveness` that `AddServiceDefaults` doesn't tag — would duplicate a live endpoint and add a
  permanently unhealthy one (documented decision).
- `appsettings.json` shipped seed defaults `Admin@123/Acc@12345/War@12345` KEPT (removal would break
  local `dotnet run` seeding); they remain production-denied by the guard.
- `.gitignore`: added `docs/test-results/`, `*.trx`, `*.coverage`, `backups/`.
- `.dockerignore`: added `tests`, `e2e`, `design-system`.

## R17-5 [LOW] stale docs / e2e repair

- README: test count `201` → `348` in both places. Page counts left (route-count reconciliation pending).
- `DEPLOY-AZURE.md`: corrupted CJK line 1 → `# Deployment Guide — Aspire + Azure`; 2 placeholder
  passwords → `REPLACE_WITH_...`. (EN copy has same placeholders — flagged, untouched.)
- `plan/feature-p4b-budgets-accounts-1.md` and `plan/feature-multicurrency-branches-shipments-1.md`:
  `status: 'In progress'` → `'Shipped'`.
- `docs/test-results/`: deleted 5 stale/contradictory artifacts (`full-run.trx`, `sales-workflow.xml`,
  3× `GATE-*.trx`), added an ignored `README.md` (folder now in `.gitignore`; CI publishes authoritative trx).
- `Backend-Audit-Report-Round5.md`: F-L3/F-L4 self-referential "Open" rows → `Closed / documented`
  (then untracked per repo convention).
- `e2e/backup_full_cycle.cjs`: fixed `fileCount.comment` (always `undefined` → `fileCount`), fixed the
  `dryRun` ReferenceError (`DRY_RUN === '1'` short-circuit), renamed legacy `SILK_USER/PASS` → `VIX_USER/VIX_PASS`.
- `ACCESSIBILITY.md:24`: tag-set typo `wcag21a` → `wcag2a` and deduped the resulting duplicate; left
  count/policy text untouched.

## Remaining (documented/deferred)

- SHA-pinning of actions, mssql image tag pinning, web-container healthcheck (need exact external values —
  maintainer action).
- M-08 Aspire AppHost: fallback JWT secret + csproj TODO + exclusion from slnx/CI (deferred, needs human decision).
- J-01 move jQuery/bootstrap to `@section Scripts`; J-02 `dark-theme.css` conditional fetch (needs manual-dark
  toggle handling, user-personalization); J-03 PrintPreview iframe/`frame-ancestors` override review.
- A-01 residual: a11y gate counts only critical/serious as failure (policy); route-count discrepancies
  across README/ACCESSIBILITY/BUILD-PLAN (41 vs 46 vs gate-discovered ~47) — needs an official count owner.
- M-02: migration `20260831220000_InvoiceRowVersionAndDropUnitId` has no `.Designer.cs` (tooling-consistent
  only; does not affect `database update`/snapshot gate).
- Remaining `<h1>` per page outside scope confirmed fine at 1/page (Login/AccessDenied/Error/Print pages each
  have their own single h1).

## Evidence

- Build 0/0; tests 348/348; `/healthz` 200.
- Commits: `bbe6e26` (a11y/CSP), `e9817e7` (CI/secrets), `1f6634a` (docs/e2e), `984a5f0` (untrack audit report).