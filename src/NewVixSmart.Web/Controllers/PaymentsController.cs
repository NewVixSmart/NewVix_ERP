using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Accounting;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class PaymentsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPaymentService _payment;

    public PaymentsController(AppDbContext db, IPaymentService payment)
    {
        _db = db;
        _payment = payment;
    }

    [RequirePerm("Payments.View")]
    public async Task<IActionResult> Index(int page = 1, string? search = null)
    {
        var query = _db.Payments
            .Include(p => p.Customer).Include(p => p.Supplier)
            .AsNoTracking()
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.ReceiptNumber.Contains(term)
                || (p.Customer != null && p.Customer.Name.Contains(term))
                || (p.Supplier != null && p.Supplier.Name.Contains(term)));
        }
        query = query.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id);

        var total = await query.CountAsync();
        page = PagerExtensions.NormalizePage(page, total);
        var payments = await query
            .Skip((page - 1) * PagerExtensions.PageSize)
            .Take(PagerExtensions.PageSize)
            .ToListAsync();
        this.SetPager(page, total, payments.Count, search);

        return View(payments);
    }

    [RequirePerm("Payments.Create")]
    public async Task<IActionResult> Create(string type = "receipt")
    {
        var vm = new PaymentFormViewModel
        {
            Type = type,
            Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            Payment = new Payment
            {
                ReceiptNumber = "يتم التوليد تلقائياً",
                PaymentDate = DateTime.Today,
                Type = type == "disbursement" ? PaymentType.Disbursement : PaymentType.Receipt
            }
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Payments.Create")]
    public async Task<IActionResult> Create(PaymentFormViewModel vm)
    {
        var payment = vm.Payment;
        payment.Type = vm.Type == "disbursement" ? PaymentType.Disbursement : PaymentType.Receipt;

        if (ModelState.IsValid)
        {
            try
            {
                var (ok, error, _) = await _payment.CreatePaymentAsync(payment, User.Identity?.Name);
                if (ok)
                {
                    TempData["Success"] = "تم حفظ الدفعة بنجاح";
                    return RedirectToAction(nameof(Index));
                }
                ModelState.AddModelError("", error ?? "تعذر حفظ الدفعة");
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        vm.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        return View(vm);
    }

    [RequirePerm("Payments.View")]
    public async Task<IActionResult> Details(string id)
    {
        if (!Guid.TryParse(id, out var publicId))
        {
            return NotFound();
        }

        var targetPaymentId = await _db.Payments.AsNoTracking()
            .Where(p => p.PublicId == publicId)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync();
        if (targetPaymentId == null)
        {
            return NotFound();
        }

        var paymentId = targetPaymentId.Value;

        var payment = await _payment.GetPaymentAsync(paymentId);
        if (payment == null)
        {
            return NotFound();
        }

        return View(payment);
    }
}
