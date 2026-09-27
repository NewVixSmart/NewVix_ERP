# Round 18 — Remaining deferred items: PrintPreview/CSP verification, route-count reconciliation, Aspire AppHost, SHA/tag pinning

Build: slnx 0 warnings / 0 errors; AppHost standalone build 0 errors. Tests: **348/348** (unchanged).
App restart: `/healthz` 200. a11y gate re-run live: **GATE: PASS — 46 light routes + 11 dark, 0 critical/serious**.
Commit: `3607bc3`.

## R18-1 [LOW] J-03 PrintPreview iframe vs CSP — VERIFIED (no change needed)

Live header capture (Playwright, authenticated sessions, after login as admin):

| Response | Content-Security-Policy | X-Frame-Options |
|---|---|---|
| `/Settings/PrintPreview?group=...` (iframe child) | `frame-ancestors 'self'` | `SAMEORIGIN` |
| Every ordinary page (e.g. `/Settings`) | `frame-ancestors 'none'` | `DENY` |

The global middleware pins `frame-ancestors 'none'` (`Program.cs:315`), and `SettingsController.PrintPreview`
(`SettingsController.cs:548-560`) deliberately removes that header and rewrites it to `frame-ancestors 'self'`
+ `SAMEORIGIN` so the preview `<iframe>` on `Settings/Printing.cshtml:349` can embed it. Only that action is
exempt; clickjacking protection remains everywhere else. Closed as verified; documented as the intended override.

## R18-2 [LOW] A-01 route-count reconciliation — FIXED (docs)

Canonical count established by running the gate (`node e2e/a11y-gate.cjs`): **46 light routes + 11 dark**.
- `README.md:35` `41 صفحة` → `46 مسارًا` (the stale 41 came from the old P4c-era suite).
- `ACCESSIBILITY.md:7` already said 46 — confirmed accurate; left untouched.
- Historical reports (AUDIT-DEEP/HARD, pulled backup reports, BUILD-PLAN P4c log, frontend-audit) keep their
  dated counts as history — not rewritten.
- Gate policy note stands: critical/serious only counts as failure; 46 is a live, gate-derived number.

## R18-3 [MEDIUM] M-08 Aspire AppHost — FIXED (buildable + secret-safe), kept out of CI

Pre-existing defects found when trying to build the AppHost standalone:
1. **It could not compile standalone at all** (`CS8805` top-level statements need `OutputType=Exe`; `CS0234`
   `Projects.NewVixSmart_Web` requires solution membership). The documented `dotnet run --project aspire/Vix.AppHost`
   path in `DEPLOY-AZURE.md` was therefore broken.
2. **Package pin was non-existent**: `Aspire.Hosting.AppHost 10.0.0` does not exist → floated to `13.0.0`
   (NU1603 ×2). Pinned to the actually-resolved **`13.0.0`** and removed the now-false GA-packages TODO.
3. **Dev-only fallback JWT secret** (`DEVELOPMENT_ONLY_JwtSecret_ChangeMe_...`) could silently seed an
   environment if the operator forgot `Jwt__Key`. Replaced with a `REPLACE_WITH_...` placeholder that the web's
   production guard now rejects (Program.cs `REPLACE_WITH` check added in Round 17) — dev Aspire runs work,
   prod misconfigs fail loudly.
4. Replaced `AddProject<Projects.NewVixSmart_Web>(...)` with the solution-independent
   `AddProject("webfrontend", "../../src/NewVixSmart.Web/NewVixSmart.Web.csproj")` (path resolved relative to the
   AppHost dir) so the AppHost builds and runs without joining the slnx.

**Kept out of `NewVixSmart.slnx` and CI deliberately** (decision, not oversight): pulling Aspire.Hosting into
every build/CI would surface **transitive MessagePack 2.5.192 CVEs** (NU1902/NU1903 — incl. HIGH GHSA-hv8m,
GHSA-vh6j) in the `package-vulnerability` gate and bloat every pipeline run. AppHost is an orchestration tool
excluded from the production/smoke path (the web image runs directly under Docker/App Service). Recommendation:
revisit when a patched MessagePack/Aspire line lands; do not enable CodeQL/dependabot on the aspire folder
without first overriding the MessagePack transitive.

Verification: `dotnet build aspire/Vix.AppHost/Vix.AppHost.csproj -c Release` → **0 errors**.

## R18-4 [MEDIUM] SHA pinning of CI actions + SQL Server image tag

- All five action refs pinned to today's verified commit SHAs (GitHub API, `refs/tags/v4` deref):
  checkout `11d5960a…`, setup-dotnet `67a3573c…`, cache `0057852b…`, upload-artifact `ea165f8d…`,
  setup-node `49933ea5…` (9 `uses:` lines total).
