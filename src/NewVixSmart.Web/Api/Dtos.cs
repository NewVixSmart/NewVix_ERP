namespace NewVixSmart.Web.Api.Dtos;

public record TokenRequest(string Username, string Password);

public class TokenResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class ItemResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal CurrentCount { get; set; }
    public decimal CurrentQuantity { get; set; }
    public bool IsActive { get; set; }
}

public class ItemDetailResponse : ItemResponse
{
    public List<StockLayerResponse> StockLayers { get; set; } = [];
}

public class StockLayerResponse
{
    public int Id { get; set; }
    public int? WarehouseId { get; set; }
    public decimal Qty { get; set; }
    public decimal Count { get; set; }
    public decimal UnitCost { get; set; }
    public DateTime DateReceived { get; set; }
}

public class StockSnapshotResponse
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal TotalCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal WarehouseCount { get; set; }
    public decimal WarehouseQuantity { get; set; }
}

public class CustomerResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; }
}

public class SupplierResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; }
}

public class CreatePaymentRequest
{
    public string Type { get; set; } = "receipt";
    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }
    public decimal Amount { get; set; }
    public int? CurrencyId { get; set; }
    public decimal? ExchangeRate { get; set; }
    public string Method { get; set; } = "Cash";
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
}

public class PaymentResponse
{
    public int Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public int? CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public int? BranchId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateSaleRequest
{
    public int CustomerId { get; set; }
    public DateTime InvoiceDate { get; set; } = DateTime.Today;
    public decimal Discount { get; set; }
    public decimal? Discount2 { get; set; }
    public decimal? Discount3 { get; set; }
    public decimal Tax { get; set; }
    public string? Notes { get; set; }
    public List<SaleItemRequest> Items { get; set; } = [];
}

public class SaleItemRequest
{
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Count { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
}

public class CreatePurchaseRequest
{
    public int SupplierId { get; set; }
    public DateTime InvoiceDate { get; set; } = DateTime.Today;
    public decimal Discount { get; set; }
    public decimal? Discount2 { get; set; }
    public decimal? Discount3 { get; set; }
    public decimal Tax { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseItemRequest> Items { get; set; } = [];
}

public class PurchaseItemRequest
{
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Count { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
}

public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
}

public class JournalEntryResponse
{
    public int Id { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int SourceId { get; set; }
    public string? CreatedBy { get; set; }
    public int? BranchId { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal Balance { get; set; }
    public List<JournalEntryLineResponse> Lines { get; set; } = [];
}

public class JournalEntryLineResponse
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Description { get; set; }
}
