using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Services;

public sealed record PalettePreset(string Id, string NameAr, string Primary, string Accent, string SidebarBg, string PageBg);

public sealed class BrandingTheme
{
    public string Primary { get; set; } = "#0e9f6e";
    public string PrimaryDark { get; set; } = "#0b7a54";
    public string PrimaryDarker { get; set; } = "#0a5e42";
    public string PrimaryFocus { get; set; } = "rgba(16, 185, 129, 0.22)";
    public string Accent { get; set; } = "#10b981";
    public string AccentStrong { get; set; } = "#0b7a54";
    public string AccentBright { get; set; } = "#34d399";
    public string SidebarBg { get; set; } = "#ffffff";
    public string SidebarText { get; set; } = "#0e1620";
    public string SidebarSection { get; set; } = "#6b7785";
    public string PageBg { get; set; } = "#f1f4f6";
    public string BsPrimaryBgSubtle { get; set; } = "#dff3ec";
    public string BsPrimaryBorderSubtle { get; set; } = "#a8ddc6";
    public string BsPrimaryText { get; set; } = "#0a5e42";
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
    private const string CacheKey = "branding.v1";
    private static readonly Dictionary<string, PalettePreset> PresetMap = new()
    {
        ["modern"] = new("modern", "كحلي أورورا — زجاجي", "#0e9f6e", "#10b981", "#ffffff", "#f1f4f6"),
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

    public PalettePreset[] Presets => PresetMap.Values.ToArray();

    public async Task<BrandingData> LoadAsync()
    {
        if (_cache.TryGetValue(CacheKey, out BrandingData? cached) && cached != null)
            return cached;

        var profile = await _db.CompanyProfiles.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync() ?? new CompanyProfile();
        var settings = await _db.SystemSettings.AsNoTracking().ToListAsync();
        var theme = BuildTheme(settings);

        var data = new BrandingData { Profile = profile, Theme = theme };
        _cache.Set(CacheKey, data, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
        });
        return data;
    }

    public async Task<CompanyProfile> GetProfileAsync() => (await LoadAsync()).Profile;

    public async Task<BrandingTheme> GetThemeAsync() => (await LoadAsync()).Theme;

    public void Invalidate() => _cache.Remove(CacheKey);

    private static BrandingTheme BuildTheme(List<SystemSetting> settings)
    {
        string? Get(string key) =>
            settings.FirstOrDefault(s => s.Key == key)?.Value;

        var presetId = Get("Theme.Preset");
        if (presetId != null && PresetMap.TryGetValue(presetId, out var preset))
            return FromPreset(preset);

        var primary = FirstValid(Get("Theme.Primary"), "#0e9f6e");
        var accent = FirstValid(Get("Theme.Accent"), "#10b981");
        var sidebarBg = FirstValid(Get("Theme.SidebarBg"), "#ffffff");
        var pageBg = FirstValid(Get("Theme.PageBg"), "#f1f4f6");

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
            SidebarText = sidebarDark ? "#cbd5e1" : "#0e1620",
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
            SidebarText = sidebarDark ? "#cbd5e1" : "#0e1620",
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
        if (string.IsNullOrWhiteSpace(value)) return false;
        var h = value.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => char.ToString(c) + char.ToString(c)));
        return h.Length == 6 && h.All(Uri.IsHexDigit);
    }

    private static string DarkenSafe(string? value, string fallback) =>
        IsValidHex(value) ? ColorUtil.Darken(value!, 0.09) : ColorUtil.Darken(fallback, 0.09);

    private static string GuaranteeLinkInk(string hex)
    {
        var current = hex;
        var guard = 0;
        while (ColorUtil.RelativeLuminance(current) > 0.15 && guard++ < 24)
            current = ColorUtil.Darken(current, 0.07);
        return current;
    }

    public string RenderThemeCss(BrandingTheme t)
    {
        var rb = ColorUtil.RgbList(t.Primary);
        var linkInk = GuaranteeLinkInk(t.PrimaryDarker);
        var linkHover = ColorUtil.Darken(linkInk, 0.05);
        var linkHoverRgb = ColorUtil.RgbList(linkHover);
        var linkInkRgb = ColorUtil.RgbList(linkInk);
        return string.Join('\n',
            ":root {",
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