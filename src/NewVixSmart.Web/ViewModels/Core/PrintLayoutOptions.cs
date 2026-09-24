namespace NewVixSmart.Web.ViewModels.Core;

using System.ComponentModel.DataAnnotations;

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
    public string AccentColor { get; set; } = "#2e6fd8";
    public string TableHeaderBg { get; set; } = "#eaf3fc";
    public string TableHeaderText { get; set; } = "#0d1b35";
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
    [StringLength(500)]
    public string FooterNoteText { get; set; } = "شكراً لتعاملكم معنا";
    [StringLength(200)]
    public string SignatureOne { get; set; } = "إعداد";
    [StringLength(200)]
    public string SignatureTwo { get; set; } = "اعتماد";
}