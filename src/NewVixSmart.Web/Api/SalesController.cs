using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Api;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[IgnoreAntiforgeryToken]
public class SalesController : ControllerBase
{
    private readonly IInventoryService _inventory;
    private readonly IHttpContextAccessor _http;

    public SalesController(IInventoryService inventory, IHttpContextAccessor http)
    {
        _inventory = inventory;
        _http = http;
    }

    [HttpPost("sales")]
    [ApiAuthorize("Sales.Create")]
    public async Task<IActionResult> CreateSale([FromBody] CreateSaleRequest request)
    {
        string? validationError = ValidateCreateSale(request);
        if (validationError != null)
        {
            return BadRequest(new { error = validationError });
        }

        var invoice = new SaleInvoice
        {
            CustomerId = request.CustomerId,
            InvoiceDate = request.InvoiceDate,
            Discount = request.Discount,
            Discount2 = request.Discount2,
            Discount3 = request.Discount3,
            Tax = request.Tax,
            Notes = request.Notes,
            PaymentTerms = InvoicePaymentTerms.OpenTerm
        };

        var items = request.Items.Select(i => new SaleInvoiceItem
        {
            ItemId = i.ItemId,
            Quantity = i.Quantity,
            Count = i.Count,
            UnitPrice = i.UnitPrice,
            Discount = i.Discount
        }).ToList();

        var branchId = _http.GetCurrentBranchId();
        var (ok, error) = await _inventory.CreateSaleAsync(invoice, items, User.Identity?.Name, branchId);
        if (!ok)
        {
            return BadRequest(new { message = error });
        }

        return Ok(new ApiResponse { Success = true, Data = new { invoice.Id, invoice.InvoiceNumber, invoice.NetAmount } });
    }

    private static string? ValidateCreateSale(CreateSaleRequest request)
    {
        if (request.Items.Count == 0)
        {
            return "يرجى إضافة صنف واحد على الأقل";
        }

        if (request.CustomerId <= 0)
        {
            return "العميل مطلوب";
        }

        if (request.Discount < 0 || (request.Discount2 ?? 0) < 0 || (request.Discount3 ?? 0) < 0 || request.Tax < 0)
        {
            return "الخصومات والضريبة يجب ألا تكون سالبة";
        }

        if (request.Discount > 99999999.99m || request.Tax > 99999999.99m)
        {
            return "قيمة الخصم أو الضريبة خارج النطاق المسموح";
        }

        var year = request.InvoiceDate.Year;
        if (year < 2000 || year > 2100)
        {
            return "تاريخ الفاتورة خارج النطاق المسموح";
        }

        for (int i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (item.ItemId <= 0)
            {
                return $"الصنف في البند {i + 1} مطلوب";
            }

            if (item.Quantity < 0 || item.Count < 0 || item.UnitPrice < 0 || item.Discount < 0)
            {
                return $"قيم البند {i + 1} يجب ألا تكون سالبة";
            }

            if (item.Quantity <= 0 && item.Count <= 0)
            {
                return $"الكمية أو العدد في البند {i + 1} يجب أن يكون أكبر من صفر";
            }

            if (item.UnitPrice > 99999999.99m)
            {
                return $"سعر الوحدة في البند {i + 1} خارج النطاق المسموح";
            }
        }
        return null;
    }
}
