using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Sales;

public class Customer
{
    public int Id { get; set; }

    [Required(ErrorMessage = "ط§ط³ظ… ط§ظ„ط¹ظ…ظٹظ„ ظ…ط·ظ„ظˆط¨")]
    [StringLength(200)]
    [Display(Name = "ط§ط³ظ… ط§ظ„ط¹ظ…ظٹظ„")]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "ظƒظˆط¯ ط§ظ„ط¹ظ…ظٹظ„")]
    public string? Code { get; set; }

    [StringLength(200)]
    [Display(Name = "ط§ظ„ط¹ظ†ظˆط§ظ†")]
    public string? Address { get; set; }

    [StringLength(20)]
    [Display(Name = "ط§ظ„طھظ„ظٹظپظˆظ†")]
    [Phone]
    public string? Phone { get; set; }

    [StringLength(200)]
    [Display(Name = "ط§ظ„ط¨ط±ظٹط¯ ط§ظ„ط¥ظ„ظƒطھط±ظˆظ†ظٹ")]
    [EmailAddress]
    public string? Email { get; set; }

    [StringLength(20)]
    [Display(Name = "ط§ظ„ط±ظ‚ظ… ط§ظ„ط¶ط±ظٹط¨ظٹ")]
    public string? TaxNumber { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "ط§ظ„ط±طµظٹط¯ ط§ظ„ط§ظپطھطھط§ط­ظٹ")]
    [Range(0, 999999999, ErrorMessage = "ط§ظ„ط±طµظٹط¯ ط§ظ„ط§ظپطھطھط§ط­ظٹ ظ„ط§ ظٹظ…ظƒظ† ط£ظ† ظٹظƒظˆظ† ط³ط§ظ„ط¨ط§ظ‹")]
    public decimal OpeningBalance { get; set; }

    [StringLength(500)]
    [Display(Name = "ظ…ظ„ط§ط­ط¸ط§طھ")]
    public string? Notes { get; set; }

    [Display(Name = "ظ†ط´ط·")]
    public bool IsActive { get; set; } = true;

    public ICollection<SaleInvoice> SaleInvoices { get; set; } = new List<SaleInvoice>();
    public ICollection<SaleReturn> SaleReturns { get; set; } = new List<SaleReturn>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
