const fs = require('fs');
const { chromium } = require('playwright');

const BASE = process.env.BASE_URL || 'http://localhost:5165';
const USER = process.env.VIX_USER || 'admin';
const PASS = process.env.VIX_PASS || 'Admin@123';

// Every GET action that renders a view, enumerated from src/NewVixSmart.Web/Controllers.
// Excluded: /Account/* (redirects once authenticated), POST-only actions, JSON/Partial
// results, and file-returning actions (Pdf/Xlsx/Csv/Template/Download/Export*/Ledger*).
// Parameterised routes use id 1 (Details fall back to the integer key).
const ROUTE_MANIFEST = [
  '/',
  '/Accounts',
  '/Accounts/Create',
  '/Accounts/Edit/1',
  '/Backup',
  '/Batch',
  '/Batch/Adjustment',
  '/Batch/Sales',
  '/Budgets',
  '/Budgets/Manage/2026',
  '/Categories',
  '/Customers',
  '/Customers/Create',
  '/Customers/Edit/1',
  '/Customers/Ledger/1',
  '/DeliveryOrders',
  '/DeliveryOrders/Create',
  '/ExportCenter',
  '/Fiscal',
  '/ImportCenter',
  '/InventoryAdjustments',
  '/InventoryAdjustments/Create',
  '/ItemTypes',
  '/Items',
  '/Items/Create',
  '/Items/Details/1',
  '/Items/Edit/1',
  '/Items/PrintLabel/1',
  '/Payments',
  '/Payments/Create',
  '/PurchaseOrders',
  '/PurchaseOrders/Create',
  '/PurchaseOrders/Edit/1',
  '/PurchaseRequests',
  '/PurchaseRequests/Create',
  '/PurchaseReturns',
  '/PurchaseReturns/Create',
  '/Purchases',
  '/Purchases/Create',
  '/Reports',
  '/Reports/Aging',
  '/Reports/AuditLedger',
  '/Reports/BalanceSheet',
  '/Reports/BudgetVariance',
  '/Reports/CashFlow',
  '/Reports/Dashboard',
  '/Reports/IncomeStatement',
  '/Reports/Payments',
  '/Reports/Purchases',
  '/Reports/Sales',
  '/Reports/TrialBalance',
  '/SaleReturns',
  '/SaleReturns/Create',
  '/Sales',
  '/Sales/Create',
  '/SalesOrders',
  '/SalesOrders/Create',
  '/SalesQuotes',
  '/SalesQuotes/Create',
  '/SalesQuotes/MassConvert',
  '/Settings',
  '/Settings/Branding',
  '/Settings/PrintPreview?group=sales_invoice',
  '/Settings/Printing',
  '/Stock',
  '/Stock/LowStock',
  '/Stock/Report',
  '/StockTransfers',
  '/StockTransfers/Create',
  '/Suppliers',
  '/Suppliers/Create',
  '/Suppliers/Edit/1',
  '/Suppliers/Ledger/1',
  '/Suppliers/Quotes',
  '/Users',
  '/Users/Create',
  '/Warehouses',
  '/Warehouses/Create',
  '/Warehouses/Edit/1'
];

// Routes scanned again in dark mode (previously failing + chrome-heavy).
const DARK_SUBSET = [
  '/',
  '/Settings',
  '/Settings/Branding',
  '/Reports',
  '/Reports/AuditLedger',
  '/Reports/Dashboard',
  '/Items',
  '/Customers',
  '/Sales',
  '/Purchases',
  '/Accounts'
];

const axeSource = fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');
const A11Y_TAGS = ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'];

async function login(page, theme) {
  await page.goto(BASE + '/Account/Login', { waitUntil: 'domcontentloaded' });
  await page.evaluate(t => { try { localStorage.setItem('theme-mode', t); } catch (e) {} }, theme);
  await page.goto(BASE + '/Account/Login', { waitUntil: 'domcontentloaded' });
  await page.fill('#Username', USER);
  await page.fill('#Password', PASS);
  await Promise.all([
    page.waitForNavigation({ waitUntil: 'load' }),
    page.click('button[type="submit"]')
  ]);
  await page.waitForTimeout(1200);
}

