using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.ViewModels.Export;

namespace NewVixSmart.Web.Services;

public class ExportCenterService : IExportCenterService
{
    private const int MaxExportRows = 50_000;
    private readonly AppDbContext _db;
    private readonly Dictionary<string, Func<Task<byte[]>>> _xlsx;
    private readonly Dictionary<string, Func<Task<byte[]>>> _csv;

    public ExportCenterService(AppDbContext db)
    {
        _db = db;
        _xlsx = new Dictionary<string, Func<Task<byte[]>>>(StringComparer.Ordinal)
        {
            ["suppliers"] = () => SuppliersXlsxAsync(),
            ["customers"] = () => CustomersXlsxAsync(),
            ["items"] = () => ItemsXlsxAsync(),
            ["gl_accounts"] = () => GlAccountsXlsxAsync(),
            ["item_categories"] = () => ItemCategoriesXlsxAsync(),
            ["item_types"] = () => ItemTypesXlsxAsync(),
            ["units"] = () => UnitsXlsxAsync(),
            ["currencies"] = () => CurrenciesXlsxAsync(),
            ["branches"] = () => BranchesXlsxAsync(),
            ["warehouses"] = () => WarehousesXlsxAsync(),
            ["payments"] = () => PaymentsXlsxAsync(),
            ["sale_invoices"] = () => SaleInvoicesXlsxAsync(),
            ["purchase_invoices"] = () => PurchaseInvoicesXlsxAsync(),
            ["sale_returns"] = () => SaleReturnsXlsxAsync(),
            ["purchase_returns"] = () => PurchaseReturnsXlsxAsync(),
            ["sale_quotes"] = () => SaleQuotesXlsxAsync(),
            ["stock_movements"] = () => StockMovementsXlsxAsync(),
            ["stock_transfers"] = () => StockTransfersXlsxAsync(),
            ["inventory_adjustments"] = () => InventoryAdjustmentsXlsxAsync(),
            ["journal_entries"] = () => JournalEntriesXlsxAsync(),
            ["fiscal_periods"] = () => FiscalPeriodsXlsxAsync(),
            ["budgets"] = () => BudgetsXlsxAsync(),
            ["purchase_orders"] = () => PurchaseOrdersXlsxAsync(),
            ["sale_orders"] = () => SaleOrdersXlsxAsync(),
            ["delivery_orders"] = () => DeliveryOrdersXlsxAsync()
        };

        _csv = new Dictionary<string, Func<Task<byte[]>>>(StringComparer.Ordinal)
        {
            ["suppliers"] = () => SuppliersCsvAsync(),
            ["customers"] = () => CustomersCsvAsync(),
            ["items"] = () => ItemsCsvAsync(),
            ["gl_accounts"] = () => GlAccountsCsvAsync(),
            ["item_categories"] = () => ItemCategoriesCsvAsync(),
            ["item_types"] = () => ItemTypesCsvAsync(),
            ["units"] = () => UnitsCsvAsync(),
            ["currencies"] = () => CurrenciesCsvAsync(),
            ["branches"] = () => BranchesCsvAsync(),
            ["warehouses"] = () => WarehousesCsvAsync(),
            ["payments"] = () => PaymentsCsvAsync(),
            ["sale_invoices"] = () => SaleInvoicesCsvAsync(),
            ["purchase_invoices"] = () => PurchaseInvoicesCsvAsync(),
            ["sale_returns"] = () => SaleReturnsCsvAsync(),
            ["purchase_returns"] = () => PurchaseReturnsCsvAsync(),
            ["sale_quotes"] = () => SaleQuotesCsvAsync(),
            ["stock_movements"] = () => StockMovementsCsvAsync(),
            ["stock_transfers"] = () => StockTransfersCsvAsync(),
            ["inventory_adjustments"] = () => InventoryAdjustmentsCsvAsync(),
            ["journal_entries"] = () => JournalEntriesCsvAsync(),
            ["fiscal_periods"] = () => FiscalPeriodsCsvAsync(),
            ["budgets"] = () => BudgetsCsvAsync(),
            ["purchase_orders"] = () => PurchaseOrdersCsvAsync(),
            ["sale_orders"] = () => SaleOrdersCsvAsync(),
            ["delivery_orders"] = () => DeliveryOrdersCsvAsync()
        };
    }

    private static readonly ExportOption[] Catalog =
    {
        new("suppliers", "المورّدون", "بيانات الموردين الأساسية", "bi-truck", true),
        new("customers", "العملاء", "بيانات العملاء الأساسية", "bi-people", true),
        new("items", "الأصناف", "بطاقات الأصناف وأرصدتها", "bi-box-seam", true),
        new("gl_accounts", "حسابات القيود", "مخطط الحسابات الكامل", "bi-journal-code", true),
        new("item_categories", "الفئات", "تصنيفات الأصناف الرئيسية", "bi-tags", true),
        new("item_types", "الأنواع", "أنواع الأصناف", "bi-grid", true),
        new("units", "الوحدات", "وحدات القياس ومشتقاتها", "bi-rulers", true),
        new("currencies", "العملات", "العملات وأسعار الصرف", "bi-currency-exchange", true),
        new("branches", "الفروع", "فروع الشركة", "bi-diagram-3", true),
        new("warehouses", "المخازن", "المخازن والمواقع", "bi-buildings", true),
        new("payments", "الدفعات", "المقبوضات والمصروفات", "bi-wallet2", true),
        new("sale_invoices", "فواتير المبيعات", "الفواتير وتفاصيل أصنافها", "bi-cart-check", true),
        new("purchase_invoices", "فواتير المشتريات", "الفواتير وتفاصيل أصنافها", "bi-cart-dash", true),
        new("sale_returns", "مرتجعات المبيعات", "المرتجعات وتفاصيل أصنافها", "bi-arrow-counterclockwise", true),
        new("purchase_returns", "مرتجعات المشتريات", "المرتجعات وتفاصيل أصنافها", "bi-arrow-counterclockwise", true),
        new("sale_quotes", "عروض الأسعار", "عروض البيع وأصنافها وعروض الموردين", "bi-file-text", true),
        new("stock_movements", "حركات المخزون", "حركات الأصناف الداخلة والخارجة", "bi-arrow-left-right", true),
        new("stock_transfers", "التحويلات المخزنية", "التحويلات بين المخازن", "bi-shuffle", true),
        new("inventory_adjustments", "تسويات المخزون", "جرد وتسويات الأرصدة", "bi-clipboard-check", true),
        new("journal_entries", "قيود اليومية", "قيود دفتر اليومية العامة وبنودها", "bi-journal-bookmark", true),
        new("fiscal_periods", "السنوات المالية", "السنوات المالية وفتراتها", "bi-calendar2-range", true),
        new("budgets", "الميزانيات", "سنوات الميزانية وبنودها", "bi-pie-chart", true),
        new("purchase_orders", "أوامر الشراء", "أوامر الشراء وتفاصيل أصنافها", "bi-basket", true),
        new("sale_orders", "أوامر البيع", "أوامر البيع وتفاصيل أصنافها", "bi-clipboard2-check", true),
        new("delivery_orders", "أذونات التسليم", "أذونات التسليم وبياناتها المرتبطة بالفاتورة", "bi-truck", true)
    };

    public IReadOnlyList<ExportOption> GetCatalog() => Catalog;

    public async Task<byte[]> ExportXlsxAsync(string key)
    {
        if (_xlsx.TryGetValue(key, out var factory))
            return await factory();
        return Array.Empty<byte>();
    }

    public async Task<byte[]> ExportCsvAsync(string key)
    {
        if (_csv.TryGetValue(key, out var factory))
            return await factory();
        return Array.Empty<byte>();
    }

