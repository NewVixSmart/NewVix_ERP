const fs = require('fs');
const { chromium } = require('playwright');

const BASE = process.env.BASE_URL || 'http://localhost:5165';
const USER = process.env.VIX_USER || 'admin';
const PASS = process.env.VIX_PASS || 'Admin@123';

// Every GET action that renders a view, enumerated from src/NewVixSmart.Web/Controllers.
// Excluded: /Account/* (redirects once authenticated), POST-only actions, JSON/Partial
// results, and file-returning actions (Pdf/Xlsx/Csv/Template/Download/Export*/Ledger*).
// Routes that need a row of real data carry a `{id}`/`{publicId}` placeholder instead of a
// literal `/1`: the id is scraped from a status-appropriate list page (see ID_SOURCES), so a
// document that no longer exists fails resolution loudly instead of rendering a 404 page.
const ROUTE_MANIFEST = [
  '/',
  '/Account/ChangePassword',
  '/Accounts',
  '/Accounts/Create',
  '/Accounts/Edit/{id}',
  '/Backup',
  '/Batch',
  '/Batch/Adjustment',
  '/Batch/Sales',
  '/Budgets',
  '/Budgets/Manage/2026',
  '/Categories',
  '/Customers',
  '/Customers/Create',
  '/Customers/Edit/{id}',
  '/Customers/Ledger/{id}',
  '/Customers/PendingDeliveries/{id}',
  '/DeliveryIssues',
  '/DeliveryIssues/Create',
  '/DeliveryIssues/Details/{publicId}',
  '/DeliveryOrders',
  '/DeliveryOrders/Create',
  '/DeliveryOrders/Details/{publicId}',
  '/ExportCenter',
  '/Fiscal',
  '/ImportCenter',
  '/InventoryAdjustments',
  '/InventoryAdjustments/Create',
  '/ItemTypes',
  '/Items',
  '/Items/Create',
  '/Items/Details/{publicId}',
  '/Items/Edit/{id}',
  '/Items/PrintLabel/{id}',
  '/Payments',
  '/Payments/Create',
  '/Payments/Details/{publicId}',
  '/PurchaseOrders',
  '/PurchaseOrders/Create',
  '/PurchaseOrders/Edit/{id}',
  '/PurchaseOrders/Details/{publicId}',
  '/PurchaseOrders/Receive/{id}',
  '/PurchaseRequests',
  '/PurchaseRequests/Create',
  '/PurchaseReturns',
  '/PurchaseReturns/Create',
  '/PurchaseReturns/Details/{publicId}',
  '/Purchases',
  '/Purchases/Create',
  '/Purchases/Details/{publicId}',
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
  '/SaleReturns/Details/{publicId}',
  '/Sales',
  '/Sales/Create',
  '/Sales/Details/{publicId}',
  '/SalesOrders',
  '/SalesOrders/Create',
  '/SalesOrders/Edit/{id}',
  '/SalesOrders/Details/{publicId}',
  '/SalesQuotes',
  '/SalesQuotes/Create',
  '/SalesQuotes/Details/{publicId}',
  '/SalesQuotes/MassConvert',
  '/Settings',
  '/Settings/Branding',
  '/Settings/PrintPreview?group=sales_invoice',
  '/Settings/Printing',
  '/Stock',
  '/Stock/LowStock',
  '/Stock/Report',
  '/StockReservations',
  '/StockReservations/Create',
  '/StockReservations/Details/{publicId}',
  '/StockTransfers',
  '/StockTransfers/Create',
  '/Suppliers',
  '/Suppliers/Create',
  '/Suppliers/Edit/{id}',
  '/Suppliers/Ledger/{id}',
  '/Suppliers/Quotes',
  '/Users',
  '/Users/Create',
  '/Users/Permissions/{publicId}',
  '/Warehouses',
  '/Warehouses/Create',
  '/Warehouses/Edit/{id}'
];

/**
 * List page each `{id}`/`{publicId}` route is resolved from. The query string matters: an
 * editable purchase order only exists in Draft, and a receive screen only has rows while
 * quantity is still outstanding, so both are resolved from the status they need.
 */
