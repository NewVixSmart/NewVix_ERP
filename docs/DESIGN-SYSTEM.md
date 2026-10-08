# New Vix Smart â€” ظ†ط¸ط§ظ… ط§ظ„طھطµظ…ظٹظ… (Design System) v0.9

> **ط¨ظˆط§ط¨ط© آ§14 ظ…ظ† `UI-EXECUTION-MANDATE.md`** â€” طھط³ظ„ظٹظ… 1 ظ…ظ† 4طŒ ظٹظڈط¹طھظ…ظژط¯ ظ‚ط¨ظ„ ط¨ط¯ط، P1.
> ظ‡ط°ط§ ط§ظ„ظ…ط³طھظ†ط¯ ظٹظ…ط«ظ‘ظ„ ط§ظ„ظ‚ظٹظ… **ط§ظ„ظ†ظ‡ط§ط¦ظٹط©** ط¨ط¹ط¯ ظپط­طµ ط§ظ„طھط¨ط§ظٹظ† AA (ط¹ط¨ط± ط³ظƒط±ط¨طھ
> `contrast-*.ps1` ظپظٹ `$env:TEMP\opencode`)طŒ ظˆظƒطھط§ظ„ظˆط¬ ط§ظ„ظ…ظƒظˆظ†ط§طھ ظ…ط¹ ظ…ط±ط§ط¬ط¹ ط§ظ„ظ…ظ„ظپط§طھ ط§ظ„ظپط¹ظ„ظٹط©.

## 1. ظ‚ط±ط§ط±ط§طھ ط§ط¹طھظ…ط¯ظ‡ط§ ط§ظ„ظ…ط³طھط®ط¯ظ… (طھط­طµظٹظ„ آ§0)

| ط§ظ„ظ‚ط±ط§ط± | ط§ظ„ظ†طھظٹط¬ط© ط§ظ„ظ…ط¹طھظ…ط¯ط© |
|---|---|
| 2.5 (antiforgery) | طھظˆط«ظٹظ‚ ط£ظ† طھظˆظƒظ†ظ‹ط§ ظˆط§ط­ط¯ظ‹ط§ ظ„ظƒظ„ `<form method="post">` ظ…ط·ظ„ظˆط¨ ظˆط¸ظٹظپظٹظ‹ط§ط› ط­ط°ظپ ط§ظ„طھظƒط±ط§ط± ط§ظ„ظپط¹ظ„ظٹ ظپظ‚ط· (ظ†ظپط³ ط§ظ„ظ†ظ…ظˆط°ط¬/طµظپط­ط§طھ ط¨ظ„ط§ ط¥ط±ط³ط§ظ„). **ظ„ط§** طھط­ظˆظٹظ„ ط´ط§ظ…ظ„ ظ„ظ€ header token. |
| 2.1 (ظ‚ط§ط¦ظ…ط© ط§ظ„ظ€20) | ط§ط¹طھظ…ط§ط¯ ط§ظ„ظ‚ط§ط¦ظ…ط© ط§ظ„ظ…ظ‚طھط±ط­ط©: ط§ظ„ظ€6 ط§ظ„ط­ط§ظ„ظٹط© (Sales, Customers, Accounts, Payments, PurchaseReturns, Stock) + 14 ط¬ط¯ظٹط¯ط© (Items, Suppliers, PurchaseOrders, PurchaseRequests, SalesQuotes, SalesOrders, DeliveryOrders, SaleReturns, Warehouses, Categories, ItemTypes, Budgets, InventoryAdjustments, StockTransfers). ظˆط³ظ… `data-server-paged` ط¬ط¯ظٹط¯ ظپظٹ site.js ظ„ظٹ opt-out ظ…ظ† ظ…ط­ط±ظƒ client-side. |
| 3.1 (ظƒط«ط§ظپط© ط§ظ„ط¬ط¯ط§ظˆظ„) | ط§ظ„ط¢ظ„ظٹط© ظ…ظ†ط¬ط²ط© ط£طµظ„ظ‹ط§ (site.js `initTableDensity` + ط²ط± `[data-table-density-toggle]` ظپظٹ `_Layout:507`)ط› ظ†ط¹ظٹط¯ ط§ظ„طھظ…ظˆظٹظ„ (styling) ظپظ‚ط· ط¹ط¨ط± tokens ظˆظ„ط§ ظ†ظ„ظ…ط³ JS. |
| ظ†ط·ط§ظ‚ P4 | Home (Dashboard 3.4) + Sales (Index/Details) طھط¬ط±ظٹط¨ظٹظ‡ط§ ط§ظ„ظ…ط±ط­ظ„ط© ط§ظ„ط£ظˆظ„ظ‰ط› ط§ظ„ط¨ط§ظ‚ظٹ ظپظٹ P5. |

