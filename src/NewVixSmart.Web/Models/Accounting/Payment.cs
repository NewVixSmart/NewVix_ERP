using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Accounting;

public enum InvoicePaymentTerms
{
    [Display(Name = "ط¹ظ†ط¯ ط§ظ„ط§ط³طھظ„ط§ظ…")]
    OnReceipt = 0,
    [Display(Name = "7 ط£ظٹط§ظ…")]
    Net7 = 1,
    [Display(Name = "15 ظٹظˆظ…")]
    Net15 = 2,
    [Display(Name = "30 ظٹظˆظ…")]
    Net30 = 3,
    [Display(Name = "60 ظٹظˆظ…")]
    Net60 = 4,
    [Display(Name = "ط¢ط¬ظ„ ظ…ظپطھظˆط­")]
    OpenTerm = 5
}

public class Payment
{
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "ط±ظ‚ظ… ط§ظ„ط¥ظٹطµط§ظ„ ظ…ط·ظ„ظˆط¨")]
    [StringLength(50)]
    [Display(Name = "ط±ظ‚ظ… ط§ظ„ط¥ظٹطµط§ظ„")]
    public string ReceiptNumber { get; set; } = string.Empty;

    [Display(Name = "ظ†ظˆط¹ ط§ظ„ط¯ظپط¹ط©")]
    public PaymentType Type { get; set; }

    [Display(Name = "ط§ظ„ط¹ظ…ظٹظ„")]
    public int? CustomerId { get; set; }

    [BindNever]
    public Customer? Customer { get; set; }

    [Display(Name = "ط§ظ„ظ…ظˆط±ط¯")]
    public int? SupplierId { get; set; }

    [BindNever]
    public Supplier? Supplier { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ظ…ط¨ظ„ط؛")]
    [Range(0.01, 999999999, ErrorMessage = "ط§ظ„ظ…ط¨ظ„ط؛ ظٹط¬ط¨ ط£ظ† ظٹظƒظˆظ† ط£ظƒط¨ط± ظ…ظ† طµظپط±")]
    public decimal Amount { get; set; }

    [Display(Name = "ط·ط±ظٹظ‚ط© ط§ظ„ط¯ظپط¹")]
    public PaymentMethod Method { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ط¯ظپط¹ط©")]
    [DataType(DataType.Date)]
    public DateTime PaymentDate { get; set; } = DateTime.Today;

    [StringLength(200)]
    [Display(Name = "ط±ظ‚ظ… ط§ظ„ظ…ط±ط¬ط¹")]
    public string? ReferenceNumber { get; set; }

    [StringLength(500)]
    [Display(Name = "ظ…ظ„ط§ط­ط¸ط§طھ")]
    public string? Notes { get; set; }

    [Display(Name = "ط§ظ„ظپط±ط¹")]
    public int? BranchId { get; set; }

    [Display(Name = "ط£ظ†ط´ط¦ ط¨ظˆط§ط³ط·ط©")]
    public string? CreatedBy { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ط¥ظ†ط´ط§ط،")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BindNever]
    public string? DedupeKey { get; set; }

    public ICollection<SalePaymentAllocation>? SalePaymentAllocations { get; set; } = [];
    public ICollection<PurchasePaymentAllocation>? PurchasePaymentAllocations { get; set; } = [];
}

public enum PaymentType
{
    [Display(Name = "ظ‚ط¨ط¶ (ظ…ظ† ط¹ظ…ظٹظ„)")]
    Receipt = 1,
    [Display(Name = "طµط±ظپ (ظ„ظ…ظˆط±ط¯)")]
    Disbursement = 2
}

public enum PaymentMethod
{
    [Display(Name = "ظ†ظ‚ط¯ط§ظ‹")]
    Cash = 1,
    [Display(Name = "ط´ظٹظƒ")]
    Check = 2,
    [Display(Name = "طھط­ظˆظٹظ„ ط¨ظ†ظƒظٹ")]
    BankTransfer = 3,
    [Display(Name = "ط¨ط·ط§ظ‚ط© ط§ط¦طھظ…ط§ظ†")]
    CreditCard = 4
}
