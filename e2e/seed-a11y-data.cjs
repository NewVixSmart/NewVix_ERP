/**
 * Seeds the accessibility gate's data set by driving the real UI with Playwright.
 *
 * Why the UI and not SQL: the gate exists to prove that the pages an operator actually reaches
 * are reachable and scannable. Writing rows straight into the database would let a broken
 * workflow, a missing permission or a rejected posting slip past, and every page would still
 * pass while the workflow that is supposed to fill it is broken. Every step below is a form a
 * person fills in, and a step the app rejects aborts the seed with its validation text.
 *
 * The set is chosen so each id-keyed route in e2e/a11y-gate.cjs resolves to a real document:
 * four sales orders in four different states (invoiced, issued, delivery pending, draft), a
 * purchase order left partly received so the receive screen still has rows, and one document
 * of every kind the index pages list.
 *
 * Usage: BASE_URL=http://localhost:5165 VIX_USER=admin VIX_PASS=... node e2e/seed-a11y-data.cjs
 */
const { chromium } = require('playwright');

const BASE = process.env.BASE_URL || 'http://localhost:5165';
const USER = process.env.VIX_USER || 'admin';
const PASS = process.env.VIX_PASS || '';

const log = m => console.log('[seed] ' + m);
const phase = m => log('--- ' + m);
const warn = m => console.log('[seed] WARNING: ' + m);
const iso = d => d.toISOString().slice(0, 10);
const addDays = (d, n) => new Date(d.getTime() + n * 86400000);
const digits = href => (href.match(/(\d+)/) || [])[1];
const round2 = n => Math.round(n * 100) / 100;

async function go(page, path) {
  const res = await page.goto(BASE + path, { waitUntil: 'load' });
  return res ? res.status() : 0;
}

/**
 * Posts a form the way a person does. The app replaces window.confirm with a Bootstrap modal
 * (site.js `#appConfirmModal`), so an approval shows a dialog that has to be accepted before
 * the form is actually submitted.
 */
async function post(page, clickSelector) {
  // The rejection handler is attached where the promise is made: the click and the modal wait
  // below can outlive a navigation that never comes, and an unhandled rejection there would
  // kill the process with a browser-closed error instead of the real one.
  const landed = page.waitForNavigation({ waitUntil: 'load', timeout: 60000 })
    .then(() => null, e => e);
  try {
    await page.click(clickSelector);
  } catch (e) {
    throw new Error(`could not click ${clickSelector}: ${e.message}`);
  }
  try {
    await page.locator('#appConfirmBtn').waitFor({ state: 'visible', timeout: 3000 });
    await page.locator('#appConfirmBtn').click();
  } catch (e) {
    /* no confirmation modal on this form, the click already posted it */
  }
  const failure = await landed;
  if (failure) {
    throw new Error(`clicking ${clickSelector} did not post: ${failure.message.split('\n')[0]}`);
  }
  await page.waitForLoadState('load');
}

async function login(page) {
  await go(page, '/Account/Login');
  await page.evaluate(() => { try { localStorage.setItem('theme-mode', 'light'); } catch (e) { /* storage off */ } });
  await page.fill('#Username', USER);
  await page.fill('#Password', PASS);
  await post(page, '#login-form button[type="submit"], form button[type="submit"]');
  if (page.url().includes('/Account/Login')) throw new Error('login failed - check VIX_USER/VIX_PASS');
}

