using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.ViewModels.Sales;

public class CustomerPendingDeliveriesViewModel
{
    public Customer Customer { get; set; } = null!;
    public List<CustomerPendingLine> Lines { get; set; } = new();
    public decimal TotalValue { get; set; }
}
