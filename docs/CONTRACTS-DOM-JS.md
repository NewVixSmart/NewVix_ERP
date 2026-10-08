# ظ‚ط§ط¦ظ…ط© ط¹ظ‚ظˆط¯ DOM/JS ط§ظ„ط­ط§ظ„ظٹط© â€” ظ†طھظٹط¬ط© P0 (ظ„ط§ طھظڈظƒط³ظژط± ظپظٹ ط£ظٹ ظ…ط±ط­ظ„ط©)

> **ط¨ظˆط§ط¨ط© آ§14 ظ…ظ† `UI-EXECUTION-MANDATE.md`** â€” طھط³ظ„ظٹظ… 3 ظ…ظ† 4طŒ ظ†ط§طھط¬ ط§ظ„طھط¯ظ‚ظٹظ‚ ط§ظ„ظپط¹ظ„ظٹ
> ظ„ظ€ `wwwroot/js/site.js` (1273 ط³ط·ط±ظ‹ط§) ظˆ `wwwroot/js/tablist.js` (132 ط³ط·ط±ظ‹ط§) ظˆظ…ط·ط§ط¨ظ‚طھظ‡ط§
> ط¨ط§ظ„ظ‚ظˆط§ظ„ط¨. ط£ظٹ طھطµظ…ظٹظ… ظپظٹ P1â€“P7 ظٹط¹ظٹط¯ طھظ„ظˆظٹظ†/ظ‡ظٹظƒظ„ط© **ظٹط­ط§ظپط¸** ط¹ظ„ظ‰ ظ‡ط°ظ‡ ط§ظ„ط¹ظ‚ظˆط¯ ط¥ظ„ط§ ظ…ط§ طµط±ظ‘ط­طھ
> ط¨ظ‡ ظ…ط±ط­ظ„ط© ظ…ط­ط¯ط¯ط© ظ‡ظ†ط§.

## ط£. ط¹ظ‚ظˆط¯ `site.js` (ط³ط¬ظ„ ظƒط§ظ…ظ„)