/** Fails the step when the app answered with a validation error or a TempData error. */
async function assertAccepted(page, what) {
  const problems = await page.evaluate(() => {
    const pick = sel => [...document.querySelectorAll(sel)]
      .map(e => e.innerText.replace(/\s+/g, ' ').trim())
      .filter(t => t && t.length > 4);
    const messages = [...pick('.alert-danger'), ...pick('.validation-summary-errors li'), ...pick('.field-validation-error')].slice(0, 4);
    // The summary repeats the same message per field, so name the fields too: "the value '' is
    // invalid" is unusable on its own when a form posts a dozen numeric inputs.
    const fields = [...document.querySelectorAll('input.is-invalid, select.is-invalid, textarea.is-invalid, input[aria-invalid="true"], select[aria-invalid="true"], textarea[aria-invalid="true"]')]
      .map(e => e.getAttribute('name') || e.id)
      .filter(Boolean);
    // A redisplayed form is a mirror of what was posted, so the still-blank inputs name the
    // fields the server could not read. Readonly and disabled ones are skipped: a number the
    // server generates, such as the payment receipt number, is blank on the form by design.
    const blank = [...document.querySelectorAll('input[name]:not([type=hidden]), select[name], textarea[name]')]
      .filter(e => !e.readOnly && !e.disabled)
      .filter(e => !(e.value || '').trim())
      .map(e => e.getAttribute('name'))
      .filter(Boolean);
    return { messages, fields, blank: [...new Set(blank)].slice(0, 10) };
  });
  const detail = [];
  if (problems.messages.length) detail.push(problems.messages.join(' | '));
  if (problems.fields.length) detail.push('fields: ' + problems.fields.join(', '));
  if (problems.blank.length) detail.push('still blank: ' + problems.blank.join(', '));
  // A redisplayed form is not a failure in itself: the layout keeps a blank search box on every
  // page, so blank inputs only name candidates once the server has actually complained.
  if (problems.messages.length || problems.fields.length) {
    throw new Error(what + ' was rejected: ' + detail.join(' | '));
  }
}

async function firstLink(page, prefix) {
  return page.evaluate(p => {
    const a = document.querySelector(`a[href^="${p}"]`);
    return a ? a.getAttribute('href') : null;
  }, prefix);
}

/** Index pages list the newest document first, so the row just created is on top. */
async function newestDetails(page, indexPath, prefix) {
  await go(page, indexPath);
  const href = await firstLink(page, prefix);
  if (!href) throw new Error(`no ${prefix} link on ${indexPath} - the document was not created`);
  return href;
}

/**
 * Finds the row with the highest document number. The indexes sort by date and every document
 * here shares one date, so a tied sort hands the rows back in whatever order the database feels
 * like: taking the first row silently grabs the wrong document once there is more than one.
 * The number is the only field that says which document is newest.
 */
async function newestByNumber(page, indexPath, prefix) {
  // Re-requesting the page the create post just redirected to aborts the navigation it is
  // still settling, so only navigate when the browser is somewhere else.
  if (new URL(page.url()).pathname !== indexPath) await go(page, indexPath);
  const found = await page.evaluate(p => {
    const shape = /[A-Z]{2,6}-\d{4,8}-?\d{1,6}/;
    let best = null;
    const rows = [];
    for (const tr of document.querySelectorAll('table tbody tr')) {
      const link = tr.querySelector(`a[href^="${p}"]`);
      if (!link) continue;
      // The number sits in the row header cell on these grids, not in a data cell.
      const cells = [...tr.querySelectorAll('th, td')].map(td => td.innerText.replace(/\s+/g, ' ').trim());
      rows.push(cells.join(' | '));
      const number = cells.map(text => text.match(shape)).find(Boolean);
      if (!number) continue;
      const seq = Number((number[0].match(/(\d+)\s*$/) || [])[1] || 0);
      if (!best || seq > best.seq) best = { seq, href: link.getAttribute('href') };
    }
    return { href: best ? best.href : null, rows: rows.slice(0, 3) };
  }, prefix);
  if (!found.href) {
    throw new Error(`no ${prefix} document on ${indexPath} - the document was not created (rows: ${found.rows.join(' // ') || 'none'})`);
  }
  return found.href;
}

/**
 * The page a create post left off on. A post that redirects to its own page is authoritative; a
 * post that redirects to the index is resolved by document number instead.
 */
async function createdDetails(page, what, indexPath, prefix) {
  if (page.url().includes('/Details/')) return new URL(page.url()).pathname;
  return newestByNumber(page, indexPath, prefix);
}

async function options(page, selector) {
  return page.$$eval(`${selector} option`, os =>
    os.map(o => ({ value: o.value, label: o.textContent.trim() })).filter(o => o.value));
}

