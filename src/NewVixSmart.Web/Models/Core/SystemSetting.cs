using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Core;

public class SystemSetting
{
    [Key]
    [StringLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}