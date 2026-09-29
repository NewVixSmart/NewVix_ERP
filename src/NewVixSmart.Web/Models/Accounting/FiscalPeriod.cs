using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Accounting;

public class FiscalPeriod
{
    public int Id { get; set; }

    [Range(2000, 2100, ErrorMessage = "السنة المالية غير صالحة")]
    [Display(Name = "السنة المالية")]
    public int Year { get; set; }

    [StringLength(200)]
    [Display(Name = "اسم السنة المالية")]
    public string? Name { get; set; }

    [Display(Name = "مغلقة")]
    public bool IsClosed { get; set; }

    [StringLength(450)]
    [Display(Name = "أغلقت بواسطة")]
    public string? ClosedById { get; set; }

    [Display(Name = "تاريخ الإغلاق")]
    public DateTime? ClosedAt { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