/** The integer key is only in form actions and links, so it is read from the page that has them. */
async function integerId(page, href, action) {
  await go(page, href);
  const id = await page.evaluate(a => {
    const el = document.querySelector(`form[action*="${a}"], a[href*="${a}"]`);
    return el ? ((el.getAttribute('action') || el.getAttribute('href')).match(/(\d+)/) || [])[1] : null;
  }, action);
  if (!id) {
    const state = await page.evaluate(() => ({
      title: document.querySelector('h2, .page-header')?.innerText.replace(/\s+/g, ' ').trim() || document.title,
      actions: [...document.querySelectorAll('form[action], a[href]')]
        .map(e => e.getAttribute('action') || e.getAttribute('href'))
        .filter(u => u && u.includes('/'))
    }));
    throw new Error(`no ${action} target on ${href} ("${state.title}": ${state.actions.join(' ') || 'none'})`);
  }
  return id;
}

async function fillLineItem(page, index, item, count, price) {
  const select = `select[name="items[${index}].ItemId"]`;
  const opts = await options(page, select);
  const chosen = item.label ? opts.find(o => o.label === item.label) : opts[0];
  if (!chosen) throw new Error(`${select} has no option «${item.label}»`);
  await page.selectOption(select, chosen.value);
  await page.fill(`input[name="items[${index}].Count"]`, String(count));
  const qty = await page.$(`input[name="items[${index}].Quantity"]`);
  if (qty) await page.fill(`input[name="items[${index}].Quantity"]`, '0');
  const priceField = await page.$(`input[name="items[${index}].UnitPrice"]`);
  if (priceField) {
    const value = await page.inputValue(`input[name="items[${index}].UnitPrice"]`);
    if ((!value || value === '0') && price) await page.fill(`input[name="items[${index}].UnitPrice"]`, String(price));
  }
}

async function createSalesOrder(page, ctx, { customerId, item, count, approve, note }) {
  await go(page, '/SalesOrders/Create');
  await page.selectOption('select[name="Order.CustomerId"]', String(customerId));
  await page.fill('input[name="Order.OrderDate"]', ctx.date);
  await page.fill('input[name="Order.ExpectedDate"]', ctx.plus7);
  await page.fill('textarea[name="Order.Notes"]', note);
  await fillLineItem(page, 0, item, count, 50);
  await post(page, '#orderForm button[type="submit"]');
  await assertAccepted(page, 'sales order creation');

  const href = await createdDetails(page, 'the sales order', '/SalesOrders', '/SalesOrders/Details/');
  const id = approve ? await integerId(page, href, '/Approve/') : null;
  if (approve) {
    await post(page, 'form[action*="/Approve/"] button[type="submit"]');
    await assertAccepted(page, 'sales order approval (' + href + ')');
  }
  return { href, id };
}

async function createDelivery(page, { orderId, count, issue, note, date }) {
  await go(page, '/DeliveryOrders/Create');
  await page.check('#srcOrder');
  // Picking the order reloads the form from data-redirect, which is what loads the lines that
  // are still owed. Waiting on that navigation is the only thing that guarantees the counts
  // below belong to the reloaded form instead of the one about to be replaced.
  const reloaded = page.waitForNavigation({ waitUntil: 'load', timeout: 20000 }).catch(() => null);
  await page.selectOption('#salesOrderSelect', String(orderId));
  await reloaded;
  if (!(await page.$('input[name="items[0].ItemId"]'))) {
    throw new Error('the delivery form for order ' + orderId + ' loaded no order lines');
  }
  await page.fill('input[name="Delivery.DeliveryDate"]', date);
  await page.fill('textarea[name="Delivery.Notes"]', note);
  if (count) {
    await page.fill('input[name="items[0].Count"]', String(count));
    const qty = await page.$('input[name="items[0].Quantity"]');
    if (qty) await page.fill('input[name="items[0].Quantity"]', '0');
  }
  await post(page, 'form[action$="/Create"] button[type="submit"]');
  await assertAccepted(page, 'delivery order creation');

  const href = await createdDetails(page, 'the delivery order', '/DeliveryOrders', '/DeliveryOrders/Details/');
  // The note's own page carries the link that opens its delivery issue, and that link is the
  // only place the integer key appears: an order-backed note has no usable Deliver form.
  const id = await integerId(page, href, 'deliveryOrderId=');
  // An order-backed note is never delivered by the note screen; the service routes it through
  // a delivery issue, and the note then turns Delivered or PartiallyIssued on its own.
  if (issue) await createDeliveryIssue(page, id);
  return { href, id };
}