    public async Task<byte[]> SuppliersXlsxAsync()
    {
        var suppliers = await _db.Suppliers
            .AsNoTracking()
            .Include(s => s.Currency)
            .OrderBy(s => s.Name)
            .ToListAsync();

        var rows = suppliers.Select(s => new object?[]
        {
            s.Code ?? "", s.Name, s.Address ?? "", s.Phone ?? "", s.Email ?? "", s.TaxNumber ?? "",
            (double)s.OpeningBalance, s.Currency?.Code ?? "", s.Notes ?? "", s.IsActive ? "نشط" : "معطل"
        });

        return BuildWorkbook("الموردون", $"تصدير الموردين — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "الكود", "الاسم", "العنوان", "الهاتف", "البريد الإلكتروني", "الرقم الضريبي", "الرصيد الافتتاحي", "العملة", "ملاحظات", "الحالة" },
            rows);
    }

    public async Task<byte[]> CustomersXlsxAsync()
    {
        var customers = await _db.Customers
            .AsNoTracking()
            .Include(c => c.Currency)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var rows = customers.Select(c => new object?[]
        {
            c.Code ?? "", c.Name, c.Address ?? "", c.Phone ?? "", c.Email ?? "", c.TaxNumber ?? "",
            (double)c.OpeningBalance, c.Currency?.Code ?? "", c.Notes ?? "", c.IsActive ? "نشط" : "معطل"
        });

        return BuildWorkbook("العملاء", $"تصدير العملاء — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "الكود", "الاسم", "العنوان", "الهاتف", "البريد الإلكتروني", "الرقم الضريبي", "الرصيد الافتتاحي", "العملة", "ملاحظات", "الحالة" },
            rows);
    }

    public async Task<byte[]> ItemsXlsxAsync()
    {
        var items = await _db.Items
            .AsNoTracking()
            .Include(i => i.Category)
            .Include(i => i.ItemType)
            .Include(i => i.CountUnit)
            .Include(i => i.QuantityUnit)
            .OrderBy(i => i.Name)
            .ToListAsync();

        var rows = items.Select(i => new object?[]
        {
            i.Code ?? "", i.Barcode ?? "", i.Name, i.Category?.Name ?? "", i.ItemType?.Name ?? "",
            i.CountUnit?.Name ?? "", i.QuantityUnit?.Name ?? "",
            (double)i.CurrentCount, (double)i.MinCount, (double)i.CurrentQuantity, (double)i.MinQuantity,
            (double)i.PurchasePrice, (double)i.SalePrice, i.IsSellable ? "نعم" : "لا", i.Notes ?? "",
            i.IsActive ? "نشط" : "معطل"
        });

        return BuildWorkbook("الأصناف", $"تصدير الأصناف وأرصدتها — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "الكود", "الباركود", "الاسم", "التصنيف", "النوع", "وحدة العدد", "وحدة الكمية", "الرصيد (عدد)", "الحد الأدنى (عدد)", "الرصيد (كمية)", "الحد الأدنى (كمية)", "سعر الشراء", "سعر البيع", "قابل للبيع", "ملاحظات", "الحالة" },
            rows);
    }

    public async Task<byte[]> GlAccountsXlsxAsync()
    {
        var accounts = await _db.GLAccounts
            .AsNoTracking()
            .Include(a => a.ParentAccount)
            .OrderBy(a => a.Code)
            .ToListAsync();

        var rows = accounts.Select(a => new object?[]
        {
            a.Code, a.Name, AccountTypeLabel(a.Type),
            a.NormalBalance == NormalBalance.Debit ? "مدين" : "دائن",
            a.ParentAccount?.Code ?? "", a.BranchId?.ToString() ?? "", a.IsActive ? "نشط" : "معطل"
        });

        return BuildWorkbook("مخطط الحسابات", $"تصدير حسابات القيود — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "كود الحساب", "اسم الحساب", "النوع", "طبيعة الحساب", "الحساب الأب", "الفرع", "الحالة" },
            rows);
    }

    public async Task<byte[]> ItemCategoriesXlsxAsync()
    {
        var categories = await _db.ItemCategories
            .AsNoTracking()
            .Include(c => c.Items)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var rows = categories.Select(c => new object?[]
        {
            c.Name, c.Items.Count, c.Notes ?? "", c.IsActive ? "نشط" : "معطل"
        });

        return BuildWorkbook("الفئات", $"تصدير الفئات — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "الاسم", "عدد الأصناف", "ملاحظات", "الحالة" },
            rows);
    }

    public async Task<byte[]> ItemTypesXlsxAsync()
    {
        var types = await _db.ItemTypes
            .AsNoTracking()
            .Include(t => t.Items)
            .OrderBy(t => t.Name)
            .ToListAsync();

        var rows = types.Select(t => new object?[]
        {
            t.Name, t.Items.Count, t.Notes ?? "", t.IsActive ? "نشط" : "معطل"
        });

        return BuildWorkbook("الأنواع", $"تصدير أنواع الأصناف — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "الاسم", "عدد الأصناف", "ملاحظات", "الحالة" },
            rows);
    }

    public async Task<byte[]> UnitsXlsxAsync()
    {
        var units = await _db.Units
            .AsNoTracking()
            .Include(u => u.ParentUnit)
            .OrderBy(u => u.Name)
            .ToListAsync();

        var rows = units.Select(u => new object?[]
        {
            u.Name, u.ShortName ?? "", (object?)u.SubUnits, u.ParentUnit?.Name ?? "", u.IsActive ? "نشط" : "معطل"
        });

        return BuildWorkbook("الوحدات", $"تصدير الوحدات — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "اسم الوحدة", "الاختصار", "الوحدات الفرعية", "وحدة فرعية من", "الحالة" },
            rows);
    }

    public async Task<byte[]> CurrenciesXlsxAsync()
    {
        var currencies = await _db.Currencies
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .ToListAsync();

        var rows = currencies.Select(c => new object?[]
        {
            c.Code, c.Name, c.Symbol ?? "", (double)c.ExchangeRate, c.IsBase ? "نعم" : "لا", c.IsActive ? "نشطة" : "معطلة"
        });

        return BuildWorkbook("العملات", $"تصدير العملات — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "الكود", "الاسم", "رمز العملة", "سعر الصرف", "العملة الأساسية", "الحالة" },
            rows);
    }

    public async Task<byte[]> BranchesXlsxAsync()
    {
        var branches = await _db.Branches
            .AsNoTracking()
            .OrderBy(b => b.Code)
            .ToListAsync();

        var rows = branches.Select(b => new object?[]
        {
            b.Code, b.Name, b.Address ?? "", b.Phone ?? "", b.IsActive ? "نشط" : "معطل", b.CreatedAt.ToString("dd/MM/yyyy")
        });

        return BuildWorkbook("الفروع", $"تصدير الفروع — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "الكود", "الاسم", "العنوان", "الهاتف", "الحالة", "تاريخ الإنشاء" },
            rows);
    }

    public async Task<byte[]> WarehousesXlsxAsync()
    {
        var warehouses = await _db.Warehouses
            .AsNoTracking()
            .OrderBy(w => w.Code)
            .ToListAsync();

        var rows = warehouses.Select(w => new object?[]
        {
            w.Code, w.Name, w.IsActive ? "نشط" : "معطل", w.CreatedAt.ToString("dd/MM/yyyy")
        });

        return BuildWorkbook("المخازن", $"تصدير المخازن — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "الكود", "الاسم", "الحالة", "تاريخ الإنشاء" },
            rows);
    }

