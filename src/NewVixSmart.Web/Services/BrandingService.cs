using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Services;

public sealed record PalettePreset(string Id, string NameAr, string Primary, string Accent, string SidebarBg, string PageBg);

public sealed class BrandingTheme
{
    public string Primary { get; set; } = "#1570EF";
    public string PrimaryDark { get; set; } = "#175CD3";
    public string PrimaryDarker { get; set; } = "#1849A9";
    public string PrimaryFocus { get; set; } = "rgba(21, 112, 239, 0.22)";
    public string Accent { get; set; } = "#53B1FD";
    public string AccentStrong { get; set; } = "#2E90FA";
    public string AccentBright { get; set; } = "#B9DCFF";
    public string SidebarBg { get; set; } = "#ffffff";
    public string SidebarText { get; set; } = "#101828";
    public string SidebarSection { get; set; } = "#475467";
    public string PageBg { get; set; } = "#f9fafb";
    public string BsPrimaryBgSubtle { get; set; } = "#eff8ff";
    public string BsPrimaryBorderSubtle { get; set; } = "#b2ddff";
    public string BsPrimaryText { get; set; } = "#175cd3";
}

public sealed class BrandingData
{
    public CompanyProfile Profile { get; set; } = new();
    public BrandingTheme Theme { get; set; } = new();
}

public interface IBrandingService
{
    PalettePreset[] Presets { get; }
    Task<BrandingData> LoadAsync();
    Task<CompanyProfile> GetProfileAsync();
    Task<BrandingTheme> GetThemeAsync();
    string RenderThemeCss(BrandingTheme theme);
    void Invalidate();
}

public class BrandingService : IBrandingService
{
    private const string _cacheKey = "branding.v1";
    private static readonly Dictionary<string, PalettePreset> _presetMap = new()
    {
        ["modern"] = new("modern", "سافاير عالمي — Global", "#1570ef", "#53b1fd", "#ffffff", "#f9fafb"),
        ["evergreen"] = new("evergreen", "زمردي ذهبي", "#115e59", "#2dd4bf", "#0f2b26", "#f4f7f6"),
        ["indigo"] = new("indigo", "ملكي نيلي", "#4f46e5", "#818cf8", "#1e1b4b", "#f5f5fb"),
        ["crimson"] = new("crimson", "قرمزي عتيق", "#be123c", "#fb7185", "#450a0a", "#faf5f7"),
        ["ocean"] = new("ocean", "أزرق محيطي", "#0369a1", "#0ea5e9", "#082f49", "#f2f7fa"),
        ["wine"] = new("wine", "نبيذي برونزي", "#7f1d1d", "#c2507a", "#2b0f0f", "#faf5f2"),
        ["slate"] = new("slate", "قائم سماوي", "#0f766e", "#14b8a6", "#122f3a", "#f2f7f7"),
        ["ivory-light"] = new("ivory-light", "عاجي فاتح", "#115e59", "#2dd4bf", "#f6f8fa", "#ffffff")
    };

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public BrandingService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public PalettePreset[] Presets => _presetMap.Values.ToArray();

