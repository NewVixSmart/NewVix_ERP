using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Services;
using Silk.Trading.Web.ViewModels.Accounting;

namespace Silk.Trading.Web.Controllers;

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
    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Max(1, page);
        const int pageSize = 50;
        var query = _db.Payments
            .Include(p => p.Customer).Include(p => p.Supplier).Include(p => p.Currency)
            .AsNoTracking()
            .OrderByDescending(p => p.PaymentDate);

        var total = await query.CountAsync();
        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var payments = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return View(payments);
    }

    [RequirePerm("Payments.Create")]
    public async Task<IActionResult> Create(string type = "receipt")
    {
        var currencies = await _db.Currencies.Where(c => c.IsActive).AsNoTracking().ToListAsync();
        var vm = new PaymentFormViewModel
        {
            Type = type,
            Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            Currencies = new SelectList(currencies, "Id", "Code"),
            CurrencyOptions = currencies,
            Payment = new Payment
            {
                ReceiptNumber = "يتم التوليد تلقائياً",
                PaymentDate = DateTime.Today,
                Type = type == "disbursement" ? PaymentType.Disbursement : PaymentType.Receipt,
                CurrencyId = await BaseCurrencyIdAsync(),
                ExchangeRate = 1m
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
            var (ok, error, _) = await _payment.CreatePaymentAsync(payment, User.Identity?.Name);
            if (ok)
            {
                TempData["Success"] = "تم حفظ الدفعة بنجاح";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError("", error ?? "تعذر حفظ الدفعة");
        }

        var currencies = await _db.Currencies.Where(c => c.IsActive).AsNoTracking().ToListAsync();
        vm.Currencies = new SelectList(currencies, "Id", "Code");
        vm.CurrencyOptions = currencies;
        vm.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        return View(vm);
    }

    [RequirePerm("Payments.View")]
    public async Task<IActionResult> Details(int id)
    {
        var payment = await _payment.GetPaymentAsync(id);
        if (payment == null) return NotFound();
        return View(payment);
    }

    private async Task<int?> BaseCurrencyIdAsync()
        => await _db.Currencies.AsNoTracking().Where(c => c.IsActive).OrderByDescending(c => c.IsBase)
            .Select(c => (int?)c.Id).FirstOrDefaultAsync();
}
