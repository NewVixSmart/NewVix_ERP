using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Accounting;

public class BudgetYear
{
    public int Id { get; set; }

    [Range(2000, 2100, ErrorMessage = "سنة الميزانية غير صالحة")]
    [Display(Name = "السنة")]
    public int Year { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [StringLength(450)]
    [Display(Name = "أنشئ بواسطة")]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BudgetLine> Lines { get; set; } = new List<BudgetLine>();
}