## 2. طھطµط­ظٹط­ط§طھ ط§ظ„طھظˆظƒظ†ط² ط¨ط¹ط¯ ظپط­طµ ط§ظ„طھط¨ط§ظٹظ† (ظ…ظ‚ط§ط¨ظ„ ط§ظ„ظ…ط§ظ†ط¯ط§ظٹطھ آ§3)

ظپط­طµ AA ط¹ظ„ظ‰ ط§ظ„ط£ط³ط·ط­ ط§ظ„ط­ظ‚ظٹظ‚ظٹط© ط£ط«ط¨طھ ظپط´ظ„ 3 ظ‚ظٹظ… ظ…ط§ظ†ط¯ط©ط› ط§ظ„طھطµط­ظٹط­ ط¨ط¯ط§ط®ظ„ ط³ظ„ط§ظ„ط© ط§ظ„ظ„ظˆظ† ظ†ظپط³ظ‡ط§ ظ…ط¹ ط§ظ„ظ…ط­ط§ظپط¸ط© ط¹ظ„ظ‰ ط§ظ„طھط±طھظٹط¨ ط§ظ„ظ‡ط±ظ…ظٹ:

| ط§ظ„طھظˆظƒظ† | ظ‚ظٹظ…ط© ط§ظ„ظ…ط§ظ†ط¯ط§ظٹطھ | ط§ظ„ظ‚ظٹظ…ط© ط§ظ„ظ†ظ‡ط§ط¦ظٹط© | ط§ظ„ط³ط¨ط¨ |
|---|---|---|---|
| `--text-faint` (ظپط§طھط­) | `#98A1AE` (2.61:1) | **`#677380`** (4.51:1 ط¹ظ„ظ‰ `#F6F7F9`ط› 4.84 ط¹ظ„ظ‰ ط§ظ„ط£ط¨ظٹط¶) | طھط­طھ 4.5:1 ط¹ظ„ظ‰ ظƒظ„ ط£ط³ط·ط­ ط§ظ„ظˆط¶ط¹ ط§ظ„ظپط§طھط­ |
| `--text-faint` (ط¯ط§ظƒظ†) | `#5F6B80` (3.30:1) | **`#7C889B`** (4.95:1 ط¹ظ„ظ‰ `#101828`ط› 4.50 ط¹ظ„ظ‰ `#16203A`) | طھط­طھ 4.5:1 |
| `--primary-hover` (ط¯ط§ظƒظ†) | `#3B7FD1` (4.08:1) | **`#3376BD`** (4.70:1)` | `--on-primary` ط£ط¨ظٹط¶ ظٹظپط´ظ„ ط¹ظ„ظ‰ ط§ظ„ط­ط§ظ„ط© ط§ظ„ط£طµظ„ظٹط© |
| `--accent` (ظپط§طھط­) | `#B08A2E` (3.22:1) | ظƒظ…ط§ ظ‡ظٹ (3.22:1) | ط²ط®ط±ظپظٹ ظپظ‚ط· â€” ظ…ظ…ظ†ظˆط¹ ط­ظ…ظ„ ظ†طµ ط¨ط´ط±ظٹط§ ط¹ظ„ظٹظ‡ط§ط› ط§ظ„ظ†طµ ط¹ط¨ط± `--accent-strong` |
| `--warning` (ظپط§طھط­طŒ ط®ظ„ظپظٹط© badge) | `#9A6700` ط¹ظ„ظ‰ `#FFF4D6` = 4.44:1 | **`#8F5F00`** (5.04:1 ط¹ظ„ظ‰ badge) | badge ظ†طµ طµط؛ظٹط± ط؛ظٹط± ظƒط¨ظٹط± (large text) |

