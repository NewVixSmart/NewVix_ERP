using Silk.Trading.Web.Services;

namespace Silk.Trading.Web.ViewModels.Batch;

public class BatchResultsViewModel
{
    public string Title { get; set; } = "نتائج الدفعة";
    public List<BatchDocumentResult> Results { get; set; } = new();
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailCount => Results.Count(r => !r.Success);
    public string BackAction { get; set; } = nameof(Silk.Trading.Web.Controllers.BatchController.Sales);
}