| # | ط§ظ„ط¹ظ‚ط¯ (selector / id / attr / state) | ظ…ظˆط¶ط¹ JS | ط§ظ„ط³ظ„ظˆظƒ ط§ظ„ظ…ط­ظ…ظٹ | ظ…ط±ط­ظ„ط© ط§ظ„ظ„ظ…ط³ |
|---|---|---|---|---|
| A1 | `i.bi` (ظƒظ„ ط§ظ„ط£ظٹظ‚ظˆظ†ط§طھ ط§ظ„ط¯ظٹظƒظˆط±ظٹط©) | site.js:6-11 | ظٹط¶ظٹظپ `aria-hidden="true"` طھظ„ظ‚ط§ط¦ظٹظ‹ط§ ظ„ط£ظٹ `i.bi` ط¨ظ„ط§ `aria-hidden` (ظٹط³طھط«ظ†ظٹ `.visually-hidden`). **ظˆط³ظ… 2.6: ظٹظڈظپط¶ظژظ‘ظ„ ط£ظ† ظٹظƒظˆظ† HTML ظٹط¯ظˆظٹظ‘ط§ طµط±ظٹط­ظ‹ط§ط› JS ط¨ظ„ط§ طھط؛ظٹظٹط±** | P1 |
| A2 | ظ‚ط±طµط© CSRF ط¹ط§ظ…ط©: `input[name="__RequestVerificationToken"]` | site.js:13-14 | ظٹظ‚ط±ط£ ط§ظ„طھظˆظƒظ† ط¹ظ†ط¯ ط§ظ„طھط­ظ…ظٹظ„ ظˆظٹط·ط¨ظ‚ظ‡ طھظ„ظ‚ط§ط¦ظٹظ‹ط§ ظپظٹ `postForm` (ط±ط£ط³ `RequestVerificationToken`). ط§ظ„ظ†ظ…ط§ط°ط¬ ط§ظ„ط£ط®ط±ظ‰ طھط­ظ…ظ„ طھظˆظƒظ†ظ‡ط§ ط§ظ„ط±ط³ظ…ظٹ â€” **ظ„ظ† ظ†ط­ط°ظپظ‡** | P1 (طھظˆط«ظٹظ‚ ظپظ‚ط·) |
| A3 | Sidebar: `#sidebar`, `#mobileOverlay`, `#sidebarToggle`, `.show`, `inert`, `aria-hidden` | site.js:17-97 | ظپطھط­/ط¥ط؛ظ„ط§ظ‚ offcanvasطŒ `aria-expanded`طŒ ظ‚ظپظ„ scrollطŒ طھط±ظƒظٹط² ط§ط®طھظٹط§ط±ظٹطŒ ط¥ط؛ظ„ط§ظ‚ ط¨ظ€ Esc ظˆ Tab trap ظˆ `.nav-link` | P2 (Shell) |
| A4 | `window.toggleSidebar()`, `window.showToast(msg,type)` | site.js:50-61, 111-137 | ط£ط³ظ…ط§ط، ظ…ظˆط«ظ‘ظ‚ط© طھط³طھط¯ط¹ظٹظ‡ط§ ط§ظ„ظ‚ظˆط§ظ„ط¨ (ط£ط²ط±ط§ط±) â€” ط§ظ„ط¨ظ‚ط§ط، ظƒظ…ط§ ظ‡ظˆط› ظٹطھط­ظˆظ‘ظ„ ط§ظ„طھظ„ظˆظٹظ† ظپظ‚ط· | P2/P3 |
| A5 | Toast DOM: `.toast-region`, `.app-toast info/error/success`, `.toast-progress` | site.js:101-137 | ظٹطھط³ظ„ظ… ظ…ظ† CSSط› ظ„ط§ طھط؛ظٹظٹط± HTML/JS | P3 |
| A6 | `postForm(url, data)`, `handleResult(res, err)` | site.js:140-162 | ظƒظ„ POST ط¹ط¨ط± JS | ظ„ط§ ظٹظڈظ„ظ…ط³ ط£ط¨ط¯ظ‹ط§ |
| A7 | Edit-name modal: `#editNameModal`, `#editNameForm`, `#editNameInput`, `#pageNewCat`, `#pageAddCatBtn` | site.js:168-223 | طھط­ط±ظٹط± طھطµظ†ظٹظپ/ظ†ظˆط¹ طµظ†ظپ ظ…ط¹ طھط­ظ‚ظ‘ظ‚ RowVersion | P3 (طھظ…ظˆظٹظ„) |
| A8 | Confirm modal: `window.confirmAction(msg, fn, okLabel)` + `#appConfirmModal`/`#appConfirmBtn`/`#appConfirmMsg` | site.js:225-299 | ط§ط³طھط¨ط¯ط§ظ„ `confirm()`ط› ظٹظڈط³طھط¯ط¹ظ‰ ظ…ظ† ط§ظ„ظ‚ظˆط§ظ„ط¨ ظ„ط£ظپط¹ط§ظ„ ط§ظ„ط­ط°ظپ/ط§ظ„طھط±ط­ظٹظ„ | ظ„ط§ ظٹظڈظ„ظ…ط³ |
| A9 | `select[data-auto-submit]` | site.js:301-306 | ط¥ط±ط³ط§ظ„ ط§ظ„ظ†ظ…ظˆط°ط¬ طھظ„ظ‚ط§ط¦ظٹظ‹ط§ ط¹ظ†ط¯ ط§ظ„طھط؛ظٹظٹط± (ظپظ„ط§طھط± Accounts/PurchaseOrders/Reports) â€” **طھط¨ظ‚ظ‰** | ظ„ط§ ظٹظڈظ„ظ…ط³ |
| A10 | `[data-auto-print]` | site.js:308-311 | `window.print()` ظ„ط£ط²ط±ط§ط± ط§ظ„ط·ط¨ط§ط¹ط© â€” **ط§ظ„ط¥طµظ„ط§ط­ 2.4 ظٹظڈط؛ظٹظگظ‘ط± ط§ظ„ط£ظ„ظˆط§ظ†/`rel` ظپظ‚ط·** | P3 |
| A11 | Category create: `#pageNewCat`, `[data-create-category]`, `#pageAddCatBtn2` | site.js:313-341 | POST `/Categories/Create` | P3 |
| A12 | Manage tables: `#categoriesTable`, `#itemTypesTable`, `.btn-edit`, `.btn-del`, `td[data-name]`, `data-rowversion` | site.js:350-390 | طھط­ط±ظٹط±/ط­ط°ظپ ظ…ط¹ conflicts | P3 (طھظ…ظˆظٹظ„) |
| A13 | Skeleton: `table[data-load="true"]` + `.skeleton-row/.skeleton` | site.js:393-431 | طھط­ظ…ظٹظ„ ظٹط³ط­ط¨ ط®ط§ط¯ظ…ظ‹ط§ ط¬ط²ط¦ظٹظ‹ط§ ظˆظٹط¨ظ†ظٹ ط§ظ„ظ‡ظٹظƒظ„ | P3 |
| A14 | `#itemsBody` + `.item-select` (ط¬ط¯ط§ظˆظ„ ط§ظ„ط£ط³ط·ط± ط§ظ„ظ‚ط§ط¨ظ„ط© ظ„ظ„طھط­ط±ظٹط±) | site.js:444-451 | طھظ†ظ‚ظٹط© ط§ظ„طµظپظˆظپ ط§ظ„ظپط§ط±ط؛ط© ظ‚ط¨ظ„ submission ظپظٹ ظ†ظ…ط§ط°ط¬ Sales/Purchase/Batch | ظ„ط§ ظٹظڈظ„ظ…ط³ |
| A15 | Scrollable regions: `.table-container, .card-body.p-0, .table-responsive` â†’ `tabindex=0` + `role=region` + label | site.js:453-469 | طھظˆط§ظپظ‚ axe scrollable-region-focusable | P1/P3: **ظٹط¬ط¨ ط£ظ„ط§ ظ†ط²ظٹظ„ `tabindex`/`role`** |
| A16 | Sidebar collapse/rail: `vix-sidebar-sections`, `vix-sidebar-rail`, `.nav-group[data-group]`, `.nav-group-header` `aria-expanded`, `#railToggle`, `.is-collapsed`, `.is-rail` | site.js:471-586 | ط­ظپط¸ ط§ظ„ظپطھط­/ط§ظ„ط¥ط؛ظ„ط§ظ‚ ظˆظˆط¶ط¹ rail ظپظٹ localStorage | P2 (Shell): **ط§ظ„ظ‚ط§ط¦ظ…ط© طھط¨ظ‚ظ‰ ط¨ظ‡ط°ط§ ط§ظ„ط¨ظ†ظٹط©** |
| A17 | Nav filter: `[data-nav-filter]`, `[data-nav-filter-clear]`, `[data-nav-empty]`, Ctrl+K | site.js:588-690 | ط¨ط­ط« ط§ظ„ظ‚ط§ط¦ظ…ط© ط§ظ„ط¬ط§ظ†ط¨ظٹط© | P2: **طھط¨ظ‚ظ‰ ظƒط¹ظ‚ط¯** |
| A18 | `form[data-confirm]`, `data-confirm-ok`, ظˆط³ظ… `data-confirmed` | site.js:692-707 | ط§ط¹طھط±ط§ط¶ submission ظ†ط­ظˆ confirmAction | ظ„ط§ ظٹظڈظ„ظ…ط³ |
| A19 | `form[data-loading-submit]`, `data-loading-text` | site.js:709-719 | طھط¹ط·ظٹظ„ ط²ط± ط§ظ„ط¥ط±ط³ط§ظ„ ظ…ط¹ ط³ط¨ظٹظ†ط± ط£ط«ظ†ط§ط، ط§ظ„ط¹ظ…ظ„ | P3 (ط³ط¨ظٹظ†ط± ظٹظڈظ…ظˆظژظ‘ظ„ ظپظ‚ط·) |
| A20 | `[data-password-toggle]` + `aria-controls` (Login) | site.js:729-742 | طھط¨ط¯ظٹظ„ ط¥ط¸ظ‡ط§ط± ظƒظ„ظ…ط© ط§ظ„ط³ط± | P2/P3 |
| A21 | `[data-count]`, `data-decimals`, `data-count-formatted` | site.js:744-789 | ط¹ط¯ظ‘ط§ط¯ KPI ظ…ط¹ ط§ط­طھط±ط§ظ… `prefers-reduced-motion` | P4 (ط§ظ„ط¬ط¯ظٹط¯ Home) |
| A22 | Bulk select: `#selectAll`, `.mass-convert-check` | site.js:791-798 | MassConvert | ظ„ط§ ظٹظڈظ„ظ…ط³ |
| A23 | Theme: `data-theme-mode/theme/bs-theme`, `.theme-mode-btn[data-theme-value]`, `localStorage theme-mode`, `__applyDarkThemeCss` | site.js:800-842 + Layout:18-45 | طھط¨ط¯ظٹظ„ ط§ظ„ظ†ط¸ط§ظ…/ظپط§طھط­/ط¯ط§ظƒظ† + ط¯ظٹظ†ظˆ طھط­ظ…ظٹظ„ dark-theme.css | P1: **ظٹظ†طھظ‚ظ„ ط¥ظ„ظ‰ Partial ظ…ط´طھط±ظƒ ظˆظ„ط§ طھظڈظƒط³ط± `__applyDarkThemeCss`** |
| A24 | `[data-clock]`, `[data-greeting]` | site.js:844-852, 721-727 | ط³ط§ط¹طھ/طھط­ظٹط© | P2/P4: طھط¨ظ‚ظ‰ |
| A25 | Client-side data tables: `.data-shell`, `.data-toolbar`, `.data-search`, `.data-size`(10/20/50/100), `.data-pager`, `.th-sort`, `aria-sort`, `localStorage vix-data-size`, `.nvs-frozen-col` (>8 ط£ط¹ظ…ط¯ط©) | site.js:854-1183 | ط¨ط­ط«/ظپط±ط²/طھط±ظ‚ظٹظ… clientside ط¹ظ„ظ‰ ط§ظ„ط¬ط¯ط§ظˆظ„ ط؛ظٹط± partialed | **2.1/3.1: طھط¨ظ‚ظ‰ ظ„ظ„ط¬ط¯ط§ظˆظ„ ط؛ظٹط± ط§ظ„ط³ظٹط±ظپط±ظٹط©ط› طھظڈط¶ط§ظپ ط´ط±ط· `data-server-paged` ظپظ‚ط·** |
| A26 | `window.refreshVixTables()` | site.js:1150-1159 | ط¨ط¹ط¯ ط¥ط¹ط§ط¯ط© طھط´ط؛ظٹظ„ ط§ظ„ط¬ط¯ط§ظˆظ„ ط§ظ„ظ‡ط¬ظٹظ†ط© | ظ„ط§ ظٹظڈظ„ظ…ط³ (ظٹظڈط³ظ…ظ‘ظ‰ ظپظٹ A13 ظ„ط¬ط¯ط§ظˆظ„ data-load) |
| A27 | Density: `[data-table-density-toggle]`, `localStorage vix-table-density` (compact/comfortable)طŒ `applyTableDensity` | site.js:1186-1209 | **ظ…ظ†ط¬ط² ظƒظ…ط§ ط¬ط§ط، ظپظٹ 3.1** â€” طھظ…ظˆظٹظ„ ط¹ط¨ط± `.table` padding ظپظ‚ط· | P3 (طھظ…ظˆظٹظ„ ظپظ‚ط·) |
| A28 | Prefetch on hover/focus (a[href]طŒ ظ‚ظˆط§ط¹ط¯ origin) | site.js:1224-1272 | طھط­ط³ظٹظ† ط£ط¯ط§ط، | ظ„ط§ ظٹظڈظ„ظ…ط³ |