const ID_SOURCES = {
  '/Accounts/Edit/{id}': '/Accounts',
  '/Customers/Edit/{id}': '/Customers',
  '/Customers/Ledger/{id}': '/Customers',
  '/Customers/PendingDeliveries/{id}': '/Customers',
  '/Items/Details/{publicId}': '/Items',
  '/Items/Edit/{id}': '/Items',
  '/Items/PrintLabel/{id}': '/Items',
  '/Suppliers/Edit/{id}': '/Suppliers',
  '/Suppliers/Ledger/{id}': '/Suppliers',
  '/Warehouses/Edit/{id}': '/Warehouses',
  '/DeliveryIssues/Details/{publicId}': '/DeliveryIssues',
  '/DeliveryOrders/Details/{publicId}': '/DeliveryOrders',
  '/Payments/Details/{publicId}': '/Payments',
  '/PurchaseOrders/Details/{publicId}': '/PurchaseOrders',
  '/PurchaseReturns/Details/{publicId}': '/PurchaseReturns',
  '/Purchases/Details/{publicId}': '/Purchases',
  '/SaleReturns/Details/{publicId}': '/SaleReturns',
  '/Sales/Details/{publicId}': '/Sales',
  '/SalesOrders/Details/{publicId}': '/SalesOrders',
  '/SalesOrders/Edit/{id}': '/SalesOrders?status=Draft',
  '/SalesQuotes/Details/{publicId}': '/SalesQuotes',
  '/StockReservations/Details/{publicId}': '/StockReservations',
  '/Users/Permissions/{publicId}': '/Users'
};

/**
 * Routes reachable only from another resolved page, so they are resolved in a second pass.
 *
 * The value is the parent route, and it may be given as `[parentRoute, listPage]`. That form is
 * for a parent that only one document has: the purchase-order index links to Details only, and
 * its Edit link belongs to a draft while its Receive link belongs to a partly received order,
 * so each parent has to be picked out of the list page that still holds that status.
 */
const ID_SOURCE_VIA = {
  '/Items/PrintLabel/{id}': '/Items/Details/{publicId}',
  '/PurchaseOrders/Edit/{id}': ['/PurchaseOrders/Details/{publicId}', '/PurchaseOrders?status=0'],
  '/PurchaseOrders/Receive/{id}': ['/PurchaseOrders/Details/{publicId}', '/PurchaseOrders?status=2']
};

/**
 * The only routes allowed to render an empty page, each with the reason it is unreachable
 * from seeded demo data. An empty page is a gate failure everywhere else: a 404 or a
 * "nothing here yet" grid means the page is unscanned, which is exactly the silent hole
 * this gate is meant to close. Keep this list as short as the reason allows.
 */
const DOCUMENTED_ALLOW_EMPTY = {
  '/Backup': 'the backup index is only populated by a full backup/restore cycle, which writes .bak files outside the throwaway database and is out of scope for an accessibility scan',
  '/DeliveryIssues/Create': 'this is a create form, not a listing: its line table starts empty and is filled either by choosing a source delivery note in the form or by the operator adding lines inline. The source-filled variant is a different url and is covered by the routes that link to it',
  '/DeliveryOrders/Create': 'this is a create form, not a listing: its line table starts empty and is filled either by picking a sales order or invoice in the form or by the operator adding lines inline. The source-filled variant is a different url and is covered by the routes that link to it',
  '/Items/Create': 'the two tables on the item form are a client-side preview of the categories and item types the operator adds inline; they hold no records and start empty by design',
  '/Items/Edit/{id}': 'the two tables on the item form are a client-side preview of the categories and item types the operator adds inline; they hold no records and start empty by design'
};

const ALLOWED_EMPTY = new Set(Object.keys(DOCUMENTED_ALLOW_EMPTY));

const axeSource = fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');
const A11Y_TAGS = ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'];

