using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Silk.Trading.Web.Api.Dtos;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Services;

namespace Silk.Trading.Web.Api;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PurchasesController : ControllerBase
{
    private readonly IInventoryService _inventory;

    public PurchasesController(IInventoryService inventory) => _inventory = inventory;

    [HttpPost("purchases")]
    [ApiAuthorize("Purchases.Create")]
    public async Task<IActionResult> CreatePurchase([FromBody] CreatePurchaseRequest request)
    {
        if (request.Items.Count == 0)
            return BadRequest(new { message = "يرجى إضافة صنف واحد على الأقل" });

        var invoice = new PurchaseInvoice
        {
            SupplierId = request.SupplierId,
            InvoiceDate = request.InvoiceDate,
            Discount = request.Discount,
            Discount2 = request.Discount2,
            Discount3 = request.Discount3,
            Tax = request.Tax,
            Notes = request.Notes,
            PaymentTerms = InvoicePaymentTerms.OnReceipt
        };

        var items = request.Items.Select(i => new PurchaseInvoiceItem
        {
            ItemId = i.ItemId,
            Quantity = i.Quantity,
            Count = i.Count,
            UnitPrice = i.UnitPrice,
            Discount = i.Discount
        }).ToList();

        var (ok, error) = await _inventory.CreatePurchaseAsync(invoice, items, User.Identity?.Name);
        if (!ok) return BadRequest(new { message = error });
        return Ok(new ApiResponse { Success = true, Data = new { invoice.Id, invoice.InvoiceNumber, invoice.NetAmount } });
    }
}