## ط¨. ط¹ظ‚ظˆط¯ `tablist.js`

| ط¹ظ‚ط¯ | ظ…ظˆط¶ط¹ | ظ…ظ„ط§ط­ط¸ط© ط§ظ„ط¥ط¨ظ‚ط§ط، |
|---|---|---|
| `[role="tablist"]` + `[role="tab"]` + ظˆط³ظ… `data-tablist-roving` (idempotent) | tablist.js: ط¨ظگط±ظژظ…ظڈظ‘ظ‡ظڈ ظƒط§ظ…ظ„ | **ظ„ط§ ظٹط¶ط§ظپ ظ…ط³طھظ…ط¹ ط«ط§ظ†ظچ**ط› ط§ظ„ظ‚ط§ظ„ط¨ ط§ظ„ظˆط­ظٹط¯ ط­ط§ظ„ظٹط§ `Settings/Printing.cshtml:44-50` |
| `aria-selected` ظ…طµط¯ط± ط§ظ„ط­ظ‚ظٹظ‚ط© ظ…ظ† ط§ظ„ط®ط§ط¯ظ…ط› `aria-controls` ظٹط´ظٹط± ظ„ظ„ظˆط­ط©ط› `tabindex` ظ…طھط¬ظˆظ‘ظ„ | tablist.js:23-66 | ظ„ط§ طھط؛ظٹظٹط± |