ط¶ظˆط§ط¨ط· ط§ظ„ط§ط³طھط®ط¯ط§ظ… ط§ظ„ظ†ظ‡ط§ط¦ظٹط©:
- `--text-faint` ظ„ظ„طھط¹ظ„ظٹظ‚ط§طھ/ط§ظ„ط·ظˆط§ط¨ط¹ ط§ظ„ط£ظ‚ظ„ ط£ظ‡ظ…ظٹط© ظپظ‚ط·طŒ ظپظٹ ط§ظ„ظˆط¶ط¹ظٹظ†طŒ ط¨ط´ط±ط· ط£ظ„ط§ ظٹظƒظˆظ† ظ‡ظˆ ط§ظ„ظ†ط§ظ‚ظ„ ط§ظ„ظˆط­ظٹط¯ ظ„ظ…ط¹ظ„ظˆظ…ط© ط­ط±ط¬ط©.
- `--accent` ظ„ظ„ظ†ظ‚ط§ط· ظˆط§ظ„ط®ط·ظˆط· ظˆط§ظ„ط®ظ„ظپظٹط§طھ ط§ظ„ط²ط®ط±ظپظٹط©ط› ط£ظٹ ظ†طµ ظ†ط­ط§ط³ظٹ (Brass) ط¹ط¨ط± `--accent-strong`.
- `--on-primary` ط£ط¨ظٹط¶ ظپظٹ ط§ظ„ظˆط¶ط¹ظٹظ†ط› ظ…ط¹ `--primary-hover` ط§ظ„ط¯ط§ظƒظ† ط§ظ„ظ…ط¹ط¯ظ‘ظ„ ط£ط¹ظ„ط§ظ‡ ظٹط¨ظ‚ظ‰ 4.70:1.

## 3. ط§ظ„طھظˆظƒظ†ط² ط§ظ„ظ†ظ‡ط§ط¦ظٹط©

### 3.1 ط§ظ„ظˆط¶ط¹ ط§ظ„ظپط§طھط­ (`:root[data-theme="light"]`)

```
--bg                : #F6F7F9   ط®ظ„ظپظٹط© ط§ظ„طµظپط­ط©
--surface           : #FFFFFF   ط§ظ„ط¨ط·ط§ظ‚ط§طھ/ط§ظ„ط¬ط¯ط§ظˆظ„/ط§ظ„ظ€Sheet
--surface-2         : #F9FAFB   طµظپ ط§ظ„ط²ظٹط¨ط±ط§/ط§ظ„ظ…ظ†ط§ط·ظ‚ ط§ظ„ظ…ط¯ظ…ط¬ط©
--surface-elevated  : #FFFFFF   ط­ظˆط§ط±ط§طھ/ط§ظ„ظ‚ظˆط§ط¦ظ… ط§ظ„ظ…ظ†ط¨ط«ظ‚ط© (ط¸ظ„)

--border-subtle     : #E7E9EE
--border            : #D8DCE3
--border-strong     : #B9BFC9   ظپط§طµظ„ ط§ظ„ط¬ط¯ظˆظ„ طھط­طھ ط§ظ„ط±ط£ط³

--text              : #0D1520
--text-secondary    : #3D4756
--text-muted        : #66707E
--text-faint        : #677380   â†گ ظ…ط¹ط¯ظژظ‘ظ„ AA

--primary           : #00213F   Navy ط¹ظ…ظٹظ‚
--primary-hover     : #06305A
--primary-active    : #0A3A6B
--on-primary        : #FFFFFF
--primary-subtle    : #EAF0F7   ط®ظ„ظپظٹط© طھظپط¹ظٹظ„ ط§ظ„ظ‚ط§ط¦ظ…ط©/ط§ظ„ط­ط´ظˆط§طھ

--accent            : #B08A2E   ط¨ط±ط§ط³ (ط²ط®ط±ظپظٹ)
--accent-strong     : #8C6D1F   ظ†طµ ط¨ط±ط§ط³ (AA ط¹ظ„ظ‰ ط£ط¨ظٹط¶ 4.86)

--success           : #0F7B4D   (ظ†طµ/ط¨ط§ظƒط¯ط¬) 5.30
--warning           : #8F5F00   (ظ†طµ) 5.52 â€” ط¬ط±ظ‰ طھطµط­ظٹط­ظ‡ ظ…ظ† #9A6700 ظ„ظ„ظ€badge
--danger            : #B42318
--info              : #175CD3
```

