using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.ViewModels.Core;
using Silk.Trading.Web.Services;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace Silk.Trading.Web.Controllers;

[Authorize]
[RequirePerm("Settings.View")]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly IBrandingService _branding;
    private readonly IPrintSettingsService _printSettings;
    public SettingsController(AppDbContext db, IHttpContextAccessor http, IBrandingService branding, IPrintSettingsService printSettings)
    {
        _db = db;
        _http = http;
        _branding = branding;
        _printSettings = printSettings;
    }

    public async Task<IActionResult> Index()
    {
        var vm = new SettingsViewModel
        {
            Units = await _db.Units.AsNoTracking().OrderBy(u => u.Name).ToListAsync(),
            Categories = await _db.ItemCategories.Include(c => c.Items).AsNoTracking().OrderBy(c => c.Name).ToListAsync(),
            ItemTypes = await _db.ItemTypes.Include(t => t.Items).AsNoTracking().OrderBy(t => t.Name).ToListAsync(),
            Currencies = await _db.Currencies.AsNoTracking().OrderByDescending(c => c.IsBase).ThenBy(c => c.Code).ToListAsync(),
            Branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Code).ToListAsync(),
            CurrentBranchId = _http.GetCurrentBranchId()
        };
        return View(vm);
    }

    // ---------- Currencies ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> AddCurrency(AddCurrencyRequest request)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct());
            return RedirectToAction(nameof(Index));
        }
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Currencies.AnyAsync(c => c.Code == code))
        {
            TempData["Error"] = "عملة بهذا الرمز موجودة بالفعل";
            return RedirectToAction(nameof(Index));
        }
        _db.Currencies.Add(new Currency
        {
            Code = code,
            Name = request.Name.Trim(),
            Symbol = request.Symbol,
            ExchangeRate = request.ExchangeRate,
            IsActive = request.IsActive,
            IsBase = false
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة العملة بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> UpdateCurrency(Currency currency)
    {
        var existing = await _db.Currencies.FindAsync(currency.Id);
        if (existing == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(currency.Code) && !string.IsNullOrWhiteSpace(currency.Name) &&
            currency.ExchangeRate > 0)
        {
            if (await _db.Currencies.AnyAsync(c => c.Id != currency.Id && c.Code == currency.Code.Trim().ToUpperInvariant()))
                TempData["Error"] = "عملة بهذا الرمز موجودة بالفعل";
            else
            {
                existing.Code = currency.Code.Trim().ToUpperInvariant();
                existing.Name = currency.Name.Trim();
                existing.Symbol = currency.Symbol;
                existing.ExchangeRate = currency.ExchangeRate;
                existing.IsActive = currency.IsActive;
                await _db.SaveChangesAsync();
                TempData["Success"] = "تم تعديل العملة بنجاح";
            }
        }
        else
        {
            TempData["Error"] = "تحقق من بيانات العملة وسعر الصرف";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> DeleteCurrency(int id)
    {
        var currency = await _db.Currencies.FindAsync(id);
        if (currency == null) return NotFound();
        if (currency.IsBase)
        {
            TempData["Error"] = "لا يمكن حذف العملة الأساسية؛ اختر أساسًا آخر أولاً";
            return RedirectToAction(nameof(Index));
        }
        bool inUse = await _db.SaleInvoices.AnyAsync(s => s.CurrencyId == id) ||
                     await _db.PurchaseInvoices.AnyAsync(p => p.CurrencyId == id) ||
                     await _db.Customers.AnyAsync(c => c.CurrencyId == id) ||
                     await _db.Suppliers.AnyAsync(s => s.CurrencyId == id);
        if (inUse)
        {
            currency.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Error"] = "لا يمكن حذف العملة لأنها مستخدمة؛ تم تعطيلها بدلاً من ذلك";
        }
        else
        {
            _db.Currencies.Remove(currency);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم حذف العملة بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> SetBaseCurrency(int id)
    {
        var currency = await _db.Currencies.FindAsync(id);
        if (currency == null) return NotFound();
        currency.IsBase = true;
        currency.ExchangeRate = 1m;
        await _db.Currencies.Where(c => c.Id != id).ForEachAsync(c => c.IsBase = false);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"أُعيدت إلى العملة الأساسية: {currency.Name}";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Branches ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> AddBranch(Branch branch)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct());
            return RedirectToAction(nameof(Index));
        }
        branch.Code = branch.Code.Trim().ToUpperInvariant();
        if (await _db.Branches.AnyAsync(b => b.Code == branch.Code))
            TempData["Error"] = "فرع بهذا الرمز موجود بالفعل";
        else
        {
            branch.CreatedAt = DateTime.UtcNow;
            _db.Branches.Add(branch);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم إضافة الفرع بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> UpdateBranch(Branch branch)
    {
        var existing = await _db.Branches.FindAsync(branch.Id);
        if (existing == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(branch.Code) && !string.IsNullOrWhiteSpace(branch.Name))
        {
            if (await _db.Branches.AnyAsync(b => b.Id != branch.Id && b.Code == branch.Code.Trim().ToUpperInvariant()))
                TempData["Error"] = "فرع بهذا الرمز موجود بالفعل";
            else
            {
                existing.Code = branch.Code.Trim().ToUpperInvariant();
                existing.Name = branch.Name.Trim();
                existing.Address = branch.Address;
                existing.Phone = branch.Phone;
                existing.IsActive = branch.IsActive;
                await _db.SaveChangesAsync();
                TempData["Success"] = "تم تعديل الفرع بنجاح";
            }
        }
        else
        {
            TempData["Error"] = "يرجى إدخال رمز واسم الفرع";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> DeleteBranch(int id)
    {
        var branch = await _db.Branches.FindAsync(id);
        if (branch == null) return NotFound();
        bool inUse = await _db.SaleInvoices.AnyAsync(s => s.BranchId == id) ||
                     await _db.PurchaseInvoices.AnyAsync(p => p.BranchId == id) ||
                     await _db.Payments.AnyAsync(p => p.BranchId == id) ||
                     await _db.JournalEntries.AnyAsync(j => j.BranchId == id);
        if (inUse)
        {
            branch.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Error"] = "لا يمكن حذف الفرع لأن لديه حركات؛ تم تعطيله بدلاً من ذلك";
        }
        else
        {
            _db.Branches.Remove(branch);
            await _db.SaveChangesAsync();
            _http.SetCurrentBranchId(null);
            TempData["Success"] = "تم حذف الفرع بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> SetCurrentBranch(int branchId)
    {
        if (branchId <= 0 || !await _db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive))
        {
            TempData["Error"] = "الفرع المحدد غير موجود أو غير نشط";
            return RedirectToAction(nameof(Index));
        }
        _http.SetCurrentBranchId(branchId);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> AddUnit(Unit unit)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct());
            return RedirectToAction(nameof(Index));
        }
        if (await _db.Units.AnyAsync(u => u.Name == unit.Name.Trim()))
        {
            TempData["Error"] = "الوحدة بهذا الاسم موجودة بالفعل";
        }
        else if (unit.Id == unit.ParentUnitId || await CreatesCycleAsync(unit.Id, unit.ParentUnitId))
        {
            TempData["Error"] = "لا يمكن ربط الوحدة بنفسها أو إنشاء حلقة في الوحدات";
        }
        else
        {
            unit.Id = 0;
            unit.Name = unit.Name.Trim();
            unit.ShortName = string.IsNullOrWhiteSpace(unit.ShortName) ? unit.Name : unit.ShortName;
            _db.Units.Add(unit);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم إضافة الوحدة بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> UpdateUnit(Unit unit)
    {
        var existing = await _db.Units.FindAsync(unit.Id);
        if (existing != null && !string.IsNullOrWhiteSpace(unit.Name) && unit.ParentUnitId != unit.Id && !await CreatesCycleAsync(unit.Id, unit.ParentUnitId))
        {
            if (await _db.Units.AnyAsync(u => u.Id != unit.Id && u.Name == unit.Name.Trim()))
                TempData["Error"] = "الوحدة بهذا الاسم موجودة بالفعل";
            else
            {
                existing.Name = unit.Name.Trim();
                existing.ShortName = string.IsNullOrWhiteSpace(unit.ShortName) ? unit.Name : unit.ShortName;
                existing.SubUnits = unit.SubUnits;
                existing.ParentUnitId = unit.ParentUnitId;
                existing.IsActive = unit.IsActive;
                await _db.SaveChangesAsync();
                TempData["Success"] = "تم تعديل الوحدة بنجاح";
            }
        }
        else
        {
            TempData["Error"] = "تعذر حفظ التعديلات، تحقق من البيانات المدخلة أو من عدم إنشاء حلقة في الوحدات";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> DeleteUnit(int id)
    {
        var unit = await _db.Units.FindAsync(id);
        if (unit == null) return NotFound();

        bool inUse = await _db.Items.AnyAsync(i => i.CountUnitId == id || i.QuantityUnitId == id) ||
                     await _db.Units.AnyAsync(u => u.ParentUnitId == id);

        if (inUse)
        {
            unit.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Error"] = "لا يمكن حذف الوحدة لأنها مستخدمة في أصناف؛ تم تعطيلها بدلاً من ذلك";
        }
        else
        {
            _db.Units.Remove(unit);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم حذف الوحدة بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<BrandingViewModel> BuildBrandingViewModelAsync()
    {
        var data = await _branding.LoadAsync();
        var p = data.Profile;
        return new BrandingViewModel
        {
            CompanyName = p.CompanyName,
            Tagline = p.Tagline,
            Address = p.Address,
            Phone = p.Phone,
            Email = p.Email,
            TaxNumber = p.TaxNumber,
            Primary = data.Theme.Primary,
            Accent = data.Theme.Accent,
            SidebarBg = data.Theme.SidebarBg,
            PageBg = data.Theme.PageBg,
            HasLogo = p.HasLogo,
            LogoFileName = p.LogoFileName,
            LogoDataUri = p.LogoDataUri(),
            Presets = _branding.Presets
        };
    }

    public async Task<IActionResult> Branding() => View(await BuildBrandingViewModelAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> SaveBranding(BrandingViewModel vm)
    {
        ModelState.Remove(nameof(vm.Presets));
        ModelState.Remove(nameof(vm.LogoDataUri));
        ModelState.Remove(nameof(vm.LogoFile));
        if (!ModelState.IsValid)
        {
            vm.Presets = _branding.Presets;
            return View("Branding", vm);
        }

        if (!IsValidHex(vm.Primary) || !IsValidHex(vm.Accent) || !IsValidHex(vm.SidebarBg) || !IsValidHex(vm.PageBg))
        {
            vm.Presets = _branding.Presets;
            ModelState.AddModelError("", "أحد قيم الألوان غير صالحة؛ استخدم صيغة #RRGGBB");
            return View("Branding", vm);
        }

        var profile = await _db.CompanyProfiles.OrderBy(p => p.Id).FirstOrDefaultAsync();
        if (profile == null)
        {
            profile = new CompanyProfile();
            _db.CompanyProfiles.Add(profile);
        }

        profile.CompanyName = vm.CompanyName.Trim();
        profile.Tagline = string.IsNullOrWhiteSpace(vm.Tagline) ? null : vm.Tagline.Trim();
        profile.Address = string.IsNullOrWhiteSpace(vm.Address) ? null : vm.Address.Trim();
        profile.Phone = string.IsNullOrWhiteSpace(vm.Phone) ? null : vm.Phone.Trim();
        profile.Email = string.IsNullOrWhiteSpace(vm.Email) ? null : vm.Email.Trim();
        profile.TaxNumber = string.IsNullOrWhiteSpace(vm.TaxNumber) ? null : vm.TaxNumber.Trim();
        profile.UpdatedAt = DateTime.UtcNow;

        if (vm.RemoveLogo)
        {
            profile.LogoData = null;
            profile.LogoContentType = null;
            profile.LogoFileName = null;
        }
        else if (vm.LogoFile is { Length: > 0 })
        {
            var (ok, error, contentType) = ValidateLogo(vm.LogoFile);
            if (!ok)
            {
                vm.Presets = _branding.Presets;
                ModelState.AddModelError(nameof(vm.LogoFile), error);
                return View("Branding", vm);
            }
            using var ms = new MemoryStream();
            await vm.LogoFile.CopyToAsync(ms);
            profile.LogoData = ms.ToArray();
            profile.LogoContentType = contentType;
            profile.LogoFileName = Path.GetFileName(vm.LogoFile.FileName);
        }

        await SetSettingAsync("Theme.Primary", NormalizeHex(vm.Primary));
        await SetSettingAsync("Theme.Accent", NormalizeHex(vm.Accent));
        await SetSettingAsync("Theme.SidebarBg", NormalizeHex(vm.SidebarBg));
        await SetSettingAsync("Theme.PageBg", NormalizeHex(vm.PageBg));
        await SetSettingAsync("Theme.Preset", vm.SelectedPreset);

        await _db.SaveChangesAsync();
        _branding.Invalidate();
        TempData["Success"] = "تم حفظ العلامة التجارية والمظهر بنجاح";
        return RedirectToAction(nameof(Branding));
    }

    public async Task<IActionResult> Printing()
    {
        var modes = new (string Slug, PrintGroup Group)[]
        {
            ("sales_invoice", PrintGroup.SalesInvoice),
            ("purchase_invoice", PrintGroup.PurchaseInvoice),
            ("sales_quote", PrintGroup.SalesQuote),
            ("item_label", PrintGroup.ItemLabel),
            ("financial_reports", PrintGroup.FinancialReports),
            ("sale_return", PrintGroup.SaleReturn),
            ("purchase_return", PrintGroup.PurchaseReturn),
            ("purchase_order", PrintGroup.PurchaseOrder),
            ("stock_transfer", PrintGroup.StockTransfer),
            ("customer_statement", PrintGroup.CustomerStatement),
            ("supplier_statement", PrintGroup.SupplierStatement)
        };
        var state = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (slug, group) in modes)
        {
            var options = await _printSettings.GetLayoutAsync(group);
            var props = typeof(PrintLayoutOptions).GetProperties();
            var entry = new Dictionary<string, string>(props.Length);
            foreach (var prop in props)
            {
                var value = prop.GetValue(options);
                entry[prop.Name] = value switch
                {
                    null => "",
                    string s => s,
                    bool b => b ? "true" : "false",
                    _ when prop.Name == nameof(PrintLayoutOptions.FontScale) => ((double)value).ToString("0.00", CultureInfo.InvariantCulture),
                    System.Enum e => e.ToString(),
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
                };
            }
            state[slug] = entry;
        }
        ViewBag.StudioState = JsonSerializer.Serialize(state).Replace("<", "\\u003c").Replace(">", "\\u003e");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> SavePrinting(PrintGroup group, PrintLayoutOptions vm, bool applyToAll)
    {
        var options = PrintSettingsService.Clamp(vm);
        if (applyToAll)
        {
            foreach (var g in Enum.GetValues<PrintGroup>())
                await _printSettings.SaveLayoutAsync(g, options);
        }
        else
        {
            await _printSettings.SaveLayoutAsync(Enum.IsDefined(group) ? group : PrintGroup.SalesInvoice, options);
        }
        TempData["Success"] = "تم حفظ إعدادات الطباعة بنجاح";
        return RedirectToAction(nameof(Printing));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> ResetPrinting()
    {
        var keys = await _db.SystemSettings.Where(s => s.Key.StartsWith("PrintStudio.") || s.Key.StartsWith("Print.")).ToListAsync();
        if (keys.Count > 0)
        {
            _db.SystemSettings.RemoveRange(keys);
            await _db.SaveChangesAsync();
        }
        _printSettings.Invalidate();
        TempData["Success"] = "تم استعادة إعدادات الطباعة الافتراضية";
        return RedirectToAction(nameof(Printing));
    }

    public async Task<IActionResult> PrintPreview(string group, string? state)
    {
        var g = PrintSettingsService.FromSlug(group);
        var options = await _printSettings.GetPreviewLayoutAsync(g, state);
        var sample = PrintSampleDocs.Build(g, options);
        var nonce = HttpContext.GetCspNonce();
        Response.Headers.Remove("Content-Security-Policy");
        Response.Headers.Remove("X-Frame-Options");
        Response.Headers.Append("Content-Security-Policy",
            $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; base-uri 'self'; object-src 'none'; frame-ancestors 'self'");
        Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
        return View("PrintPreview", sample);
    }

    public async Task<IActionResult> PrintPdfPreview(string group, string? state)
    {
        var g = PrintSettingsService.FromSlug(group);
        var options = await _printSettings.GetPreviewLayoutAsync(g, state);
        var bytes = PdfInvoiceService.RenderPreviewPdf(g, options);
        return File(bytes, "application/pdf", $"preview-{group}.pdf");
    }

    private async Task SetSettingAsync(string key, string? value)
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

    private static (bool Ok, string Error, string? ContentType) ValidateLogo(IFormFile file)
    {
        if (file.Length == 0 || file.Length > 2 * 1024 * 1024)
            return (false, "حجم الشعار يجب ألا يتجاوز 2 ميجابايت", null);
        using var stream = file.OpenReadStream();
        var head = new byte[12];
        int read = stream.Read(head, 0, head.Length);
        string? type = null;
        if (read >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
            type = "image/png";
        else if (read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            type = "image/jpeg";
        else if (read >= 8 && head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F' && head[4] == (byte)'W' && head[5] == (byte)'E' && head[6] == (byte)'B' && head[7] == (byte)'P')
            type = "image/webp";
        if (type == null)
            return (false, "صيغة غير صالحة؛ استخدم PNG أو JPEG أو WEBP", null);
        return (true, "", type);
    }

    private static bool IsValidHex(string value)
    {
        try { _ = ColorUtil.HexToRgb(value); return true; }
        catch { return false; }
    }

    private static string NormalizeHex(string value)
    {
        var h = value.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => char.ToString(c) + char.ToString(c)));
        return $"#{h.ToLowerInvariant()}";
    }

    private async Task<bool> CreatesCycleAsync(int unitId, int? parentUnitId)
    {
        if (parentUnitId == null) return false;
        var visited = new HashSet<int>();
        int? current = parentUnitId;
        while (current != null && visited.Add(current.Value))
        {
            if (current.Value == unitId) return true;
            var parent = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.Value);
            current = parent?.ParentUnitId;
        }
        return false;
    }
}

public class AddCurrencyRequest
{
    [Required(ErrorMessage = "رمز العملة مطلوب")]
    [StringLength(10)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم العملة مطلوب")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(10)]
    public string? Symbol { get; set; }

    [Range(0.000001, 999999999, ErrorMessage = "سعر الصرف يجب أن يكون أكبر من صفر")]
    public decimal ExchangeRate { get; set; } = 1m;

    public bool IsActive { get; set; } = true;
}