- SQL Server pinned from the moving `2022-latest` to **`2022-CU26-ubuntu-22.04`** (CU26 = latest CU,
  released 2026-07-16) in `docker-compose.yml:3`, the CI `docker-image` service (`ci.yml:93`), and the live
  `docs/DEPLOY-AZURE.md` compose table. English guide's `YourStr0ng!Password` placeholders were aligned to
  `REPLACE_WITH_...` too (`7e0fa57`).
- `e2e/package-lock.json` now committed (npm ci-reproducible Playwright/axe-core pins).

## R18-5 [MEDIUM] Saved-page weight: jQuery scoped to form pages (J-01) — CLOSED

jQuery is now loaded **only** by `_ValidationScriptsPartial` (used by the 14 form views that need
client-side validation), removed from the global layout script stack. Verified (live Playwright):
- Light/system pages: `bootstrap.bundle` + `site.js` still global; **no** `jquery.min.js` tag on `/Home`
  (~87 KB saved on the ~100 non-form pages).
- `Customers/Create` (partial page): jquery + validate + unobtrusive present, bootstrap still present.
- **The deferred J-01 risk check (ModelState re-render)**: forced server POST of an empty form
  (`form.submit()`, bypasses client validation) → `200` re-render with validation errors shown,
  `main#mainContent` intact, and jquery reloaded after re-render. No console/page errors.
- Deliberate deviation from the original fix note: `bootstrap.bundle` stays global because `data-bs-*`
  components (alerts, modals, dropdowns, collapse, toasts) are used app-wide; only jQuery was
  truly form-scoped. That is the cost the finding was about.

## R18-6 [LOW] J-02 dark-theme.css fetched conditionally — CLOSED

`dark-theme.css` (989 lines) is no longer an unconditional `<link>`. The head inline theme script (already
nonce'd, runs pre-paint) injects `link#dark-theme-css` only when the resolved theme is dark, and
`site.js applyThemeMode()` injects/removes it on every button toggle **and** on OS-level
`prefers-color-scheme` change. Verified live:
- OS light + system mode: dark-theme.css **not fetched** (no link tag).
- Manual dark while OS light: injected + `data-theme="dark"`.
- Back to light: link removed. Zero console/page errors.
- Trade-off: a document-first-paint flash is possible for dark users (link loads via JS); acceptable for
  the ~9 KB css file vs unconditional fetch on every page.

## R18-7 [LOW] M-02 missing migration Designer — CLOSED

Reconstructed `Migrations/20260831220000_InvoiceRowVersionAndDropUnitId.Designer.cs` (1370 lines) by
recurrence: started from the immediate-next migration's Designer (`20260902104741`) and reversed its
`Up()` (removed `PurchaseInvoices.OrderReference`, entire `SupplierQuotes` entity + its FK/PK/indexes).
Verified against the previous sibling (`20260831071841`): delta equals exactly `20260831220000`'s own
`Up()` (RowVersion on Sale/PurchaseInvoices added, `Items.UnitId` + its index/FK removed). The attributes
`[Migration]`/`[DbContext]` remain in the `.cs` half (the only migration where they live there) — the
combined partial class is attribute-equivalent to its siblings, duplicate attributes would break EF
discovery (`AmbiguousMatchException`). Build: 0 errors; tests 348/348. No migration IDs renamed.

## R18-8 [MEDIUM] MessagePack CVE pin in Aspire AppHost — CLOSED

Real ancestor identified: `StreamJsonRpc 2.22.23` (under `Aspire.Hosting.* 13.0.0`) depends on
MessagePack with a **bare minimum** constraint `>= 2.5.192` (no exact pin), so a direct override is clean.
Added `<PackageReference Include="MessagePack" Version="2.5.301" />` (first patched v2 for CVE-2026-48109 /
GHSA-hv8m-jj95-wg3x; v3 fix would be a breaking 3.1.7). AppHost standalone build now reports
**0 warnings / 0 errors** — all NU1902/NU1903 vulnerability warnings cleared. Residual risk noted:
untested DCP/dashboard runtime RPC under the bumped version (would appear as DCP connection error, not a
build failure); AppHost remains out of CI/deployment.

## Remaining (documented / accepted)

- **Accepted-by-decision, no work:** M-4 sequential public IDs (internal tool), L-2 `CountAsync()+1` numbers
  (unique-index safe), H-3 residual unlocked reads, gate policy (moderate/minor not fatal).
- **Cosmetic/notes, no work:** no `healthcheck` inside compose `web` service (Dockerfile HEALTHCHECK + CI
  wait-loop cover), `AllowedHosts:"*"` dev setting, historical reports retain dated counts (R18-2).

## Evidence

- `dotnet build NewVixSmart.slnx -c Release` 0W/0E; AppHost 0E; tests 348/348; `/healthz` 200.
- `GATE: PASS (46 light routes + 11 dark, 0 critical/serious)`.
- Commit `3607bc3` (6 files: ci.yml, docker-compose.yml, README.md, AppHost Program.cs + csproj, e2e/package-lock.json).