## ط¬. ط¹ظ‚ظˆط¯ ط§ظ„ظ‡ظٹظƒظ„ ط§ظ„ط¹ط§ظ…ط© (Layout/ظ‚ظˆط§ظ„ط¨)

- `Views/Shared/_Layout.cshtml`:
  - C1: `<html lang="ar" dir="rtl" data-theme-mode data-theme data-bs-theme>` (ط³ط·ط± 2) + ط³ظƒط±ط¨طھ ط§ظ„ط«ظٹظ… 18-45 (ظٹظڈظ†ظ‚ظ„ ظ„ظ€ `_ThemeScript.cshtml` Partial ظپظٹ P1 â€” **ظ†ظپط³ ط§ظ„ط³ظ„ظˆظƒ ط­ط±ظپظٹظ‘ط§**).
  - C2: `@Html.AntiForgeryToken()` ط§ظ„ط¹ط§ظ… ظپظٹ ط§ظ„ط³ط·ط± 48 â€” **ظٹط¨ظ‚ظ‰** (ظٹط±طھظƒط² ط¹ظ„ظٹظ‡ A2).
  - C3: ط¨ظ†ظٹط© Sidebar: `#sidebar`, `#mainMenu`, `.sidebar-brand`, `.nav-filter`, `.nav-group[data-group]`, `.nav-link...` â€” ظٹط¨ظ‚ظ‰ ط§ظ„ط´ظƒظ„ ط­طھظ‰ P2.
  - C4: Topbar `data-greeting`/`data-clock`/`.theme-mode-btn`/`[data-table-density-toggle]` â€” طھط¨ظ‚ظ‰ ظƒظ„ ط§ظ„ظ…ط¹ط±ظپط§طھ.
  - C5: `mainContent` ظ„ظ„ظ€ skip-link â€” ظ„ط§ ظٹظڈط²ط§ظ„.
