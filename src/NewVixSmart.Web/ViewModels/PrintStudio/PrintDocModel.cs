using NewVixSmart.Web.ViewModels.Core;

namespace NewVixSmart.Web.ViewModels.PrintStudio;

public sealed class PrintDocLine
{
    public string ItemCode = null!;
    public string? Barcode;
    public string Name = null!;
    public string Unit = null!;
    public decimal Count;
    public decimal Quantity;
    public decimal UnitPrice;
    public decimal Discount;
    public decimal LineTotal;
}

public sealed class PrintDocTotals
{
    public decimal Subtotal;
    public decimal Discount;
    public decimal Tax;
    public decimal GrandTotal;
    public bool IsPaid;
    public string CurrencyCode = null!;
}

public sealed class PrintDocModel
{
    public PrintGroup Group;
    public PrintLayoutOptions Layout = null!;
    public string DocTitle = null!;
    public string DocNumber = null!;
    public string DocDateLabel = null!;
    public string DocDateValue = null!;
    public string? SecondDateLabel;
    public string? SecondDateValue;
    public string PartyTitle = null!;
    public string PartyName = null!;
    public string? PartyCode;
    public string? LogoUri;
    public string CompanyName = null!;
    public string Tagline = null!;
    public string CompanyContact = null!;
    public string TaxNumber = null!;
    public string? CreatedBy;
    public string? Note;
    public bool HidesPartyBlock;
    public List<PrintDocLine> Lines = new();
    public PrintDocTotals Totals = new();
}