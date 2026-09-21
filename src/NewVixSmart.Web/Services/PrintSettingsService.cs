using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.ViewModels.Core;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NewVixSmart.Web.Services;

public interface IPrintSettingsService
{
    Task<PrintSettingsViewModel> LoadAsync();
    bool ShowLogo(PrintGroup group);
    bool ShowCompanyName(PrintGroup group);
    bool ShowTagline(PrintGroup group);
    bool ShowCompanyContact(PrintGroup group);
    bool ShowFooter(PrintGroup group);
    bool ShowBarcode(PrintGroup group);
    bool ShowUnitPrice(PrintGroup group);
    bool ShowDiscountColumn(PrintGroup group);
    double FontScale(PrintGroup group);
    string PaperMargin(PrintGroup group);
    void Invalidate();
    Task<PrintLayoutOptions> GetLayoutAsync(PrintGroup group);
    Task SaveLayoutAsync(PrintGroup group, PrintLayoutOptions options);
    Task<PrintLayoutOptions> GetPreviewLayoutAsync(PrintGroup group, string? state);
}

public class PrintSettingsService : IPrintSettingsService
{
    private const string CacheKey = "print.settings.v1";
    private const string StudioCacheKey = "print.studio.v1";
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public PrintSettingsService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<PrintSettingsViewModel> LoadAsync()
    {
        if (_cache.TryGetValue(CacheKey, out PrintSettingsViewModel? cached) && cached != null)
            return cached;

        var rows = await _db.SystemSettings.AsNoTracking().ToListAsync();
        var vm = new PrintSettingsViewModel
        {
            SalesInvoice = BuildGroup(rows, PrintGroup.SalesInvoice),
            PurchaseInvoice = BuildGroup(rows, PrintGroup.PurchaseInvoice),
            SalesQuote = BuildGroup(rows, PrintGroup.SalesQuote),
            ItemLabel = BuildGroup(rows, PrintGroup.ItemLabel),
            FinancialReports = BuildGroup(rows, PrintGroup.FinancialReports)
        };

        _cache.Set(CacheKey, vm, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
        });
        return vm;
    }

    public void Invalidate()
    {
        _cache.Remove(CacheKey);
        _cache.Remove(StudioCacheKey);
    }

    public bool ShowLogo(PrintGroup group) => Get(group)?.ShowLogo ?? LegacyDefaultsFor(group).ShowLogo;
    public bool ShowCompanyName(PrintGroup group) => Get(group)?.ShowCompanyName ?? LegacyDefaultsFor(group).ShowCompanyName;
    public bool ShowTagline(PrintGroup group) => Get(group)?.ShowTagline ?? LegacyDefaultsFor(group).ShowTagline;
    public bool ShowCompanyContact(PrintGroup group) => Get(group)?.ShowCompanyContact ?? LegacyDefaultsFor(group).ShowCompanyContact;
    public bool ShowFooter(PrintGroup group) => Get(group)?.ShowFooter ?? LegacyDefaultsFor(group).ShowFooter;
    public bool ShowBarcode(PrintGroup group) => Get(group)?.ShowBarcode ?? LegacyDefaultsFor(group).ShowBarcode;
    public bool ShowUnitPrice(PrintGroup group) => Get(group)?.ShowUnitPrice ?? LegacyDefaultsFor(group).ShowUnitPrice;
    public bool ShowDiscountColumn(PrintGroup group) => Get(group)?.ShowDiscountColumn ?? LegacyDefaultsFor(group).ShowDiscountColumn;

    public double FontScale(PrintGroup group) => ParseScale(Get(group)?.FontScale);

    public string PaperMargin(PrintGroup group) => Get(group)?.PaperMargin ?? "normal";

    public async Task<PrintLayoutOptions> GetLayoutAsync(PrintGroup group)
    {
        var all = await LoadStudioAsync();
        return all.TryGetValue(group, out var options) ? options : DefaultsFor(group);
    }

    public async Task SaveLayoutAsync(PrintGroup group, PrintLayoutOptions options)
    {
        var o = Clamp(options);
        var slug = GroupSlug(group);

        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.PageSize)}", o.PageSize.ToString());
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.Orientation)}", o.Orientation.ToString());
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.Margin)}", o.Margin.ToString());
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.FontScale)}", o.FontScale.ToString(CultureInfo.InvariantCulture));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.Decimals)}", o.Decimals.ToString(CultureInfo.InvariantCulture));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowLogo)}", BoolString(o.ShowLogo));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.LogoScalePercent)}", o.LogoScalePercent.ToString(CultureInfo.InvariantCulture));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowCompanyName)}", BoolString(o.ShowCompanyName));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowTagline)}", BoolString(o.ShowTagline));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowCompanyContact)}", BoolString(o.ShowCompanyContact));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowTaxNumber)}", BoolString(o.ShowTaxNumber));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowDocTitle)}", BoolString(o.ShowDocTitle));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.AccentColor)}", o.AccentColor);
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.TableHeaderBg)}", o.TableHeaderBg);
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.TableHeaderText)}", o.TableHeaderText);
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowItemCode)}", BoolString(o.ShowItemCode));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowBarcode)}", BoolString(o.ShowBarcode));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowUnitPrice)}", BoolString(o.ShowUnitPrice));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowDiscountColumn)}", BoolString(o.ShowDiscountColumn));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowCount)}", BoolString(o.ShowCount));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowQuantity)}", BoolString(o.ShowQuantity));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowSubtotal)}", BoolString(o.ShowSubtotal));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowTotalDiscount)}", BoolString(o.ShowTotalDiscount));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowTotalTax)}", BoolString(o.ShowTotalTax));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowGrandTotal)}", BoolString(o.ShowGrandTotal));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowAmountInWords)}", BoolString(o.ShowAmountInWords));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowPaidBadge)}", BoolString(o.ShowPaidBadge));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowCreatedBy)}", BoolString(o.ShowCreatedBy));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowFooter)}", BoolString(o.ShowFooter));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowSignatureLines)}", BoolString(o.ShowSignatureLines));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.ShowPageNumbers)}", BoolString(o.ShowPageNumbers));
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.FooterNoteText)}", o.FooterNoteText);
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.SignatureOne)}", o.SignatureOne);
        await UpsertSettingAsync($"PrintStudio.{slug}.{nameof(PrintLayoutOptions.SignatureTwo)}", o.SignatureTwo);

        var legacyKeys = await _db.SystemSettings
            .Where(s => s.Key.StartsWith($"Print.{slug}."))
            .ToListAsync();
        if (legacyKeys.Count > 0) _db.SystemSettings.RemoveRange(legacyKeys);
        await _db.SaveChangesAsync();
        Invalidate();
    }

    public async Task<PrintLayoutOptions> GetPreviewLayoutAsync(PrintGroup group, string? state)
    {
        return DecodeState(state) ?? await GetLayoutAsync(group);
    }

    public static string SettingKey(PrintGroup group, string option) => $"Print.{GroupSlug(group)}.{option}";

    public static string StudioKey(PrintGroup group, string property) => $"PrintStudio.{GroupSlug(group)}.{property}";

    public static bool AnyKey(string key) =>
        key.StartsWith("PrintStudio.", StringComparison.Ordinal) || key.StartsWith("Print.", StringComparison.Ordinal);

    public static PrintGroup FromSlug(string? slug) => slug switch
    {
        "sales_invoice" => PrintGroup.SalesInvoice,
        "purchase_invoice" => PrintGroup.PurchaseInvoice,
        "sales_quote" => PrintGroup.SalesQuote,
        "item_label" => PrintGroup.ItemLabel,
        "financial_reports" => PrintGroup.FinancialReports,
        "sale_return" => PrintGroup.SaleReturn,
        "purchase_return" => PrintGroup.PurchaseReturn,
        "purchase_order" => PrintGroup.PurchaseOrder,
        "stock_transfer" => PrintGroup.StockTransfer,
        "customer_statement" => PrintGroup.CustomerStatement,
        "supplier_statement" => PrintGroup.SupplierStatement,
        _ => PrintGroup.SalesInvoice
    };

    public static PrintLayoutOptions Clamp(PrintLayoutOptions o)
    {
        o.PageSize = Enum.IsDefined(o.PageSize) ? o.PageSize : PrintPageSize.A4;
        o.Orientation = Enum.IsDefined(o.Orientation) ? o.Orientation : PrintOrientation.Portrait;
        o.Margin = Enum.IsDefined(o.Margin) ? o.Margin : PrintMarginSize.Normal;
        o.FontScale = Math.Clamp(Math.Round(o.FontScale * 20) / 20, 0.8, 1.3);
        o.Decimals = Math.Clamp((int)Math.Round((double)o.Decimals), 0, 4);
        o.LogoScalePercent = Math.Clamp((int)Math.Round(o.LogoScalePercent / 10d) * 10, 50, 150);
        o.AccentColor = AsHex(o.AccentColor, "#2e6fd8");
        o.TableHeaderBg = AsHex(o.TableHeaderBg, "#eaf3fc");
        o.TableHeaderText = AsHex(o.TableHeaderText, "#0d1b35");
        o.FooterNoteText = AsText(o.FooterNoteText, "شكراً لتعاملكم معنا");
        o.SignatureOne = AsText(o.SignatureOne, "إعداد");
        o.SignatureTwo = AsText(o.SignatureTwo, "اعتماد");
        return o;
    }

    public static string EncodeState(PrintLayoutOptions o)
    {
        var json = JsonSerializer.Serialize(o);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    public static PrintLayoutOptions? DecodeState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(state);
            var options = JsonSerializer.Deserialize<PrintLayoutOptions>(Encoding.UTF8.GetString(bytes));
            return options == null ? null : Clamp(options);
        }
        catch
        {
            return null;
        }
    }

    private async Task<Dictionary<PrintGroup, PrintLayoutOptions>> LoadStudioAsync()
    {
        if (_cache.TryGetValue(StudioCacheKey, out Dictionary<PrintGroup, PrintLayoutOptions>? cached) && cached != null)
            return cached;

        var rows = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.Key.StartsWith("PrintStudio."))
            .ToListAsync();

        var dict = new Dictionary<PrintGroup, PrintLayoutOptions>();
        foreach (var g in Enum.GetValues<PrintGroup>())
            dict[g] = BuildLayout(rows, g);

        _cache.Set(StudioCacheKey, dict, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
        });
        return dict;
    }

    private static PrintLayoutOptions BuildLayout(List<SystemSetting> rows, PrintGroup group)
    {
        var d = DefaultsFor(group);
        string? Raw(string property) => rows.FirstOrDefault(s => s.Key == StudioKey(group, property))?.Value;

        return new PrintLayoutOptions
        {
            PageSize = AsEnum(Raw(nameof(PrintLayoutOptions.PageSize)), d.PageSize),
            Orientation = AsEnum(Raw(nameof(PrintLayoutOptions.Orientation)), d.Orientation),
            Margin = AsEnum(Raw(nameof(PrintLayoutOptions.Margin)), d.Margin),
            FontScale = AsClampedDouble(Raw(nameof(PrintLayoutOptions.FontScale)), d.FontScale, 0.8, 1.3),
            Decimals = AsClampedInt(Raw(nameof(PrintLayoutOptions.Decimals)), d.Decimals, 0, 4),
            ShowLogo = AsBool(Raw(nameof(PrintLayoutOptions.ShowLogo)), d.ShowLogo),
            LogoScalePercent = AsClampedInt(Raw(nameof(PrintLayoutOptions.LogoScalePercent)), d.LogoScalePercent, 50, 150, 10),
            ShowCompanyName = AsBool(Raw(nameof(PrintLayoutOptions.ShowCompanyName)), d.ShowCompanyName),
            ShowTagline = AsBool(Raw(nameof(PrintLayoutOptions.ShowTagline)), d.ShowTagline),
            ShowCompanyContact = AsBool(Raw(nameof(PrintLayoutOptions.ShowCompanyContact)), d.ShowCompanyContact),
            ShowTaxNumber = AsBool(Raw(nameof(PrintLayoutOptions.ShowTaxNumber)), d.ShowTaxNumber),
            ShowDocTitle = AsBool(Raw(nameof(PrintLayoutOptions.ShowDocTitle)), d.ShowDocTitle),
            AccentColor = AsHex(Raw(nameof(PrintLayoutOptions.AccentColor)), d.AccentColor),
            TableHeaderBg = AsHex(Raw(nameof(PrintLayoutOptions.TableHeaderBg)), d.TableHeaderBg),
            TableHeaderText = AsHex(Raw(nameof(PrintLayoutOptions.TableHeaderText)), d.TableHeaderText),
            ShowItemCode = AsBool(Raw(nameof(PrintLayoutOptions.ShowItemCode)), d.ShowItemCode),
            ShowBarcode = AsBool(Raw(nameof(PrintLayoutOptions.ShowBarcode)), d.ShowBarcode),
            ShowUnitPrice = AsBool(Raw(nameof(PrintLayoutOptions.ShowUnitPrice)), d.ShowUnitPrice),
            ShowDiscountColumn = AsBool(Raw(nameof(PrintLayoutOptions.ShowDiscountColumn)), d.ShowDiscountColumn),
            ShowCount = AsBool(Raw(nameof(PrintLayoutOptions.ShowCount)), d.ShowCount),
            ShowQuantity = AsBool(Raw(nameof(PrintLayoutOptions.ShowQuantity)), d.ShowQuantity),
            ShowSubtotal = AsBool(Raw(nameof(PrintLayoutOptions.ShowSubtotal)), d.ShowSubtotal),
            ShowTotalDiscount = AsBool(Raw(nameof(PrintLayoutOptions.ShowTotalDiscount)), d.ShowTotalDiscount),
            ShowTotalTax = AsBool(Raw(nameof(PrintLayoutOptions.ShowTotalTax)), d.ShowTotalTax),
            ShowGrandTotal = AsBool(Raw(nameof(PrintLayoutOptions.ShowGrandTotal)), d.ShowGrandTotal),
            ShowAmountInWords = AsBool(Raw(nameof(PrintLayoutOptions.ShowAmountInWords)), d.ShowAmountInWords),
            ShowPaidBadge = AsBool(Raw(nameof(PrintLayoutOptions.ShowPaidBadge)), d.ShowPaidBadge),
            ShowCreatedBy = AsBool(Raw(nameof(PrintLayoutOptions.ShowCreatedBy)), d.ShowCreatedBy),
            ShowFooter = AsBool(Raw(nameof(PrintLayoutOptions.ShowFooter)), d.ShowFooter),
            ShowSignatureLines = AsBool(Raw(nameof(PrintLayoutOptions.ShowSignatureLines)), d.ShowSignatureLines),
            ShowPageNumbers = AsBool(Raw(nameof(PrintLayoutOptions.ShowPageNumbers)), d.ShowPageNumbers),
            FooterNoteText = AsText(Raw(nameof(PrintLayoutOptions.FooterNoteText)), d.FooterNoteText),
            SignatureOne = AsText(Raw(nameof(PrintLayoutOptions.SignatureOne)), d.SignatureOne),
            SignatureTwo = AsText(Raw(nameof(PrintLayoutOptions.SignatureTwo)), d.SignatureTwo)
        };
    }

    private PrintGroupSettings? Get(PrintGroup group) =>
        _cache.TryGetValue(CacheKey, out PrintSettingsViewModel? vm) && vm != null ? vm.GetGroup(group) : null;

    private static PrintGroupSettings BuildGroup(List<SystemSetting> rows, PrintGroup group)
    {
        var d = LegacyDefaultsFor(group);
        string? Raw(string option) => rows.FirstOrDefault(s => s.Key == SettingKey(group, option))?.Value;
        var fontScale = Raw(nameof(PrintGroupSettings.FontScale));
        var paperMargin = Raw(nameof(PrintGroupSettings.PaperMargin));

        return new PrintGroupSettings
        {
            ShowLogo = AsBool(Raw(nameof(PrintGroupSettings.ShowLogo)), d.ShowLogo),
            ShowCompanyName = AsBool(Raw(nameof(PrintGroupSettings.ShowCompanyName)), d.ShowCompanyName),
            ShowTagline = AsBool(Raw(nameof(PrintGroupSettings.ShowTagline)), d.ShowTagline),
            ShowCompanyContact = AsBool(Raw(nameof(PrintGroupSettings.ShowCompanyContact)), d.ShowCompanyContact),
            ShowFooter = AsBool(Raw(nameof(PrintGroupSettings.ShowFooter)), d.ShowFooter),
            ShowBarcode = AsBool(Raw(nameof(PrintGroupSettings.ShowBarcode)), d.ShowBarcode),
            ShowUnitPrice = AsBool(Raw(nameof(PrintGroupSettings.ShowUnitPrice)), d.ShowUnitPrice),
            ShowDiscountColumn = AsBool(Raw(nameof(PrintGroupSettings.ShowDiscountColumn)), d.ShowDiscountColumn),
            FontScale = fontScale is "0.9" or "1.0" or "1.1" ? fontScale! : d.FontScale,
            PaperMargin = paperMargin is "normal" or "narrow" ? paperMargin! : d.PaperMargin
        };
    }

    private static PrintLayoutOptions DefaultsFor(PrintGroup group)
    {
        var o = new PrintLayoutOptions();
        switch (group)
        {
            case PrintGroup.SalesInvoice:
            case PrintGroup.PurchaseInvoice:
                o.ShowTagline = false;
                o.ShowCompanyContact = false;
                o.ShowDiscountColumn = false;
                break;
            case PrintGroup.SalesQuote:
                o.ShowBarcode = false;
                o.ShowTagline = false;
                o.ShowCompanyContact = false;
                break;
            case PrintGroup.ItemLabel:
                o.ShowTagline = false;
                o.ShowCompanyContact = false;
                o.ShowDiscountColumn = false;
                o.ShowCount = false;
                o.ShowQuantity = false;
                break;
            case PrintGroup.FinancialReports:
                o.ShowBarcode = false;
                o.ShowUnitPrice = false;
                o.ShowDiscountColumn = false;
                o.ShowCount = false;
                o.ShowQuantity = false;
                break;
            case PrintGroup.SaleReturn:
            case PrintGroup.PurchaseReturn:
                o.ShowTagline = false;
                o.ShowCompanyContact = false;
                break;
            case PrintGroup.PurchaseOrder:
                o.ShowTagline = false;
                o.ShowCompanyContact = false;
                o.ShowDiscountColumn = false;
                break;
            case PrintGroup.StockTransfer:
            case PrintGroup.CustomerStatement:
            case PrintGroup.SupplierStatement:
                o.ShowBarcode = false;
                o.ShowUnitPrice = false;
                o.ShowDiscountColumn = false;
                o.ShowCount = false;
                o.ShowQuantity = false;
                break;
        }
        return o;
    }

    private static PrintGroupSettings LegacyDefaultsFor(PrintGroup group)
    {
        var d = new PrintGroupSettings
        {
            ShowLogo = true,
            ShowCompanyName = true,
            ShowTagline = true,
            ShowCompanyContact = true,
            ShowFooter = true,
            ShowUnitPrice = true,
            ShowDiscountColumn = true
        };
        switch (group)
        {
            case PrintGroup.SalesInvoice:
            case PrintGroup.PurchaseInvoice:
            case PrintGroup.ItemLabel:
                d.ShowBarcode = true;
                d.ShowTagline = false;
                d.ShowCompanyContact = false;
                d.ShowDiscountColumn = false;
                break;
            case PrintGroup.SalesQuote:
                d.ShowBarcode = false;
                break;
            case PrintGroup.FinancialReports:
                d.ShowBarcode = false;
                d.ShowUnitPrice = false;
                d.ShowDiscountColumn = false;
                break;
        }
        return d;
    }

    private static bool AsBool(string? value, bool fallback) =>
        value == "true" ? true : value == "false" ? false : fallback;

    private static string BoolString(bool value) => value ? "true" : "false";

    private static double ParseScale(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
            ? Math.Clamp(scale, 0.8, 1.3)
            : 1.0;

    private static TEnum AsEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
    {
        if (value != null && Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        return fallback;
    }

    private static double AsClampedDouble(string? value, double fallback, double min, double max) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;

    private static int AsClampedInt(string? value, int fallback, int min, int max, int step = 1) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp((int)Math.Round(parsed / (double)step) * step, min, max)
            : fallback;

    private static string AsText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string AsHex(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value.Trim().TrimStart('#');
        if (v.Length != 6) return fallback;
        foreach (var c in v)
            if (!Uri.IsHexDigit(c)) return fallback;
        return $"#{v.ToLowerInvariant()}";
    }

    private async Task UpsertSettingAsync(string key, string? value)
    {
        var existing = await _db.SystemSettings.FindAsync(key);
        if (existing == null)
        {
            _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static string GroupSlug(PrintGroup group) => group switch
    {
        PrintGroup.SalesInvoice => "sales_invoice",
        PrintGroup.PurchaseInvoice => "purchase_invoice",
        PrintGroup.SalesQuote => "sales_quote",
        PrintGroup.ItemLabel => "item_label",
        PrintGroup.FinancialReports => "financial_reports",
        PrintGroup.SaleReturn => "sale_return",
        PrintGroup.PurchaseReturn => "purchase_return",
        PrintGroup.PurchaseOrder => "purchase_order",
        PrintGroup.StockTransfer => "stock_transfer",
        PrintGroup.CustomerStatement => "customer_statement",
        PrintGroup.SupplierStatement => "supplier_statement",
        _ => "sales_invoice"
    };
}