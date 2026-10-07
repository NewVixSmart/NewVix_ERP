using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Purchases;

public class Supplier
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم المورد مطلوب")]
    [StringLength(200)]
    [Display(Name = "اسم المورد")]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "كود المورد")]
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

    public ICollection<PurchaseInvoice> PurchaseInvoices { get; set; } = new List<PurchaseInvoice>();
    public ICollection<PurchaseReturn> PurchaseReturns { get; set; } = new List<PurchaseReturn>();
    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
