const fs = require('fs');
const { chromium } = require('playwright');

const BASE = process.env.BASE_URL || 'http://localhost:5165';
const USER = process.env.VIX_USER || 'admin';
const PASS = process.env.VIX_PASS || 'Admin@123';

// Extra routes reachable from the app but not present as `.sidebar-nav .nav-link` links.
const EXTRA_ROUTES = [
  '/Sales/Create',
  '/Settings/Printing'
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

async function discoverRoutes(page) {
  await page.goto(BASE + '/', { waitUntil: 'load' });
  const links = await page.evaluate(() =>
    Array.from(document.querySelectorAll('.sidebar-nav .nav-link'))
      .map(a => a.getAttribute('href'))
      .filter(Boolean)
  );
  const paths = [...new Set(links.map(h => new URL(h, BASE).pathname))];
  for (const extra of EXTRA_ROUTES) if (!paths.includes(extra)) paths.push(extra);
  if (!paths.length) throw new Error('No nav routes discovered — sidebar markup changed?');
  return paths.sort();
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
  const routes = await discoverRoutes(page);

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
  for (const [route, r] of Object.entries(summary.dark)) console.log(`  dark ${r.themeApplied} ${route}`);

  if (failures.length) {
    console.log('GATE: FAIL');
    console.log(JSON.stringify(failures, null, 2));
    process.exit(1);
  }
  console.log(`GATE: PASS (${Object.keys(summary.light).length} light routes + ${Object.keys(summary.dark).length} dark, 0 critical/serious axe violations)`);
}

main().catch(e => { console.error(e); process.exit(1); });