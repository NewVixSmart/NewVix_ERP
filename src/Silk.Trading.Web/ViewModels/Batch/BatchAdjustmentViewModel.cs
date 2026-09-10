using System.ComponentModel.DataAnnotations;
using Silk.Trading.Web.Models.Core;

namespace Silk.Trading.Web.ViewModels.Batch;

public class BatchAdjustmentViewModel
{
    [Display(Name = "تاريخ الجرد")]
    [DataType(DataType.Date)]
    public DateTime AdjustDate { get; set; } = DateTime.Today;

    [StringLength(500)]
    [Display(Name = "سبب التعديل")]
    public string? Reason { get; set; }

    public List<BatchAdjustmentLineInput> Lines { get; set; } = new() { new() };

    public List<Item> ItemsData { get; set; } = new();
}

public class BatchAdjustmentLineInput
{
    public int ItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد الجديد لا يمكن أن يكون سالبًا")]
    [Display(Name = "العدد الجديد")]
    public decimal NewCount { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية الجديدة لا يمكن أن تكون سالبة")]
    [Display(Name = "الكمية الجديدة")]
    public decimal NewQuantity { get; set; }
}