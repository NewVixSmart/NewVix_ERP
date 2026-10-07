using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NewVixSmart.Web.Models.Core;

public class Unit
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم الوحدة مطلوب")]
    [StringLength(50)]
    [Display(Name = "اسم الوحدة")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20)]
    [Display(Name = "الاختصار")]
    public string? ShortName { get; set; }

    [Display(Name = "عدد الوحدات الفرعية")]
    public int? SubUnits { get; set; }

    [Display(Name = "وحدة فرعية")]
    public int? ParentUnitId { get; set; }

    [BindNever]
    public Unit? ParentUnit { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
