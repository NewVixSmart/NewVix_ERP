namespace Silk.Trading.Web.ViewModels.Dashboard;

public class LowStockItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal CurrentCount { get; set; }
    public decimal CurrentQuantity { get; set; }
    public decimal MinCount { get; set; }
    public decimal MinQuantity { get; set; }

    public decimal CountDeficit => (MinCount > 0 && CurrentCount < MinCount) ? MinCount - CurrentCount : 0;
    public decimal QuantityDeficit => (MinQuantity > 0 && CurrentQuantity < MinQuantity) ? MinQuantity - CurrentQuantity : 0;
    public bool IsCountLow => MinCount > 0 && CurrentCount < MinCount;
    public bool IsQuantityLow => MinQuantity > 0 && CurrentQuantity < MinQuantity;
}