async function createDeliveryIssue(page, deliveryId) {
  await go(page, '/DeliveryIssues/Create?deliveryOrderId=' + deliveryId);
  if (!(await page.$('select[name="Issue.DeliveryOrderId"]'))) {
    throw new Error('the delivery issue form has no DeliveryOrderId select - no delivery has quantity due');
  }
  if (!(await page.inputValue('select[name="Issue.DeliveryOrderId"]'))) {
    await page.selectOption('select[name="Issue.DeliveryOrderId"]', String(deliveryId));
  }
  await page.fill('input[name="Issue.IssueDate"]', new Date().toISOString().slice(0, 10));
  await page.fill('input[name="Issue.Carrier"]', 'شحن داخلي');
  await page.fill('input[name="Issue.TrackingNumber"]', 'TRK-GATE-1');
  await post(page, 'form[action$="/Create"] button[type="submit"]');
  await assertAccepted(page, 'delivery issue creation');
  if (!page.url().includes('/DeliveryIssues/Details/')) {
    throw new Error('the delivery issue did not reach its details page: ' + page.url());
  }
  if (!(await page.$('form[action*="/Issue/"]'))) throw new Error('the delivery issue has no post form');
  await post(page, 'form[action*="/Issue/"] button[type="submit"]');
  await assertAccepted(page, 'delivery issue posting');
  return page.url();
}

/**
 * Reads the net off a summary card. A payment has to stay under what is really due, and the
 * due figure depends on the price the item form defaulted to, so it is read from the invoice
 * rather than assumed here.
 */
