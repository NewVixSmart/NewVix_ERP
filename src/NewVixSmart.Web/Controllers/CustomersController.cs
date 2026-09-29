using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Sales;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class CustomersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IReportService _report;
    public CustomersController(AppDbContext db, IReportService report)
    {
        _db = db;
        _report = report;
    }

    [RequirePerm("Customers.View")]
    public async Task<IActionResult> Index(int page = 1, string? search = null)
    {
        var query = _db.Customers.AsNoTracking().Where(c => c.IsActive).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.Name.Contains(term)
                || (c.Code != null && c.Code.Contains(term))
                || (c.Phone != null && c.Phone.Contains(term)));
        }
        query = query.OrderBy(c => c.Name);

        var total = await query.CountAsync();
        page = PagerExtensions.NormalizePage(page, total);
        var customers = await query
            .Skip((page - 1) * PagerExtensions.PageSize)
            .Take(PagerExtensions.PageSize)
            .ToListAsync();
        this.SetPager(page, total, customers.Count, search);

        return View(customers);
    }

    [RequirePerm("Customers.Create")]
    public IActionResult Create() => View(new Customer { IsActive = true });

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Customers.Create")]
    public async Task<IActionResult> Create(Customer customer)
    {
        if (!string.IsNullOrWhiteSpace(customer.Code) && await _db.Customers.AnyAsync(c => c.Code == customer.Code))
        {
            ModelState.AddModelError(nameof(Customer.Code), "الكود مستخدم بالفعل لعميل آخر");
        }

        if (ModelState.IsValid)
        {
            try
            {
                _db.Customers.Add(customer);
                await _db.SaveChangesAsync();
                TempData["Success"] = "تم إضافة العميل بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError("", "تعذر الحفظ: تأكد من عدم تكرار كود العميل");
            }
        }
        return View(customer);
    }

    [RequirePerm("Customers.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null)
        {
            return NotFound();
        }

        return View(customer);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Customers.Edit")]
    public async Task<IActionResult> Edit(int id, Customer customer)
    {
        if (id != customer.Id)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            var existing = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null)
            {
                return NotFound();
            }

            existing.Name = customer.Name;
            existing.Code = customer.Code;
            existing.Address = customer.Address;
            existing.Phone = customer.Phone;
            existing.Email = customer.Email;
            existing.TaxNumber = customer.TaxNumber;
            if (existing.OpeningBalance != customer.OpeningBalance)
            {
                var hasHistory = await _db.SaleInvoices.AnyAsync(i => i.CustomerId == id)
                    || await _db.Payments.AnyAsync(p => p.CustomerId == id)
                    || await _db.SaleReturns.AnyAsync(r => r.CustomerId == id);
                if (hasHistory)
                {
                    ModelState.AddModelError(nameof(Customer.OpeningBalance), "لا يمكن تغيير الرصيد الافتتاحي بعد وجود حركات مالية — عالج الرصيد بقيد تسوية");
                }
                else
                {
                    existing.OpeningBalance = customer.OpeningBalance;
                }
            }
            existing.Notes = customer.Notes;
            existing.IsActive = customer.IsActive;

            if (!string.IsNullOrWhiteSpace(existing.Code) && await _db.Customers.AnyAsync(c => c.Id != id && c.Code == existing.Code))
            {
                ModelState.AddModelError(nameof(Customer.Code), "الكود مستخدم بالفعل لعميل آخر");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    await _db.SaveChangesAsync();
                    TempData["Success"] = "تم تعديل العميل بنجاح";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateException)
                {
                    ModelState.AddModelError("", "تعذر الحفظ: تأكد من عدم تكرار كود العميل");
                }
            }
        }
        return View(customer);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Customers.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer == null)
        {
            return NotFound();
        }

        customer.IsActive = false;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حذف العميل بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [RequirePerm("Customers.View")]
    public async Task<IActionResult> PendingDeliveries(int id)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null)
        {
            return NotFound();
        }

        var lines = await PendingLinesAsync(id);
        return View(new CustomerPendingDeliveriesViewModel
        {
            Customer = customer,
            Lines = lines,
            TotalValue = lines.Sum(l => l.PendingValue)
        });
    }

    private async Task<List<CustomerPendingLine>> PendingLinesAsync(int customerId)
    {
        var rows = await _db.SalesOrderItems.AsNoTracking()
            .Where(i => i.SalesOrder.CustomerId == customerId
                && i.SalesOrder.Status != SalesOrderStatus.Cancelled
                && (i.Quantity - i.DeliveredQty > 0 || i.Count - i.DeliveredCount > 0))
            .Select(i => new
            {
                i.SalesOrderId,
                OrderNumber = i.SalesOrder.OrderNumber,
                OrderPublicId = i.SalesOrder.PublicId,
                i.SalesOrder.OrderDate,
                i.SalesOrder.ExpectedDate,
                ItemName = i.Item.Name,
                i.Quantity,
                i.Count,
                i.UnitPrice,
                i.DeliveredQty,
                i.DeliveredCount,
                i.InvoicedQty,
                i.InvoicedCount,
                i.ReservedQty,
                i.ReservedCount
            })
            .ToListAsync();

        var orderIds = rows.Select(r => r.SalesOrderId).Distinct().ToList();
        var notes = await _db.DeliveryOrders.AsNoTracking()
            .Where(d => orderIds.Contains(d.SalesOrderId!.Value))
            .Select(d => new { d.SalesOrderId, d.DeliveryNumber })
            .ToListAsync();

        return rows
            .Select(r => new CustomerPendingLine
            {
                OrderId = r.SalesOrderId,
                OrderNumber = r.OrderNumber,
                OrderPublicId = r.OrderPublicId,
                OrderDate = r.OrderDate,
                ExpectedDate = r.ExpectedDate,
                ItemName = r.ItemName,
                Quantity = r.Quantity,
                Count = r.Count,
                UnitPrice = r.UnitPrice,
                DeliveredQty = r.DeliveredQty,
                DeliveredCount = r.DeliveredCount,
                InvoicedQty = r.InvoicedQty,
                InvoicedCount = r.InvoicedCount,
                ReservedQty = r.ReservedQty,
                ReservedCount = r.ReservedCount,
                PendingQty = r.Quantity - r.DeliveredQty,
                PendingCount = r.Count - r.DeliveredCount,
                DeliveryNoteNumbers = string.Join("، ", notes
                    .Where(n => n.SalesOrderId == r.SalesOrderId)
                    .Select(n => n.DeliveryNumber))
            })
            .OrderBy(r => r.OrderDate)
            .ThenBy(r => r.ItemName)
            .ToList();
    }

    [RequirePerm("Customers.View")]
    public async Task<IActionResult> Ledger(int id)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null)
        {
            return NotFound();
        }

        var invoices = await _db.SaleInvoices.AsNoTracking().Where(s => s.CustomerId == id).OrderByDescending(s => s.InvoiceDate).ToListAsync();
        var payments = await _db.Payments.AsNoTracking().Where(p => p.CustomerId == id).OrderByDescending(p => p.PaymentDate).ToListAsync();
        var returns = await _db.SaleReturns.AsNoTracking().Where(r => r.CustomerId == id && r.Status == Models.Accounting.ReturnStatus.Posted).OrderByDescending(r => r.ReturnDate).ToListAsync();

        decimal totalInvoices = invoices.Sum(i => i.NetAmount);
        decimal onReceiptPaid = invoices.Where(i => i.PaymentTerms == Models.Accounting.InvoicePaymentTerms.OnReceipt && i.PaidAmount > 0).Sum(i => i.PaidAmount);
        decimal totalReturns = returns.Sum(r => r.TotalAmount);
        decimal totalPayments = payments.Where(p => p.Type == Models.Accounting.PaymentType.Receipt).Sum(p => p.Amount);

        var vm = new CustomerLedgerViewModel
        {
            Customer = customer,
            Invoices = invoices,
            Payments = payments,
            Returns = returns,
            Balance = customer.OpeningBalance + totalInvoices - onReceiptPaid - totalReturns - totalPayments,
            PendingLines = await PendingLinesAsync(id)
        };
        return View(vm);
    }

    [RequirePerm("Customers.View")]
    public async Task<IActionResult> LedgerXlsx(int id)
    {
        var bytes = await _report.ExportCustomerStatementXlsxAsync(id);
        if (bytes.Length == 0)
        {
            return NotFound();
        }

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"customer-statement-{id}.xlsx");
    }

    [RequirePerm("Customers.View")]
    public async Task<IActionResult> LedgerPdf(int id)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null)
        {
            return NotFound();
        }

        var invoices = await _db.SaleInvoices.AsNoTracking().Where(s => s.CustomerId == id).OrderBy(s => s.InvoiceDate).ThenBy(s => s.Id).ToListAsync();
        var returns = await _db.SaleReturns.AsNoTracking().Where(r => r.CustomerId == id && r.Status == Models.Accounting.ReturnStatus.Posted).OrderBy(r => r.ReturnDate).ThenBy(r => r.Id).ToListAsync();
        var receipts = await _db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == id && p.Type == Models.Accounting.PaymentType.Receipt)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToListAsync();

        var lines = new List<StatementLine>();
        foreach (var inv in invoices)
        {
            lines.Add(new StatementLine(inv.InvoiceDate, $"فاتورة بيع {inv.InvoiceNumber}", decimal.Round(inv.NetAmount, 2), 0));
            if (inv.PaymentTerms == Models.Accounting.InvoicePaymentTerms.OnReceipt && inv.PaidAmount > 0)
            {
                lines.Add(new StatementLine(inv.InvoiceDate, $"مدفوع عند الاستلام {inv.InvoiceNumber}", 0, decimal.Round(inv.PaidAmount, 2)));
            }
        }
        foreach (var r in returns)
        {
            lines.Add(new StatementLine(r.ReturnDate, $"مرتجع بيع {r.ReturnNumber}", 0, decimal.Round(r.TotalAmount, 2)));
        }

        foreach (var r in receipts)
        {
            lines.Add(new StatementLine(r.PaymentDate, $"سند قبض {r.ReceiptNumber}", 0, r.Amount));
        }

        lines = lines.OrderBy(l => l.Date).ThenBy(l => l.Description).ToList();

        var from = lines.Count > 0 ? lines.Min(l => l.Date) : DateTime.Today;
        var to = lines.Count > 0 ? lines.Max(l => l.Date) : DateTime.Today;
        var closing = customer.OpeningBalance + lines.Sum(l => l.Debit - l.Credit);
        var pendingValue = await _report.GetPendingDeliveriesValueAsync(id);
        var bytes = PrintPdfBuilder.RenderCustomerStatementPdf(customer.Name, from, to, customer.OpeningBalance, lines, closing, pendingValue);
        return File(bytes, "application/pdf", $"customer-statement-{id}.pdf");
    }
}
