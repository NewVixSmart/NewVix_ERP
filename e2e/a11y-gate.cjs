const fs = require('fs');
const { chromium } = require('playwright');

const BASE = process.env.BASE_URL || 'http://localhost:5165';
const USER = process.env.SILK_USER || 'admin';
const PASS = process.env.SILK_PASS || 'Admin@123';

const axeSource = fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');

const routes = [
  '/',
  '/Reports/Dashboard',
  '/Sales/Create',
  '/Settings',
  '/Settings/Printing',
  '/PurchaseOrders',
  '/StockTransfers',
  '/Accounts',
  '/Items',
  '/Customers',
  '/Backup',
  '/SaleReturns',
  '/PurchaseReturns',
  '/SalesQuotes',
  '/Fiscal',
  '/ImportCenter',
  '/ExportCenter'
];

async function main() {
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1400, height: 950 } });
  const page = await context.newPage();

  await page.goto(BASE + '/Account/Login', { waitUntil: 'domcontentloaded' });
  await page.fill('#Username', USER);
  await page.fill('#Password', PASS);
  await page.click('button[type="submit"]');
  await page.waitForTimeout(1500);

  const failures = [];
  for (const route of routes) {
    const response = await page.goto(BASE + route, { waitUntil: 'load' });
    await page.waitForTimeout(500);
    await page.evaluate(src => { (0, eval)(src); }, axeSource);
    const violations = await page.evaluate(async () => {
      const res = await axe.run(document, {
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] },
        rules: { region: { enabled: false } }
      });
      return res.violations
        .filter(v => v.impact === 'critical' || v.impact === 'serious')
        .map(v => ({ id: v.id, impact: v.impact, nodes: v.nodes.length }));
    });
    const status = response ? response.status() : 0;
    if (status !== 200 || violations.length) {
      failures.push({ route, status, violations });
    }
  }

  await browser.close();

  if (failures.length) {
    console.log('GATE: FAIL');
    console.log(JSON.stringify(failures, null, 2));
    process.exit(1);
  }
  console.log(`GATE: PASS (${routes.length} routes, 0 critical/serious axe violations)`);
}

main().catch(e => { console.error(e); process.exit(1); });
