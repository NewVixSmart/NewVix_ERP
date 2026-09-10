using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Silk.Trading.Web.Api.Dtos;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Services;

namespace Silk.Trading.Web.Api;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payment;

    public PaymentsController(IPaymentService payment) => _payment = payment;

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
        var payment = new Payment
        {
            ReceiptNumber = $"PAY-{DateTime.UtcNow:yyyyMMddHHmmss}",
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

        var (ok, error, result) = await _payment.CreatePaymentAsync(payment, User.Identity?.Name);
        if (!ok) return BadRequest(new { message = error });
        return Ok(new ApiResponse<Payment> { Success = true, Data = result });
    }
}
