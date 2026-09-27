using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.ViewModels.Reports;

public class OverdueInvoiceViewModel
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Outstanding => NetAmount - PaidAmount;
    public int DaysOverdue => Math.Max(0, (int)(DateTime.Today - DueDate).TotalDays);
}

public class LowStockItemViewModel
{
    public int Id { get; set; }
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Barcode { get; set; }
    public string? CategoryName { get; set; }
    public decimal CurrentCount { get; set; }
    public decimal CurrentQuantity { get; set; }
    public decimal MinCount { get; set; }
    public decimal MinQuantity { get; set; }
}

public class DashboardReportViewModel
{
    public DateTime AsOf { get; set; } = DateTime.Today;
    public List<OverdueInvoiceViewModel> OverdueReceivables { get; set; } = new();
    public List<OverdueInvoiceViewModel> OverduePayables { get; set; } = new();
    public List<LowStockItemViewModel> LowStockItems { get; set; } = new();
    public List<PendingDeliveryLineViewModel> PendingDeliveries { get; set; } = new();
    public int ActiveReservationCount { get; set; }
    public decimal ActiveReservedQuantity { get; set; }

    public int OverdueReceivableCount => OverdueReceivables.Count;
    public decimal OverdueReceivableTotal => OverdueReceivables.Sum(x => x.Outstanding);
    public int OverduePayableCount => OverduePayables.Count;
    public decimal OverduePayableTotal => OverduePayables.Sum(x => x.Outstanding);
    public int LowStockCount => LowStockItems.Count;
    public int PendingDeliveryCount => PendingDeliveries.Count;
    public decimal PendingDeliveryTotal => PendingDeliveries.Sum(x => x.PendingValue);
    public int PendingDeliveryQty => (int)PendingDeliveries.Sum(x => x.PendingQty);
}