ط­ط§ظ„ط§طھ ط§ظ„ط­ط´ظˆط§طھ (Badge backgrounds):
```
--success-bg        : #E7F5EE   (ظ†طµ success 4.72)
--warning-bg        : #FFF4D6   (ظ†طµ 8F5F00 5.04)
--danger-bg         : #FEECEB   (ظ†طµ danger 5.76)
--info-bg           : #EAF1FD   (ظ†طµ info 5.27)
--neutral-bg        : #EEF1F5   (ظ†طµ secondary)
```

### 3.2 ط§ظ„ظˆط¶ط¹ ط§ظ„ط¯ط§ظƒظ† (`:root[data-theme="dark"]`)

```
--bg                : #0A101E   Navy ط¯ط§ظƒظ† ط¹ظ…ظٹظ‚
--surface           : #101828
--surface-2         : #16203A   طµظپ ط§ظ„ط²ظٹط¨ط±ط§
--surface-elevated  : #182338   ط­ظˆط§ط±ط§طھ/ظ…ظ†ط¨ط«ظ‚ط§طھ

--border-subtle     : rgba(255,255,255,.07)
--border            : rgba(255,255,255,.12)
--border-strong     : rgba(255,255,255,.20)

--text              : #EAEDF3
--text-secondary    : #C0C7D4
--text-muted        : #8B95A7
--text-faint        : #7C889B   â†گ ظ…ط¹ط¯ظژظ‘ظ„ AA

--primary           : #2B6CB8
--primary-hover     : #3376BD   â†گ ظ…ط¹ط¯ظژظ‘ظ„ AA (ظƒط§ظ† #3B7FD1)
--primary-active    : #3A86D9
--on-primary        : #FFFFFF
--primary-subtle    : #214160   ط£ظˆ rgba(43,108,184,.16)

--accent            : #D4B36A
--accent-strong     : #CFA84C

--success           : #4CC38A
--warning           : #E3B341
--danger            : #F97066
--info              : #6BA6F5
```

ط­ط´ظˆط§طھ dark: `--success-bg: rgba(76,195,138,.14)`طŒ `--warning-bg: rgba(227,179,65,.14)`طŒ `--danger-bg: rgba(249,112,102,.14)`طŒ `--info-bg: rgba(107,166,245,.14)` â€” ط§ظ„ظ†طµظˆطµ ط§ظ„ظ…ظ„ظˆظ†ط© ط¹ظ„ظٹظ‡ط§ طھط¸ظ„ â‰¥ 5:1 (ظ…ط­ط³ظˆط¨ط© ط¹ظ„ظ‰ surface).

## 4. ط§ظ„ظ‚ظٹط§ط³ (Typography & Rhythm)