    public async Task<BrandingData> LoadAsync()
    {
        if (_cache.TryGetValue(_cacheKey, out BrandingData? cached) && cached != null)
        {
            return cached;
        }

        var profile = await _db.CompanyProfiles.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync() ?? new CompanyProfile();
        var settings = await _db.SystemSettings.AsNoTracking().ToListAsync();
        var theme = BuildTheme(settings);

        var data = new BrandingData { Profile = profile, Theme = theme };
        _cache.Set(_cacheKey, data, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
        });
        return data;
    }

    public async Task<CompanyProfile> GetProfileAsync() => (await LoadAsync()).Profile;

    public async Task<BrandingTheme> GetThemeAsync() => (await LoadAsync()).Theme;

    public void Invalidate() => _cache.Remove(_cacheKey);

    private static BrandingTheme BuildTheme(List<SystemSetting> settings)
    {
        string? Get(string key) =>
            settings.FirstOrDefault(s => s.Key == key)?.Value;

        var presetId = Get("Theme.Preset");
        if (presetId != null && _presetMap.TryGetValue(presetId, out var preset))
        {
            return FromPreset(preset);
        }

        var primary = FirstValid(Get("Theme.Primary"), "#1570ef");
        var accent = FirstValid(Get("Theme.Accent"), "#53b1fd");
        var sidebarBg = FirstValid(Get("Theme.SidebarBg"), "#ffffff");
        var pageBg = FirstValid(Get("Theme.PageBg"), "#f9fafb");

        var sidebarDark = !ColorUtil.IsLight(sidebarBg);
        return new BrandingTheme
        {
            Primary = primary,
            PrimaryDark = DarkenSafe(Get("Theme.PrimaryDark"), primary),
            PrimaryDarker = DarkenSafe(Get("Theme.PrimaryDarker"), primary),
            PrimaryFocus = $"rgba({ColorUtil.RgbList(primary)}, 0.16)",
            Accent = accent,
            AccentStrong = DarkenSafe(Get("Theme.AccentStrong"), accent),
            AccentBright = ColorUtil.Lighten(accent, 0.15),
            SidebarBg = sidebarBg,
            SidebarText = sidebarDark ? "#e8e8ee" : "#0e1620",
            SidebarSection = sidebarDark ? "#94a3b8" : "#6b7785",
            PageBg = pageBg,
            BsPrimaryBgSubtle = FirstValid(Get("Theme.BsPrimaryBgSubtle"), ColorUtil.Blend(primary, "#ffffff", 0.82)),
            BsPrimaryBorderSubtle = FirstValid(Get("Theme.BsPrimaryBorderSubtle"), ColorUtil.Blend(primary, "#ffffff", 0.68)),
            BsPrimaryText = FirstValid(Get("Theme.BsPrimaryText"), ColorUtil.Blend(primary, "#000000", 0.35))
        };
    }

    private static BrandingTheme FromPreset(PalettePreset p)
    {
        var primary = p.Primary;
        var sidebarDark = !ColorUtil.IsLight(p.SidebarBg);
        return new BrandingTheme
        {
            Primary = primary,
            PrimaryDark = ColorUtil.Darken(primary, 0.08),
            PrimaryDarker = ColorUtil.Darken(primary, 0.12),
            PrimaryFocus = $"rgba({ColorUtil.RgbList(primary)}, 0.16)",
            Accent = p.Accent,
            AccentStrong = ColorUtil.Darken(p.Accent, 0.18),
            AccentBright = ColorUtil.Lighten(p.Accent, 0.15),
            SidebarBg = p.SidebarBg,
            SidebarText = sidebarDark ? "#e8e8ee" : "#0e1620",
            SidebarSection = sidebarDark ? "#94a3b8" : "#6b7785",
            PageBg = p.PageBg,
            BsPrimaryBgSubtle = ColorUtil.Blend(primary, "#ffffff", 0.82),
            BsPrimaryBorderSubtle = ColorUtil.Blend(primary, "#ffffff", 0.68),
            BsPrimaryText = ColorUtil.Blend(primary, "#000000", 0.35)
        };
    }

    private static string FirstValid(string? value, string fallback) =>
        IsValidHex(value) ? value! : fallback;

    private static bool IsValidHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var h = value.Trim().TrimStart('#');
        if (h.Length == 3)
        {
            h = string.Concat(h.Select(c => char.ToString(c) + char.ToString(c)));
        }

        return h.Length == 6 && h.All(Uri.IsHexDigit);
    }

    private static string DarkenSafe(string? value, string fallback) =>
        IsValidHex(value) ? ColorUtil.Darken(value!, 0.09) : ColorUtil.Darken(fallback, 0.09);

    private static string GuaranteeLinkInk(string hex)
    {
        var current = hex;
        var guard = 0;
        while (ColorUtil.RelativeLuminance(current) > 0.15 && guard++ < 24)
        {
            current = ColorUtil.Darken(current, 0.07);
        }

        return current;
    }

    private static string BrightenToLuminance(string hex, double target)
    {
        var current = hex;
        var guard = 0;
        while (ColorUtil.RelativeLuminance(current) < target && guard++ < 40)
        {
            current = ColorUtil.Lighten(current, 0.04);
        }

        return current;
    }

    public string RenderThemeCss(BrandingTheme t)
    {
        var rb = ColorUtil.RgbList(t.Primary);
        var linkInk = GuaranteeLinkInk(t.PrimaryDarker);
        var linkHover = ColorUtil.Darken(linkInk, 0.05);
        var linkHoverRgb = ColorUtil.RgbList(linkHover);
        var linkInkRgb = ColorUtil.RgbList(linkInk);

        // §3.4 — recompute the brand chroma for the carbon background (#0A0B0D):
        // keep the hue but brighten until it reads as a light-accent on dark surfaces.
        var dPrimary = BrightenToLuminance(t.Primary, 0.21);
        var dPrimaryHover = BrightenToLuminance(t.Primary, 0.38);
        var dPrimaryBright = BrightenToLuminance(t.Primary, 0.48);
        var dPrimaryRgb = ColorUtil.RgbList(dPrimary);
        var dGold = BrightenToLuminance(t.Accent, 0.30);
        var dGoldStrong = BrightenToLuminance(t.Accent, 0.48);
        var dGoldBright = BrightenToLuminance(t.Accent, 0.62);
        var dLink = BrightenToLuminance(t.Primary, 0.40);
        var dLinkHover = BrightenToLuminance(t.Primary, 0.52);

        return string.Join('\n',
            ":root[data-theme=\"light\"] {",
            $"    --color-primary: {t.Primary};",
            $"    --color-primary-dark: {t.PrimaryDark};",
            $"    --color-primary-darker: {t.PrimaryDarker};",
            $"    --color-primary-focus: {t.PrimaryFocus};",
            $"    --color-gold: {t.Accent};",
            $"    --color-gold-strong: {t.AccentStrong};",
            $"    --color-gold-bright: {t.AccentBright};",
            $"    --color-sidebar-bg: {t.SidebarBg};",
            $"    --color-sidebar-text: {t.SidebarText};",
            $"    --color-sidebar-section: {t.SidebarSection};",
            $"    --color-bg-page: {t.PageBg};",
            $"    --color-focus-ring: {t.Primary};",
            $"    --bs-primary: {t.Primary};",
            $"    --bs-primary-rgb: {rb};",
            $"    --bs-primary-bg-subtle: {t.BsPrimaryBgSubtle};",
            $"    --bs-primary-border-subtle: {t.BsPrimaryBorderSubtle};",
            $"    --bs-primary-text: {t.BsPrimaryText};",
            $"    --bs-link-color: {linkInk};",
            $"    --bs-link-color-rgb: {linkInkRgb};",
            $"    --bs-link-hover-color: {linkHover};",
            $"    --bs-link-hover-color-rgb: {linkHoverRgb};",
            $"    --bs-body-bg: {t.PageBg};",
            $"    --bs-focus-ring-color: rgba({rb}, 0.28);",
            "}",
            "",
            "html:root[data-theme=\"dark\"] {",
            $"    --color-primary: {dPrimary};",
            $"    --color-primary-dark: {dPrimaryHover};",
            $"    --color-primary-darker: {dPrimaryBright};",
            $"    --color-primary-focus: rgba({dPrimaryRgb}, 0.30);",
            $"    --color-gold: {dGold};",
            $"    --color-gold-strong: {dGoldStrong};",
            $"    --color-gold-bright: {dGoldBright};",
            $"    --color-focus-ring: {dPrimaryHover};",
            $"    --bs-primary: {dPrimary};",
            $"    --bs-primary-rgb: {dPrimaryRgb};",
            $"    --bs-primary-bg-subtle: {ColorUtil.Blend(dPrimaryHover, "#0A0B0D", 0.82)};",
            $"    --bs-primary-border-subtle: {dPrimaryHover};",
            $"    --bs-primary-text: {dPrimaryBright};",
            $"    --bs-link-color: {dLink};",
            $"    --bs-link-color-rgb: {ColorUtil.RgbList(dLink)};",
            $"    --bs-link-hover-color: {dLinkHover};",
            $"    --bs-link-hover-color-rgb: {ColorUtil.RgbList(dLinkHover)};",
            $"    --bs-focus-ring-color: rgba({dPrimaryRgb}, 0.30);",
            "}");
    }
}

public static class BrandingExtensions
{
    public static string? LogoDataUri(this CompanyProfile? profile) =>
        profile is { HasLogo: true, LogoData: not null } && !string.IsNullOrEmpty(profile.LogoContentType)
            ? $"data:{profile.LogoContentType};base64,{Convert.ToBase64String(profile.LogoData)}"
            : null;
}
