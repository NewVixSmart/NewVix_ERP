using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Silk.Trading.Web.Models.Core;

public class ItemType
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم النوع مطلوب")]
    [StringLength(100)]
    [Display(Name = "اسم النوع")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [BindNever]
    public ICollection<Item> Items { get; set; } = new List<Item>();
}