| ط§ظ„ط¯ظˆط± | ط§ظ„ط¹ط§ط¦ظ„ط© | ط§ظ„ط­ط¬ظ…/ط§ظ„ط§ط±طھظپط§ط¹ | ط§ظ„ظˆط²ظ† |
|---|---|---|---|
| H1 (ط¹ظ†ظˆط§ظ† ط§ظ„طµظپط­ط©) | Cairo | 24/32 | 700 |
| H2 (ط£ظ‚ط³ط§ظ… ط¯ط§ط®ظ„ ط§ظ„ط¨ط·ط§ظ‚ط©) | Cairo | 18/26 | 700 |
| Body | Cairo | 14/22 | 400 |
| Table Cell | Cairo | 13.5/20 (tab) | 400 |
| Numeric/Currency | ط¨ظٹط§ظ†ط§طھ ط±ظ‚ظ…ظٹط© | 14/22 **ظ†ظپط³ ط­ط¬ظ… ط§ظ„ط®ظ„ظٹط© ط§ظ„ظ…ط­ظٹط·ط© ط¥ظ† ط£ظ…ظƒظ†** | 500 |
| Label ظ†ظ…ظˆط°ط¬ | Cairo | 13/18 | 600 |
| Caption/Faint | Cairo | 12/16 | 400 |

- ط§ظ„ط£ط±ظ‚ط§ظ…: `font-variant-numeric: tabular-nums`طŒ ط§طھط¬ط§ظ‡ LTR ط¹ط¨ط± `.num` ظˆ `dir="ltr"` ظˆظ…ط­ط§ط°ط§ط© ظ†ظ‡ط§ظٹط© ط§ظ„ط¨ظ„ظˆظƒ ظپظٹ RTL ط¨ظ‚ظˆط©.
- ط§ظ„ط³ط·ط± ط§ظ„ظ…ط±ظƒط²ظٹ ظ„ظ„ط¹ظ…ظ„ط©: `Services/Money.cs` ظ…ظˆط¬ظˆط¯ â€” ظ†ظ…ط¯ظ‘ظ‡ ط¨ط£ط¯ط§ط© ظ…ظˆط­ظ‘ط¯ط© ظ„ظ„ط¹ط±ط¶ ظپظٹ ط§ظ„ظ‚ظˆط§ظ„ط¨ ظپظ‚ط· (ظ„ط§ ظ†ط؛ظٹط± ط§ظ„ط¨ظ†ظٹط©).

Spacing scale: `--space-1..8` = 4/8/12/16/24/32/48/64.
Radius: `--radius-xs 4, sm 6, md 8, lg 12, xl 16`.
Elevation: `--shadow-1` (card)طŒ `--shadow-2` (drawer)طŒ `--shadow-3` (dialog/ط³ط·ط­ ط¹ط§ط¦ظ…) â€” ظ†ط³ط¨ ط¹طھط§ظ…ط© ظ…ظ†ط®ظپط¶ط©طŒ ظ„ط§ ط¸ظ„ط§ظ„ ط«ظ‚ظٹظ„ط©.
Motion: `120ms` hover/focusطŒ `180ms` ط§ظ†طھظ‚ط§ظ„ط§طھ ط§ظ„ط­ط§ظ„ط©طŒ `240ms` ط¯ط®ظˆظ„/ط®ط±ظˆط¬ ط§ظ„ط­ظˆط§ط±. ط§ط­طھط±ظ… `prefers-reduced-motion` (ظ…ظ†ط¬ط² ط£طµظ„ظ‹ط§ ظپظٹ count-up).
Focus: `2px` ط­ظ„ظ‚ط© `--primary`طŒ offset `2px`طŒ ظ„ط§ ط¥ط®ظپط§ط، ظ†ظ‡ط§ط¦ظٹ.

## 5. ط®ط±ظٹط·ط© ط±ط¨ط· Bootstrap vars â†گ ط§ظ„طھظˆظƒظ†ط²

