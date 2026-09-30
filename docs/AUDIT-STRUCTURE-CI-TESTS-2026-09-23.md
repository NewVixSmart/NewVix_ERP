# Audit: Structure, Architecture, Infrastructure, CI/CD, Tests — NewVixSmart

- **Date**: 2026-09-23
- **Scope**: structure, architecture, infrastructure, CI/CD, and test-suite audit. **Research-only** — read-only; no build, test, SQL, or container runs performed. Live app at http://localhost:5165 untouched.
- **Baseline verified**: the 12-item prior baseline (round-4 table); statuses in the section below.
- **Cross-reference artifacts in repo (uncommitted)**: `Backend-Audit-Report-Round5.md`, `docs/frontend-audit.md`, `docs/AUDIT-DEEP-2026-09-12.md`, `docs/AUDIT-HARD-2026-09-12.md`, `Deep-Audit-Report.md`.

---

## 1. Summary

| Severity | Count | Notes |
|---|---|---|
| **Critical** | 0 | No confirmed exploitable or data-integrity issue in scope |
| **High** | 2 | H-01 docker-compose production web cannot start; H-02 vulnerability CI gate is a false gate |
| **Medium** | 8 | M-01 CI actions hygiene; M-02 missing migration `.Designer.cs`; M-03 tests bypass migrations; M-04 contradictory test artifacts; M-05 unignored `docs/test-results/`; M-06 `backups/` unignored; M-07 placeholder secrets pass prod guard; M-08 orphaned Aspire AppHost |
| **Low** | 8 | L-01 dead `MapDefaultEndpoints`; L-02 stale README counts; L-03 corrupted `DEPLOY-AZURE.md` + placeholder secret; L-04 stale plan statuses; L-05 `Program.cs:232` indent; L-06 BREACH/BREACH-class compression note; L-07 dockerignore build-context bloat; L-08 leftover `Silk.*` rebrand artifacts |
| **Info** | 2 | I-01 snapshot ↔ models consistent in spot-check, but no automated gate; I-02 compose `version: "3.9"` + floating image tags |
| **Strengths** | 7 | See section 4 |
| **Total findings** | **20** | |

**Verdict**: A genuinely production-hardened app and CI with real SQL smoke and an axe accessibility gate; but two HIGH gaps (the *documented* `docker compose up` path cannot boot the web container, and the declared "fail on any CVE" job never fails) plus a batch of MED hygiene issues must be closed before externalizing. No confirmed critical data-integrity issue was found in this scope.

---

## 2. Findings

### High

**H-01 — `docker compose up -d` cannot start the web container (documented deployment path is broken)**
- **Files**: `docker-compose.yml:29-33`, `src/NewVixSmart.Web/Program.cs:346-353`, `src/NewVixSmart.Web/appsettings.json:9-13`
- **Issue**: The compose web service sets `ASPNETCORE_ENVIRONMENT: Production` but never sets `Seed__AdminPassword`. In Production the startup guard throws when `Seed:AdminPassword` is missing **or** one of `["Admin@123","Acc@12345","War@12345"]` — and `appsettings.json` ships `Seed:AdminPassword: "Admin@123"`. `docker inspect` will report `unhealthy`/restart-loop; `README.md:66` instructs `docker compose up -d` as the deployment path.
- **Evidence**: CI's `docker-image` job only passes because it injects `Seed__AdminPassword="Vix-CiAdmin-Passw0rd-2026!"` explicitly (`.github/workflows/ci.yml:91`). Compose has no equivalent; `.env.example` documents `SQL_SA_PASSWORD` and `JWT_KEY` only, never a seed password.
- **Fix**: In compose web environment add `Seed__AdminPassword: "${SEED_ADMIN_PASSWORD:?...}"`; document it in `.env.example`. Better: remove the shipped defaults from `appsettings.json` so the guard's default list stops being self-contradictory.

**H-02 — CI vulnerability job claims "fail on any CVE" but always passes**
- **Files**: `.github/workflows/ci.yml:49-55`
- **Issue**: The `package-vulnerability` job runs `dotnet list NewVixSmart.slnx package --vulnerable --include-transitive` with no exit-code check. `dotnet list package` returns `0` even when vulnerable packages are reported, so the step (and the whole declared gate) can never fail.
- **Evidence**: Job description says "(fail on any CVE)" (`ci.yml:53`); command at `ci.yml:54` has no post-step that greps output.
- **Fix**: Run with `--format json` and fail when the JSON contains vulnerable nodes, or pipe through `rg -i "vulnerable"` and `exit 1` on match; wire the job into `build-and-test` as a required dependency.

### Medium

