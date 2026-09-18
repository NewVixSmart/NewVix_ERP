namespace NewVixSmart.Web.ViewModels.Reports;

public class AgingBucketRow
{
    public string PartyName { get; set; } = string.Empty;
    public decimal Current { get; set; }
    public decimal Days1To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Days90Plus { get; set; }
    public decimal Total => Current + Days1To30 + Days31To60 + Days61To90 + Days90Plus;
}

public class AgingReportViewModel
{
    public DateTime AsOf { get; set; } = DateTime.Today;
    public List<AgingBucketRow> Receivables { get; set; } = new();
    public List<AgingBucketRow> Payables { get; set; } = new();

    public decimal ArCurrent => Receivables.Sum(r => r.Current);
    public decimal ArDays1To30 => Receivables.Sum(r => r.Days1To30);
    public decimal ArDays31To60 => Receivables.Sum(r => r.Days31To60);
    public decimal ArDays61To90 => Receivables.Sum(r => r.Days61To90);
    public decimal ArDays90Plus => Receivables.Sum(r => r.Days90Plus);
    public decimal ArTotal => Receivables.Sum(r => r.Total);

    public decimal ApCurrent => Payables.Sum(r => r.Current);
    public decimal ApDays1To30 => Payables.Sum(r => r.Days1To30);
    public decimal ApDays31To60 => Payables.Sum(r => r.Days31To60);
    public decimal ApDays61To90 => Payables.Sum(r => r.Days61To90);
    public decimal ApDays90Plus => Payables.Sum(r => r.Days90Plus);
    public decimal ApTotal => Payables.Sum(r => r.Total);

    public decimal ArOverdue => ArTotal - ArCurrent;
    public decimal ApOverdue => ApTotal - ApCurrent;
}