طھظڈط·ط¨ظژظ‘ظ‚ ظپظٹ `tokens.css` ط¹ظ„ظ‰ `:root[data-theme]` ط¨ط§ظ„ظ…ط±ظˆط± ط¹ظ„ظ‰ Bootstrap 5.3 semantic root vars:
- `--bs-body-bg` â†گ `--bg`ط› `--bs-body-color` â†گ `--text`
- `--bs-primary` â†گ `--primary`ط› `--bs-primary-rgb` â†گ ط§ظ†ظپطµط§ظ„ RGB
- `--bs-primary-color` â†گ `--on-primary`
- `--bs-primary-bg-subtle` / `-border-subtle` / `-text` â†گ `--primary-subtle`/ط¨ط·ط§ظ‚ط§طھظ‡ط§
- `--bs-border-color` â†گ `--border`ط› `--bs-secondary-color` â†گ `--text-secondary`ط› `--bs-secondary-bg` â†گ `--surface-2`
- `--bs-emphasis-color` â†گ `--text`ط› `--bs-heading-color` â†گ `--text`
- `--bs-link-color` / `-hover` â†گ `--primary` / `--primary-hover` (ظ…ط¹ ط¶ظ…ط§ظ† طھط­ظˆظ‘ظ„ ط¨ط±ظ†ط¯ signature)
- `--bs-focus-ring-color` â†گ `rgba(primary, 0.28)`
- `--bs-table-bg`, `--bs-table-hover-bg` â†گ `--surface` / `--surface-2`
- ط§ظ„ظ†ط¬ط§ط­/ط§ظ„طھط­ط°ظٹط±/ط§ظ„ط®ط·ط±/ط§ظ„ظ…ط¹ظ„ظˆظ…ط§طھ + `-bg-subtle` ظˆ `-border-subtle` ظˆ `-text` â†گ ط§ظ„ط³ظ„ط§ظ„ط§طھ ط£ط¹ظ„ط§ظ‡

> ط§ظ„ط¹ظ„ط§ظ…ط§طھ ط§ظ„طھط¬ط§ط±ظٹط©: طھظڈط¹ط±ظژظ‘ظپ ط¹ط¨ط± `RenderThemeCss` ظپظٹ `BrandingService.cs` ط¨ظ€ `:root[data-theme="light"]{...}` ظˆ `:root[data-theme="dark"]{...}` (ظˆظ„ظٹط³ `:root` ظپظ‚ط·). ظ‡ط°ط§ ط¥طµظ„ط§ط­ ط¥ظ„ط²ط§ظ…ظٹ A (ط§ظ„ظ…ط§ظ†ط¯ط§ظٹطھ آ§3.4) â€” ظپظٹ P1.

## 6. ظƒطھط§ظ„ظˆط¬ ط§ظ„ظ…ظƒظˆظ†ط§طھ (ط§ظ„ظ…ط·ظ„ظˆط¨ 12 â€” آ§7)

ظ…ط±ط§ط¬ط¹ ظ…ظ„ظپط§طھ ظ…ظ‚طھط±ط­ط© (ظ„ط§ طھظڈظƒطھط¨ ظ‚ط¨ظ„ ط§ط¹طھظ…ط§ط¯ P1 ط¥ظ„ط§ ط¥ط°ط§ ظپط±ط¶طھظ‡ط§ ظ…ط±ط­ظ„ط© ط³ط§ط¨ظ‚ط©):

