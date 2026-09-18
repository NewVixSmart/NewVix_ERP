using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.ViewModels.Batch;

public class BatchResultsViewModel
{
    public string Title { get; set; } = "نتائج الدفعة";
    public List<BatchDocumentResult> Results { get; set; } = new();
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailCount => Results.Count(r => !r.Success);
    public string BackAction { get; set; } = nameof(NewVixSmart.Web.Controllers.BatchController.Sales);
}