- **Login.cshtml** (`Layout=null`) ظˆ **AccessDenied.cshtml** (`Layout=null`): طھط­ظ…ظ„ط§ظ† ط³ظƒط±ط¨طھ ط§ظ„ط«ظٹظ… ط§ظ„ط®ط§طµ ط¨ظ‡ظ…ط§ (Login 18-30طŒ AccessDenied 17-27) ظ…ط¹ `#Password`, `.lg-*`, `[data-password-toggle]` â€” ظپظٹ P2 ظٹظڈظˆط­ط¯ط§ظ† ظ†ط­ظˆ Partial ط§ظ„ط«ظٹظ… ط§ظ„ظ…ط´طھط±ظƒ **ط¯ظˆظ† ظƒط³ط± FOUC** (ط§ظ„ط³ط·ط± `document.documentElement.setAttribute` ظ‚ط¨ظ„ CSS).
- **Print/PrintDocument.cshtml** ظˆ `Items/PrintLabel.cshtml`: ظ…ظ„ظƒظٹط© ط§ظ„ط·ط¨ط§ط¹ط© â€” ط®ط§ط±ط¬ ط§ظ„طھطµظ…ظٹظ… طھظ…ط§ظ…ط§ (ظ…ط§ظ†ط¯ط§ظٹطھ آ§13: ظ„ط§ طھظڈظ„ظ…ط³).

