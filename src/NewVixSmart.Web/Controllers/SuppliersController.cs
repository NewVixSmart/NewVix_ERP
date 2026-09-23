using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Purchases;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class SuppliersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IReportService _report;
    public SuppliersController(AppDbContext db, IReportService report)
    {
        _db = db;
        _report = report;
    }

    [RequirePerm("Suppliers.View")]
    public async Task<IActionResult> Index()
    {
        var query = _db.Suppliers.AsNoTracking().AsQueryable();
        query = query.Where(s => s.IsActive).OrderBy(s => s.Name);

        return View(await query.ToListAsync());
    }

    [RequirePerm("Suppliers.Create")]
    public IActionResult Create() => View(new Supplier { IsActive = true });

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Suppliers.Create")]
    public async Task<IActionResult> Create(Supplier supplier)
    {
        if (!string.IsNullOrWhiteSpace(supplier.Code) && await _db.Suppliers.AnyAsync(s => s.Code == supplier.Code))
            ModelState.AddModelError(nameof(Supplier.Code), "الكود مستخدم بالفعل لمورد آخر");

        if (ModelState.IsValid)
        {
            try
            {
                _db.Suppliers.Add(supplier);
                await _db.SaveChangesAsync();
                TempData["Success"] = "تم إضافة المورد بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError("", "تعذر الحفظ: تأكد من عدم تكرار كود المورد");
            }
        }
        return View(supplier);
    }

    [RequirePerm("Suppliers.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (supplier == null) return NotFound();
        return View(supplier);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Suppliers.Edit")]
    public async Task<IActionResult> Edit(int id, Supplier supplier)
    {
        if (id != supplier.Id) return NotFound();
        if (ModelState.IsValid)
        {
            var existing = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
            if (existing == null) return NotFound();

            existing.Name = supplier.Name;
            existing.Code = supplier.Code;
            existing.Address = supplier.Address;
            existing.Phone = supplier.Phone;
            existing.Email = supplier.Email;
            existing.TaxNumber = supplier.TaxNumber;
            if (existing.OpeningBalance != supplier.OpeningBalance)
            {
                var hasHistory = await _db.PurchaseInvoices.AnyAsync(i => i.SupplierId == id)
                    || await _db.Payments.AnyAsync(p => p.SupplierId == id)
                    || await _db.PurchaseReturns.AnyAsync(r => r.SupplierId == id);
                if (hasHistory)
                    ModelState.AddModelError(nameof(Supplier.OpeningBalance), "لا يمكن تغيير الرصيد الافتتاحي بعد وجود حركات مالية — عالج الرصيد بقيد تسوية");
                else
                    existing.OpeningBalance = supplier.OpeningBalance;
            }
            existing.Notes = supplier.Notes;
            existing.IsActive = supplier.IsActive;

            if (!string.IsNullOrWhiteSpace(existing.Code) && await _db.Suppliers.AnyAsync(s => s.Id != id && s.Code == existing.Code))
                ModelState.AddModelError(nameof(Supplier.Code), "الكود مستخدم بالفعل لمورد آخر");

            if (ModelState.IsValid)
            {
                try
                {
                    await _db.SaveChangesAsync();
                    TempData["Success"] = "تم تعديل المورد بنجاح";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateException)
                {
                    ModelState.AddModelError("", "تعذر الحفظ: تأكد من عدم تكرار كود المورد");
                }
            }
        }
        return View(supplier);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Suppliers.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await _db.Suppliers.FindAsync(id);
        if (supplier == null) return NotFound();
        supplier.IsActive = false;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حذف المورد بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [RequirePerm("Suppliers.View")]
    public async Task<IActionResult> Ledger(int id)
    {
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (supplier == null) return NotFound();

        var invoices = await _db.PurchaseInvoices.AsNoTracking().Where(p => p.SupplierId == id).OrderByDescending(p => p.InvoiceDate).ToListAsync();
        var payments = await _db.Payments.AsNoTracking().Where(p => p.SupplierId == id).OrderByDescending(p => p.PaymentDate).ToListAsync();
        var returns = await _db.PurchaseReturns.AsNoTracking().Where(r => r.SupplierId == id && r.Status == Models.Accounting.ReturnStatus.Posted).OrderByDescending(r => r.ReturnDate).ToListAsync();

        decimal totalInvoices = invoices.Sum(i => i.NetAmount);
        decimal totalReturns = returns.Sum(r => r.TotalAmount);
        decimal totalPayments = payments.Where(p => p.Type == Models.Accounting.PaymentType.Disbursement).Sum(p => p.Amount);

        var vm = new SupplierLedgerViewModel
        {
            Supplier = supplier,
            Invoices = invoices,
            Payments = payments,
            Returns = returns,
            Balance = supplier.OpeningBalance + totalInvoices - totalReturns - totalPayments
        };
        return View(vm);
    }

    [RequirePerm("Suppliers.View")]
    public async Task<IActionResult> LedgerXlsx(int id)
    {
        var bytes = await _report.ExportSupplierStatementXlsxAsync(id);
        if (bytes.Length == 0) return NotFound();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"supplier-statement-{id}.xlsx");
    }

    [RequirePerm("Suppliers.View")]
    public async Task<IActionResult> LedgerPdf(int id)
    {
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (supplier == null) return NotFound();

        var invoices = await _db.PurchaseInvoices.AsNoTracking().Where(p => p.SupplierId == id).OrderBy(p => p.InvoiceDate).ThenBy(p => p.Id).ToListAsync();
        var returns = await _db.PurchaseReturns.AsNoTracking().Where(r => r.SupplierId == id && r.Status == Models.Accounting.ReturnStatus.Posted).OrderBy(r => r.ReturnDate).ThenBy(r => r.Id).ToListAsync();
        var disbursements = await _db.Payments.AsNoTracking()
            .Where(p => p.SupplierId == id && p.Type == Models.Accounting.PaymentType.Disbursement)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToListAsync();

        var lines = new List<StatementLine>();
        foreach (var inv in invoices) lines.Add(new StatementLine(inv.InvoiceDate, $"فاتورة شراء {inv.InvoiceNumber}", inv.NetAmount, 0));
        foreach (var r in returns) lines.Add(new StatementLine(r.ReturnDate, $"مرتجع شراء {r.ReturnNumber}", 0, r.TotalAmount));
        foreach (var d in disbursements) lines.Add(new StatementLine(d.PaymentDate, $"سند صرف {d.ReceiptNumber}", 0, d.Amount));

        var from = lines.Count > 0 ? lines.Min(l => l.Date) : DateTime.Today;
        var to = lines.Count > 0 ? lines.Max(l => l.Date) : DateTime.Today;
        var closing = supplier.OpeningBalance + lines.Sum(l => l.Debit - l.Credit);
        var bytes = PrintPdfBuilder.RenderSupplierStatementPdf(supplier.Name, from, to, supplier.OpeningBalance, lines, closing);
        return File(bytes, "application/pdf", $"supplier-statement-{id}.pdf");
    }

    [RequirePerm("Suppliers.View")]
    public async Task<IActionResult> Quotes(int? supplierId)
    {
        var query = _db.SupplierQuotes
            .Include(q => q.Supplier)
            .Include(q => q.Item)
            .AsNoTracking()
            .AsQueryable();

        if (supplierId.HasValue && supplierId > 0)
            query = query.Where(q => q.SupplierId == supplierId.Value);

        query = query.OrderByDescending(q => q.EffectiveDate);

        ViewBag.Suppliers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
            await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        ViewBag.SelectedSupplierId = supplierId;

        return View(await query.Take(200).ToListAsync());
    }
}