    public async Task<byte[]> PaymentsXlsxAsync()
    {
        var payments = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .OrderByDescending(p => p.PaymentDate)
            .ThenBy(p => p.ReceiptNumber)
            .ToListAsync();

        var rows = payments.Select(p => new object?[]
        {
            p.ReceiptNumber,
            p.Type == PaymentType.Receipt ? "قبض" : "صرف",
            p.Customer?.Name ?? p.Supplier?.Name ?? "",
            (double)p.Amount,
            (double)p.BaseAmount,
            p.Currency?.Code ?? "",
            p.ExchangeRate.HasValue ? (object?)(double)p.ExchangeRate.Value : null,
            p.Method.GetDisplayName(),
            p.ReferenceNumber ?? "",
            p.PaymentDate.ToString("dd/MM/yyyy"),
            p.Notes ?? ""
        });

        return BuildWorkbook("الدّفعات", $"تصدير الدفعات — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم الإيصال", "النوع", "العميل/المورد", "المبلغ", "المبلغ بالأساس", "العملة", "سعر الصرف", "طريقة الدفع", "رقم المرجع", "التاريخ", "ملاحظات" },
            rows);
    }

    public async Task<byte[]> SaleInvoicesXlsxAsync()
    {
        var invoices = await _db.SaleInvoices
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Currency)
            .Include(s => s.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(s => s.InvoiceDate)
            .ThenBy(s => s.InvoiceNumber)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var invoiceRows = invoices.SelectMany(s => s.Items.Select(i => new object?[]
        {
            s.InvoiceNumber, s.Customer?.Name ?? "",
            s.InvoiceDate.ToString("dd/MM/yyyy"), s.Currency?.Code ?? "",
            s.ExchangeRate.HasValue ? (object?)(double)s.ExchangeRate.Value : null,
            s.PaymentTerms.GetDisplayName(),
            (double)s.Discount, (double)(s.Discount2 ?? 0), (double)(s.Discount3 ?? 0),
            (double)s.Tax, s.Notes ?? "",
            i.Item.Name, (double)i.Quantity, (double)i.Count, (double)i.UnitPrice, (double)i.Discount,
            (double)i.Total, (double)s.NetAmount, (double)s.PaidAmount, s.IsPaid ? "مدفوعة" : "غير مدفوعة"
        }));
        WriteSheet(wb, "فواتير المبيعات", $"تصدير فواتير المبيعات — {DateTime.Today:dd/MM/yyyy}",
            new List<string>
            {
                "رقم الفاتورة", "العميل", "التاريخ", "العملة", "سعر الصرف", "شروط الدفع",
                "خصم الفاتورة", "خصم إضافي 2", "خصم إضافي 3", "الضريبة", "ملاحظات",
                "اسم الصنف", "الكمية", "العدد", "سعر الوحدة", "خصم الصنف",
                "إجمالي الصنف", "الصافي", "المدفوع", "الحالة"
            },
            invoiceRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> PurchaseInvoicesXlsxAsync()
    {
        var invoices = await _db.PurchaseInvoices
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .Include(p => p.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(p => p.InvoiceDate)
            .ThenBy(p => p.InvoiceNumber)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var invoiceRows = invoices.SelectMany(p => p.Items.Select(i => new object?[]
        {
            p.InvoiceNumber, p.Supplier?.Name ?? "",
            p.InvoiceDate.ToString("dd/MM/yyyy"), p.Currency?.Code ?? "",
            p.ExchangeRate.HasValue ? (object?)(double)p.ExchangeRate.Value : null,
            p.PaymentTerms.GetDisplayName(),
            (double)p.Discount, (double)(p.Discount2 ?? 0), (double)(p.Discount3 ?? 0),
            (double)p.Tax, p.Notes ?? "",
            i.Item.Name, (double)i.Quantity, (double)i.Count, (double)i.UnitPrice, (double)i.Discount,
            (double)i.Total, (double)p.NetAmount, (double)p.PaidAmount, p.IsPaid ? "مدفوعة" : "غير مدفوعة"
        }));
        WriteSheet(wb, "فواتير المشتريات", $"تصدير فواتير المشتريات — {DateTime.Today:dd/MM/yyyy}",
            new List<string>
            {
                "رقم الفاتورة", "المورد", "التاريخ", "العملة", "سعر الصرف", "شروط الدفع",
                "خصم الفاتورة", "خصم إضافي 2", "خصم إضافي 3", "الضريبة", "ملاحظات",
                "اسم الصنف", "الكمية", "العدد", "سعر الوحدة", "خصم الصنف",
                "إجمالي الصنف", "الصافي", "المدفوع", "الحالة"
            },
            invoiceRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> SaleReturnsXlsxAsync()
    {
        var returns = await _db.SaleReturns
            .AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.SaleInvoice)
            .Include(r => r.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(r => r.ReturnDate)
            .ThenBy(r => r.ReturnNumber)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var returnRows = returns.SelectMany(r => r.Items.Select(i => new object?[]
        {
            r.ReturnNumber, r.Customer?.Name ?? "", r.SaleInvoice?.InvoiceNumber ?? "",
            r.ReturnDate.ToString("dd/MM/yyyy"), r.Currency?.Code ?? "",
            r.ExchangeRate.HasValue ? (object?)(double)r.ExchangeRate.Value : null,
            r.Reason ?? "",
            i.Item.Name, (double)i.Quantity, (double)i.Count, (double)i.UnitPrice, (double)i.Total,
            r.Status.GetDisplayName()
        }));
        WriteSheet(wb, "مرتجعات المبيعات", $"تصدير مرتجعات المبيعات — {DateTime.Today:dd/MM/yyyy}",
            new List<string>
            {
                "رقم المرتجع", "العميل", "الفاتورة الأصلية", "التاريخ", "العملة", "سعر الصرف", "سبب المرتجع",
                "الصنف", "الكمية", "العدد", "سعر الوحدة", "الإجمالي", "الحالة"
            },
            returnRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> PurchaseReturnsXlsxAsync()
    {
        var returns = await _db.PurchaseReturns
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.PurchaseInvoice)
            .Include(r => r.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(r => r.ReturnDate)
            .ThenBy(r => r.ReturnNumber)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var returnRows = returns.SelectMany(r => r.Items.Select(i => new object?[]
        {
            r.ReturnNumber, r.Supplier?.Name ?? "", r.PurchaseInvoice?.InvoiceNumber ?? "",
            r.ReturnDate.ToString("dd/MM/yyyy"), r.Currency?.Code ?? "",
            r.ExchangeRate.HasValue ? (object?)(double)r.ExchangeRate.Value : null,
            r.Reason ?? "",
            i.Item.Name, (double)i.Quantity, (double)i.Count, (double)i.UnitPrice, (double)i.Total,
            r.Status.GetDisplayName()
        }));
        WriteSheet(wb, "مرتجعات المشتريات", $"تصدير مرتجعات المشتريات — {DateTime.Today:dd/MM/yyyy}",
            new List<string>
            {
                "رقم المرتجع", "المورد", "الفاتورة الأصلية", "التاريخ", "العملة", "سعر الصرف", "سبب المرتجع",
                "الصنف", "الكمية", "العدد", "سعر الوحدة", "الإجمالي", "الحالة"
            },
            returnRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> SaleQuotesXlsxAsync()
    {
        var quotes = await _db.SaleQuotes
            .AsNoTracking()
            .Include(q => q.Customer)
            .Include(q => q.Currency)
            .Include(q => q.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(q => q.QuoteDate)
            .ThenBy(q => q.QuoteNumber)
            .ToListAsync();

        var supplierQuotes = await _db.SupplierQuotes
            .AsNoTracking()
            .Include(q => q.Supplier)
            .Include(q => q.Item)
            .OrderBy(q => q.Supplier.Name)
            .ThenBy(q => q.Item.Name)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var quoteRows = quotes.Select(q => new object?[]
        {
            q.QuoteNumber, q.Customer?.Name ?? "", q.QuoteDate.ToString("dd/MM/yyyy"),
            q.ValidUntil?.ToString("dd/MM/yyyy") ?? "", q.Currency?.Code ?? "",
            (double)q.TotalAmount, (double)q.Discount, (double)q.Tax, (double)q.NetAmount,
            q.Status.GetDisplayName(), q.Notes ?? ""
        });
        WriteSheet(wb, "عروض الأسعار", $"تصدير عروض الأسعار — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم العرض", "العميل", "التاريخ", "صالح حتى", "العملة", "الإجمالي", "الخصم", "الضريبة", "الصافي", "الحالة", "ملاحظات" },
            quoteRows);

        var itemRows = quotes.SelectMany(q => q.Items.Select(i => new object?[]
        {
            q.QuoteNumber, i.Item.Name, (double)i.Quantity, (double)i.Count, (double)i.UnitPrice, (double)i.Total
        }));
        WriteSheet(wb, "أصناف العروض", $"تفاصيل أصناف عروض الأسعار — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم العرض", "الصنف", "الكمية", "العدد", "سعر الوحدة", "الإجمالي" },
            itemRows);

        var sqRows = supplierQuotes.Select(q => new object?[]
        {
            q.Supplier.Name, q.Item.Name, (double)q.UnitPrice, q.EffectiveDate.ToString("dd/MM/yyyy"), q.Notes ?? ""
        });
        WriteSheet(wb, "عروض الموردين", $"عروض أسعار الموردين — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "المورد", "الصنف", "سعر الوحدة", "تاريخ السعر", "ملاحظات" },
            sqRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> StockMovementsXlsxAsync()
    {
        var movements = await _db.StockMovements
            .AsNoTracking()
            .Include(m => m.Item)
            .OrderByDescending(m => m.MovementDate)
            .ThenBy(m => m.Id)
            .ToListAsync();

        var rows = movements.Select(m => new object?[]
        {
            m.MovementDate.ToString("dd/MM/yyyy"), m.Item.Name, m.Type.GetDisplayName(),
            (double)m.Quantity, (double)m.Count,
            (double)m.BalanceBefore, (double)m.BalanceAfter, (double)m.CountBefore, (double)m.CountAfter,
            m.DocumentNumber ?? "", m.DocumentType.HasValue ? m.DocumentType.Value.GetDisplayName() : "",
            m.CreatedBy ?? ""
        });

        return BuildWorkbook("حركات المخزون", $"تصدير حركات المخزون — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "التاريخ", "الصنف", "النوع", "الكمية", "العدد", "الرصيد الكمي قبل", "الرصيد الكمي بعد", "الرصيد العددي قبل", "الرصيد العددي بعد", "رقم المستند", "نوع المستند", "أنشئ بواسطة" },
            rows);
    }

    public async Task<byte[]> StockTransfersXlsxAsync()
    {
        var transfers = await _db.StockTransfers
            .AsNoTracking()
            .Include(t => t.SourceWarehouse)
            .Include(t => t.TargetWarehouse)
            .Include(t => t.Items)
            .ThenInclude(i => i.Item)
            .OrderByDescending(t => t.TransferDate)
            .ThenBy(t => t.TransferNumber)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var itemRows = transfers.SelectMany(t => t.Items.Select(i => new object?[]
        {
            t.TransferNumber, t.SourceWarehouse.Name, t.TargetWarehouse.Name,
            t.TransferDate.ToString("dd/MM/yyyy"), t.Notes ?? "",
            i.Item.Name, (double)i.Quantity, (double)i.Count, (double)i.UnitCost,
            (double)(i.Quantity > 0 ? i.Quantity : i.Count) * (double)i.UnitCost
        }));
        WriteSheet(wb, "التحويلات", $"تصدير التحويلات المخزنية — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم التحويل", "المستودع المصدر", "المستودع الوجهة", "التاريخ", "ملاحظات", "الصنف", "الكمية", "العدد", "تكلفة الوحدة", "الإجمالي" },
            itemRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> InventoryAdjustmentsXlsxAsync()
    {
        var adjustments = await _db.InventoryAdjustments
            .AsNoTracking()
            .Include(a => a.Item)
            .OrderByDescending(a => a.AdjustmentDate)
            .ThenBy(a => a.ReferenceNumber)
            .ToListAsync();

        var rows = adjustments.Select(a => new object?[]
        {
            a.ReferenceNumber, a.Item.Name, (double)a.NewQuantity, (double)a.NewCount,
            a.AdjustmentDate.ToString("dd/MM/yyyy"), a.Reason ?? "", a.CreatedBy ?? ""
        });

        return BuildWorkbook("تسويات المخزون", $"تصدير تسويات المخزون — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم المستند", "الصنف", "الكمية الجديدة", "العدد الجديد", "التاريخ", "السبب", "أنشئ بواسطة" },
            rows);
    }

    public async Task<byte[]> JournalEntriesXlsxAsync()
    {
        var entries = await _db.JournalEntries
            .AsNoTracking()
            .Include(j => j.Lines)
            .ThenInclude(l => l.Account)
            .OrderBy(j => j.EntryNumber)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var lineRows = entries.SelectMany(j => j.Lines.Select(l => new object?[]
        {
            j.EntryNumber, j.Date.ToString("dd/MM/yyyy"), j.Description, j.Source.GetDisplayName(),
            j.SourceId, j.IsPosted ? "مرحّل" : "مسودة", j.CreatedBy ?? "",
            l.Account?.Code ?? "", l.Account?.Name ?? "", (double)l.Debit, (double)l.Credit, l.Description ?? ""
        }));
        WriteSheet(wb, "قيود اليومية", $"تصدير قيود اليومية — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم القيد", "التاريخ", "بيان القيد", "المصدر", "معرف المستند", "الحالة", "أنشئ بواسطة", "رمز الحساب", "اسم الحساب", "مدين", "دائن", "بيان البند" },
            lineRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> FiscalPeriodsXlsxAsync()
    {
        var periods = await _db.FiscalPeriods
            .AsNoTracking()
            .OrderByDescending(p => p.Year)
            .ToListAsync();

        var rows = periods.Select(p => new object?[]
        {
            p.Year, p.Name ?? "", p.IsClosed ? "مغلقة" : "مفتوحة", p.ClosedById ?? "",
            p.ClosedAt?.ToString("dd/MM/yyyy") ?? ""
        });

        return BuildWorkbook("السنوات المالية", $"تصدير السنوات المالية — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "السنة", "الاسم", "الحالة", "أغلقت بواسطة", "تاريخ الإغلاق" },
            rows);
    }

    public async Task<byte[]> BudgetsXlsxAsync()
    {
        var years = await _db.BudgetYears
            .AsNoTracking()
            .OrderByDescending(b => b.Year)
            .ToListAsync();

        var lines = await _db.BudgetLines
            .AsNoTracking()
            .Include(l => l.BudgetYear)
            .Include(l => l.Account)
            .OrderBy(l => l.BudgetYear!.Year)
            .ThenBy(l => l.Account!.Code)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var yearRows = years.Select(b => new object?[]
        {
            b.Year, b.IsActive ? "نشطة" : "معطلة", b.CreatedBy ?? "", b.CreatedAt.ToString("dd/MM/yyyy")
        });
        WriteSheet(wb, "سنوات الميزانية", $"تصدير سنوات الميزانية — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "السنة", "الحالة", "أنشئ بواسطة", "تاريخ الإنشاء" },
            yearRows);

        var lineRows = lines.Select(l => new object?[]
        {
            l.BudgetYear!.Year, l.Account?.Code ?? "", l.Account?.Name ?? "", (double)l.AnnualAmount
        });
        WriteSheet(wb, "بنود الميزانية", $"تفاصيل بنود الميزانية — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "السنة", "رمز الحساب", "اسم الحساب", "المبلغ السنوي" },
            lineRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> PurchaseOrdersXlsxAsync()
    {
        var orders = await _db.PurchaseOrders
            .AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Items)
            .ThenInclude(i => i.Item)
            .OrderByDescending(o => o.OrderDate)
            .ThenBy(o => o.OrderNumber)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var orderRows = orders.Select(o => new object?[]
        {
            o.OrderNumber, o.Supplier.Name, o.OrderDate.ToString("dd/MM/yyyy"),
            o.ExpectedDate?.ToString("dd/MM/yyyy") ?? "", o.Status.GetDisplayName(), o.Notes ?? ""
        });
        WriteSheet(wb, "أوامر الشراء", $"تصدير أوامر الشراء — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم الأمر", "المورد", "التاريخ", "التاريخ المتوقع", "الحالة", "ملاحظات" },
            orderRows);

        var itemRows = orders.SelectMany(o => o.Items.Select(i => new object?[]
        {
            o.OrderNumber, i.Item.Name, (double)i.Quantity, (double)i.Count, (double)i.UnitPrice,
            (double)i.ReceivedQty, (double)i.ReceivedCount, (double)i.Total
        }));
        WriteSheet(wb, "أصناف الأوامر", $"تفاصيل أصناف أوامر الشراء — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم الأمر", "الصنف", "الكمية المطلوبة", "العدد المطلوب", "سعر الوحدة", "الكمية المستلمة", "العدد المستلم", "الإجمالي" },
            itemRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> SaleOrdersXlsxAsync()
    {
        var orders = await _db.SalesOrders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .ThenInclude(i => i.Item)
            .OrderByDescending(o => o.OrderDate)
            .ThenBy(o => o.OrderNumber)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var orderRows = orders.Select(o => new object?[]
        {
            o.OrderNumber, o.Customer.Name, o.OrderDate.ToString("dd/MM/yyyy"),
            o.ExpectedDate?.ToString("dd/MM/yyyy") ?? "", o.Status.GetDisplayName(),
            o.SaleQuoteId?.ToString() ?? "", o.Notes ?? ""
        });
        WriteSheet(wb, "أوامر البيع", $"تصدير أوامر البيع — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم الأمر", "العميل", "التاريخ", "التاريخ المتوقع", "الحالة", "عرض السعر", "ملاحظات" },
            orderRows);

        var itemRows = orders.SelectMany(o => o.Items.Select(i => new object?[]
        {
            o.OrderNumber, i.Item.Name, (double)i.Quantity, (double)i.Count, (double)i.UnitPrice,
            (double)i.InvoicedQty, (double)i.InvoicedCount, (double)i.Total
        }));
        WriteSheet(wb, "أصناف الأوامر", $"تفاصيل أصناف أوامر البيع — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم الأمر", "الصنف", "الكمية المطلوبة", "العدد المطلوب", "سعر الوحدة", "الكمية المفوتَرة", "العدد المفوتَر", "الإجمالي" },
            itemRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> DeliveryOrdersXlsxAsync()
    {
        var deliveries = await _db.DeliveryOrders
            .AsNoTracking()
            .Include(d => d.Customer)
            .Include(d => d.SaleInvoice)
            .Include(d => d.Items)
            .ThenInclude(i => i.Item)
            .OrderByDescending(d => d.DeliveryDate)
            .ThenBy(d => d.DeliveryNumber)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var deliveryRows = deliveries.Select(d => new object?[]
        {
            d.DeliveryNumber, d.SaleInvoice?.InvoiceNumber ?? "", d.Customer?.Name ?? "",
            d.DeliveryDate.ToString("dd/MM/yyyy"), d.Status.GetDisplayName(),
            d.Carrier ?? "", d.TrackingNumber ?? "", d.Notes ?? "",
            d.DeliveredBy ?? "", d.DeliveredAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? ""
        });
        WriteSheet(wb, "أذونات التسليم", $"تصدير أذونات التسليم — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم الإذن", "فاتورة البيع", "العميل", "تاريخ التسليم", "الحالة", "الناقل", "رقم التتبع", "ملاحظات", "رحّلها", "تاريخ الترحيل" },
            deliveryRows);

        var itemRows = deliveries.SelectMany(d => d.Items.Select(i => new object?[]
        {
            d.DeliveryNumber, i.Item.Name, (double)i.Quantity, (double)i.Count
        }));
        WriteSheet(wb, "أصناف الإذونات", $"تفاصيل أصناف أذونات التسليم — {DateTime.Today:dd/MM/yyyy}",
            new List<string> { "رقم الإذن", "الصنف", "الكمية المسلّمة", "العدد المسلّم" },
            itemRows);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> SuppliersCsvAsync()
    {
        var suppliers = await _db.Suppliers
            .AsNoTracking()
            .Include(s => s.Currency)
            .OrderBy(s => s.Name)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الكود,الاسم,العنوان,الهاتف,البريد الإلكتروني,الرقم الضريبي,الرصيد الافتتاحي,العملة,ملاحظات,الحالة");
        foreach (var s in suppliers)
        {
            sb.AppendLine(string.Join(",",
                CsvField(s.Code ?? ""),
                CsvField(s.Name),
                CsvField(s.Address ?? ""),
                CsvField(s.Phone ?? ""),
                CsvField(s.Email ?? ""),
                CsvField(s.TaxNumber ?? ""),
                CsvField(s.OpeningBalance.ToString("0.00", CultureInfo.InvariantCulture)),
                CsvField(s.Currency?.Code ?? ""),
                CsvField(s.Notes ?? ""),
                CsvField(s.IsActive ? "نشط" : "معطل")));
        }

        return CsvBytes(sb);
    }

    public async Task<byte[]> CustomersCsvAsync()
    {
        var customers = await _db.Customers
            .AsNoTracking()
            .Include(c => c.Currency)
            .OrderBy(c => c.Name)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الكود,الاسم,العنوان,الهاتف,البريد الإلكتروني,الرقم الضريبي,الرصيد الافتتاحي,العملة,ملاحظات,الحالة");
        foreach (var c in customers)
        {
            sb.AppendLine(string.Join(",",
                CsvField(c.Code ?? ""),
                CsvField(c.Name),
                CsvField(c.Address ?? ""),
                CsvField(c.Phone ?? ""),
                CsvField(c.Email ?? ""),
                CsvField(c.TaxNumber ?? ""),
                CsvField(c.OpeningBalance.ToString("0.00", CultureInfo.InvariantCulture)),
                CsvField(c.Currency?.Code ?? ""),
                CsvField(c.Notes ?? ""),
                CsvField(c.IsActive ? "نشط" : "معطل")));
        }

        return CsvBytes(sb);
    }

    public async Task<byte[]> ItemsCsvAsync()
    {
        var items = await _db.Items
            .AsNoTracking()
            .Include(i => i.Category)
            .Include(i => i.ItemType)
            .Include(i => i.CountUnit)
            .Include(i => i.QuantityUnit)
            .OrderBy(i => i.Name)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الكود,الباركود,الاسم,التصنيف,النوع,وحدة العدد,وحدة الكمية,الرصيد (عدد),الحد الأدنى (عدد),الرصيد (كمية),الحد الأدنى (كمية),سعر الشراء,سعر البيع,قابل للبيع,ملاحظات,الحالة");
        foreach (var i in items)
        {
            sb.AppendLine(string.Join(",",
                CsvField(i.Code ?? ""),
                CsvField(i.Barcode ?? ""),
                CsvField(i.Name),
                CsvField(i.Category?.Name ?? ""),
                CsvField(i.ItemType?.Name ?? ""),
                CsvField(i.CountUnit?.Name ?? ""),
                CsvField(i.QuantityUnit?.Name ?? ""),
                CsvField(i.CurrentCount.ToString("0.00", CultureInfo.InvariantCulture)),
                CsvField(i.MinCount.ToString("0.00", CultureInfo.InvariantCulture)),
                CsvField(i.CurrentQuantity.ToString("0.00", CultureInfo.InvariantCulture)),
                CsvField(i.MinQuantity.ToString("0.00", CultureInfo.InvariantCulture)),
                CsvField(i.PurchasePrice.ToString("0.00", CultureInfo.InvariantCulture)),
                CsvField(i.SalePrice.ToString("0.00", CultureInfo.InvariantCulture)),
                CsvField(i.IsSellable ? "نعم" : "لا"),
                CsvField(i.Notes ?? ""),
                CsvField(i.IsActive ? "نشط" : "معطل")));
        }

        return CsvBytes(sb);
    }

    public async Task<byte[]> GlAccountsCsvAsync()
    {
        var accounts = await _db.GLAccounts
            .AsNoTracking()
            .Include(a => a.ParentAccount)
            .OrderBy(a => a.Code)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("كود الحساب,اسم الحساب,النوع,طبيعة الحساب,الحساب الأب,الفرع,الحالة");
        foreach (var a in accounts)
        {
            sb.AppendLine(string.Join(",",
                CsvField(a.Code),
                CsvField(a.Name),
                CsvField(AccountTypeLabel(a.Type)),
                CsvField(a.NormalBalance == NormalBalance.Debit ? "مدين" : "دائن"),
                CsvField(a.ParentAccount?.Code ?? ""),
                CsvField(a.BranchId?.ToString() ?? ""),
                CsvField(a.IsActive ? "نشط" : "معطل")));
        }

        return CsvBytes(sb);
    }

    public async Task<byte[]> ItemCategoriesCsvAsync()
    {
        var categories = await _db.ItemCategories
            .AsNoTracking()
            .Include(c => c.Items)
            .OrderBy(c => c.Name)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الاسم,عدد الأصناف,ملاحظات,الحالة");
        foreach (var c in categories)
        {
            sb.AppendLine(string.Join(",",
                CsvField(c.Name), c.Items.Count.ToString(CultureInfo.InvariantCulture),
                CsvField(c.Notes ?? ""), CsvField(c.IsActive ? "نشط" : "معطل")));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> ItemTypesCsvAsync()
    {
        var types = await _db.ItemTypes
            .AsNoTracking()
            .Include(t => t.Items)
            .OrderBy(t => t.Name)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الاسم,عدد الأصناف,ملاحظات,الحالة");
        foreach (var t in types)
        {
            sb.AppendLine(string.Join(",",
                CsvField(t.Name), t.Items.Count.ToString(CultureInfo.InvariantCulture),
                CsvField(t.Notes ?? ""), CsvField(t.IsActive ? "نشط" : "معطل")));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> UnitsCsvAsync()
    {
        var units = await _db.Units
            .AsNoTracking()
            .Include(u => u.ParentUnit)
            .OrderBy(u => u.Name)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("اسم الوحدة,الاختصار,الوحدات الفرعية,وحدة فرعية من,الحالة");
        foreach (var u in units)
        {
            sb.AppendLine(string.Join(",",
                CsvField(u.Name), CsvField(u.ShortName ?? ""),
                u.SubUnits.HasValue ? u.SubUnits.Value.ToString(CultureInfo.InvariantCulture) : "",
                CsvField(u.ParentUnit?.Name ?? ""), CsvField(u.IsActive ? "نشط" : "معطل")));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> CurrenciesCsvAsync()
    {
        var currencies = await _db.Currencies
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الكود,الاسم,رمز العملة,سعر الصرف,العملة الأساسية,الحالة");
        foreach (var c in currencies)
        {
            sb.AppendLine(string.Join(",",
                CsvField(c.Code), CsvField(c.Name), CsvField(c.Symbol ?? ""),
                c.ExchangeRate.ToString("0.0000", CultureInfo.InvariantCulture),
                CsvField(c.IsBase ? "نعم" : "لا"), CsvField(c.IsActive ? "نشطة" : "معطلة")));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> BranchesCsvAsync()
    {
        var branches = await _db.Branches
            .AsNoTracking()
            .OrderBy(b => b.Code)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الكود,الاسم,العنوان,الهاتف,الحالة,تاريخ الإنشاء");
        foreach (var b in branches)
        {
            sb.AppendLine(string.Join(",",
                CsvField(b.Code), CsvField(b.Name), CsvField(b.Address ?? ""), CsvField(b.Phone ?? ""),
                CsvField(b.IsActive ? "نشط" : "معطل"), CsvField(b.CreatedAt.ToString("dd/MM/yyyy"))));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> WarehousesCsvAsync()
    {
        var warehouses = await _db.Warehouses
            .AsNoTracking()
            .OrderBy(w => w.Code)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الكود,الاسم,الحالة,تاريخ الإنشاء");
        foreach (var w in warehouses)
        {
            sb.AppendLine(string.Join(",",
                CsvField(w.Code), CsvField(w.Name),
                CsvField(w.IsActive ? "نشط" : "معطل"), CsvField(w.CreatedAt.ToString("dd/MM/yyyy"))));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> PaymentsCsvAsync()
    {
        var payments = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .OrderByDescending(p => p.PaymentDate)
            .ThenBy(p => p.ReceiptNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم الإيصال,النوع,العميل/المورد,المبلغ,المبلغ بالأساس,العملة,سعر الصرف,طريقة الدفع,رقم المرجع,التاريخ,ملاحظات");
        foreach (var p in payments)
        {
            sb.AppendLine(string.Join(",",
                CsvField(p.ReceiptNumber),
                CsvField(p.Type == PaymentType.Receipt ? "قبض" : "صرف"),
                CsvField(p.Customer?.Name ?? p.Supplier?.Name ?? ""),
                p.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                p.BaseAmount.ToString("0.00", CultureInfo.InvariantCulture),
                CsvField(p.Currency?.Code ?? ""),
                p.ExchangeRate.HasValue ? p.ExchangeRate.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "",
                CsvField(p.Method.GetDisplayName()),
                CsvField(p.ReferenceNumber ?? ""),
                CsvField(p.PaymentDate.ToString("dd/MM/yyyy")),
                CsvField(p.Notes ?? "")));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> SaleInvoicesCsvAsync()
    {
        var invoices = await _db.SaleInvoices
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Currency)
            .Include(s => s.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(s => s.InvoiceDate)
            .ThenBy(s => s.InvoiceNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم الفاتورة,العميل,التاريخ,العملة,سعر الصرف,شروط الدفع,خصم الفاتورة,خصم إضافي 2,خصم إضافي 3,الضريبة,ملاحظات,الصنف,الكمية,العدد,سعر الوحدة,خصم الصنف,إجمالي الصنف,الصافي,المدفوع,الحالة");
        foreach (var s in invoices)
        {
            foreach (var i in s.Items)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(s.InvoiceNumber), CsvField(s.Customer?.Name ?? ""),
                    CsvField(s.InvoiceDate.ToString("dd/MM/yyyy")), CsvField(s.Currency?.Code ?? ""),
                    s.ExchangeRate.HasValue ? s.ExchangeRate.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "",
                    CsvField(s.PaymentTerms.GetDisplayName()),
                    s.Discount.ToString("0.00", CultureInfo.InvariantCulture),
                    (s.Discount2 ?? 0).ToString("0.00", CultureInfo.InvariantCulture),
                    (s.Discount3 ?? 0).ToString("0.00", CultureInfo.InvariantCulture),
                    s.Tax.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(s.Notes ?? ""),
                    CsvField(i.Item.Name),
                    i.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Count.ToString("0.000", CultureInfo.InvariantCulture),
                    i.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture),
                    i.Discount.ToString("0.00", CultureInfo.InvariantCulture),
                    i.Total.ToString("0.00", CultureInfo.InvariantCulture),
                    s.NetAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    s.PaidAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(s.IsPaid ? "مدفوعة" : "غير مدفوعة")));
            }
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> PurchaseInvoicesCsvAsync()
    {
        var invoices = await _db.PurchaseInvoices
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .Include(p => p.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(p => p.InvoiceDate)
            .ThenBy(p => p.InvoiceNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم الفاتورة,المورد,التاريخ,العملة,سعر الصرف,شروط الدفع,خصم الفاتورة,خصم إضافي 2,خصم إضافي 3,الضريبة,ملاحظات,الصنف,الكمية,العدد,سعر الوحدة,خصم الصنف,إجمالي الصنف,الصافي,المدفوع,الحالة");
        foreach (var p in invoices)
        {
            foreach (var i in p.Items)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(p.InvoiceNumber), CsvField(p.Supplier?.Name ?? ""),
                    CsvField(p.InvoiceDate.ToString("dd/MM/yyyy")), CsvField(p.Currency?.Code ?? ""),
                    p.ExchangeRate.HasValue ? p.ExchangeRate.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "",
                    CsvField(p.PaymentTerms.GetDisplayName()),
                    p.Discount.ToString("0.00", CultureInfo.InvariantCulture),
                    (p.Discount2 ?? 0).ToString("0.00", CultureInfo.InvariantCulture),
                    (p.Discount3 ?? 0).ToString("0.00", CultureInfo.InvariantCulture),
                    p.Tax.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(p.Notes ?? ""),
                    CsvField(i.Item.Name),
                    i.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Count.ToString("0.000", CultureInfo.InvariantCulture),
                    i.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture),
                    i.Discount.ToString("0.00", CultureInfo.InvariantCulture),
                    i.Total.ToString("0.00", CultureInfo.InvariantCulture),
                    p.NetAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    p.PaidAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(p.IsPaid ? "مدفوعة" : "غير مدفوعة")));
            }
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> SaleReturnsCsvAsync()
    {
        var returns = await _db.SaleReturns
            .AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.SaleInvoice)
            .Include(r => r.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(r => r.ReturnDate)
            .ThenBy(r => r.ReturnNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم المرتجع,العميل,الفاتورة الأصلية,التاريخ,العملة,سعر الصرف,سبب المرتجع,الصنف,الكمية,العدد,سعر الوحدة,الإجمالي,الحالة");
        foreach (var r in returns)
        {
            foreach (var i in r.Items)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(r.ReturnNumber), CsvField(r.Customer?.Name ?? ""),
                    CsvField(r.SaleInvoice?.InvoiceNumber ?? ""),
                    CsvField(r.ReturnDate.ToString("dd/MM/yyyy")), CsvField(r.Currency?.Code ?? ""),
                    r.ExchangeRate.HasValue ? r.ExchangeRate.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "",
                    CsvField(r.Reason ?? ""),
                    CsvField(i.Item.Name),
                    i.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Count.ToString("0.000", CultureInfo.InvariantCulture),
                    i.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture),
                    i.Total.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(r.Status.GetDisplayName())));
            }
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> PurchaseReturnsCsvAsync()
    {
        var returns = await _db.PurchaseReturns
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.PurchaseInvoice)
            .Include(r => r.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(r => r.ReturnDate)
            .ThenBy(r => r.ReturnNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم المرتجع,المورد,الفاتورة الأصلية,التاريخ,العملة,سعر الصرف,سبب المرتجع,الصنف,الكمية,العدد,سعر الوحدة,الإجمالي,الحالة");
        foreach (var r in returns)
        {
            foreach (var i in r.Items)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(r.ReturnNumber), CsvField(r.Supplier?.Name ?? ""),
                    CsvField(r.PurchaseInvoice?.InvoiceNumber ?? ""),
                    CsvField(r.ReturnDate.ToString("dd/MM/yyyy")), CsvField(r.Currency?.Code ?? ""),
                    r.ExchangeRate.HasValue ? r.ExchangeRate.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "",
                    CsvField(r.Reason ?? ""),
                    CsvField(i.Item.Name),
                    i.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Count.ToString("0.000", CultureInfo.InvariantCulture),
                    i.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture),
                    i.Total.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(r.Status.GetDisplayName())));
            }
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> SaleQuotesCsvAsync()
    {
        var quotes = await _db.SaleQuotes
            .AsNoTracking()
            .Include(q => q.Customer)
            .Include(q => q.Currency)
            .Include(q => q.Items)
            .ThenInclude(i => i.Item)
            .OrderBy(q => q.QuoteDate)
            .ThenBy(q => q.QuoteNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم العرض,العميل,التاريخ,صالح حتى,العملة,الصنف,الكمية,العدد,سعر الوحدة,إجمالي الصنف,خصم,ضريبة,الصافي,الحالة");
        foreach (var q in quotes)
        {
            foreach (var i in q.Items)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(q.QuoteNumber), CsvField(q.Customer?.Name ?? ""),
                    CsvField(q.QuoteDate.ToString("dd/MM/yyyy")),
                    CsvField(q.ValidUntil?.ToString("dd/MM/yyyy") ?? ""),
                    CsvField(q.Currency?.Code ?? ""), CsvField(i.Item.Name),
                    i.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Count.ToString("0.000", CultureInfo.InvariantCulture),
                    i.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture),
                    i.Total.ToString("0.00", CultureInfo.InvariantCulture),
                    q.Discount.ToString("0.00", CultureInfo.InvariantCulture),
                    q.Tax.ToString("0.00", CultureInfo.InvariantCulture),
                    q.NetAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(q.Status.GetDisplayName())));
            }
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> StockMovementsCsvAsync()
    {
        var movements = await _db.StockMovements
            .AsNoTracking()
            .Include(m => m.Item)
            .OrderByDescending(m => m.MovementDate)
            .ThenBy(m => m.Id)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("التاريخ,الصنف,النوع,الكمية,العدد,الرصيد الكمي قبل,الرصيد الكمي بعد,الرصيد العددي قبل,الرصيد العددي بعد,رقم المستند,نوع المستند,أنشئ بواسطة");
        foreach (var m in movements)
        {
            sb.AppendLine(string.Join(",",
                CsvField(m.MovementDate.ToString("dd/MM/yyyy")), CsvField(m.Item.Name), CsvField(m.Type.GetDisplayName()),
                m.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                m.Count.ToString("0.000", CultureInfo.InvariantCulture),
                m.BalanceBefore.ToString("0.000", CultureInfo.InvariantCulture),
                m.BalanceAfter.ToString("0.000", CultureInfo.InvariantCulture),
                m.CountBefore.ToString("0.000", CultureInfo.InvariantCulture),
                m.CountAfter.ToString("0.000", CultureInfo.InvariantCulture),
                CsvField(m.DocumentNumber ?? ""),
                CsvField(m.DocumentType.HasValue ? m.DocumentType.Value.GetDisplayName() : ""),
                CsvField(m.CreatedBy ?? "")));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> StockTransfersCsvAsync()
    {
        var transfers = await _db.StockTransfers
            .AsNoTracking()
            .Include(t => t.SourceWarehouse)
            .Include(t => t.TargetWarehouse)
            .Include(t => t.Items)
            .ThenInclude(i => i.Item)
            .OrderByDescending(t => t.TransferDate)
            .ThenBy(t => t.TransferNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم التحويل,المستودع المصدر,المستودع الوجهة,التاريخ,الصنف,الكمية,العدد,تكلفة الوحدة,الإجمالي,ملاحظات");
        foreach (var t in transfers)
        {
            foreach (var i in t.Items)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(t.TransferNumber), CsvField(t.SourceWarehouse.Name), CsvField(t.TargetWarehouse.Name),
                    CsvField(t.TransferDate.ToString("dd/MM/yyyy")), CsvField(i.Item.Name),
                    i.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Count.ToString("0.000", CultureInfo.InvariantCulture),
                    i.UnitCost.ToString("0.00", CultureInfo.InvariantCulture),
                    ((double)(i.Quantity > 0 ? i.Quantity : i.Count) * (double)i.UnitCost).ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(t.Notes ?? "")));
            }
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> InventoryAdjustmentsCsvAsync()
    {
        var adjustments = await _db.InventoryAdjustments
            .AsNoTracking()
            .Include(a => a.Item)
            .OrderByDescending(a => a.AdjustmentDate)
            .ThenBy(a => a.ReferenceNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم المستند,الصنف,الكمية الجديدة,العدد الجديد,التاريخ,السبب,أنشئ بواسطة");
        foreach (var a in adjustments)
        {
            sb.AppendLine(string.Join(",",
                CsvField(a.ReferenceNumber), CsvField(a.Item.Name),
                a.NewQuantity.ToString("0.000", CultureInfo.InvariantCulture),
                a.NewCount.ToString("0.000", CultureInfo.InvariantCulture),
                CsvField(a.AdjustmentDate.ToString("dd/MM/yyyy")),
                CsvField(a.Reason ?? ""), CsvField(a.CreatedBy ?? "")));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> JournalEntriesCsvAsync()
    {
        var entries = await _db.JournalEntries
            .AsNoTracking()
            .Include(j => j.Lines)
            .ThenInclude(l => l.Account)
            .OrderBy(j => j.EntryNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم القيد,التاريخ,بيان القيد,المصدر,معرف المستند,الحالة,أنشئ بواسطة,رمز الحساب,اسم الحساب,مدين,دائن,بيان البند");
        foreach (var j in entries)
        {
            foreach (var l in j.Lines)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(j.EntryNumber), CsvField(j.Date.ToString("dd/MM/yyyy")),
                    CsvField(j.Description), CsvField(j.Source.GetDisplayName()),
                    j.SourceId.ToString(CultureInfo.InvariantCulture),
                    CsvField(j.IsPosted ? "مرحّل" : "مسودة"), CsvField(j.CreatedBy ?? ""),
                    CsvField(l.Account?.Code ?? ""), CsvField(l.Account?.Name ?? ""),
                    l.Debit.ToString("0.00", CultureInfo.InvariantCulture),
                    l.Credit.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(l.Description ?? "")));
            }
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> FiscalPeriodsCsvAsync()
    {
        var periods = await _db.FiscalPeriods
            .AsNoTracking()
            .OrderByDescending(p => p.Year)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("السنة,الاسم,الحالة,أغلقت بواسطة,تاريخ الإغلاق");
        foreach (var p in periods)
        {
            sb.AppendLine(string.Join(",",
                p.Year.ToString(CultureInfo.InvariantCulture), CsvField(p.Name ?? ""),
                CsvField(p.IsClosed ? "مغلقة" : "مفتوحة"), CsvField(p.ClosedById ?? ""),
                CsvField(p.ClosedAt?.ToString("dd/MM/yyyy") ?? "")));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> BudgetsCsvAsync()
    {
        var lines = await _db.BudgetLines
            .AsNoTracking()
            .Include(l => l.BudgetYear)
            .Include(l => l.Account)
            .OrderBy(l => l.BudgetYear!.Year)
            .ThenBy(l => l.Account!.Code)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("السنة,رمز الحساب,اسم الحساب,المبلغ السنوي");
        foreach (var l in lines)
        {
            sb.AppendLine(string.Join(",",
                l.BudgetYear!.Year.ToString(CultureInfo.InvariantCulture),
                CsvField(l.Account?.Code ?? ""), CsvField(l.Account?.Name ?? ""),
                l.AnnualAmount.ToString("0.00", CultureInfo.InvariantCulture)));
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> PurchaseOrdersCsvAsync()
    {
        var orders = await _db.PurchaseOrders
            .AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Items)
            .ThenInclude(i => i.Item)
            .OrderByDescending(o => o.OrderDate)
            .ThenBy(o => o.OrderNumber)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم الأمر,المورد,التاريخ,التاريخ المتوقع,الحالة,الصنف,الكمية المطلوبة,العدد المطلوب,سعر الوحدة,الكمية المستلمة,العدد المستلم,الإجمالي,ملاحظات");
        foreach (var o in orders)
        {
            foreach (var i in o.Items)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(o.OrderNumber), CsvField(o.Supplier.Name),
                    CsvField(o.OrderDate.ToString("dd/MM/yyyy")),
                    CsvField(o.ExpectedDate?.ToString("dd/MM/yyyy") ?? ""),
                    CsvField(o.Status.GetDisplayName()), CsvField(i.Item.Name),
                    i.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Count.ToString("0.000", CultureInfo.InvariantCulture),
                    i.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture),
                    i.ReceivedQty.ToString("0.000", CultureInfo.InvariantCulture),
                    i.ReceivedCount.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Total.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvField(o.Notes ?? "")));
            }
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> SaleOrdersCsvAsync()
    {
        var orders = await _db.SalesOrders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Item)
            .OrderByDescending(o => o.OrderDate)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم الأمر,العميل,التاريخ,التاريخ المتوقع,الحالة,عرض السعر,ملاحظات,الصنف,الكمية المطلوبة,العدد المطلوب,سعر الوحدة,الكمية المفوتَرة,العدد المفوتَر,الإجمالي");
        foreach (var o in orders)
        {
            foreach (var i in o.Items)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(o.OrderNumber),
                    CsvField(o.Customer?.Name ?? ""),
                    CsvField(o.OrderDate.ToString("dd/MM/yyyy")),
                    CsvField(o.ExpectedDate?.ToString("dd/MM/yyyy") ?? ""),
                    CsvField(o.Status.GetDisplayName()),
                    o.SaleQuoteId?.ToString() ?? "",
                    CsvField(o.Notes ?? ""),
                    CsvField(i.Item?.Name ?? ""),
                    i.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Count.ToString("0.000", CultureInfo.InvariantCulture),
                    i.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture),
                    i.InvoicedQty.ToString("0.000", CultureInfo.InvariantCulture),
                    i.InvoicedCount.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Total.ToString("0.00", CultureInfo.InvariantCulture)));
            }
        }
        return CsvBytes(sb);
    }

    public async Task<byte[]> DeliveryOrdersCsvAsync()
    {
        var deliveries = await _db.DeliveryOrders
            .AsNoTracking()
            .Include(d => d.Customer)
            .Include(d => d.SaleInvoice)
            .Include(d => d.Items).ThenInclude(i => i.Item)
            .OrderByDescending(d => d.DeliveryDate)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("رقم الإذن,فاتورة البيع,العميل,تاريخ التسليم,الحالة,الناقل,رقم التتبع,ملاحظات,رحّلها,تاريخ الترحيل,الصنف,الكمية المسلّمة,العدد المسلّم");
        foreach (var d in deliveries)
        {
            foreach (var i in d.Items)
            {
                sb.AppendLine(string.Join(",",
                    CsvField(d.DeliveryNumber),
                    CsvField(d.SaleInvoice?.InvoiceNumber ?? ""),
                    CsvField(d.Customer?.Name ?? ""),
                    CsvField(d.DeliveryDate.ToString("dd/MM/yyyy")),
                    CsvField(d.Status.GetDisplayName()),
                    CsvField(d.Carrier ?? ""),
                    CsvField(d.TrackingNumber ?? ""),
                    CsvField(d.Notes ?? ""),
                    CsvField(d.DeliveredBy ?? ""),
                    CsvField(d.DeliveredAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? ""),
                    CsvField(i.Item?.Name ?? ""),
                    i.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    i.Count.ToString("0.000", CultureInfo.InvariantCulture)));
            }
        }
        return CsvBytes(sb);
    }

    private static byte[] CsvBytes(StringBuilder sb)
    {
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string CsvField(string value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && "=+-\t@\r\n".IndexOf(value[0]) >= 0)
            value = "'" + value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static byte[] BuildWorkbook(string sheetName, string title, IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        using var wb = new XLWorkbook();
        WriteSheet(wb, sheetName, title, headers, rows);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteSheet(XLWorkbook wb, string sheetName, string title, IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        var ws = wb.Worksheets.Add(sheetName);
        ws.Range(1, 1, 1, headers.Count).Style.Font.Bold = true;
        for (int c = 0; c < headers.Count; c++)
            WriteCell(ws, 1, c + 1, headers[c]);

        int row = 2;
        foreach (var r in rows.Take(MaxExportRows))
        {
            for (int c = 0; c < r.Length; c++)
                WriteCell(ws, row, c + 1, r[c]);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void WriteCell(IXLWorksheet ws, int row, int col, object? value)
    {
        var cell = ws.Cell(row, col);
        if (value is null)
        {
            cell.Value = "";
            return;
        }
        if (value is bool b)
        {
            cell.Value = b;
            return;
        }
        if (value is int n)
        {
            cell.Value = n;
            return;
        }
        if (value is decimal d)
        {
            cell.Value = (double)d;
            return;
        }
        if (value is double db)
        {
            cell.Value = db;
            return;
        }
        var text = value.ToString() ?? "";
        if (text.Length > 0 && "=+-\t@\r\n".IndexOf(text[0]) >= 0)
        {
            cell.SetValue("'" + text);
            return;
        }
        cell.Value = text;
    }

    private static string AccountTypeLabel(GLAccountType type) => type switch
    {
        GLAccountType.Asset => "أصل",
        GLAccountType.Liability => "خصم",
        GLAccountType.Equity => "حقوق ملكية",
        GLAccountType.Revenue => "إيراد",
        _ => "مصروف"
    };
}