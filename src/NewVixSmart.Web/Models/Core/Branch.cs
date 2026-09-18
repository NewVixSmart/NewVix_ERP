using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Core;

public class Branch
{
    public int Id { get; set; }

    [Required(ErrorMessage = "رمز الفرع مطلوب")]
    [StringLength(50)]
    [Display(Name = "رمز الفرع")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم الفرع مطلوب")]
    [StringLength(200)]
    [Display(Name = "اسم الفرع")]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    [Display(Name = "العنوان")]
    public string? Address { get; set; }

    [StringLength(20)]
    [Display(Name = "الهاتف")]
    [Phone]
    public string? Phone { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
