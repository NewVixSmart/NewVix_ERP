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
    public async Task<IActionResult> Index()
    {
        var query = _db.Customers.AsNoTracking().AsQueryable();
        query = query.Where(c => c.IsActive).OrderBy(c => c.Name);

        return View(await query.ToListAsync());
    }

    [RequirePerm("Customers.Create")]
    public IActionResult Create() => View(new Customer { IsActive = true });

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Customers.Create")]
    public async Task<IActionResult> Create(Customer customer)
    {
        if (!string.IsNullOrWhiteSpace(customer.Code) && await _db.Customers.AnyAsync(c => c.Code == customer.Code))
            ModelState.AddModelError(nameof(Customer.Code), "الكود مستخدم بالفعل لعميل آخر");

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
        if (customer == null) return NotFound();
        return View(customer);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Customers.Edit")]
    public async Task<IActionResult> Edit(int id, Customer customer)
    {
        if (id != customer.Id) return NotFound();
        if (ModelState.IsValid)
        {
            var existing = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null) return NotFound();

            existing.Name = customer.Name;
            existing.Code = customer.Code;
            existing.Address = customer.Address;
            existing.Phone = customer.Phone;
            existing.Email = customer.Email;
            existing.TaxNumber = customer.TaxNumber;
            existing.OpeningBalance = customer.OpeningBalance;
            existing.Notes = customer.Notes;
            existing.IsActive = customer.IsActive;

            if (!string.IsNullOrWhiteSpace(existing.Code) && await _db.Customers.AnyAsync(c => c.Id != id && c.Code == existing.Code))
                ModelState.AddModelError(nameof(Customer.Code), "الكود مستخدم بالفعل لعميل آخر");

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
        if (customer == null) return NotFound();
        customer.IsActive = false;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حذف العميل بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [RequirePerm("Customers.View")]
    public async Task<IActionResult> Ledger(int id)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null) return NotFound();

        var invoices = await _db.SaleInvoices.AsNoTracking().Where(s => s.CustomerId == id).OrderByDescending(s => s.InvoiceDate).ToListAsync();
        var payments = await _db.Payments.AsNoTracking().Where(p => p.CustomerId == id).OrderByDescending(p => p.PaymentDate).ToListAsync();
        var returns = await _db.SaleReturns.AsNoTracking().Where(r => r.CustomerId == id && r.Status == Models.Accounting.ReturnStatus.Posted).OrderByDescending(r => r.ReturnDate).ToListAsync();

        decimal totalInvoices = invoices.Sum(i => i.NetAmount);
        decimal totalReturns = returns.Sum(r => r.TotalAmount);
        decimal totalPayments = payments.Where(p => p.Type == Models.Accounting.PaymentType.Receipt).Sum(p => p.Amount);

        var vm = new CustomerLedgerViewModel
        {
            Customer = customer,
            Invoices = invoices,
            Payments = payments,
            Returns = returns,
            Balance = customer.OpeningBalance + totalInvoices - totalReturns - totalPayments
        };
        return View(vm);
    }

    [RequirePerm("Customers.View")]
    public async Task<IActionResult> LedgerXlsx(int id)
    {
        var bytes = await _report.ExportCustomerStatementXlsxAsync(id);
        if (bytes.Length == 0) return NotFound();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"customer-statement-{id}.xlsx");
    }

    [RequirePerm("Customers.View")]
    public async Task<IActionResult> LedgerPdf(int id)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null) return NotFound();

        var invoices = await _db.SaleInvoices.AsNoTracking().Where(s => s.CustomerId == id).OrderBy(s => s.InvoiceDate).ThenBy(s => s.Id).ToListAsync();
        var returns = await _db.SaleReturns.AsNoTracking().Where(r => r.CustomerId == id && r.Status == Models.Accounting.ReturnStatus.Posted).OrderBy(r => r.ReturnDate).ThenBy(r => r.Id).ToListAsync();
        var receipts = await _db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == id && p.Type == Models.Accounting.PaymentType.Receipt)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToListAsync();

        var lines = new List<StatementLine>();
        foreach (var inv in invoices) lines.Add(new StatementLine(inv.InvoiceDate, $"فاتورة بيع {inv.InvoiceNumber}", inv.NetAmount, 0));
        foreach (var r in returns) lines.Add(new StatementLine(r.ReturnDate, $"مرتجع بيع {r.ReturnNumber}", 0, r.TotalAmount));
        foreach (var r in receipts) lines.Add(new StatementLine(r.PaymentDate, $"سند قبض {r.ReceiptNumber}", 0, r.Amount));

        var from = lines.Count > 0 ? lines.Min(l => l.Date) : DateTime.Today;
        var to = lines.Count > 0 ? lines.Max(l => l.Date) : DateTime.Today;
        var closing = customer.OpeningBalance + lines.Sum(l => l.Debit - l.Credit);
        var bytes = PrintPdfBuilder.RenderCustomerStatementPdf(customer.Name, from, to, customer.OpeningBalance, lines, closing);
        return File(bytes, "application/pdf", $"customer-statement-{id}.pdf");
    }
}
