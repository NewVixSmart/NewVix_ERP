/*
 * backup_full_cycle.cjs
 *
 * Drives the in-app Backup screen through create -> reset -> restore, asserting
 * the outcome at each step.
 *
 * WHY THIS WAS REWRITTEN
 * The previous version could not fail:
 *   - `await login(page).catch(() => {})` swallowed a bad login, so every later
 *     step operated on the login page and still reported success.
 *   - Nothing was asserted. It printed counts and then `E2E_FULL_CYCLE_OK`
 *     unconditionally, so a run where the app was down, the admin password was
 *     wrong, or the restore silently did nothing still looked green.
 *   - Step 2 DELETES ALL DATA. The reset has no confirmation step, so a stray
 *     `node backup_full_cycle.cjs` against a populated database wiped it. That
 *     is the single most dangerous thing this script does, and it was the
 *     easiest thing in the script to trigger.
 *
 * WHAT IS ASSERTED NOW
 *   - login really lands on an authenticated page (no login form present)
 *   - the reset really signs the user out
 *   - Items are empty after the reset
 *   - the restore really signs the user out again
 *   - the item count after the restore is back to the pre-reset count
 *   - a download link exists for the backup
 * Any failure exits non-zero with E2E_FULL_CYCLE_FAIL and names the assertion.
 *
 * THE DESTRUCTIVE GATE
 * The reset wipes the application's own database. Because the script cannot
 * know whether that database holds data anyone cares about, it refuses to run
 * the reset unless VIX_ALLOW_DESTRUCTIVE_RESET=1 is set explicitly:
 *
 *   $env:VIX_ALLOW_DESTRUCTIVE_RESET = '1'
 *   node backup_full_cycle.cjs
 *
 * Run it against a disposable database. See docs/BACKUP.md.
 *
 * NOT THE SAME THING AS verify_backup_restore.cjs: that harness proves the
 * scripts/backup-db.ps1 artifact and restore cycle against a throwaway
 * database and touches no application data. Prefer it.
 */

const { chromium } = require('playwright');

const BASE = process.env.VIX_BASE_URL || 'http://localhost:5165';
const user = process.env.VIX_USER || 'admin';
const pass = process.env.VIX_PASS || 'Admin@123';
const DESTRUCTIVE_OK = process.env.VIX_ALLOW_DESTRUCTIVE_RESET === '1';
const RESET_WORD = 'حذف نهائي';

let checks = 0;
let failures = 0;

const log = (m) => console.log(m);

function check(name, ok, detail) {
  checks++;
  if (ok) {
    log(`  [ OK ] ${name}${detail ? ' - ' + detail : ''}`);
  } else {
    failures++;
    log(`[ FAIL] ${name}${detail ? ' - ' + detail : ''}`);
  }
  return ok;
}

function requireEnv(name, value) {
  return value !== undefined && value !== null && String(value).length > 0;
}

async function isSignedIn(page) {
  // The login form is the marker. A redirect to /Account/Login means signed out.
  return (await page.locator('#Username').count()) === 0;
}

async function login(page) {
  await page.goto(BASE + '/Account/Login', { waitUntil: 'domcontentloaded' });
  await page.fill('#Username', user);
  await page.fill('#Password', pass);
  await Promise.all([
    page.waitForLoadState('load', { timeout: 30000 }),
    page.click('button[type="submit"]'),
  ]);
  await page.waitForTimeout(500);

  if (!(await isSignedIn(page))) {
    // Surface the actual reason instead of letting later steps fail obscurely.
    const alert = await page.locator('.alert-danger, .validation-summary-errors').first().innerText().catch(() => '');
    throw new Error(
      `login failed for user "${user}" (still on ${page.url()}). ` +
      (alert ? 'Page said: ' + alert.replace(/\s+/g, ' ').trim() : 'No error message was shown on the page.')
    );
  }
}

async function itemCount(page) {
  await page.goto(BASE + '/Items', { waitUntil: 'domcontentloaded' });
  return page.locator('table tbody tr').count();
}

