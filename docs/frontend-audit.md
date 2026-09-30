# Static Frontend Audit — RTL ERP (NewVixSmart.Web)

**Scope:** static review only — Razor views (113 `.cshtml`), `site.css` (3219 ln), `dark-theme.css` (989 ln), `auth.css` (1002 ln), `site.js` (1226 ln), layout/partials, CSP middleware, `e2e/a11y-gate.cjs`, `ACCESSIBILITY.md` claims. **No browser was launched** — contrast-ratio and rendered-behavior claims must be confirmed by the live-testing agent (A4).

## Summary

| Severity | Count | Notes |
|---|---|---|
| 🔴 High | 0 | — |
| 🟠 Medium | 5 | a11y-gate ≠ claim (A-01), `<th scope>` gaps incl. complex matrix (A-02), CSP blocks inline handler → broken feature (A-03), dark-theme duplicates structural rules (C-01), jQuery loaded app-wide (J-01) |
| 🟡 Low | 9 | A-04…A-07, C-02…C-03, J-02…J-03 |
| ✅ Verified-good | 11 categories | tokens + banned-hex absence, keyframes/backdrop allowlists, reduced-motion, forced-colors, print, skip-link, live regions, focus management/ARIA in site.js, barcode `role="img"`, bound labels, nonce/CSP |

**Verdict:** The claim document is by and large **honest and technically strong** (no banned legacy hexes, clean keyframe/backdrop allowlists, deep keyboard/ARIA support, nonce-clean CSP). But the headline "41 صفحة → 0 انتهاكات" axe claim is **not true for the current repo** (17 routes, critical/serious only, `region` disabled, light mode only), and several small markup/claim gaps need tightening. Fixing A-01, A-02, A-03, J-01 first would make the claims fully defensible.

---

## 1. Findings

### A-01 (🟠 Medium) — Axe gate ≠ the documented "41 صفحات / 0 انتهاكات"
- **Where:** `e2e/a11y-gate.cjs:10-28` (exactly 17 routes), `:52` (critical/serious only), `:48` (tags wcag2a…wcag22aa), `:49` (`region` rule disabled), `:32` (default chromium, no dark mode).
- **Evidence:** Routes list is `/, /Reports/Dashboard, /Sales/Create, /Settings, /Settings/Printing, /PurchaseOrders, /StockTransfers, /Accounts, /Items, /Customers, /Backup, /SaleReturns, /PurchaseReturns, /SalesQuotes, /Fiscal, /ImportCenter, /ExportCenter` = **17** pages, not 41. `violations.filter(v => v.impact === 'critical' || v.impact === 'serious')` means moderate issues are masked; gate PASS text itself says "0 critical/serious". `region` (landmark) is whitelisted off. No `[data-theme=dark]` context is ever tested.
- **Also contradicts "41 صفحة":** no program that realizes 41 routes exists in the repo (scripts + docs).
- **Fix:** Make the gate run all MVC routes (derive from `ApplicationModel` or a route manifest), stop dropping `region`, report *all* violation counts (min/moderate) instead of filtering, and add a dark-mode pass (`page.emulateMedia({ colorScheme: 'dark' })` + the theme button click). Then update `ACCESSIBILITY.md` to state the true numbers (e.g., "43 routed pages, axe: 0 critical/serious, 3 moderate").