## ط¯. ط¹ظ‚ظˆط¯ طھظڈظ†ط´ط£ (ط¬ط¯ظٹط¯ط©طŒ ظ…ظ‚طھط±ط­ط© ظپظٹ ظ‡ط°ظ‡ ط§ظ„ظ…ط±ط­ظ„ط©)

| ط§ظ„ط¹ظ‚ط¯ ط§ظ„ط¬ط¯ظٹط¯ | ط§ظ„ط؛ط±ط¶ | ط§ظ„ظ…ظƒط§ظ† |
|---|---|---|
| `[data-server-paged]` ط¹ظ„ظ‰ `<table>` ظپظٹ ط§ظ„ظ‚ظˆط§ط¦ظ… ط§ظ„طھط³ظٹط±ظپظٹط© ط§ظ„ظ€20 | opt-out ظ…ظ† A25 (client engine ظ„ط§ ظٹظ„ظ…ط³ظ‡ط§) | site.js `buildDataTable` ط´ط±ط· ط¥ط¶ط§ظپظٹ + ظ‚ظˆط§ظ„ط¨ ط§ظ„ظ€14 ط§ظ„ط¬ط¯ظٹط¯ط© ظˆظ…ظ„ظپ ط§ظ„ظ€6 ط§ظ„ط­ط§ظ„ظٹط© |
| `Views/Shared/_PageHeader.cshtml` (Partial) | طھظˆط­ظٹط¯ ط±ط£ط³ ط§ظ„طµظپط­ط© (2.3) | ط§ظ„ظ‚ظˆط§ظ„ط¨ |
| `Views/Shared/_EmptyState.cshtml` (Partial) | ط­ط§ظ„ط© ظپط§ط±ط؛ط© ظ…ظˆط­ط¯ط© (2.2 â€” ط§ظ„ظ‚ط±ط§ط± آ«ط¨آ») | ط§ظ„ظ‚ظˆط§ظ„ط¨ |
| `Views/Shared/_StatusBadge.cshtml` (Partial) | ط´ط§ط±ط© ط§ظ„ط­ط§ظ„ط© + ط§ظ„ظ‚ط§ظ…ظˆط³ | ظ‚ظˆط§ط¦ظ…/طھظپط§طµظٹظ„ |
| `Views/Shared/_StatCard.cshtml` (Partial) | KPI ظ„ظ„ظ€ Dashboard ط§ظ„ط¬ط¯ظٹط¯ | Home/IndexطŒ Reports/Dashboard |
| `Views/Shared/_ThemeScript.cshtml` (Partial) | ط¨ط¯ظٹظ„ ط³ظƒط±ط¨طھ ط§ظ„ط«ظٹظ… ط§ظ„ط«ظ„ط§ط«ظٹ ط§ظ„ظ…ظƒط±ط± (2.9) | Layout + Login + AccessDenied |

## ظ‡ظ€. ظ…ظ„ط§ط­ط¸ط§طھ P0 ط­ط±ط¬ط© (ط§ظ„ط§ظƒطھط´ط§ظپط§طھ)

