namespace Silk.Trading.Web.ViewModels.Accounting;

public sealed class FiscalCloseViewModel
{
    public List<FiscalPeriodRowViewModel> Rows { get; set; } = new();
    public int LatestYear { get; set; }
    public int NextYear { get; set; }
}

public sealed class FiscalPeriodRowViewModel
{
    public int Year { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedById { get; set; }
    public int CloseEntryCount { get; set; }
    public decimal NetIncome { get; set; }
}