using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Sales;

public class Customer
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم العميل مطلوب")]
    [StringLength(200)]
    [Display(Name = "اسم العميل")]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "كود العميل")]
    public string? Code { get; set; }

    [StringLength(200)]
    [Display(Name = "العنوان")]
    public string? Address { get; set; }

    [StringLength(20)]
    [Display(Name = "التليفون")]
    [Phone]
    public string? Phone { get; set; }

    [StringLength(200)]
    [Display(Name = "البريد الإلكتروني")]
    [EmailAddress]
    public string? Email { get; set; }

    [StringLength(20)]
    [Display(Name = "الرقم الضريبي")]
    public string? TaxNumber { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الرصيد الافتتاحي")]
    [Range(0, 999999999, ErrorMessage = "الرصيد الافتتاحي لا يمكن أن يكون سالباً")]
    public decimal OpeningBalance { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    public ICollection<SaleInvoice> SaleInvoices { get; set; } = new List<SaleInvoice>();
    public ICollection<SaleReturn> SaleReturns { get; set; } = new List<SaleReturn>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