**M-01 — CI action hygiene: no `permissions:`, mutable-tag pins, no caching**
- **Files**: `.github/workflows/ci.yml:1-137`
- **Issue**: No top-level `permissions:` block; `actions/checkout@v4`, `setup-dotnet@v4`, `setup-node@v4`, `upload-artifact@v4` are pinned by mutable major tags, not commit SHAs; restore is duplicated across jobs; no NuGet cache.
- **Fix**: Add `permissions: contents: read`; SHA-pin actions with `with attrs`; cache `~/.nuget/packages` via `actions/cache@v4`.

**M-02 — EF migration tracked without its `.Designer.cs`**
- **Files**: `src/NewVixSmart.Web/Migrations/20260831220000_InvoiceRowVersionAndDropUnitId.cs` (no `*.Designer.cs`)
- **Issue**: 22 of 23 migrations have a `Migration.Designer.cs`; this one does not. EF Designer is normally used by tooling for scaffolding and previews; its absence is a convention break and a tripwire for future `dotnet ef migrations add`.
- **Fix**: Delete and re-add the migration with `dotnet ef migrations add` so the Designer file is regenerated (or regenerate in place).

**M-03 — Unit tests never execute EF migrations**
- **Files**: all 24 test classes (e.g., `AgingTests.cs:24`, `ConcurrencyTests.cs:23`, `BudgetAndAccountsTests.cs:21`) use `db.Database.EnsureCreated()` with in-memory SQLite; zero uses of `Database.Migrate()`.
- **Issue**: Migrations are not exercised by `dotnet test`; model↔snapshot drift (pending model changes) cannot fail CI. Mitigation exists only in the CI docker smoke job, which runs the app's startup `db.Database.Migrate()` against a real SQL Server (`Program.cs:359`) — so SQL-specific migration errors are caught there, but **only if** someone runs the full pipeline.
- **Evidence**: `rg -l "Database.Migrate" tests` → empty; `rg -c "EnsureCreated" tests` → 24 files × 1.
- **Fix**: Where feasible have tests seed via `database.Migrate()`; at minimum add a `dotnet ef migrations has-pending-model-changes` step (or a snapshot-consistency test) to `build-and-test`.

**M-04 — Contradictory test artifacts left in the tree; "241 green" claim unprovable locally**
- **Files**: `docs/test-results/full-run.trx` (2026-09-18 21:49) — executed 241, **passed 222, failed 19**; `docs/test-results/sales-workflow.xml` (22:05) — 241/241 passed; `docs/test-results/gate-2026-09-27.trx` — 239/239 passed.
- **Issue**: The 19-failure run names failures on live financial paths: `AuditN15FxTests.ForeignInvoice_Lifecycle_CreateThenSettle_AtHigherRate`, `InventoryServiceTests` (10, incl. `Sale_ConsumesStock`/`CreateSale_*`), `OperationsIntegrityTests` (4), `ReturnsFixtureTests` (2), `BatchOperationsTests` (2), `FiscalCloseTests.Guard_CreateSaleInClosedYear_ReturnsFalse`. A later run shows green, but with such a stale failing artifact still present, the README claim of parity cannot be certified without a fresh run (prohibited this audit).
- **Fix**: Delete stale trx files; make CI publish + retain the authoritative trx per PR; fix `.gitignore` (see M-05).

**M-05 — `docs/test-results/` (~1.7 MB trx/xml) is untracked and effectively unignorable-safe**
- **Files**: `.gitignore:13` (`TestResults/` is case-sensitive), `git status` shows `?? docs/test-results/`.
- **Issue**: The lowercase path slips past the ignore rule; every diff pollutes `git status` and risks accidental commits of large binary-ish trx files.
- **Fix**: Add `docs/test-results/` and/or `*.trx`, `*.coverage` to `.gitignore`.

**M-06 — `backups/` is absent from `.gitignore`**
- **Files**: `.gitignore`, `docs/BACKUP.md:13`, `scripts/backup-db.ps1` (default target `<repo>\backups`)
- **Issue**: The documented backup flow writes dumps into the repo folder; a scheduled dump would be committed accidentally (or committed at all, leaking financial data).
- **Fix**: Add `backups/` to `.gitignore` and mirror in `.dockerignore` context trimming where relevant.

**M-07 — `.env.example` placeholder secrets would pass the production guards**
- **Files**: `.env.example` (`JWT_KEY=Change-Me-Strong-Jwt-Secret-Min-32-Chars-2026`, length 48), `src/NewVixSmart.Web/Program.cs:44-48`
- **Issue**: The JWT guard rejects only empty, `<32` char, or containing `REPLACE_WITH`. The example value is ≥32 chars and does not contain `REPLACE_WITH`, so a user who follows the documented `cp .env.example .env` flow deploys with a well-known public signing key and SA password.
- **Fix**: Blacklist the documented placeholder values in the guard (or change the placeholder to start with `REPLACE_WITH`), and warn in `.env.example` that defaults must be rotated.

