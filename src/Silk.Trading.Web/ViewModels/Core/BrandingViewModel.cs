using System.ComponentModel.DataAnnotations;
using Silk.Trading.Web.Services;

namespace Silk.Trading.Web.ViewModels.Core;

public class BrandingViewModel
{
    [Required(ErrorMessage = "اسم الشركة مطلوب")]
    [StringLength(200)]
    [Display(Name = "اسم الشركة")]
    public string CompanyName { get; set; } = "سلك للتجارة";

    [StringLength(200)]
    [Display(Name = "الشعار النصي")]
    public string? Tagline { get; set; }

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

    [Display(Name = "لوحة الألوان")]
    public string? SelectedPreset { get; set; }

    [Display(Name = "اللون الأساسي")]
    public string Primary { get; set; } = "#0f766e";

    [Display(Name = "اللون الذهبي")]
    public string Accent { get; set; } = "#a8842c";

    [Display(Name = "لون الشريط الجانبي")]
    public string SidebarBg { get; set; } = "#0b2e26";

    [Display(Name = "لون خلفية الصفحات")]
    public string PageBg { get; set; } = "#f5f4ef";

    [Display(Name = "شعار الشركة")]
    public IFormFile? LogoFile { get; set; }

    public bool RemoveLogo { get; set; }
    public bool HasLogo { get; set; }
    public string? LogoFileName { get; set; }
    public string? LogoDataUri { get; set; }

    public PalettePreset[] Presets { get; set; } = [];
    public BrandingTheme PreviewTheme { get; set; } = new();
}