| # | ط§ظ„ظ…ظƒظˆظ‘ظ† | ظ…ط³ط§ط± ط§ظ„ط§ظ‚طھط±ط§ط­ | ط³ظ„ظˆظƒ ظ…ط¹ظ„ظژظ‘ظ‚ ط¨ط§ظ„طھظˆظƒظٹظ† |
|---|---|---|---|
| 1 | Button | `Views/Shared/Components/Button.cshtml` ط£ظˆ ط£طµظ†ط§ظپ CSS ظپظٹ `components.css` | `btn`, `btn-primary/secondary/ghost/destructive`, `.btn-icon`, `.spinner-border` ظپظٹ-button (ظˆظڈط¬ط¯ `data-loading-submit` ظ…ظ†ط¬ط²ط§ ظپظٹ site.js â€” ظٹط¯ط¹ظ‘ظ… ط§ظ„طھظˆظƒظٹظ†ط² ظپظ‚ط·) |
| 2 | PageHeader | `Views/Shared/_PageHeader.cshtml` (ط¬ط¯ظٹط¯) | ط­ظ„ ظ„ظ€ 2.3: ط®ظ„ظپظٹط© `--surface`طŒ ط­ط§ظپط© `--border`, ط§ظ„ط¹ظ†ظˆط§ظ† H1طŒ ط²ط± آ«ط±ط¬ظˆط¹آ» `bi-arrow-right` RTLطŒ ط²ط± ط§ظ„ط¥ط¬ط±ط§ط، `btn-primary` ظˆط§ط­ط¯ ظƒط­ط¯ ط£ظ‚طµظ‰ |
| 3 | DataTable | `Views/Shared/Components/DataTable.cshtml` + `.table-container` | `--surface`, hover `--surface-2`, `.num`, ظƒط«ط§ظپط© `data-table-density` (ظ…ظ†ط¬ط²)طŒ freeze `nvs-frozen-col` |
| 4 | EmptyState | `Views/Shared/_EmptyState.cshtml` (ط¬ط¯ظٹط¯) | ط£ظٹظ‚ظˆظ†ط© 56px ظپظٹ ط¯ط§ط¦ط±ط© tinted + ط¹ظ†ظˆط§ظ† + ظˆطµظپ + ط¥ط¬ط±ط§ط، ظˆط§ط­ط¯ |
| 5 | StatusBadge | `Views/Shared/_StatusBadge.cshtml` (ط¬ط¯ظٹط¯) | ظ‚ط§ظ…ظˆط³ ظ…ظˆط­ظ‘ط¯: draft/pending/approved/posted/cancelled/paid/partially-paid/overdue â€” ط£ظ„ظˆط§ظ† tokens |
| 6 | Pager | `Views/Shared/_Pager.cshtml` (ظٹظڈط­ط¯ظژظ‘ط« ط´ظƒظ„ظٹظ‹ط§) | `PagerExtensions.PageSize=50` ظ…ظ†ط¬ط²ط› ظ†ط¹ظٹط¯ ط§ظ„طھظ…ظˆظٹظ„ ظپظ‚ط· |
| 7 | FormField | `Views/Shared/_FormField.cshtml` (ط¬ط¯ظٹط¯ ط§ط®طھظٹط§ط±ظٹ) | ظ„طµظ‚ `label` + `input` + `validation` ط¹ظ„ظ‰ Bootstrap ط£طµظ†ط§ظپ |
| 8 | Dialog/Drawer | site.js `#appConfirmModal` ظ…ظˆط¬ظˆط¯ط› `components.css` | ظ…ظƒظˆظ‘ظ†ط§طھ Bootstrap ظ…ظ†ط¬ط²ط› ظ„ط§ ظ…ط¶ط§ظ‡ط§ط© |
| 9 | Toolbar/FilterBar | `.filter-bar` ظپظٹ components.css | ظ„ظˆط­ط§طھ ط§ظ„طھطµظپظٹط©: ط²ط± ط¨ط­ط« `--primary`, `select[data-auto-submit]` (ط¹ظ‚ط¯ ظ…ظڈط­ط§ظپط¸ ط¹ظ„ظٹظ‡) |
| 10 | StatCard KPI | `Views/Home/_StatCard.cshtml` (ط¬ط¯ظٹط¯) | `--surface`, ط±ظ‚ظ… tabular-nums, `data-count` ظ…ط¯ط¹ظˆظ… ظ…ظ†ط¬ط²ط§ |
| 11 | toast | `toast-region/app-toast` (ظ…ظ†ط¬ط² site.js) | طھظ…ظˆظٹظ„ ط¹ط¨ط± tokens |
| 12 | Skeleton | `.skeleton-row/.skeleton` (ظ…ظ†ط¬ط² site.js) | طھظ…ظˆظٹظ„ ط¹ط¨ط± `--surface-2` ظƒظ…ط®ط·ط· ظ…ظˆط­ط¯ |

## 7. ط¨ظ†ظٹط© ط§ظ„طµظپط­ط© ط§ظ„ظ…ظ†طھط¸ظ…ط©

