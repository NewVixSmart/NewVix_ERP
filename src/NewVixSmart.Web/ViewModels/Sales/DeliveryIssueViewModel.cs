using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.ViewModels.Sales;

public class DeliveryIssueViewModel
{
    public DeliveryIssue Issue { get; set; } = new();

    public List<DeliveryIssueItem> Items { get; set; } = new();

    public SelectList Notes { get; set; } = new(new List<object>());

    public List<DeliveryOrderItem> NoteLines { get; set; } = new();

    public List<DeliveryOrder> NoteOptions { get; set; } = new();
}
