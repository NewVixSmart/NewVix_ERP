using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Silk.Trading.Web.Models.Accounting;

public class Currency
{
    public int Id { get; set; }

    [Required(ErrorMessage = "رمز العملة مطلوب")]
    [StringLength(10)]
    [Display(Name = "رمز العملة")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم العملة مطلوب")]
    [StringLength(100)]
    [Display(Name = "اسم العملة")]
    public string Name { get; set; } = string.Empty;

    [StringLength(10)]
    [Display(Name = "الرمز")]
    public string? Symbol { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    [Display(Name = "سعر الصرف")]
    [Range(0.000001, 999999999, ErrorMessage = "سعر الصرف يجب أن يكون أكبر من صفر")]
    public decimal ExchangeRate { get; set; } = 1m;

    [Display(Name = "العملة الأساسية")]
    public bool IsBase { get; set; }

    [Display(Name = "نشطة")]
    public bool IsActive { get; set; } = true;
}