1. **A25 ظٹظ…ط³ ظ‚ظˆط§ط¦ظ… ط§ظ„ط³ظٹط±ظپط± ط¨ط§ظ„ظپط¹ظ„**: ط§ظ„ظ€6 ط§ظ„ظ‚ظˆط§ط¦ظ… ط§ظ„طھط³ظٹط±ظپظٹط© ط°ظˆط§طھ `_Pager` (Sales, Customers, Accounts, Payments, PurchaseReturns, Stock) ط§ظ„ط¢ظ† طھظڈط¹ط§ظ„ظژط¬ ط£ظٹط¶ظ‹ط§ ظ…ط­ط±ظ‘ظƒ client-sideط› ط§ظ„ظ†طھظٹط¬ط© طھط±ظ‚ظٹظ… ظ…ط²ط¯ظˆط¬/ط¨ط­ط« ظ…ط²ط¯ظˆط¬. ظˆط³ظ… `data-server-paged` ظ‡ظˆ **ط­ظ„ظ‘** ظ‡ط°ظ‡ ط§ظ„ط§ط²ط¯ظˆط§ط¬ظٹط© ط¹ظ†ط¯ طھظپط¹ظٹظ„ظ‡ ظپظٹ ط§ظ„ظ€6 + ط§ظ„ظ€14 ط§ظ„ط¬ط¯ظٹط¯ط©.
2. **Density ظ…ظ†ط¬ط²ط© (A27)**: ظ„ط§ ط­ط§ط¬ط© ظ„ط¨ظ†ط§ط¦ظ‡ط§ ظپظٹ P3 â€” طھظ…ظˆظٹظ„ ظپظ‚ط·.
3. **antiforgery (A2/C2 + 44 طھظˆظƒظ†ظ‹ط§ ظ†ظ…ظˆط°ط¬ظٹظ‹ظ‘ط§)**: ظ„ظ† ظٹط­ط°ظپ ط£ظٹ طھظˆظƒظ† ظ†ظ…ظˆط°ط¬ط› ظ†ط²ظٹظ„ ظپظ‚ط· ظ…ط§ طھظƒط±ط± ظپط¹ظ„ظٹظ‹ط§ ظپظٹ ظ†ظپط³ ط§ظ„ظ†ظ…ظˆط°ط¬ ط¹ظ†ط¯ ظˆط¬ظˆط¯ظ‡. ظ‡ط°ط§ ظ‡ظˆ ظ…ط¯ظ‰ ط§ظ„ظ‚ط±ط§ط± 2.5 ط§ظ„ظ…ط¹طھظ…ط¯.
4. **Login ظٹط³طھط¯ط¹ظٹ site.js** (ط³ط·ط± 169) ظ„ظƒظ†ظ‡ `Layout=null`: `data-loading-submit` ط¹ظ„ظ‰ ط§ظ„ظ†ظ…ظˆط°ط¬ ظٹط¹ظ…ظ„ ظپظٹظ‡ ظ„ط£ظ†ظ‡ ظٹظڈط´ط؛ظ„ site.js â€” طھطµظ…ظٹظ…ظ†ط§ ظ„ط§ ظٹظƒط³ط± ط°ظ„ظƒ.
5. **`--text-faint` ظ„ظˆظ† ظ…ظ‚ط±ظˆط، ط¹ظ„ظ‰ ط§ظ„ط£ط³ط·ط­ (طھطµط­ظٹط­ ظ…ط¹طھظ…ط¯ ظپظٹ DESIGN-SYSTEM آ§2)**.

## ظˆ. ط¹ظ…ظ„ظٹط§طھ ط§ظ„طھط­ظ‚ظ‚ ظ…ظ† ط§ظ„ط¹ظ‚ظˆط¯ (ظ„ط§ طھطھط؛ظٹط±)

- `dotnet build ... -c Release` â†’ 0W/0E
- `dotnet test ... -c Release` â†’ Failed: 0
- `scripts/check-text-hygiene.ps1` â†’ exit 0
- a11y gate (`scripts/run-a11y-gate.ps1`) â†’ ط£ط®ط¶ط± ظپظٹ ط§ظ„ظپط§طھط­ ظˆط§ظ„ط¯ط§ظƒظ†
- `dotnet ef migrations has-pending-model-changes` â†’ No changes