/**
 * Sign in and do not start crawling until the session cookie actually works. A submit
 * that lands on a fresh /Account/* page means the credentials were rejected (or the POST was
 * swallowed), and crawling on that page is indistinguishable from a crawl of the real app:
 * every route answers 200 and renders no violations, and the id-keyed routes can never find
 * their anchors. Retry instead of letting a silent login failure poison the whole scan.
 */
async function login(page) {
  for (let attempt = 1; attempt <= 4; attempt++) {
    await page.goto(BASE + '/Account/Login', { waitUntil: 'domcontentloaded' });
    await page.fill('#Username', USER);
    await page.fill('#Password', PASS);
    await Promise.all([
      page.waitForNavigation({ waitUntil: 'load' }),
      page.click('button[type="submit"]')
    ]);
    await page.waitForTimeout(500);
    const authed = await page.evaluate(() => !location.pathname.startsWith('/Account/'));
    if (authed) {
      await page.waitForTimeout(800);
      return;
    }
  }
  throw new Error('login did not authenticate after 4 attempts');
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

/**
 * Some keys are GUIDs and some state-gated actions need a specific document status, so a
 * literal `/1` can never resolve. Rather than hardcoding an id that only exists in one
 * database, scrape the real link from the list page that links to it. A route with no
 * matching link is a failure: the alternative was scanning a 404 and calling it a pass.
 */
async function resolveIdRoutes(page, routes) {
  const bySource = new Map();
  for (const route of routes) {
    const source = ID_SOURCES[route];
    if (!source) continue;
    if (!bySource.has(source)) bySource.set(source, []);
    bySource.get(source).push(route);
  }

  const hits = new Map();
  const unresolved = [];
  for (const [source, group] of bySource) {
    let status = 0;
    try { const r = await page.goto(BASE + source, { waitUntil: 'load' }); status = r ? r.status() : 0; } catch { status = 0; }
    await page.waitForTimeout(250);
    const hrefs = await page.evaluate(
      prefixes => prefixes.map(p => {
        const a = document.querySelector(`a[href^="${p}"]`);
        return a ? a.getAttribute('href') : null;
      }),
      group.map(r => r.split('{')[0])
    );
    group.forEach((route, i) => {
      if (hrefs[i]) hits.set(route, hrefs[i]);
      else unresolved.push({ route, source, sourceStatus: status });
    });
  }

  // A route that no list page links to directly is resolved from the page that does.
  const remaining = unresolved.filter(u => !(u.route in ID_SOURCE_VIA));
  for (const [route, via] of Object.entries(ID_SOURCE_VIA)) {
    const [parentRoute, listPage] = Array.isArray(via) ? via : [via, null];
    let parent = null;
    if (listPage) {
      // A named list page is authoritative for that parent: the unfiltered index resolves the
      // same route to whichever order is newest, which is not the one carrying the link.
      let listStatus = 0;
      try { const r = await page.goto(BASE + listPage, { waitUntil: 'load' }); listStatus = r ? r.status() : 0; } catch { listStatus = 0; }
      parent = await page.evaluate(p => {
        const a = document.querySelector(`a[href^="${p}"]`);
        return a ? a.getAttribute('href') : null;
      }, parentRoute.split('{')[0]);
      if (parent) hits.set(parentRoute, parent);
      else { remaining.push({ route, source: listPage, sourceStatus: listStatus }); continue; }
    } else {
      parent = hits.get(parentRoute);
    }
    if (!parent) { remaining.push({ route, source: parentRoute, sourceStatus: 0 }); continue; }
    let status = 0;
    try { const r = await page.goto(BASE + parent, { waitUntil: 'load' }); status = r ? r.status() : 0; } catch { status = 0; }
    const href = await page.evaluate(p => {
      const a = document.querySelector(`a[href^="${p}"]`);
      return a ? a.getAttribute('href') : null;
    }, route.split('{')[0]);
    if (href) hits.set(route, href);
    else remaining.push({ route, source: parent, sourceStatus: status });
  }
  return { hits, unresolved: remaining };
}

/**
 * An empty grid is as unscannable as a 404: axe never sees the markup that only exists once
 * rows exist. Flag it when every table on the page has no body rows, or when the page renders
 * the project's "nothing here yet" notice instead of a table.
 */
async function detectEmptyPage(page) {
  return page.evaluate(() => {
    const tables = [...document.querySelectorAll('table')];
    if (tables.some(t => t.querySelectorAll('tbody tr').length > 0)) return null;
    if (tables.length) {
      const named = tables
        .map(t => (t.closest('.card, section, main, .table-container')?.querySelector('caption, .card-header, h2, h3')?.textContent || '').trim())
        .filter(Boolean);
      return `no body rows in ${tables.length} table(s)${named.length ? `: ${named.join(' | ')}` : ''}`;
    }
    const notice = (document.body.innerText || '').match(/لا (?:توجد|يوجد)[^\n]{0,90}/);
    return notice ? `empty-state notice: ${notice[0].trim()}` : null;
  });
}

const isSerious = v => v.impact === 'critical' || v.impact === 'serious';

async function main() {
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  await login(page);
  const { hits, unresolved } = await resolveIdRoutes(page, ROUTE_MANIFEST);
  const routes = ROUTE_MANIFEST.filter(r => !ID_SOURCES[r] || hits.has(r));

  const failures = [...unresolved.map(u => ({ route: u.route, status: 0, reason: `no matching link on ${u.source} (source status ${u.sourceStatus})` }))];
  const allowedEmpty = [];
  const summary = { light: {} };

  const evaluate = (route, status, violations, empty) => {
    summary.light[route] = { status, empty, violations };
    const serious = violations.filter(isSerious);
    if (status !== 200) { failures.push({ route, status, violations: serious }); return; }
    if (empty) {
      const documented = DOCUMENTED_ALLOW_EMPTY[route];
      if (documented) { allowedEmpty.push({ route, empty, reason: documented }); return; }
      failures.push({ route, status, reason: `page rendered no data (${empty})` });
      return;
    }
    if (serious.length) failures.push({ route, status, violations: serious });
  };

  for (const route of routes) {
    const target = hits.get(route) || route;
    let status = 0;
    try { const r = await page.goto(BASE + target, { waitUntil: 'load' }); status = r ? r.status() : 0; } catch { status = 0; }
    await page.waitForTimeout(350);
    let violations = [];
    try { violations = await axeRun(page); } catch (e) { violations = [{ id: 'AXE-EXEC-ERR', impact: 'serious', nodes: 1, targets: [e.message] }]; }
    let empty = null;
    if (status === 200) { try { empty = await detectEmptyPage(page); } catch { empty = null; } }
    evaluate(route, status, violations, empty);
  }

  await browser.close();

  const countByImpact = rows => rows.reduce((a, v) => ({ ...a, [v.impact]: (a[v.impact] || 0) + 1 }), {});
  const lightAll = Object.values(summary.light).flatMap(r => r.violations);

  console.log(`Routes scanned: light=${Object.keys(summary.light).length}`);
  console.log('Light non-critical/serious: ' + JSON.stringify(countByImpact(lightAll.filter(v => !isSerious(v)))));
  const nonSeriousAll = lightAll.filter(v => !isSerious(v));
  const countImpact = impact => nonSeriousAll.filter(v => v.impact === impact).length;
  console.log(`Total moderate: ${countImpact('moderate')}, total minor: ${countImpact('minor')} (reported only - not gate-failing)`);

  if (allowedEmpty.length) {
    console.log(`documented-empty (visited + scanned, rendered nothing by design): ${allowedEmpty.length}`);
    for (const a of allowedEmpty) console.log(`  documented-empty ${a.route} - ${a.reason}`);
  }

  if (failures.length) {
    console.log(`GATE: FAIL (${failures.length} failing route checks)`);
    console.log(JSON.stringify(failures, null, 2));
    process.exit(1);
  }
  console.log(`GATE: PASS (${Object.keys(summary.light).length} light routes, 0 critical/serious axe violations, 0 empty pages${allowedEmpty.length ? `, ${allowedEmpty.length} documented-empty` : ''})`);
}

main().catch(e => { console.error(e); process.exit(1); });
