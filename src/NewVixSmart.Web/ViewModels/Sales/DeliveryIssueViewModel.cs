using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Forms;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.ViewModels.Sales;

public class DeliveryIssueViewModel
{
    public DeliveryIssueFormModel Issue { get; set; } = new();

    public List<DeliveryIssueLineFormModel> Items { get; set; } = new();

    public SelectList Notes { get; set; } = new(new List<object>());

    public List<DeliveryOrderItem> NoteLines { get; set; } = new();

    public List<DeliveryOrder> NoteOptions { get; set; } = new();
}
