using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NewVixSmart.Web.Models.Stock;

public class Warehouse
{
    public int Id { get; set; }

    [Required(ErrorMessage = "الرمز مطلوب")]
    [StringLength(50)]
    [Display(Name = "الرمز")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "الاسم مطلوب")]
    [StringLength(200)]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
