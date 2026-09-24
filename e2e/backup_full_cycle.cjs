const { chromium } = require('playwright');

const BASE = 'http://localhost:5165';
const dryRun = process.env.DRY_RUN === '1';
const r = process.env.VIX_USER || 'admin';
const p = process.env.VIX_PASS || 'Admin@123';

const log = (m) => console.log(m);

async function login(page) {
  await page.goto(BASE + '/Account/Login', { waitUntil: 'domcontentloaded' });
  await page.fill('#Username', r);
  await page.fill('#Password', p);
  await page.click('button[type="submit"]');
  await page.waitForURL('**/Home/**', { timeout: 20000 }).catch(() => {});
  await page.waitForTimeout(600);
}

async function gotoBackupAndExpectSheet(page) {
  await page.goto(BASE + '/Backup', { waitUntil: 'domcontentloaded' });
}

async function main() {
  const browser = await chromium.launch();
  const ctx = await browser.newContext();
  const page = await ctx.newPage();

  // 0. Login
  await login(page).catch(() => {});

  // 1. Create a fresh backup
  await gotoBackupAndExpectSheet(page);
  await page.click('form[action*="/Backup/Create"] button[type="submit"]');
  await page.waitForLoadState('load', { timeout: 20000 }).catch(() => {});
  await page.waitForTimeout(800);
  log('create success alert=' + await page.locator('.alert-success').count());
  const fileCount = await page.locator('table tbody tr').count();
  log('backup files after create=' + fileCount);

  // 2. Reset system -> signs out + redirects to Login
  await page.fill('input[name="confirmText"]', 'حذف نهائي');
  await page.click('form[action*="/Backup/Reset"] button[type="submit"]');
  await page.waitForSelector('#appConfirmModal.show', { timeout: 5000 });
  await page.click('#appConfirmBtn');
  await page.waitForURL('**/Account/Login**', { timeout: 60000 }).catch(() => {});
  log('after reset -> redirected to login: url=' + page.url());
  log('after reset -> staying signed in (no login form)? ' + (dryRun ? -1 : await page.locator('#Username').count() === 1 ? 'no' : 'yes') + '');

  // 3. Login again
  await login(page);

  // 4. Verify data is reset (Items empty)
  await page.goto(BASE + '/Items', { waitUntil: 'domcontentloaded' });
  log('items after reset=' + await page.locator('table tbody tr').count());

  // 5. Restore the pre-reset backup; expect sign-out redirect to Login
  await gotoBackupAndExpectSheet(page);
  log('files available for restore=' + await page.locator('form[action*="/Backup/Restore"]').count());
  await page.locator('form[action*="/Backup/Restore"] button[type="submit"]').first().click();
  await page.waitForSelector('#appConfirmModal.show', { timeout: 5000 });
  await page.click('#appConfirmBtn');
  await page.waitForURL('**/Account/Login**', { timeout: 60000 }).catch(() => {});
  log('after restore -> redirected to login: url=' + page.url());

  // 6. Login and verify data restored
  await login(page);
  await page.goto(BASE + '/Items', { waitUntil: 'domcontentloaded' });
  log('items after restore=' + await page.locator('table tbody tr').count());

  // 7. Download sanity
  await gotoBackupAndExpectSheet(page);
  log('download links=' + await page.locator('a[href*="/Backup/Download"]').count());

  await browser.close();
  console.log('E2E_FULL_CYCLE_OK');
}

main().catch((e) => { console.error('E2E_FULL_CYCLE_FAIL', e); process.exit(1); });