**M-08 — Orphaned Aspire AppHost with a prod-passing developer JWT fallback**
- **Files**: `aspire/Vix.AppHost/Program.cs:8` (fallback `DEVELOPMENT_ONLY_JwtSecret_ChangeMe_0123456789_ABCDEFGHIJKLMNOP`, 55 chars), `aspire/Vix.AppHost/Vix.AppHost.csproj:16` (TODO "once Aspire GA packages are published for .NET 10")
- **Issue**: AppHost is excluded from `NewVixSmart.slnx`, never built in CI, and has never been restored (no `obj/` exists while `Vix.ServiceDefaults/obj/project.assets.json` does). If someone launches it in Production, the 55-char fallback key **passes** the Jwt guard (M-07 same class).
- **Fix**: Pin Aspire for .NET 10 and add AppHost to slnx + CI, or delete it; make the fallback key fail the Production guard.

### Low

- **L-01 — Dead code**: `aspire/Vix.ServiceDefaults/Extensions.cs:39-49` `MapDefaultEndpoints` is never called; `Program.cs:342` maps `/healthz` itself. Use it or remove it.
- **L-02 — Stale documentation**: `README.md:5` and `README.md:43` claim "201 اختبارًا"; actual suite is 241 executed (per trx). README structure tree predates `Api/` and `Migrations/`.
- **L-03 — Deployment doc corruption**: `docs/DEPLOY-AZURE.md:1` title is corrupted mixed-CJK (`# Deployment Guide — Aspire + Azure / الت经验lightly — …`); sample `docker run` commands embed placeholder password `YourStr0ng!Password`.
- **L-04 — Feature statuses stale**: `plan/feature-p4b-budgets-accounts-1.md:7` and `plan/feature-multicurrency-branches-shipments-1.md:6` still `status: 'In progress'` although implementations (Budget, Multicurrency, Shipments, SalesOrders) are shipped and tested.
- **L-05 — Cosmetic**: `src/NewVixSmart.Web/Program.cs:232` has a 4-space-extra indent on the `SalesOrdersService` registration block.
- **L-06 — Compression + secrets**: brotli/gzip compression is enabled (`EnableForHttps=true`, `Program.cs:148-170`) and the JWT token endpoint `/api/tokens/create` returns the token in a compressed response — a BREACH-class informational risk. Add `[ResponseCache(NoStore=true)]`/disable compression on that endpoint.
- **L-07 — Build context bloat**: `.dockerignore` ignores `docs`, `plan`, `scripts`, `aspire/Vix.AppHost` but not `tests/`, `e2e/`, `design-system/`.
- **L-08 — Rebrand residue**: `Silk.*` project artifacts remain in `obj/`/`bin/` (e.g., `aspire/Vix.ServiceDefaults/obj`). Untracked and self-cleaning; cosmetic.

### Info

- **I-01 — Snapshot drift**: spot-check of `AppDbContextModelSnapshot.cs` (2962 lines) covers `SalesOrder/DeliveryOrder/SaleQuote` (~lines 2940-2961) and latest migration `20260918190622_AddSalesOrdersDeliveryOrders` exists; no drift found by inspection — but there is still no automated gate (see M-03).
- **I-02 — Compose/Tags**: `docker-compose.yml:1` uses deprecated `version: "3.9"`; images `mcr.microsoft.com/mssql/server:2022-latest`, `dotnet/sdk:10.0`, `aspnet:10.0` are floating (no digest pinning). Web service lacks its own healthcheck (relies on db health only).

---

## 3. Prior-baseline status (round-4 12-item table)

