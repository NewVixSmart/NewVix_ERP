namespace NewVixSmart.Web.ViewModels.Core;

public enum PrintGroup
{
    SalesInvoice,
    PurchaseInvoice,
    SalesQuote,
    ItemLabel,
    FinancialReports,
    SaleReturn,
    PurchaseReturn,
    PurchaseOrder,
    StockTransfer,
    CustomerStatement,
    SupplierStatement
}

public sealed class PrintGroupSettings
{
    public bool ShowLogo { get; set; }
    public bool ShowCompanyName { get; set; }
    public bool ShowTagline { get; set; }
    public bool ShowCompanyContact { get; set; }
    public bool ShowFooter { get; set; }
    public bool ShowBarcode { get; set; }
    public bool ShowUnitPrice { get; set; }
    public bool ShowDiscountColumn { get; set; }
    public string FontScale { get; set; } = "1.0";
    public string PaperMargin { get; set; } = "normal";
}

public sealed record PrintGroupEntry(PrintGroup Group, string PropertyName, string Title, string Icon, PrintGroupSettings Settings);

public sealed class PrintSettingsViewModel
{
    public PrintGroupSettings SalesInvoice { get; set; } = new();
    public PrintGroupSettings PurchaseInvoice { get; set; } = new();
    public PrintGroupSettings SalesQuote { get; set; } = new();
    public PrintGroupSettings ItemLabel { get; set; } = new();
    public PrintGroupSettings FinancialReports { get; set; } = new();

    public IReadOnlyList<PrintGroupEntry> GroupEntries
    {
        get
        {
            return new PrintGroupEntry[]
            {
                new(PrintGroup.SalesInvoice, nameof(SalesInvoice), "فاتورة البيع", "bi-receipt", SalesInvoice),
                new(PrintGroup.PurchaseInvoice, nameof(PurchaseInvoice), "فاتورة الشراء", "bi-basket", PurchaseInvoice),
                new(PrintGroup.SalesQuote, nameof(SalesQuote), "عرض السعر", "bi-file-earmark-text", SalesQuote),
                new(PrintGroup.ItemLabel, nameof(ItemLabel), "ملصق الصنف", "bi-tag", ItemLabel),
                new(PrintGroup.FinancialReports, nameof(FinancialReports), "التقارير المالية", "bi-graph-up", FinancialReports)
            };
        }
    }

    public PrintGroupSettings GetGroup(PrintGroup group) => group switch
    {
        PrintGroup.SalesInvoice => SalesInvoice,
        PrintGroup.PurchaseInvoice => PurchaseInvoice,
        PrintGroup.SalesQuote => SalesQuote,
        PrintGroup.ItemLabel => ItemLabel,
        PrintGroup.FinancialReports => FinancialReports,
        _ => SalesInvoice
    };
}