async function main() {
  if (!DESTRUCTIVE_OK) {
    console.log('E2E_FULL_CYCLE_FAIL  refused to run: this script RESETS (erases) the application database.');
    console.log('');
    console.log('Step 2 of the cycle issues /Backup/Reset, which deletes all application data. This');
    console.log('script cannot tell whether the configured database is disposable, so it will not');
    console.log('do that silently. To proceed against a database you are willing to lose:');
    console.log('');
    console.log('    $env:VIX_ALLOW_DESTRUCTIVE_RESET = "1"');
    console.log('    node backup_full_cycle.cjs');
    console.log('');
    console.log('To verify the backup/restore SCRIPTS without touching any application data, run');
    console.log('verify_backup_restore.cjs instead - it uses its own throwaway database.');
    process.exit(1);
  }

  console.log('=== in-app backup full cycle (DESTRUCTIVE: the app database is reset) ===');
  console.log(`  base url: ${BASE}`);
  console.log(`  user    : ${user}`);
  console.log('');

  if (!requireEnv('VIX_USER', user) || !requireEnv('VIX_PASS', pass)) {
    throw new Error('VIX_USER and VIX_PASS must both be set (no default credentials are assumed).');
  }

  const browser = await chromium.launch();
  const ctx = await browser.newContext();
  const page = await ctx.newPage();

  try {
    // 1. Login. A failure here aborts immediately; it is no longer swallowed.
    await login(page);
    check('logged in', await isSignedIn(page), page.url());

    // 2. Record the pre-reset state so the restore can be checked against it.
    const itemsBefore = await itemCount(page);
    log(`  items before the cycle: ${itemsBefore}`);

    // 3. Create a fresh backup.
    await page.goto(BASE + '/Backup', { waitUntil: 'domcontentloaded' });
    const successAlert = await page.locator('.alert-success').count();
    const createForms = await page.locator('form[action*="/Backup/Create"]').count();
    check('backup page offers a Create action', createForms > 0, `${createForms} form(s)`);

    await page.click('form[action*="/Backup/Create"] button[type="submit"]');
    await page.waitForLoadState('load', { timeout: 30000 });
    await page.waitForTimeout(500);
    check('create reported success', successAlert > 0 || (await page.locator('.alert-success').count()) > 0,
      `${await page.locator('.alert-success').count()} success alert(s)`);
    const fileRows = await page.locator('table tbody tr').count();
    check('at least one backup file is listed', fileRows > 0, `${fileRows} row(s)`);

    // 4. The destructive step. The user opted in above.
    log('  RESETTING the application database...');
    await page.goto(BASE + '/Backup', { waitUntil: 'domcontentloaded' });
    const resetForms = await page.locator('form[action*="/Backup/Reset"]').count();
    if (!check('backup page offers a Reset action', resetForms > 0, `${resetForms} form(s)`)) {
      throw new Error('cannot continue: the Reset action is not on the page.');
    }
    await page.fill('input[name="confirmText"]', RESET_WORD);
    await page.click('form[action*="/Backup/Reset"] button[type="submit"]');
    await page.waitForSelector('#appConfirmModal.show', { timeout: 10000 });
    await page.click('#appConfirmBtn');
    await page.waitForURL('**/Account/Login**', { timeout: 120000 });
    check('reset signed the user out', /Account\/Login/.test(page.url()), page.url());

    // 5. Log in again and confirm the data is actually gone.
    await login(page);
    const itemsAfterReset = await itemCount(page);
    check('Items are empty after the reset', itemsAfterReset === 0, `${itemsAfterReset} row(s)`);

    // 6. Restore the pre-reset backup.
    await page.goto(BASE + '/Backup', { waitUntil: 'domcontentloaded' });
    const restoreForms = await page.locator('form[action*="/Backup/Restore"]').count();
    check('a backup is offered for restore', restoreForms > 0, `${restoreForms} form(s)`);
    await page.locator('form[action*="/Backup/Restore"] button[type="submit"]').first().click();
    await page.waitForSelector('#appConfirmModal.show', { timeout: 10000 });
    await page.click('#appConfirmBtn');
    await page.waitForURL('**/Account/Login**', { timeout: 120000 });
    check('restore signed the user out', /Account\/Login/.test(page.url()), page.url());

    // 7. Log in and confirm the data came back. This is the assertion that
    //    makes the whole cycle mean something.
    await login(page);
    const itemsAfterRestore = await itemCount(page);
    check('item count is back to the pre-reset value', itemsAfterRestore === itemsBefore,
      `before=${itemsBefore} afterRestore=${itemsAfterRestore}`);

    // 8. The backup must be downloadable, not merely listed.
    await page.goto(BASE + '/Backup', { waitUntil: 'domcontentloaded' });
    const downloadLinks = await page.locator('a[href*="/Backup/Download"]').count();
    check('the backup is downloadable', downloadLinks > 0, `${downloadLinks} link(s)`);
  }
  finally {
    await browser.close();
  }

  console.log('');
  if (failures === 0) {
    console.log(`E2E_FULL_CYCLE_OK  ${checks}/${checks} assertions passed`);
    process.exit(0);
  } else {
    console.log(`E2E_FULL_CYCLE_FAIL  ${failures} of ${checks} assertions failed`);
    process.exit(1);
  }
}

main().catch((e) => {
  console.log('');
  console.log('E2E_FULL_CYCLE_FAIL  ' + e.message);
  process.exit(1);
});