| # | Baseline item | Status | Evidence |
|---|---|---|---|
| 1 | Seed-guard for default admin passwords | **Partial** | Guard exists (`Program.cs:346-353`) but `appsettings.json:10` still ships `Admin@123`; guard only enforced in non-Development — Docker-production path ships it (→ H-01). |
| 2 | TRX `--results-directory`/path mismatch | **Fixed** | `ci.yml:29` `--results-directory TestResults --logger "trx;LogFileName=test-results.trx"`; upload glob `**/TestResults/*.trx` (`ci.yml:35-36`). |
| 3 | Arabic mojibake + indentation in AgingTests | **Fixed (mojibake) / Open (indent)** | File is valid UTF-8, no replacement chars (byte-verified); Arabic renders (`AgingTests.cs:33-34,87,138`). Indent anomaly remains at column 0, lines 33 and 109. |
| 4 | Dockerfile non-root + healthcheck | **Fixed** | `Dockerfile:36-38` chown+`USER 1654`; `HEALTHCHECK curl /healthz` (`:40-41`). |
| 5 | Missing HTTP-level integration tests | **Partial** | `AuthorizationSweepTests.cs` (reflection sweep + anonymous allowlist), `SecurityHardeningTests.cs`, `IdentityAndTokenTests.cs`, new `AuditN15FxTests.cs` (6 FX tests) added. Still **zero** `WebApplicationFactory`/`TestServer`/`HttpClient` in the suite → no real HTTP pipeline (JWT middleware, antiforgery, cookie-auth end-to-end) coverage. |
| 6 | Budget edit permission test + naming | **Fixed** | Test renamed to match behavior: `BudgetAndAccountsTests.cs:308` `BudgetClosedYear_EditAllowedAtDbLevel_GuardIsInView`. |
| 7 | RowVersion concurrency coverage | **Partial** | `ConcurrencyTests.cs` covers 2-min duplicate window + sequential payment numbers. `StaleContext_WritesSuccessful_OnSqlite` expressly documents that SQLite ignores RowVersion → SQL Server concurrency semantics still untested. |
| 8 | Corrupted `docs/DEPLOY-AZURE.md` | **Still open** | Line 1 mixed-CJK title. (→ L-03.) |
| 9 | Feature-plan statuses | **Still open** | Both files still `'In progress'`. (→ L-04.) |
| 10 | Root test-artifact pollution | **Fixed** | No root `test-out*.txt`/`tests-result.txt`; patterns gitignored (`gitignore:23-24`) and dockerignored. Artifacts now accumulate under `docs/test-results/` but unignored (→ M-05). |
| 11 | GitHub Actions a11y gate wired to CI | **Fixed** | `ci.yml:118-137` docker-image job runs `node a11y-gate.cjs` (axe, critical/serious) after Playwright Chromium install; `ACCESSIBILITY.md:22` states the gate. |
| 12 | AuthZ audit / RequirePerm coverage | **Verified** | 100+ `[RequirePerm(...)]` applications across 25+ controllers (e.g., `BudgetsController.cs:12,39…`, `FiscalController.cs:13…`, `DeliveryOrdersController.cs:26…`); `AuthorizationSweepTests.cs` enforces allowlist (public: `AccountController.Login/AccessDenied`, `HomeController.Error`, `TokensController.CreateToken`). |

Legend: **Fixed** = verified in current source with line evidence; **Partial** = some aspect open; **Open** = unchanged; **Verified** = audit re-confirmed without regression.

---

## 4. Strengths

1. **Real production hardening in `Program.cs`** — JWT signature-key guard, seed-password guard, per-IP rate limiting on token/login (`Program.cs:118-141`), HSTS, CSP-with-nonce + security headers (`:300-312`), cookie hardening, CORS locked-down by default, JSON error middleware, forwarded-headers opt-in.
2. **CI actually exercises migrations + seed on a real SQL Server** (`ci.yml:84-116` runs the Production container against SQL 2022), plus an axe critical/serious accessibility gate with real Playwright Chromium (`ci.yml:127-137`).
3. **Test suite depth + breadth** — 24 files / 241 executed, `TreatWarningsAsErrors` in both web and tests projects; dedicated suites for FX/units (`AuditN15FxTests`), concurrency windows, budget/accounts behavior, fiscal close, backups, import/export, statements correctness.
4. **AuthZ breadth** — permission attributes applied per-operation, backed by a reflection sweep test with a curated anonymous allowlist.
5. **Non-root container** with healthcheck and curl; `.dockerignore` excludes secrets/env, docs, plans, scripts; compose fails fast on missing env via `:?` expansion instead of defaulting to weak secrets.
6. **Model/snapshot hygiene** — 23 migrations tracked, 22 with Designer files, snapshot consistent in inspection; EF conventions respected otherwise.
7. **Unified rebrand** — namespace/project naming is consistently `NewVixSmart`/`Vix`; `master` branch clean apart from untracked audit artifacts.

---

## 5. Reproducibility note

Research-only constraints: no `dotnet build`, `dotnet test`, SQL, or container execution was performed. Test-count and pass/fail figures are taken from checked-in/untracked artifacts under `docs/test-results/` and from source attribute counting. Re-run `dotnet test NewVixSmart.slnx -c Release --results-directory TestResults` and `docker compose up -d` to confirm H-01/H-02 behavior and retire the stale M-04 artifacts.
