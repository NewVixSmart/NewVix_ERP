using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Api;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payment;
    private readonly IHttpContextAccessor _http;

    public PaymentsController(IPaymentService payment, IHttpContextAccessor http)
    {
        _payment = payment;
        _http = http;
    }

    [HttpGet("payments")]
    [ApiAuthorize("Payments.View")]
    public async Task<IActionResult> GetPayments([FromQuery] int page = 1)
    {
        var payments = await _payment.GetPaymentsAsync(page, 50);
        return Ok(payments);
    }

    [HttpPost("payments")]
    [ApiAuthorize("Payments.Create")]
    public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request)
    {
        string? validationError = ValidateCreatePayment(request);
        if (validationError != null)
            return BadRequest(new { error = validationError });

        var payment = new Payment
        {
            Type = request.Type.Equals("disbursement", StringComparison.OrdinalIgnoreCase)
                ? PaymentType.Disbursement : PaymentType.Receipt,
            CustomerId = request.CustomerId,
            SupplierId = request.SupplierId,
            Amount = request.Amount,
            CurrencyId = request.CurrencyId,
            ExchangeRate = request.ExchangeRate,
            Method = Enum.TryParse<PaymentMethod>(request.Method, true, out var m) ? m : PaymentMethod.Cash,
            PaymentDate = request.PaymentDate,
            Notes = request.Notes
        };

        var branchId = _http.GetCurrentBranchId();
        var (ok, error, result) = await _payment.CreatePaymentAsync(payment, User.Identity?.Name, branchId);
        if (!ok) return BadRequest(new { message = error });
        return Ok(new ApiResponse<Payment> { Success = true, Data = result });
    }

    private static string? ValidateCreatePayment(CreatePaymentRequest request)
    {
        bool receipt = string.Equals(request.Type, "receipt", StringComparison.OrdinalIgnoreCase);
        bool disbursement = string.Equals(request.Type, "disbursement", StringComparison.OrdinalIgnoreCase);
        if (!receipt && !disbursement)
            return "نوع الدفعة مطلوب ويجب أن يكون receipt أو disbursement";
        if (request.Amount <= 0)
            return "المبلغ يجب أن يكون أكبر من صفر";
        if (request.Amount > 99999999.99m)
            return "المبلغ خارج النطاق المسموح";
        if (receipt && (request.CustomerId is null or <= 0))
            return "عميل المقبوض مطلوب";
        if (disbursement && (request.SupplierId is null or <= 0))
            return "مورد المصروف مطلوب";
        if (request.ExchangeRate is <= 0)
            return "سعر الصرف يجب أن يكون أكبر من صفر";
        var year = request.PaymentDate.Year;
        if (year < 2000 || year > 2100)
            return "تاريخ الدفعة خارج النطاق المسموح";
        return null;
    }
}
