using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.ViewModels.Reports;

public class AuditLedgerViewModel
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? AccountId { get; set; }
    public JournalSource? Source { get; set; }

    public List<AuditLedgerEntryViewModel> Entries { get; set; } = new();
    public int TotalEntries { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public bool IsBalanced => Math.Round(TotalDebit, 2) == Math.Round(TotalCredit, 2);
}

public class AuditLedgerEntryViewModel
{
    public int Id { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public JournalSource Source { get; set; }
    public string SourceDisplayName { get; set; } = string.Empty;
    public int SourceId { get; set; }
    public string? CreatedBy { get; set; }
    public int? BranchId { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal Balance => Math.Round(TotalDebit - TotalCredit, 2);
    public bool IsBalanced => Balance == 0m;
    public List<AuditLedgerLineViewModel> Lines { get; set; } = new();
}

public class AuditLedgerLineViewModel
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Description { get; set; }
}
