using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Silk.Trading.Web.Models.Accounting;

public class GLAccount
{
    public int Id { get; set; }

    [Required(ErrorMessage = "رمز الحساب مطلوب")]
    [StringLength(20)]
    [Display(Name = "رمز الحساب")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم الحساب مطلوب")]
    [StringLength(200)]
    [Display(Name = "اسم الحساب")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "نوع الحساب")]
    public GLAccountType Type { get; set; }

    [Display(Name = "طبيعة الحساب")]
    public NormalBalance NormalBalance { get; set; }

    [Display(Name = "الحساب الأب")]
    public int? ParentAccountId { get; set; }

    [ForeignKey(nameof(ParentAccountId))]
    public GLAccount? ParentAccount { get; set; }

    [Display(Name = "الفرع")]
    public int? BranchId { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
