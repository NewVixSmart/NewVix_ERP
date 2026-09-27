using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Purchases;

namespace NewVixSmart.Web.Models.Sales;

public class SaleQuote
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "ط±ظ‚ظ… ط§ظ„ط¹ط±ط¶ ظ…ط·ظ„ظˆط¨")]
    [StringLength(50)]
    [Display(Name = "ط±ظ‚ظ… ط§ظ„ط¹ط±ط¶")]
    public string QuoteNumber { get; set; } = string.Empty;

    [Display(Name = "ط§ظ„ط¹ظ…ظٹظ„")]
    public int CustomerId { get; set; }

    [BindNever]
    public Customer Customer { get; set; } = null!;

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ط¹ط±ط¶")]
    [DataType(DataType.Date)]
    public DateTime QuoteDate { get; set; } = DateTime.Today;

    [Display(Name = "طµط§ظ„ط­ ط­طھظ‰")]
    [DataType(DataType.Date)]
    public DateTime? ValidUntil { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ط¥ط¬ظ…ط§ظ„ظٹ")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ط¥ط¬ظ…ط§ظ„ظٹ ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ط®طµظ…")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ط®طµظ… ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal Discount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ط¶ط±ظٹط¨ط©")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ط¶ط±ظٹط¨ط© ظ„ط§ ظٹظ…ظƒظ† ط£ظ† طھظƒظˆظ† ط³ط§ظ„ط¨ط©")]
    public decimal Tax { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ظ…ط¨ظ„ط؛ ط§ظ„طµط§ظپظٹ")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ظ…ط¨ظ„ط؛ ط§ظ„طµط§ظپظٹ ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal NetAmount { get; set; }

    [StringLength(500)]
    [Display(Name = "ظ…ظ„ط§ط­ط¸ط§طھ")]
    public string? Notes { get; set; }

    [Display(Name = "ط­ط§ظ„ط© ط§ظ„ط¹ط±ط¶")]
    public SaleQuoteStatus Status { get; set; } = SaleQuoteStatus.Draft;

    [Display(Name = "ط¹ط±ط¶ ظ…ظˆط±ط¯ ظ…ط±ط¬ط¹ظٹ (ط§ط®طھظٹط§ط±ظٹ)")]
    public int? SupplierQuoteId { get; set; }

    [BindNever]
    public SupplierQuote? SupplierQuote { get; set; }

    [Display(Name = "ظپط§طھظˆط±ط© ط§ظ„ط¨ظٹط¹")]
    public int? SaleInvoiceId { get; set; }

    [BindNever]
    public SaleInvoice? SaleInvoice { get; set; }

    [Display(Name = "ط£ظ…ط± ط§ظ„ط¨ظٹط¹")]
    public int? SalesOrderId { get; set; }

    [BindNever]
    public SalesOrder? SalesOrder { get; set; }

    [Display(Name = "ط­ظڈظˆظ‘ظ„ ط¨ظˆط§ط³ط·ط©")]
    [BindNever]
    public string? ConvertedBy { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„طھط­ظˆظٹظ„")]
    [BindNever]
    public DateTime? ConvertedAt { get; set; }

    [Display(Name = "ط£ظ†ط´ط¦ ط¨ظˆط§ط³ط·ط©")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ط¥ظ†ط´ط§ط،")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    [BindNever]
    public ICollection<SaleQuoteItem> Items { get; set; } = new List<SaleQuoteItem>();
}
