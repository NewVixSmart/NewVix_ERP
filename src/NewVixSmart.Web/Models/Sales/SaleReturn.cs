using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Sales;

public class SaleReturn
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "ط±ظ‚ظ… ط§ظ„ظ…ط±طھط¬ط¹ ظ…ط·ظ„ظˆط¨")]
    [StringLength(50)]
    [Display(Name = "ط±ظ‚ظ… ط§ظ„ظ…ط±طھط¬ط¹")]
    public string ReturnNumber { get; set; } = string.Empty;

    [Display(Name = "ط§ظ„ط¹ظ…ظٹظ„")]
    public int CustomerId { get; set; }

    [BindNever]
    public Customer Customer { get; set; } = null!;

    [Display(Name = "ظپط§طھظˆط±ط© ط§ظ„ط¨ظٹط¹ ط§ظ„ط£طµظ„ظٹط©")]
    public int? SaleInvoiceId { get; set; }

    [BindNever]
    public SaleInvoice? SaleInvoice { get; set; }

    [Display(Name = "ط§ظ„ظپط±ط¹")]
    public int? BranchId { get; set; }

    [Display(Name = "ط­ط§ظ„ط© ط§ظ„ظ…ط±طھط¬ط¹")]
    public ReturnStatus Status { get; set; } = ReturnStatus.Draft;

    [Display(Name = "ط±ط­ظ‘ظ„ظ‡")]
    [BindNever]
    public string? PostedBy { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„طھط±ط­ظٹظ„")]
    [BindNever]
    public DateTime? PostedAt { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ظ…ط±طھط¬ط¹")]
    [DataType(DataType.Date)]
    public DateTime ReturnDate { get; set; } = DateTime.Today;

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ط¥ط¬ظ…ط§ظ„ظٹ")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ط¥ط¬ظ…ط§ظ„ظٹ ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal TotalAmount { get; set; }

    [StringLength(500)]
    [Display(Name = "ط³ط¨ط¨ ط§ظ„ظ…ط±طھط¬ط¹")]
    public string? Reason { get; set; }

    [Display(Name = "ط£ظ†ط´ط¦ ط¨ظˆط§ط³ط·ط©")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ط¥ظ†ط´ط§ط،")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BindNever]
    public ICollection<SaleReturnItem> Items { get; set; } = new List<SaleReturnItem>();
}
