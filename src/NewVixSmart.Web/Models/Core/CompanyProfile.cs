using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Core;

public class CompanyProfile
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم الشركة مطلوب")]
    [StringLength(200)]
    [Display(Name = "اسم الشركة")]
    public string CompanyName { get; set; } = "NewVix";

    [StringLength(200)]
    [Display(Name = "الشعار النصي")]
    public string? Tagline { get; set; } = "Smart Solutions";

    [StringLength(300)]
    [Display(Name = "العنوان")]
    public string? Address { get; set; }

    [StringLength(50)]
    [Phone]
    [Display(Name = "الهاتف")]
    public string? Phone { get; set; }

    [StringLength(100)]
    [EmailAddress]
    [Display(Name = "البريد الإلكتروني")]
    public string? Email { get; set; }

    [StringLength(50)]
    [Display(Name = "الرقم الضريبي")]
    public string? TaxNumber { get; set; }

    [Display(Name = "شعار الشركة")]
    public byte[]? LogoData { get; set; }

    [StringLength(50)]
    public string? LogoContentType { get; set; }

    [StringLength(100)]
    public string? LogoFileName { get; set; }

    [Display(Name = "أيقونة المتصفح")]
    public byte[]? FaviconData { get; set; }

    [StringLength(50)]
    public string? FaviconContentType { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool HasLogo => LogoData is { Length: > 0 };
}
