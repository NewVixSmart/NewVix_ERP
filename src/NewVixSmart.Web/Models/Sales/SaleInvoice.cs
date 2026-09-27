using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Sales;

public class SaleInvoice
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "ط±ظ‚ظ… ط§ظ„ظپط§طھظˆط±ط© ظ…ط·ظ„ظˆط¨")]
    [StringLength(50)]
    [Display(Name = "ط±ظ‚ظ… ط§ظ„ظپط§طھظˆط±ط©")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Display(Name = "ط§ظ„ط¹ظ…ظٹظ„")]
    public int CustomerId { get; set; }

    [BindNever]
    public Customer Customer { get; set; } = null!;

    [Display(Name = "ط§ظ„ظپط±ط¹")]
    public int? BranchId { get; set; }

    [BindNever]
    public Core.Branch? Branch { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ظپط§طھظˆط±ط©")]
    [DataType(DataType.Date)]
    public DateTime InvoiceDate { get; set; } = DateTime.Today;

    [Display(Name = "ط´ط±ظˆط· ط§ظ„ط¯ظپط¹")]
    public InvoicePaymentTerms PaymentTerms { get; set; } = InvoicePaymentTerms.OnReceipt;

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ط§ط³طھط­ظ‚ط§ظ‚")]
    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ظ…ط¨ظ„ط؛ ط§ظ„ط¥ط¬ظ…ط§ظ„ظٹ")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ظ…ط¨ظ„ط؛ ط§ظ„ط¥ط¬ظ…ط§ظ„ظٹ ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ط®طµظ…")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ط®طµظ… ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal Discount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط®طµظ… ط¥ط¶ط§ظپظٹ 2")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ط®طµظ… ط§ظ„ط¥ط¶ط§ظپظٹ ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal? Discount2 { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط®طµظ… ط¥ط¶ط§ظپظٹ 3")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ط®طµظ… ط§ظ„ط¥ط¶ط§ظپظٹ ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal? Discount3 { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ط¶ط±ظٹط¨ط©")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ط¶ط±ظٹط¨ط© ظ„ط§ ظٹظ…ظƒظ† ط£ظ† طھظƒظˆظ† ط³ط§ظ„ط¨ط©")]
    public decimal Tax { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ظ…ط¨ظ„ط؛ ط§ظ„طµط§ظپظٹ")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ظ…ط¨ظ„ط؛ ط§ظ„طµط§ظپظٹ ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal NetAmount { get; set; }

    [Display(Name = "طھظ… ط§ظ„ط¯ظپط¹")]
    public bool IsPaid { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ظ…ط¨ظ„ط؛ ط§ظ„ظ…ط¯ظپظˆط¹")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ظ…ط¨ظ„ط؛ ط§ظ„ظ…ط¯ظپظˆط¹ ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal PaidAmount { get; set; }

    [StringLength(500)]
    [Display(Name = "ظ…ظ„ط§ط­ط¸ط§طھ")]
    public string? Notes { get; set; }

    [Display(Name = "ط£ظ…ط± ط§ظ„ط¨ظٹط¹ ط§ظ„ظ…ط±طھط¨ط·")]
    public int? SalesOrderId { get; set; }

    [BindNever]
    public SalesOrder? SalesOrder { get; set; }

    [StringLength(50)]
    [Display(Name = "ظ…ط±ط¬ط¹ ط§ظ„ط£ظ…ط±")]
    public string? OrderReference { get; set; }

    [Display(Name = "ظ…ظˆط¶ط¹ ط§ظ„طھط±ط­ظٹظ„ ط§ظ„ظ…ط­ط§ط³ط¨ظٹ")]
    public SalesPostingMode PostingMode { get; set; } = SalesPostingMode.AtDelivery;

    [Display(Name = "ط£ظ†ط´ط¦ ط¨ظˆط§ط³ط·ط©")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ط¥ظ†ط´ط§ط،")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    [BindNever]
    public ICollection<SaleInvoiceItem> Items { get; set; } = new List<SaleInvoiceItem>();

    [BindNever]
    public ICollection<DeliveryIssue> DeliveryIssues { get; set; } = new List<DeliveryIssue>();
}