### A-02 (🟠 Medium) — `<th>` missing `scope`; one complex matrix
- **Where:** `Views\Payments\Details.cshtml:36-41`; `Views\Warehouses\Index.cshtml:18-21`; `Views\Users\Permissions.cshtml:31-34`; `Views\Reports\Dashboard.cshtml:63,97,134`.
- **Evidence:** Payments Details key/value table and Warehouses index headers have bare `<th>`; Permissions is a matrix (th # ID, account, module rows) with no `scope`/`headers` — worst case of the four for WCAG 1.3.1. Dashboard uses `<th colspan>` as row-section titles.
- **Fix:** `scope="col"` on all column headers; Permissions should additionally use `scope="row"` on first column and `headers`/`id` wiring if cells span logical groups.

### A-03 (🟠 Medium) — CSP blocks the invoice-pre-fill feature (`onchange`)
- **Where:** `Views\DeliveryOrders\Create.cshtml:24` + `Program.cs:304-307` (`script-src 'self' 'nonce-…'`, **no** `'unsafe-inline'`).
- **Evidence:** `<select … id="invoiceSelect" onchange="if(this.value) location.href='@Url.Action(...)?invoiceId=' + this.value;">`. Inline event-handler attributes are not permitted by that CSP directive; the handler never runs → selecting an invoice will not populate the form. Also the only inline-hander in the whole views tree (asset: all 25 inline `<script>` blocks are nonce'd; the 6 external `src=` scripts rely on `'self'`).
- **Fix:** Remove the attribute; bind in a nonce'd `<script>` block in the same view (or a delegated `change` listener in `site.js` keyed on `[data-invoice-select]`).

### A-04 (🟡 Low) — Multiple `<h1>` per page; one empty `<h1>`
- **Where:** `_Layout.cshtml:479` (visually-hidden `<h1>`); `Views\Settings\Printing.cshtml:20`; `Views\Settings\Branding.cshtml:6`; `Views\Home\Error.cshtml:7-8`.
- **Evidence:** Every layout page already injects an SR-only `<h1>` with the page title; Settings pages add a second visible `<h1 class="h3 mb-0">`. Home/Error has `<h1>` containing **only** an `aria-hidden` icon (no accessible name) followed by the real message in `<h2>` → AT announces an empty heading 1 then jumps to h2.
- **Fix:** Header row (`_Layout:479`) should become the single `<h1>`; Settings/Error use `<h2>`/`<p class="display">`. Alternatively drop the SR-only h1 on pages that render their own `<h1>`.

### A-05 (🟡 Low) — `target="_blank"` without `rel="noopener"`
- **Where:** `Views\Items\Details.cshtml:78`, `Views\Purchases\Details.cshtml:7`, `Views\Sales\Details.cshtml:12` (all Print/PrintLabel links). The other 8 `target="_blank"` anchors already carry `rel="noopener"`.
- **Fix:** add `rel="noopener"` (or `noopener noreferrer`) — reverse-tabnabbing hygiene; modern browsers auto-add it, but be explicit and consistent.

### A-06 (🟡 Low) — Invalid nesting `<h6>` inside `<span>`
- **Where:** `_Layout.cshtml:452`: `<span class="topbar-title"><h6 class="mb-0">@ViewData["Title"]</h6></span>`.
- **Evidence:** `<span>` only allows phrasing content; heading (`h6`) inside can confuse validators/AT reading landmarks.
- **Fix:** change the wrapper to `<h6 class="topbar-title mb-0">` and add a heading class style, or keep span and drop the h6.

### A-07 (🟡 Low) — Login form has no client-side validation
- **Where:** `Views\Account\Login.cshtml:131-162` (+ `:170` only loads `site.js`).
- **Evidence:** The standalone layout includes no jQuery/`_ValidationScriptsPartial`; `asp-validation-for` summary only renders server-side on postback. Username/Password errors appear only after a round-trip.
- **Fix (optional):** include `_ValidationScriptsPartial` (plus jquery) on Login, or add a tiny validation stub — low priority, login is the common commerce password-field pattern and server-side is authoritative.

### C-01 (🟠 Medium) — dark-theme.css duplicates structural rules instead of token-only overrides
- **Where:** `dark-theme.css:555-698` (`html[data-theme="dark"] .sidebar-nav .nav-link { … min-height:44px; gap:6px; border-radius:14px; padding-inline… }` at :589, `.nav-group-header` same at :578, collapse grid rules at :651-652 re-implementing `site.css:798-799`).
- **Evidence:** ~110 lines re-declare layout/sizing that `:root` tokens already drive; if the light file changes `min-height`/radius, dark silently keeps old values → drift. (Colors themselves are fine/navy.)
- **Fix:** keep only `background/color/border-color` + genuinely dark-unique values in dark-theme; let geometry live in the shared base rules (ideally behind `--sidebar-nav-*`/`--control-*` tokens).

### C-02 (🟡 Low) — 246 hardcoded hexes remain in site.css; legacy palette leftovers
- **Where:** `site.css:331-436` (btn-outline/badge/alert overrides: `#166534, #075985, #3C4A5E, #B91C1C, #991b1b, #fee2e2, #fecaca, #dcfce7, #bbf7d0`), `:2279-2302` (`#8A97A8, #0D1B35, #EAF2FE` on sort headers), `:1800-1823`, plus print block (`#fff`/`#0d1b35`).
- **Verified green:** the 7 **banned** legacy hexes (`#10b981 #0e9f6e #0b7a54 #34d399 #5eead4 #052e22 #0d9265`) return **0 matches**; `--color-*` tokens are consistently used for surfaces/text.
- **Fix:** migrate the leftover badge/alert/outline literals to new `--color-*` tokens (they are the "explicit btn-outline override" the doc allows, but hoist them to tokens so dark/forced-colors can be applied from one place).

### C-03 (🟡 Low) — `!important` density
- **Where:** `site.css:136`, `dark-theme.css:114`, `auth.css:14`.
- **Evidence:** mostly justified (Bootstrap-override section, print `@media print`, `prefers-reduced-motion`, `forced-colors`), but the count is high and a few (e.g., `site.css:207,210,213` text utilities, `:1767`) can be restructured as normal overrides with higher specificity or tokens.
- **Fix:** consolidate the overrides into dedicated variables; future editors will otherwise add yet more `!important`.

### J-01 (🟠 Medium) — jQuery + Bootstrap bundle downloaded on every page for <15 pages that need it
- **Where:** `_Layout.cshtml:498-500` (`bootstrap.bundle.min.js`, `jquery.min.js`, `site.js`).
- **Evidence:** all inline logic in `site.js` is vanilla (verified: no `$(`, no `document.write`, no `eval`); jQuery is only needed by `jquery.validate(.unobtrusive)` used in 14 views via `_ValidationScriptsPartial`.
- **Fix:** load `bootstrap.bundle` and `jquery` inside `@section Scripts` before `_ValidationScriptsPartial`, or use a WebOptimizer/BundlerMinifier combo so only form pages pay the ~90-100 KB parse cost. Keep `site.js` global.

### J-02 (🟡 Low) — `dark-theme.css` (989 ln) fetched unconditionally
- **Where:** `_Layout.cshtml:31`, `Login.cshtml:31`, `AccessDenied.cshtml:30` — no `media` attribute.
- **Evidence:** light-mode clients download/parse the whole file even though it only activates under `html[data-theme="dark"]`.
- **Fix (optional):** keep the toggle at runtime but preload with `media="(prefers-color-scheme: dark)"` + `onload` swap, or split into a `dark-critical.css` (chrome/forced-colors) + lazy full file.

### J-03 (🟡 Low) — PrintPreview endpoint relaxes embed/CSP posture
- **Where:** `Controllers\SettingsController.cs:512-516` (`frame-ancestors 'self'` + `X-Frame-Options: SAMEORIGIN` override of `Program.cs:307` `'none'`/`:309` DENY).
- **Evidence:** global policy is deliberately locked (`frame-ancestors 'none'`, DENY, plus `nosniff`, Referrer-Policy same-origin). The print-preview page softens embeddability — only justified if the preview is genuinely shown in an iframe.
- **Fix:** if the preview opens in a tab (not iframe), delete the override so it inherits the strict policy.

### Verified-good (11 categories — turn these into acceptance tests)
1. **Document/lang:** `lang="ar" dir="rtl"` on all three shells (`_Layout:2`, `Login:5`, `AccessDenied:4`); logical properties (`inset-inline-end`, `margin-inline`) used broadly; bootstrap.rtl.min.css.
2. **Tokens + banned hexes:** `:root` token blocks (`site.css:18,82,158`); 0 matches for the 7 banned legacy hexes; no legacy shell classes (`graphite/forge/saffron/hud/v1-v5` = 0 hits in css/js/views).
3. **Animation allowlist:** exactly 8 unique `@keyframes` — `toastIn, shimmer, toastProgress, rowIn, pulseNum` (site.css), `lgRise, lgFloat, lgGlow` (auth.css); dark-theme re-declares 5, no strays.
4. **backdrop-filter allowlist:** only `.modal-header` glass (`dark-theme.css:450-451`, `site.css:2075-2076`), dropdown/popover (`dark-theme.css:461-462`), auth `lg-hero__eyebrow`/`lg-preview` (`auth.css:175-176, 198-199`). Chrome (topbar/sidebar/theme chip) has **none**.
5. **No layout-prop transitions:** 0 matches for `transition` of width/height/inset/margin/top/left/right/bottom across all CSS (⚠ but `.main-content` util classes at `site.css:1004-1012` and `dark:861` need a live reflow check — geometry changes are instant, matching commit `aa41114` intent).
6. **Reduced motion:** `site.css:2917`, `auth.css:877`, dark-theme equivalents zero-out all animation/transition/transform.
7. **Forced colors (HC):** `site.css:3152-3219`, `dark-theme.css:855-983`, `auth.css:906-952` — full active-state coverage incl. `.skip-link`, theme buttons, active nav, print white-flips for dark too.
8. **Print:** `site.css:2331-2345` & `:2956-3053` hide `.sidebar/.topbar/.no-print/.toast-region/…`, force `#fff`, `@page margin:14mm`, repeat table headers via `display:table-header-group`; `PrintDocument.cshtml` injects `@page { size; margin }` and uses `th scope="col"` (`:345`).
9. **Skip link + focus:** `.skip-link` (`site.css:268`, `:focus` `:284`), `_Layout:35` → `#mainContent` (`:478` `role="main" tabindex="-1"`); site.css `:focus-visible` on every component (`.th-sort`, `.report-tile`, `.theme-mode-btn`, `.preset-card`, …).
10. **Live regions:** `role="alert"` + `aria-live="assertive"` (TempData error, `_Layout:482`), `role="status"` + `aria-live="polite"` (success, `:489`), toast region `aria-live="polite"` (`site.js:106`), nav-filter count `aria-live="polite"` (`site.js:940`).
11. **site.js ARIA engine (spot-checked, all real):** modal dialogs `aria-modal/labelledby/describedby` + focus trap/restore (`:174-177, 233-296`), `inert` symlink for sidebar/hidden rows (`:29,1078`), `aria-sort` on sortable th (`:992,1025`), data-table pager region `aria-label` + `aria-current/page` (`:948,1033-1039`), password toggle `aria-pressed`/`aria-controls`/label swap (`:727-735`), theme `aria-pressed` sync (`:814`), auto `aria-hidden` on `<i class="bi">` (`:6-11`), scrollable regions `tabindex=0` + label (`:460-462`).

---

## 2. Claims vs Reality (`ACCESSIBILITY.md`)

| # | Claim | Verdict | Evidence / Gap |
|---|---|---|---|
| 1 | WCAG 2.2 AA | ⚠ **Partial** | Built-ins support it well, but conformance is *not proven*: axe runs only 17/41 pages, critical/serious-only, `region` disabled, light-mode-only. Real-dark and moderate-level issues untested. |
| 2 | "41 صفحة – 0 انتهاكات" | ❌ **Contradicted** | `e2e/a11y-gate.cjs:10-28` = 17 routes; `:52` filters to critical/serious; `:49` `region` disabled; `:32` no dark emulation. Upgrade the gate (A-01). |
| 3 | Keyboard-operable + `:focus-visible` | ✅ **Verified** | site.js focus trap/restoration + site.css `:focus-visible` rules + skip-link. Live key-check pending (A4). |
| 4 | `lang="ar" dir="rtl"` | ✅ **Verified** | `_Layout:2`, `Login:5`, `AccessDenied:4`. |
| 5 | Skip-link | ✅ **Verified** | `_Layout:35` + `#mainContent` target + visible focus styling. |
| 6 | `role="alert"`/`status` | ✅ **Verified** | `_Layout:482/489`, site.js toasts/nav-count, respectful `polite` for success/status. |
| 7 | Touch/click target ≥44px | ⚠ **Partial** | Nav links (44px), nav-group headers (44px), auth inputs (48-52px), password toggle (40px), theme chip (38px), icon-only `.btn` (~32px BS default) are the exceptions. |
| 8 | Bound `<label>` all inputs | ✅ **Verified** | `asp-for` + `for`/`id` everywhere sampled; per-field `asp-validation-for`. |
| 9 | `<th scope>` | ⚠ **Partial** | Most tables have `scope="col"`; **4 files don't** (A-02), incl. the Permissions matrix. |
| 10 | `aria-label` icon-only buttons | ✅ **Verified** | Row actions (`Customers/Index:26,29,34`), rail/topbar toggles, password toggle, logout. |
| 11 | Barcode `role="img"` | ✅ **Verified** | `_Barcode.cshtml:7` injects `role="img" aria-label="رمز الباركود: …"` (HTML-encoded), plus visible monospace text, `th scope="col"` in PrintDocument. |
| 12 | Visually-hidden headings | ⚠ **Partial** | SR-only `<h1>` exists (`_Layout:479`), BUT duplicates visible `<h1>` on Settings pages and an empty `<h1>` on Home/Error (A-04). |
| 13 | "No manual colors — use `--color-*`" | ⚠ **Partial** | Tokens dominant, 0 banned hexes, but 246 literals remain (badge/alert/outline + print block; C-02), and dark-theme re-declares full rules (C-01). `.btn-outline-*` overrides are the doc-sanctioned exception. |
| 14 | Print hides `.no-print` | ✅ **Verified** | `site.css:2957` hides `.no-print`/`.d-print-none`/chrome; `@media print` blocks for both themes. |
| 15 | Core nav works without JS | ✅ **Verified** (static) | Nav = real `<a>` links; groups default `aria-expanded="true"`; filter/collapse/rail are progressive enhancement in `site.js`. Need live no-JS run (A4). |
| 16 | CSP nonce-script policy | ✅ **Verified** | `Program.cs:304-307`; 25/25 inline scripts nonce'd; external `src=` via `'self'`; no other inline handlers besides A-03. |

---

## 3. Strengths worth keeping as regression guards
- The **design-token system + banned-hex + keyframes/backdrop allowlist** is real and consistent — add a CI grep to keep it.
- **Deep keyboard/ARIA work** in `site.js` (focus trap/restore, `inert`, `aria-sort`, live-count, password/theme/rail toggles) is production-grade.
- **Motion & color accessibility are designed in** (reduced-motion + forced-colors coverage in all three stylesheets incl. dark print).
- **CSP hygiene is excellent** — nonce for all inline scripts, `frame-ancestors 'none'`, `form-action 'self'`, no `'unsafe-inline'` script.
- Barcode a11y (role/aria-label + visible text) is correct and matches the claim.

## Notes for the live-testing agent (A4)
- Verify real WCAG contrast for the *explicit* text colors: `.theme-mode-btn` `#4A5A70` on `rgba(255,255,255,.6)`, `lg-stat__label` `#bef0fc`, badge/warning text tokens, and the dark-mode `.nav-link` `#D6E2F0` at 44px/hover.
- Re-flow check at 320px for `Reports/Dashboard` (`th colspan` tables) & `Payments/Details` grid, and confirm no horizontal scroll from `Topbar` (clock/theme chip) in RTL.
- No-JS pass on nav (collapse defaults) + sidebar `inert` restoration on resize.
