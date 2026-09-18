namespace NewVixSmart.Web.ViewModels.Core;

public enum PrintPageSize { A4, A5, Letter }

public enum PrintOrientation { Portrait, Landscape }

public enum PrintMarginSize { Normal, Narrow, Wide }

public sealed class PrintLayoutOptions
{
    public PrintPageSize PageSize { get; set; } = PrintPageSize.A4;
    public PrintOrientation Orientation { get; set; } = PrintOrientation.Portrait;
    public PrintMarginSize Margin { get; set; } = PrintMarginSize.Normal;
    public double FontScale { get; set; } = 1.0;
    public int Decimals { get; set; } = 2;
    public bool ShowLogo { get; set; } = true;
    public int LogoScalePercent { get; set; } = 100;
    public bool ShowCompanyName { get; set; } = true;
    public bool ShowTagline { get; set; } = true;
    public bool ShowCompanyContact { get; set; } = true;
    public bool ShowTaxNumber { get; set; } = true;
    public bool ShowDocTitle { get; set; } = true;
    public string AccentColor { get; set; } = "#2563eb";
    public string TableHeaderBg { get; set; } = "#eef2f7";
    public string TableHeaderText { get; set; } = "#1e293b";
    public bool ShowItemCode { get; set; } = true;
    public bool ShowBarcode { get; set; } = true;
    public bool ShowUnitPrice { get; set; } = true;
    public bool ShowDiscountColumn { get; set; } = true;
    public bool ShowCount { get; set; } = true;
    public bool ShowQuantity { get; set; } = true;
    public bool ShowSubtotal { get; set; } = true;
    public bool ShowTotalDiscount { get; set; } = true;
    public bool ShowTotalTax { get; set; } = true;
    public bool ShowGrandTotal { get; set; } = true;
    public bool ShowAmountInWords { get; set; } = false;
    public bool ShowPaidBadge { get; set; } = true;
    public bool ShowCreatedBy { get; set; } = false;
    public bool ShowFooter { get; set; } = true;
    public bool ShowSignatureLines { get; set; } = false;
    public bool ShowPageNumbers { get; set; } = true;
    public string FooterNoteText { get; set; } = "شكراً لتعاملكم معنا";
    public string SignatureOne { get; set; } = "إعداد";
    public string SignatureTwo { get; set; } = "اعتماد";
}