- ظ‚ط§ط¦ظ…ط©: `PageHeader` â†’ `FilterBar` â†’ `DataTable` â†’ `_Pager` â†’ `EmptyState`.
- ظ†ظ…ظˆط°ط¬: `PageHeader` â†’ ط¨ط·ط§ظ‚ط© ظˆط§ط­ط¯ط© ط£ظˆ ط£ظ‚ط³ط§ظ… ظ…ط³طھظ‚ظ„ط© ط¨ط¹ظ†ط§ظˆظٹظ† H2ط› ط²ط±ط§ ط­ظپط¸/ط¥ظ„ط؛ط§ط، ظپظٹ طھط°ظٹظٹظ„ `--surface`.
- ظ…ط³طھظ†ط¯: ظ†ظ…ط· ظ…ط´طھط±ظƒ ظˆط§ط­ط¯ ظ„ظ„ظ…ط¨ظٹط¹ط§طھ/ط§ظ„ظ…ط´طھط±ظٹط§طھ (طھظپط§ط³ظٹط± ظپظٹ `PrintStudio`/`PrintDocument.cshtml` ظ„ط§ طھظڈظ…ظژط³).
- ط£ظپط¹ط§ظ„ ظ…ط§ظ„ظٹط© ظ„ط§ ظٹظ…ظƒظ† ط¹ظƒط³ظ‡ط§: `confirmAction()` ظ…ظˆط¬ظˆط¯ط© (site.js) â€” ظ†ط³طھط¯ط¹ظٹظ‡ط§ ط­ظ‚ظٹظ‚ط©ظ‹ ظ…ط¹ `btn-danger`.

## 8. ط§ظ„ظ‚ظˆط§ط¹ط¯ RTL/BiDi (ط£ط¹ظ„ظ‰ ط£ظˆظ„ظˆظٹط©)

- `.num`: `direction:ltr; unicode-bidi:embed; text-align:end` ط¹ظ„ظ‰ ط§ظ„طھظˆظƒظٹظ† `--num-dir`.
- ط³ظ‡ظ… ط§ظ„ط±ط¬ظˆط¹ ظپظٹ RTL = `bi-arrow-right`ط› ط£ظٹظ‚ظˆظ†ط§طھ ط§ظ„طھط±ط­ظٹظ„ طھظڈظ‚ظ„ط¨ ط¨ظ…ظ†ط·ظ‚ ط¨ظ†ط§ط، ظ„ط§ ط¨ط¹ظ„ط§ظ‚ط© CSS ظ…ط·ظ„ظ‚ط©.
- ط§ظ„ظ…ظ‚ظٹط§ط³ â€” ط¹ط¯ط¯ ظٹطھظ… ط§ظ„ظ…ط­ط§ط°ط§ط© `-` ظƒط¹ظ„ط§ظ…ط© ط³ط§ظ„ط¨ط© ظ…ط­ظپظˆط¸ط©.

## 9. ط§ظ„ط­ط§ظ„ط§طھ ط§ظ„ظ…ط³ظ…ظˆط­ ط¥ط¨ظ‚ط§ط¤ظ‡ط§ ط®ط§ط±ط¬ ط§ظ„طھظˆط­ظٹط¯ (ظ…ظˆط«ظ‚ط©)

- `Reports/*` (BalanceSheet, TrialBalance, IncomeStatement, BudgetVariance, AuditLedger, Sales, Purchases, Payments): `data-table-plain` + ط·ط¨ط§ط¹ط©ط› ط£ط¨ط¬ط¯ظٹط§طھظ‡ط§ ط®ط§ط±ط¬ ط§ظ„طھظپطµظٹظ„.
- `Sales/Create`, `PurchaseOrders/Create`, `SalesQuotes/Create`, `Batch/Sales`, `StockTransfers/Create`: ط¬ط¯ط§ظˆظ„ ط£ط³ط·ط± ظ‚ط§ط¨ظ„ط© ظ„ظ„طھط­ط±ظٹط± (`#itemsBody`, `.item-select`) â€” ط¹ظ‚ط¯ JS ظƒط§ظ…ظ„ ظ…ط­ظپظˆط¸ ظƒظ…ط§ ظ‡ظˆ.
- `Items/PrintLabel.cshtml`: طµظپط­ط© ظ…ظ„طµظ‚ ط£طµظ„ظٹط©طŒ ظ„ط§ طھظ„ظ…ط³ (ط·ط¨ط§ط¹ط©).