async function axeRun(page) {
  await page.evaluate(src => { (0, eval)(src); }, axeSource);
  return page.evaluate(async (tags) => {
    const res = await window.axe.run(document, { runOnly: { type: 'tag', values: tags } });
    return res.violations.map(v => ({
      id: v.id, impact: v.impact, nodes: v.nodes.length,
      targets: v.nodes.slice(0, 3).map(n => (n.target || []).join(' '))
    }));
  }, A11Y_TAGS);
}

const isSerious = v => v.impact === 'critical' || v.impact === 'serious';

async function main() {
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  await login(page, 'light');
  const routes = ROUTE_MANIFEST;

  const failures = [];
  const summary = { light: {}, dark: {} };

  for (const route of routes) {
    let status = 0;
    try { const r = await page.goto(BASE + route, { waitUntil: 'load' }); status = r ? r.status() : 0; } catch { status = 0; }
    await page.waitForTimeout(350);
    let violations = [];
    try { violations = await axeRun(page); } catch (e) { violations = [{ id: 'AXE-EXEC-ERR', impact: 'serious', nodes: 1, targets: [e.message] }]; }
    summary.light[route] = { status, violations };
    const serious = violations.filter(isSerious);
    if (status !== 200 || serious.length) failures.push({ route, status, violations: serious });
  }

  const darkContext = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'dark' });
  const dp = await darkContext.newPage();
  await login(dp, 'dark');
  for (const route of DARK_SUBSET) {
    let status = 0;
    try { const r = await dp.goto(BASE + route, { waitUntil: 'load' }); status = r ? r.status() : 0; } catch { status = 0; }
    await dp.waitForTimeout(350);
    let violations = [];
    try { violations = await axeRun(dp); } catch (e) { violations = [{ id: 'AXE-EXEC-ERR', impact: 'serious', nodes: 1, targets: [e.message] }]; }
    const themeApplied = await dp.evaluate(() => document.documentElement.getAttribute('data-theme'));
    summary.dark[route] = { status, themeApplied, violations };
    const serious = violations.filter(isSerious);
    if (status !== 200 || serious.length) failures.push({ route: route + ' [dark]', status, themeApplied, violations: serious });
  }
  await darkContext.close();
  await browser.close();

  const countByImpact = rows => rows.reduce((a, v) => ({ ...a, [v.impact]: (a[v.impact] || 0) + 1 }), {});
  const lightAll = Object.values(summary.light).flatMap(r => r.violations);
  const darkAll = Object.values(summary.dark).flatMap(r => r.violations);

  console.log(`Routes scanned: light=${Object.keys(summary.light).length}, dark=${Object.keys(summary.dark).length}`);
  console.log('Light non-critical/serious: ' + JSON.stringify(countByImpact(lightAll.filter(v => !isSerious(v)))));
  console.log('Dark  non-critical/serious: ' + JSON.stringify(countByImpact(darkAll.filter(v => !isSerious(v)))));
  const nonSeriousAll = [...lightAll, ...darkAll].filter(v => !isSerious(v));
  const countImpact = impact => nonSeriousAll.filter(v => v.impact === impact).length;
  console.log(`Total moderate: ${countImpact('moderate')}, total minor: ${countImpact('minor')} (reported only - not gate-failing)`);
  for (const [route, r] of Object.entries(summary.dark)) console.log(`  dark ${r.themeApplied} ${route}`);

  if (failures.length) {
    console.log('GATE: FAIL');
    console.log(JSON.stringify(failures, null, 2));
    process.exit(1);
  }
  console.log(`GATE: PASS (${Object.keys(summary.light).length} light routes + ${Object.keys(summary.dark).length} dark, 0 critical/serious axe violations)`);
}

main().catch(e => { console.error(e); process.exit(1); });