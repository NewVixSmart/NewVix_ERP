using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NewVixSmart.Web.Models.Core;

public class ItemCategory
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم التصنيف مطلوب")]
    [StringLength(100)]
    [Display(Name = "اسم التصنيف")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [BindNever]
    public ICollection<Item> Items { get; set; } = new List<Item>();
}
