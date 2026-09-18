using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Core;

namespace Silk.Trading.Web.Services;

public sealed record PalettePreset(string Id, string NameAr, string Primary, string Accent, string SidebarBg, string PageBg);

public sealed class BrandingTheme
{
    public string Primary { get; set; } = "#0b7a54";
    public string PrimaryDark { get; set; } = "#096b49";
    public string PrimaryDarker { get; set; } = "#075636";
    public string PrimaryFocus { get; set; } = "rgba(11, 122, 84, 0.22)";
    public string Accent { get; set; } = "#b98b12";
    public string AccentStrong { get; set; } = "#8f6b08";
    public string AccentBright { get; set; } = "#e8b83a";
    public string SidebarBg { get; set; } = "#ffffff";
    public string SidebarText { get; set; } = "#22302c";
    public string SidebarSection { get; set; } = "#55625e";
    public string PageBg { get; set; } = "#f5f8f6";
    public string BsPrimaryBgSubtle { get; set; } = "#e6f4ee";
    public string BsPrimaryBorderSubtle { get; set; } = "#cbeae0";
    public string BsPrimaryText { get; set; } = "#075636";
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
        ["modern"] = new("modern", "أبيض زمردي — عصري", "#0b7a54", "#b98b12", "#ffffff", "#f5f8f6"),
        ["evergreen"] = new("evergreen", "زمردي ذهبي", "#0f766e", "#a8842c", "#0b2e26", "#f5f4ef"),
        ["indigo"] = new("indigo", "ملكي نيلي", "#4338ca", "#b45309", "#1e1b4b", "#f6f6fb"),
        ["crimson"] = new("crimson", "قرمزي عتيق", "#b91c1c", "#b45309", "#450a0a", "#faf6f3"),
        ["ocean"] = new("ocean", "أزرق محيطي", "#0369a1", "#0f766e", "#082f49", "#f4f7fa"),
        ["wine"] = new("wine", "نبيذي برونزي", "#7f1d1d", "#8a5a00", "#2b0f0f", "#faf6f2"),
        ["slate"] = new("slate", "قائم سماوي", "#0e7490", "#0f766e", "#122f3a", "#f4f7f8"),
        ["ivory-light"] = new("ivory-light", "عاجي فاتح", "#0f766e", "#a8842c", "#f5f4ef", "#ffffff")
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

        var primary = FirstValid(Get("Theme.Primary"), "#0b7a54");
        var accent = FirstValid(Get("Theme.Accent"), "#b98b12");
        var sidebarBg = FirstValid(Get("Theme.SidebarBg"), "#ffffff");
        var pageBg = FirstValid(Get("Theme.PageBg"), "#f5f8f6");

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
            SidebarText = sidebarDark ? "#cbd5e1" : "#22302c",
            SidebarSection = sidebarDark ? "#94a3b8" : "#55625e",
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
            SidebarText = sidebarDark ? "#cbd5e1" : "#22302c",
            SidebarSection = sidebarDark ? "#94a3b8" : "#55625e",
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

    public string RenderThemeCss(BrandingTheme t)
    {
        var rb = ColorUtil.RgbList(t.Primary);
        var linkHover = ColorUtil.Darken(t.Primary, 0.08);
        var linkHoverRgb = ColorUtil.RgbList(linkHover);
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
            $"    --bs-link-color: {t.Primary};",
            $"    --bs-link-color-rgb: {rb};",
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