async function readNetAmount(page) {
  const text = await page.evaluate(() => {
    for (const card of document.querySelectorAll('.card')) {
      const body = card.querySelector('.card-body');
      const label = body ? body.firstElementChild : null;
      const amount = card.querySelector('h4');
      if (label && amount && label.innerText.includes('الصافي')) return amount.innerText;
    }
    return null;
  });
  const parsed = text ? parseFloat(text.replace(/[^\d.]/g, '')) : NaN;
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

async function createPayment(page, { kind, partyField, partyId, amount, date }) {
  // The screen switches on the word, not on the enum number: `Create(string type = "receipt")`
  // and only "disbursement" renders the supplier select.
  await go(page, `/Payments/Create?type=${kind}`);
  await page.fill('input[name="Payment.PaymentDate"]', date);
  await page.fill('input[name="Payment.Amount"]', String(amount));
  await page.selectOption(`select[name="${partyField}"]`, String(partyId));
  await page.selectOption('select[name="Payment.Method"]', '1');
  await page.fill('input[name="Payment.ReferenceNumber"]', 'REF-GATE-1');
  await page.fill('textarea[name="Payment.Notes"]', 'حركة تجريبية لبوابة الإتاحة');
  // The payments form posts to /Payments/Create?type=..., so the action carries a query string.
  await post(page, 'form[action*="/Payments/Create"] button[type="submit"]');
  await assertAccepted(page, `${kind} payment creation`);
}

/** Receiving posts one line at a time, so a partial receipt stops after the first line. */
async function receivePartially(page, orderId, count) {
  await go(page, '/PurchaseOrders/Receive/' + orderId);
  const hidden = await page.$('input[name="orderItemId"]');
  if (!hidden) throw new Error('the receive screen for order ' + orderId + ' has no line to receive');
  const itemId = await hidden.getAttribute('value');
  // The posted field is the bare name and the id carries the line key, so the selector is the id.
  await page.fill(`input#receiveCount-${itemId}`, String(count));
  const qty = await page.$(`input#receiveQty-${itemId}`);
  if (qty) await page.fill(`input#receiveQty-${itemId}`, '0');
  await post(page, 'form[action*="/Receive/"] button[type="submit"]');
  await assertAccepted(page, 'purchase receipt of line ' + itemId);
}

/**
 * A purchase invoice typed straight into /Purchases/Create, on credit terms.
 *
 * The one that a purchase order converts to is booked at the model's default terms, "عند
 * الاستلام", which marks it paid the moment it is saved - so there is nothing left for a
 * supplier payment to allocate. Paying the supplier needs an invoice the operator actually
 * left open, which is what this one is.
 */
async function createCreditPurchase(page, ctx, { supplierId, item, count, unitPrice }) {
  await go(page, '/Purchases/Create');
  await page.selectOption('select[name="Invoice.SupplierId"]', String(supplierId));
  await page.fill('input[name="Invoice.InvoiceDate"]', ctx.date);
  await page.selectOption('select[name="items[0].ItemId"]', item.value);
  await page.fill('input[name="items[0].Count"]', String(count));
  await page.fill('input[name="items[0].Quantity"]', '0');
  await page.fill('input[name="items[0].UnitPrice"]', String(unitPrice));
  await page.selectOption('select[name="Invoice.PaymentTerms"]', '3');
  await page.fill('textarea[name="Invoice.Notes"]', 'فاتورة شراء آجلة تجريبية لبوابة الإتاحة');
  await post(page, 'form[action$="/Create"] button[type="submit"]');
  await assertAccepted(page, 'credit purchase invoice creation');
  return newestByNumber(page, '/Purchases', '/Purchases/Details/');
}

async function createPurchaseOrder(page, ctx, { supplierId, item, count, approve, receiveCount }) {
  await go(page, '/PurchaseOrders/Create');
  await page.selectOption('select[name="Order.SupplierId"]', String(supplierId));
  await page.fill('input[name="Order.OrderDate"]', ctx.date);
  await page.fill('input[name="Order.ExpectedDate"]', ctx.plus7);
  await page.fill('textarea[name="Order.Notes"]', 'أمر شراء تجريبي لبوابة الإتاحة');
  await fillLineItem(page, 0, item, count, 30);
  await post(page, '#orderForm button[type="submit"]');
  await assertAccepted(page, 'purchase order creation');

  const href = await createdDetails(page, 'the purchase order', '/PurchaseOrders', '/PurchaseOrders/Details/');
  const id = await integerId(page, href, '/Approve');
  if (!approve) return { href, id };

  await post(page, 'form[action*="/Approve/"] button[type="submit"]');
  await assertAccepted(page, 'purchase order approval (' + href + ')');
  if (receiveCount) await receivePartially(page, id, receiveCount);

  await go(page, href);
  if (!(await page.$('form[action*="/CreateInvoice/"]'))) {
    throw new Error('purchase order ' + href + ' has no create-invoice form');
  }
  await post(page, 'form[action*="/CreateInvoice/"] button[type="submit"]');
  await assertAccepted(page, 'purchase invoice creation (' + href + ')');
  return { href, id };
}

async function createReturn(page, { base, partyField, partyId, invoiceField, item, count, date }) {
  await go(page, base);
  const invoiceSelect = `select[name="${invoiceField}"]`;
  const invoices = await options(page, invoiceSelect);
  if (!invoices.length) throw new Error(base + ' offers no invoice to return against');
  await page.selectOption(invoiceSelect, invoices[0].value);
  await page.selectOption(`select[name="${partyField}"]`, String(partyId));
  await page.fill('input[name="ReturnDate"]', date);
  await page.fill('textarea[name="Reason"]', 'مرتجع تجريبي لبوابة الإتاحة');
  await fillLineItem(page, 0, item, count, 50);
  await post(page, 'button[value="post"]');
  await assertAccepted(page, base + ' creation');
  // Posting from the form lands on the index, not on the return, so the return only counts as
  // created once the index lists it and its own details page opens.
  const index = base.replace(/\/Create$/, '');
  const href = page.url().includes('/Details/')
    ? new URL(page.url()).pathname
    : await newestByNumber(page, index, index + '/Details/');
  await go(page, href);
  if (!(await page.$('h2'))) throw new Error('the return details page did not render: ' + href);
  return href;
}

async function createStockTransfer(page, ctx, item) {
  await go(page, '/StockTransfers/Create');
  const warehouses = await options(page, 'select[name="Transfer.SourceWarehouseId"]');
  if (warehouses.length < 2) throw new Error('a transfer needs two warehouses, found ' + warehouses.length);
  await page.selectOption('select[name="Transfer.SourceWarehouseId"]', warehouses[0].value);
  await page.selectOption('select[name="Transfer.TargetWarehouseId"]', warehouses[1].value);
  await page.fill('input[name="Transfer.TransferDate"]', ctx.date);
  await page.fill('input[name="Transfer.Notes"]', 'تحويل تجريبي لبوابة الإتاحة');
  await page.selectOption('select[name="items[0].ItemId"]', item.value);
  await page.fill('input[name="items[0].Count"]', '1');
  const qty = await page.$('input[name="items[0].Quantity"]');
  if (qty) await page.fill('input[name="items[0].Quantity"]', '0');
  await post(page, '#transferForm button[type="submit"]');
  await assertAccepted(page, 'stock transfer creation');
}

async function createReservation(page, { customerId, item, count }) {
  await go(page, '/StockReservations/Create');
  await page.selectOption('select[name="Reservation.CustomerId"]', String(customerId));
  await page.fill('input[name="Reservation.Reason"]', 'حجز تجريبي لبوابة الإتاحة');
  await page.fill('textarea[name="Reservation.Notes"]', 'حجز مستقل لبوابة الإتاحة');
  await page.selectOption('select[name="StandaloneItems[0].ItemId"]', item.value);
  await page.fill('input[name="StandaloneItems[0].Count"]', String(count));
  const qty = await page.$('input[name="StandaloneItems[0].Quantity"]');
  if (qty) await page.fill('input[name="StandaloneItems[0].Quantity"]', '0');
  await post(page, 'form[action$="/Create"] button[type="submit"]');
  await assertAccepted(page, 'stock reservation creation');
  if (!page.url().includes('/StockReservations/Details/')) {
    throw new Error('the reservation did not reach its details page: ' + page.url());
  }
}

async function createQuote(page, ctx, { customerId, item, count }) {
  await go(page, '/SalesQuotes/Create');
  await page.selectOption('select[name="Quote.CustomerId"]', String(customerId));
  await page.fill('input[name="Quote.QuoteDate"]', ctx.date);
  await page.fill('input[name="Quote.ValidUntil"]', ctx.plus7);
  await page.fill('textarea[name="Quote.Notes"]', 'عرض سعر تجريبي لبوابة الإتاحة');
  await fillLineItem(page, 0, item, count, 50);
  await post(page, '#quoteForm button[type="submit"]');
  await assertAccepted(page, 'sales quote creation');
}

async function createAdjustment(page, ctx, item) {
  await go(page, '/InventoryAdjustments/Create');
  await page.selectOption('select[name="ItemId"]', item.value);
  await page.fill('input[name="AdjustmentDate"]', ctx.date);
  await page.fill('textarea[name="Reason"]', 'جرد تجريبي لبوابة الإتاحة');
  const count = await page.$('input[name="NewCount"]');
  if (count) await page.fill('input[name="NewCount"]', '11');
  const qty = await page.$('input[name="NewQuantity"]');
  if (qty) await page.fill('input[name="NewQuantity"]', '0');
  await post(page, 'form[action$="/Create"] button[type="submit"]');
  await assertAccepted(page, 'inventory adjustment creation');
}

async function createSupplierQuote(page, ctx, { supplierId, item, unitPrice }) {
  await go(page, '/PurchaseRequests/Create');
  await page.selectOption('select[name="SupplierId"]', String(supplierId));
  await page.selectOption('select[name="ItemId"]', item.value);
  await page.fill('input[name="UnitPrice"]', String(unitPrice));
  await page.fill('input[name="EffectiveDate"]', ctx.date);
  await page.fill('textarea[name="Notes"]', 'طلب شراء تجريبي لبوابة الإتاحة');
  await post(page, 'form[action$="/Create"] button[type="submit"]');
  await assertAccepted(page, 'purchase request creation');
}

async function createBudget(page, year) {
  await go(page, '/Budgets');
  // The default route binds `year` from the query string, so the manage link reads
  // "/Budgets/Manage?year=2026" and not "/Budgets/Manage/2026".
  const manageHref = await page.evaluate(() => {
    const a = document.querySelector('a[href^="/Budgets/Manage"]');
    return a ? a.getAttribute('href') : null;
  });
  if (!manageHref) {
    await page.fill('#budgetYear', String(year));
    await post(page, 'form[action$="/Create"] button[type="submit"]');
    await assertAccepted(page, 'budget creation');
  }
  await go(page, '/Budgets/Manage?year=' + year);
  const lines = await page.$$eval('input[name$=".AnnualAmount"]', els => els.length);
  if (!lines) {
    warn('no budgetable P&L accounts exist, so /Budgets/Manage stays empty');
    return;
  }
  for (let i = 0; i < lines; i++) {
    await page.fill(`input[name="lines[${i}].AnnualAmount"]`, String(120000 + i * 1000));
  }
  await post(page, 'form[action$="/Manage"] button[type="submit"]');
  await assertAccepted(page, 'budget lines');
}

async function main() {
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();
  page.on('dialog', d => d.accept());

  try {
    await login(page);
    log('signed in as ' + USER);

    // The gate resolves ids from the first row of each list page, so the party that receives
    // every document is the one those pages list first. That is read from the page, not assumed.
    await go(page, '/Customers');
    const customerLink = await firstLink(page, '/Customers/Edit/');
    if (!customerLink) throw new Error('no customers to work with - the development seed did not run');
    const customerId = digits(customerLink);
    await go(page, '/Suppliers');
    const supplierLink = await firstLink(page, '/Suppliers/Edit/');
    if (!supplierLink) throw new Error('no suppliers to work with - the development seed did not run');
    const supplierId = digits(supplierLink);

    await go(page, '/SalesOrders/Create');
    const items = await options(page, 'select[name="items[0].ItemId"]');
    if (items.length < 4) throw new Error('expected at least 4 items, found ' + items.length);

    await go(page, '/Fiscal');
    const fiscalText = await page.evaluate(() => document.body.innerText);
    const openYear = Number((fiscalText.match(/(20\d{2})/) || [])[1]) || new Date().getFullYear();

    const today = new Date();
    const date = today.getFullYear() === openYear ? iso(today) : openYear + '-12-31';
    const ctx = { date, plus7: iso(addDays(today, 7)) };
    log(`customer ${customerId}, supplier ${supplierId}, ${items.length} items, document date ${date}`);

    // 1. an order taken all the way to an invoice, so the customer ledger, the sales index and
    //    the ageing report all have rows.
    phase('sales order: approve, deliver in full, invoice');
    const invoiced = await createSalesOrder(page, ctx, {
      customerId, item: items[0], count: 3, approve: true, note: 'أمر بيع يُفوتر بالكامل'
    });
    await createDelivery(page, { orderId: invoiced.id, count: 3, issue: true, note: 'تسليم كامل', date });
    await go(page, invoiced.href);
    if (!(await page.$('form[action*="/CreateInvoice/"]'))) {
      throw new Error('the invoiced sales order has no create-invoice form');
    }
    await post(page, 'form[action*="/CreateInvoice/"] button[type="submit"]');
    await assertAccepted(page, 'sale invoice creation');
    const invoiceHref = await newestByNumber(page, '/Sales', '/Sales/Details/');
    log('invoice ' + invoiceHref);

    // 2. part payment, so the invoice keeps an open balance for the ageing report.
    phase('customer receipt');
    await go(page, invoiceHref);
    const invoiceNet = await readNetAmount(page);
    if (!invoiceNet) throw new Error('could not read the net off ' + invoiceHref);
    await createPayment(page, {
      kind: 'receipt', partyField: 'Payment.CustomerId', partyId: customerId, amount: round2(invoiceNet / 3), date
    });

    // 3. a return against that invoice.
    phase('sale return');
    await createReturn(page, {
      base: '/SaleReturns/Create', partyField: 'CustomerId', partyId: customerId,
      invoiceField: 'SaleInvoiceId', item: items[0], count: 1, date
    });

    // 4. an order issued in two delivery notes, so the note index holds both a delivered and a
    //    partly issued document and the order screen has more than one note to list.
    phase('sales order: approve, two delivery issues');
    const issued = await createSalesOrder(page, ctx, {
      customerId, item: items[1], count: 4, approve: true, note: 'أمر بيع بأذنين تسليم مرحّلان'
    });
    await createDelivery(page, { orderId: issued.id, count: 2, issue: true, note: 'تسليم جزئي', date });
    await createDelivery(page, { orderId: issued.id, count: 2, issue: true, note: 'تسليم الباقي', date });

    // 5. an order whose delivery note is still open: it keeps the issue screen supplied and
    //    leaves the customer with pending deliveries.
    phase('sales order: approve, delivery note left open');
    const pending = await createSalesOrder(page, ctx, {
      customerId, item: items[2], count: 5, approve: true, note: 'أمر بيع وتسليمه معلّق'
    });
    await createDelivery(page, { orderId: pending.id, count: 1, issue: false, note: 'تسليم لم يُنفّذ بعد', date });

    // 6. a draft order, for the edit screen.
    phase('sales order: draft');
    await createSalesOrder(page, ctx, {
      customerId, item: items[3], count: 1, approve: false, note: 'أمر بيع مسودة'
    });

    // 7. a draft quote, for the mass convert screen.
    phase('sales quote: draft');
    await createQuote(page, ctx, { customerId, item: items[0], count: 2 });

    // 8. a standalone reservation, a transfer and a stock count.
    phase('reservation, transfer, stock count');
    await createReservation(page, { customerId, item: items[3], count: 1 });
    await createStockTransfer(page, ctx, items[0]);
    await createAdjustment(page, ctx, items[1]);

    // 9. purchases: a supplier quote, a draft order, and one left partly received so the
    //    receive screen keeps its rows before the invoice is cut.
    phase('purchases');
    await createSupplierQuote(page, ctx, { supplierId, item: items[0], unitPrice: 30 });
    await createPurchaseOrder(page, ctx, { supplierId, item: items[0], count: 5, approve: false });
    await createPurchaseOrder(page, ctx, { supplierId, item: items[1], count: 6, approve: true, receiveCount: 3 });
    const purchaseHref = await newestByNumber(page, '/Purchases', '/Purchases/Details/');
    log('purchase invoice from the order ' + purchaseHref);

    // Pay part of the credit invoice, then return against it. The order matters: a return
    // lowers what is owed to the supplier, while the invoice's own net card keeps showing the
    // un-returned figure, so paying after the return would offer more than is actually due.
    const creditHref = await createCreditPurchase(page, ctx, {
      supplierId, item: items[2], count: 4, unitPrice: 25
    });
    await go(page, creditHref);
    const purchaseNet = await readNetAmount(page);
    if (!purchaseNet) throw new Error('could not read the net off ' + creditHref);
    await createPayment(page, {
      kind: 'disbursement', partyField: 'Payment.SupplierId', partyId: supplierId, amount: round2(purchaseNet / 3), date
    });

    await createReturn(page, {
      base: '/PurchaseReturns/Create', partyField: 'SupplierId', partyId: supplierId,
      invoiceField: 'PurchaseInvoiceId', item: items[2], count: 1, date
    });

    // 10. a budget with lines, for the budget variance report.
    phase('budget');
    await createBudget(page, openYear);

    log('seed complete');
  } finally {
    await context.close();
    await browser.close();
  }
}

main().catch(e => { console.error('[seed] FAILED: ' + (e && e.message ? e.message : e)); process.exit(1); });
