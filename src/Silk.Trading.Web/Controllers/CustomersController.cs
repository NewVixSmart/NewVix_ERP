using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Services;
using Silk.Trading.Web.ViewModels.Sales;

namespace Silk.Trading.Web.Controllers;

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
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        page = Math.Max(1, page);
        search = search?.Trim();
        if (search?.Length > 100) search = search[..100];
        var query = _db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.Name.Contains(search) || (c.Code != null && c.Code.Contains(search)));
        query = query.Where(c => c.IsActive).OrderBy(c => c.Name);

        const int pageSize = 50;
        var total = await query.CountAsync();
        ViewBag.Search = search;
        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        return View(await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
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
        var returns = await _db.SaleReturns.AsNoTracking().Where(r => r.CustomerId == id).OrderByDescending(r => r.ReturnDate).ToListAsync();

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
}
