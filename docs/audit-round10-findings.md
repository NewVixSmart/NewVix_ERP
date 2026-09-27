# Audit Round 10 — Identity, Authorization & Backup-Security Self-Audit

Date: 2026-09-24 · Baseline: 302/302 (Round-9 `0aac79f`) → Result: **308/308** passing, 0 warnings, `/healthz` 200.

Scope: identity/token handling, MVC authorization coverage, backup file exposure, permission-key parity. (Explore subagents were temporarily unable to connect; the audit was performed directly with file/grep verification.)

---

## Verified secure (evidence recorded)

- **Backups are not web-downloadable.** Backups are written to `ContentRootPath\App_Data\Backups` (`BackupService.cs:28`), outside `wwwroot`. Static files serve only wwwroot (`Program.cs:293-299`); `WithStaticAssets()` exposes only compiled static web assets. No anonymous path reaches `.bak` files.
- **JWT revocation via security stamp, fail-closed.** `OnTokenValidated` (`Program.cs:78-98`) rejects if the user is missing/locked out, and revalidates the security stamp on **every** authenticated request. Missing a stamp requirement or DB exceptions fail the request (throw), never fail-open. Stamp changes therefore revoke all outstanding JWTs.
- **Cookie hardening.** `HttpOnly`, `SameSite=Lax`, and `SecurePolicy.Always` outside Development (`Program.cs:107-116`).
- **Production startup guards.** Real SQL connection required outside dev (`Program.cs:30-35`); `Jwt:Key` must be ≥32 chars and not a placeholder (`44-57`); weak/absent default seed passwords abort startup in production (`345-359`).
- **MVC authorization coverage.** Every `[HttpPost]/[HttpPut]/[HttpDelete]` action in `Controllers` is covered — by `[RequirePerm("...")]`, class-level `[Authorize(Roles="Admin")]` (Backup, Users, ...), or the dedicated auth-specific handling on `AccountController` (login rate-limited, logout/change-password `[Authorize]`, all `[ValidateAntiForgeryToken]`). Global `AutoValidateAntiforgeryToken` still covers MVC POSTs (kept; only JWT-only `Api/*` controllers were exempted in R-9). No MVC controller accidentally disables it.
- **Permission-key parity.** All keys used in `[RequirePerm]`/`[ApiAuthorize]` (e.g., `Sales.Create`, `FiscalClose.Reopen`, `ImportCenter.Import`, `ExportCenter.View`) are derivatives of `PermissionCatalog` modules/actions, which is exactly what the Permissions UI validates against (`UsersController.Permissions` filters to `validKeys` and forces a `View` key). Role defaults (`PermissionDefaults.DefaultsFor`) are a strict subset — no default over-grants, no non-grantable controller key.
- **Backup path containment.** `ResolveBackupPath` (`BackupService.cs:247-261`) validates the filename format, computes the full path, requires it to be inside `BackupDirectory`, and checks existence — used by Delete/Read/Restore. A `SemaphoreSlim` serializes backup operations so concurrent backup/restore/delete cannot interleave.

## Closed this round

### R10-1 [LOW] Non-constant-time security-stamp comparison
`TokenStampChecks.StampMatches` used `string.Equals(..., Ordinal)` — a (theoretical) timing side-channel on the JWT security stamp.

**Fix:** constant-time comparison via `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes (length-mismatch short-circuits to `false`, preserving prior semantics). Class moved from being nested in top-level `Program.cs` into `src/NewVixSmart.Web/Infrastructure/TokenStampChecks.cs` so it is unit-testable.

**Tests:** `TokenStampChecksTests` — equal, different, null claim, length mismatch, empty values, same-length-different-bytes (6 cases).

## Deferred
- Import-preview upload size is bounded only by the 128MB form limit (`ImportCenterController.Preview`) — acceptable for an internal admin tool.
- API `POST api/payments` amount cap vs MVC: matched (`ValidateCreatePayment` checks `> 99999999.99m`).

## Artifacts
- `src/NewVixSmart.Web/Infrastructure/TokenStampChecks.cs` (new), `src/NewVixSmart.Web/Program.cs`, `src/NewVixSmart.Web/Api/TokensController.cs` (using), `tests/NewVixSmart.Web.Tests/TokenStampChecksTests.cs` (new). No migrations.