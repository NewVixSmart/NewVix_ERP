using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.ViewModels.Import;

namespace NewVixSmart.Web.Services;

public class ImportCenterService : IImportCenterService
{
    private const int MaxRows = 5000;
    private const long MaxFileBytes = 25 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IPaymentService _payments;
    private readonly IAccountingService _accounting;
    private readonly IMemoryCache _cache;

    public ImportCenterService(AppDbContext db, IInventoryService inventory, IPaymentService payments, IAccountingService accounting, IMemoryCache cache)
    {
        _db = db;
        _inventory = inventory;
        _payments = payments;
        _accounting = accounting;
        _cache = cache;
    }

    private static readonly ImportEntityDefinition[] Entities =
    [
        new("suppliers", "الموردون", "بيانات الموردين الأساسية", "bi-truck", "suppliers",
            "يُحدَّث المورد إن وُجد بالكود، وإلا فبالاسم", "Code", "Name", "Code",
            [
                Col("Code", "الكود", ImportValueType.Text, maxLength: 50),
                Col("Name", "الاسم", ImportValueType.Text, isRequired: true, maxLength: 200),
                Col("Address", "العنوان", ImportValueType.Text, maxLength: 200),
                Col("Phone", "التليفون", ImportValueType.Text, maxLength: 20, aliases: "الهاتف"),
                Col("Email", "البريد الإلكتروني", ImportValueType.Text, maxLength: 200),
                Col("TaxNumber", "الرقم الضريبي", ImportValueType.Text, maxLength: 20),
                Col("OpeningBalance", "الرصيد الافتتاحي", ImportValueType.Decimal, minInclusive: 0),
                Col("CurrencyCode", "العملة", ImportValueType.Lookup, lookupKey: "currencies", lookupUseCode: true),
                Col("Notes", "ملاحظات", ImportValueType.Text, maxLength: 500),
                Col("IsActive", "نشط", ImportValueType.Bool, aliases: "الحالة")
            ]),
        new("customers", "العملاء", "بيانات العملاء الأساسية", "bi-people", "customers",
            "يُحدَّث العميل إن وُجد بالكود، وإلا فبالاسم", "Code", "Name", "Code",
            [
                Col("Code", "الكود", ImportValueType.Text, maxLength: 50),
                Col("Name", "الاسم", ImportValueType.Text, isRequired: true, maxLength: 200),
                Col("Address", "العنوان", ImportValueType.Text, maxLength: 200),
                Col("Phone", "التليفون", ImportValueType.Text, maxLength: 20, aliases: "الهاتف"),
                Col("Email", "البريد الإلكتروني", ImportValueType.Text, maxLength: 200),
                Col("TaxNumber", "الرقم الضريبي", ImportValueType.Text, maxLength: 20),
                Col("OpeningBalance", "الرصيد الافتتاحي", ImportValueType.Decimal, minInclusive: 0),
                Col("CurrencyCode", "العملة", ImportValueType.Lookup, lookupKey: "currencies", lookupUseCode: true),
                Col("Notes", "ملاحظات", ImportValueType.Text, maxLength: 500),
                Col("IsActive", "نشط", ImportValueType.Bool, aliases: "الحالة")
            ]),
        new("items", "الأصناف", "بطاقات الأصناف وأرصدتها", "bi-box-seam", "items",
            "يُحدَّث الصنف إن وُجد بالباركود، وإلا فبالاسم", "Barcode", "Name", "Code",
            [
                Col("Code", "الكود", ImportValueType.Text, maxLength: 50, aliases: "كود الصنف"),
                Col("Barcode", "الباركود", ImportValueType.Text, maxLength: 50),
                Col("Name", "الاسم", ImportValueType.Text, isRequired: true, maxLength: 200, aliases: "اسم الصنف"),
                Col("ItemTypeName", "النوع", ImportValueType.Lookup, isRequired: true, lookupKey: "itemTypes", aliases: "نوع الصنف"),
                Col("CategoryName", "التصنيف", ImportValueType.Lookup, isRequired: true, lookupKey: "categories", aliases: "اسم التصنيف"),
                Col("CountUnitName", "وحدة العدد", ImportValueType.Lookup, lookupKey: "units", aliases: "وحدة القياس"),
                Col("QuantityUnitName", "وحدة الكمية", ImportValueType.Lookup, lookupKey: "units"),
                Col("PurchasePrice", "سعر الشراء", ImportValueType.Decimal, minInclusive: 0, aliases: "سعر الشراء (الافتراضي)"),
                Col("SalePrice", "سعر البيع", ImportValueType.Decimal, minInclusive: 0, aliases: "سعر البيع (الافتراضي)"),
                Col("MinCount", "الحد الأدنى (عدد)", ImportValueType.Decimal, minInclusive: 0, aliases: "الحد الأدنى للعدد"),
                Col("MinQuantity", "الحد الأدنى (كمية)", ImportValueType.Decimal, minInclusive: 0, aliases: "الحد الأدنى للكمية"),
                Col("CurrentCount", "الرصيد (عدد)", ImportValueType.Decimal, minInclusive: 0, aliases: ["رصيد أول المدة (عدد)", "رصيد العدد"]),
                Col("CurrentQuantity", "الرصيد (كمية)", ImportValueType.Decimal, minInclusive: 0, aliases: ["رصيد أول المدة (كمية)", "رصيد الكمية"]),
                Col("IsSellable", "قابل للبيع", ImportValueType.Bool),
                Col("Notes", "ملاحظات", ImportValueType.Text, maxLength: 500),
                Col("IsActive", "الحالة", ImportValueType.Bool, aliases: ["نشط", "يُباع خارجياً"])
            ]),
        new("itemCategories", "الفئات", "تصنيفات الأصناف الرئيسية", "bi-tags", "item_categories",
            "يُحدَّث التصنيف إن وُجد بالاسم", "Name", null, null,
            [
                Col("Name", "اسم التصنيف", ImportValueType.Text, isRequired: true, maxLength: 100, aliases: "الاسم"),
                Col("Notes", "ملاحظات", ImportValueType.Text, maxLength: 500),
                Col("IsActive", "نشط", ImportValueType.Bool, aliases: "الحالة")
            ],
            IgnoredHeaders: ["عدد الأصناف"]),
        new("itemTypes", "الأنواع", "أنواع الأصناف", "bi-grid", "item_types",
            "يُحدَّث النوع إن وُجد بالاسم", "Name", null, null,
            [
                Col("Name", "اسم النوع", ImportValueType.Text, isRequired: true, maxLength: 100, aliases: "الاسم"),
                Col("Notes", "ملاحظات", ImportValueType.Text, maxLength: 500),
                Col("IsActive", "نشط", ImportValueType.Bool, aliases: "الحالة")
            ],
            IgnoredHeaders: ["عدد الأصناف"]),
        new("units", "الوحدات", "وحدات القياس ومشتقاتها", "bi-rulers", "units",
            "تُحدَّث الوحدة إن وُجدت بالاسم، وترتبط بالوحدة الفرعية منها", "Name", null, null,
            [
                Col("Name", "اسم الوحدة", ImportValueType.Text, isRequired: true, maxLength: 50, aliases: "الاسم"),
                Col("ShortName", "الاختصار", ImportValueType.Text, maxLength: 20),
                Col("SubUnits", "الوحدات الفرعية", ImportValueType.Integer, minInclusive: 0),
                Col("ParentUnitName", "وحدة فرعية من", ImportValueType.Lookup, lookupKey: "units", aliases: "الوحدة الأم"),
                Col("IsActive", "نشط", ImportValueType.Bool, aliases: "الحالة")
            ]),
        new("currencies", "العملات", "العملات وأسعار الصرف", "bi-currency-exchange", "currencies",
            "تُحدَّث العملة إن وُجدت بالرمز", "Code", null, "Code",
            [
                Col("Code", "الكود", ImportValueType.Text, isRequired: true, maxLength: 10, aliases: "الرمز"),
                Col("Name", "الاسم", ImportValueType.Text, isRequired: true, maxLength: 100),
                Col("Symbol", "رمز العملة", ImportValueType.Text, maxLength: 10),
                Col("ExchangeRate", "سعر الصرف", ImportValueType.Decimal, minInclusive: 0.000001m),
                Col("IsBase", "العملة الأساسية", ImportValueType.Bool, aliases: ["عملة أساسية", "العملة الرئيسية"]),
                Col("IsActive", "الحالة", ImportValueType.Bool, aliases: ["نشطة", "نشط"])
            ]),
        new("branches", "الفروع", "فروع الشركة", "bi-diagram-3", "branches",
            "يُحدَّث الفرع إن وُجد بالرمز", "Code", null, "Code",
            [
                Col("Code", "الكود", ImportValueType.Text, isRequired: true, maxLength: 50, aliases: "الرمز"),
                Col("Name", "الاسم", ImportValueType.Text, isRequired: true, maxLength: 200),
                Col("Address", "العنوان", ImportValueType.Text, maxLength: 300),
                Col("Phone", "الهاتف", ImportValueType.Text, maxLength: 20),
                Col("IsActive", "الحالة", ImportValueType.Bool, aliases: "نشط")
            ],
            IgnoredHeaders: ["تاريخ الإنشاء"]),
        new("warehouses", "المخازن", "المخازن والمواقع", "bi-buildings", "warehouses",
            "يُحدَّث المخزن إن وُجد بالرمز", "Code", null, "Code",
            [
                Col("Code", "الكود", ImportValueType.Text, isRequired: true, maxLength: 50, aliases: "الرمز"),
                Col("Name", "الاسم", ImportValueType.Text, isRequired: true, maxLength: 200),
                Col("IsActive", "الحالة", ImportValueType.Bool, aliases: "نشط")
            ],
            IgnoredHeaders: ["تاريخ الإنشاء"]),
        new("glAccounts", "حسابات القيود", "مخطط الحسابات الكامل", "bi-journal-code", "gl_accounts",
            "يُحدَّث الحساب إن وُجد بالرمز، ويُربط حسابه الأب بمن سبقه في الملف", "Code", null, "Code",
            [
                Col("Code", "كود الحساب", ImportValueType.Text, isRequired: true, maxLength: 20, aliases: "الرمز"),
                Col("Name", "اسم الحساب", ImportValueType.Text, isRequired: true, maxLength: 200, aliases: "الاسم"),
                Col("Type", "نوع الحساب", ImportValueType.Enum, enumMap: "أصل=1;خصوم|خصم=2;حقوق ملكية=3;إيراد=4;مصروف=5", aliases: "حقوق ملكية=3"),
                Col("NormalBalance", "طبيعة الحساب", ImportValueType.Enum, enumMap: "مدين=1;دائن=2"),
                Col("ParentCode", "الحساب الأب", ImportValueType.Lookup, lookupKey: "glAccounts", lookupUseCode: true),
                Col("IsActive", "الحالة", ImportValueType.Bool, aliases: "نشط")
            ],
            IgnoredHeaders: ["الفرع"]),
        new("payments", "الدفعات", "القبض والصرف وقيودهما", "bi-wallet2", "payments",
            "صف لكل دفعة؛ تُنشأ الدفعة وقيودها وارتباطها بالفواتير تلقائيًا", "ReferenceNumber", null, null,
            [
                Col("ReceiptNumber", "رقم الإيصال", ImportValueType.Text, maxLength: 50),
                Col("Type", "النوع", ImportValueType.Enum, isRequired: true, enumMap: "قبض=1;صرف=2"),
                Col("PartyName", "العميل/المورد", ImportValueType.Lookup, isRequired: true, lookupKey: "parties"),
                Col("Amount", "المبلغ", ImportValueType.Decimal, isRequired: true, minInclusive: 0.01m),
                Col("BaseAmount", "المبلغ بالأساس", ImportValueType.Decimal, minInclusive: 0),
                Col("CurrencyCode", "العملة", ImportValueType.Lookup, lookupKey: "currencies", lookupUseCode: true),
                Col("ExchangeRate", "سعر الصرف", ImportValueType.Decimal, minInclusive: 0.000001m),
                Col("Method", "طريقة الدفع", ImportValueType.Enum, isRequired: true, enumMap: "نقداً=1;شيك=2;تحويل بنكي=3;بطاقة ائتمان=4"),
                Col("PaymentDate", "التاريخ", ImportValueType.Date, isRequired: true),
                Col("ReferenceNumber", "رقم المرجع", ImportValueType.Text, maxLength: 50),
                Col("Notes", "ملاحظات", ImportValueType.Text, maxLength: 500)
            ]),
        new("journalEntries", "قيود اليومية", "القيود العامة المحاسبية", "bi-journal-bookmark", "journal_entries",
            "قسما بصف لكل سطر قيد؛ تُجمع الأسطر برقم القيد ويجب أن يتوازن كل قيد", "EntryNumber", null, null,
            [
                Col("EntryNumber", "رقم القيد", ImportValueType.Text, isRequired: true, maxLength: 50),
                Col("EntryDate", "التاريخ", ImportValueType.Date, isRequired: true),
                Col("Description", "بيان القيد", ImportValueType.Text, maxLength: 500),
                Col("AccountCode", "رمز الحساب", ImportValueType.Lookup, isRequired: true, lookupKey: "glAccounts", lookupUseCode: true, isLineOnly: true),
                Col("Debit", "مدين", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("Credit", "دائن", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("EntryDescription", "بيان البند", ImportValueType.Text, maxLength: 500, isLineOnly: true)
            ],
            GroupColumn: "EntryNumber",
            IgnoredHeaders: ["المصدر", "اسم الحساب", "الحالة", "معرف المستند", "أنشئ بواسطة"]),
        new("saleInvoices", "فواتير المبيعات", "الفواتير وقيودها وحركة المخزون", "bi-cart-check", "sale_invoices",
            "قسما بصف لكل صنف؛ تُجمع الأصناف برقم الفاتورة وتُفتح القيود وحركة المخزون تلقائيًا", "InvoiceNumber", null, null,
            [
                Col("InvoiceNumber", "رقم الفاتورة", ImportValueType.Text, isRequired: true, maxLength: 50),
                Col("CustomerName", "اسم العميل", ImportValueType.Lookup, isRequired: true, lookupKey: "customers", aliases: "العميل"),
                Col("InvoiceDate", "تاريخ الفاتورة", ImportValueType.Date, isRequired: true, aliases: "التاريخ"),
                Col("CurrencyCode", "العملة", ImportValueType.Lookup, lookupKey: "currencies", lookupUseCode: true),
                Col("ExchangeRate", "سعر الصرف", ImportValueType.Decimal, minInclusive: 0.000001m),
                Col("PaymentTerms", "شروط الدفع", ImportValueType.Enum, enumMap: "عند الاستلام=0;7 أيام=1;15 يوم=2;30 يوم=3;60 يوم=4"),
                Col("Discount", "خصم الفاتورة", ImportValueType.Decimal, minInclusive: 0),
                Col("Discount2", "خصم إضافي 2", ImportValueType.Decimal, minInclusive: 0),
                Col("Discount3", "خصم إضافي 3", ImportValueType.Decimal, minInclusive: 0),
                Col("Tax", "الضريبة", ImportValueType.Decimal, minInclusive: 0),
                Col("Notes", "ملاحظات", ImportValueType.Text, maxLength: 500),
                Col("ItemName", "اسم الصنف", ImportValueType.Lookup, isRequired: true, lookupKey: "items", isLineOnly: true, aliases: "الصنف"),
                Col("Quantity", "الكمية", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("Count", "العدد", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("UnitPrice", "سعر الوحدة", ImportValueType.Decimal, isRequired: true, minInclusive: 0, isLineOnly: true),
                Col("ItemDiscount", "خصم الصنف", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true)
            ],
            GroupColumn: "InvoiceNumber",
            IgnoredHeaders: ["إجمالي الصنف", "الصافي", "المدفوع", "الحالة"]),
        new("purchaseInvoices", "فواتير الشراء", "فواتير المشتريات وقيودها وحركة المخزون", "bi-cart-dash", "purchase_invoices",
            "قسما بصف لكل صنف؛ تُجمع الأصناف برقم الفاتورة وتُفتح القيود وحركة المخزون تلقائيًا", "InvoiceNumber", null, null,
            [
                Col("InvoiceNumber", "رقم الفاتورة", ImportValueType.Text, isRequired: true, maxLength: 50),
                Col("SupplierName", "اسم المورد", ImportValueType.Lookup, isRequired: true, lookupKey: "suppliers", aliases: "المورد"),
                Col("InvoiceDate", "تاريخ الفاتورة", ImportValueType.Date, isRequired: true, aliases: "التاريخ"),
                Col("CurrencyCode", "العملة", ImportValueType.Lookup, lookupKey: "currencies", lookupUseCode: true),
                Col("ExchangeRate", "سعر الصرف", ImportValueType.Decimal, minInclusive: 0.000001m),
                Col("PaymentTerms", "شروط الدفع", ImportValueType.Enum, enumMap: "عند الاستلام=0;7 أيام=1;15 يوم=2;30 يوم=3;60 يوم=4"),
                Col("Discount", "خصم الفاتورة", ImportValueType.Decimal, minInclusive: 0),
                Col("Discount2", "خصم إضافي 2", ImportValueType.Decimal, minInclusive: 0),
                Col("Discount3", "خصم إضافي 3", ImportValueType.Decimal, minInclusive: 0),
                Col("Tax", "الضريبة", ImportValueType.Decimal, minInclusive: 0),
                Col("Notes", "ملاحظات", ImportValueType.Text, maxLength: 500),
                Col("ItemName", "اسم الصنف", ImportValueType.Lookup, isRequired: true, lookupKey: "items", isLineOnly: true, aliases: "الصنف"),
                Col("Quantity", "الكمية", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("Count", "العدد", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("UnitPrice", "سعر الوحدة", ImportValueType.Decimal, isRequired: true, minInclusive: 0, isLineOnly: true),
                Col("ItemDiscount", "خصم الصنف", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true)
            ],
            GroupColumn: "InvoiceNumber",
            IgnoredHeaders: ["إجمالي الصنف", "الصافي", "المدفوع", "الحالة"]),
        new("saleReturns", "مرتجعات المبيعات", "إرجاع الأصناف المستلمة من العملاء", "bi-arrow-90deg-left", "sale_returns",
            "قسما بصف لكل صنف؛ تُجمع الأسطر برقم المرتجع وتُدقق الكميات المرتجعة مع المبيعات", "ReturnNumber", null, null,
            [
                Col("ReturnNumber", "رقم المرتجع", ImportValueType.Text, isRequired: true, maxLength: 50),
                Col("CustomerName", "اسم العميل", ImportValueType.Lookup, isRequired: true, lookupKey: "customers", aliases: "العميل"),
                Col("SaleInvoiceNumber", "فاتورة البيع الأصلية", ImportValueType.Lookup, lookupKey: "saleInvoices", aliases: "الفاتورة الأصلية"),
                Col("ReturnDate", "تاريخ المرتجع", ImportValueType.Date, isRequired: true, aliases: "التاريخ"),
                Col("CurrencyCode", "العملة", ImportValueType.Lookup, lookupKey: "currencies", lookupUseCode: true),
                Col("ExchangeRate", "سعر الصرف", ImportValueType.Decimal, minInclusive: 0.000001m),
                Col("Reason", "سبب المرتجع", ImportValueType.Text, maxLength: 500),
                Col("ItemName", "اسم الصنف", ImportValueType.Lookup, isRequired: true, lookupKey: "items", isLineOnly: true, aliases: "الصنف"),
                Col("Quantity", "الكمية", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("Count", "العدد", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("UnitPrice", "سعر الوحدة", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true)
            ],
            GroupColumn: "ReturnNumber",
            IgnoredHeaders: ["الإجمالي", "الحالة"]),
        new("purchaseReturns", "مرتجعات المشتريات", "إعادة الأصناف إلى الموردين", "bi-arrow-90deg-right", "purchase_returns",
            "قسما بصف لكل صنف؛ تُجمع الأسطر برقم المرتجع وتُدقق الكميات المرتجعة مع المشتريات", "ReturnNumber", null, null,
            [
                Col("ReturnNumber", "رقم المرتجع", ImportValueType.Text, isRequired: true, maxLength: 50),
                Col("SupplierName", "اسم المورد", ImportValueType.Lookup, isRequired: true, lookupKey: "suppliers", aliases: "المورد"),
                Col("PurchaseInvoiceNumber", "فاتورة الشراء الأصلية", ImportValueType.Lookup, lookupKey: "purchaseInvoices", aliases: "الفاتورة الأصلية"),
                Col("ReturnDate", "تاريخ المرتجع", ImportValueType.Date, isRequired: true, aliases: "التاريخ"),
                Col("CurrencyCode", "العملة", ImportValueType.Lookup, lookupKey: "currencies", lookupUseCode: true),
                Col("ExchangeRate", "سعر الصرف", ImportValueType.Decimal, minInclusive: 0.000001m),
                Col("Reason", "سبب المرتجع", ImportValueType.Text, maxLength: 500),
                Col("ItemName", "اسم الصنف", ImportValueType.Lookup, isRequired: true, lookupKey: "items", isLineOnly: true, aliases: "الصنف"),
                Col("Quantity", "الكمية", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("Count", "العدد", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("UnitPrice", "سعر الوحدة", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true)
            ],
            GroupColumn: "ReturnNumber",
            IgnoredHeaders: ["الإجمالي", "الحالة"]),
        new("inventoryAdjustments", "تسويات المخزون", "جرد وتعديل أرصدة الأصناف", "bi-clipboard-check", "inventory_adjustments",
            "صف لكل تسوية؛ تُحدَّث أرصدة الصنف وتُفتح قيود الفروقات تلقائيًا", "ReferenceNumber", null, null,
            [
                Col("ReferenceNumber", "رقم المستند", ImportValueType.Text, maxLength: 50),
                Col("ItemName", "اسم الصنف", ImportValueType.Lookup, isRequired: true, lookupKey: "items", aliases: "الصنف"),
                Col("NewCount", "العدد الجديد", ImportValueType.Decimal, minInclusive: 0),
                Col("NewQuantity", "الكمية الجديدة", ImportValueType.Decimal, minInclusive: 0),
                Col("AdjustmentDate", "تاريخ الجرد", ImportValueType.Date, isRequired: true, aliases: "التاريخ"),
                Col("Reason", "سبب التعديل", ImportValueType.Text, maxLength: 500, aliases: "السبب")
            ],
            IgnoredHeaders: ["أنشئ بواسطة"]),
        new("stockTransfers", "تحويلات المخازن", "نقل الأصناف بين المخازن", "bi-shuffle", "stock_transfers",
            "قسما بصف لكل صنف؛ تُجمع الأصناف برقم التحويل وتُنقل تلقائيًا", "TransferNumber", null, null,
            [
                Col("TransferNumber", "رقم التحويل", ImportValueType.Text, isRequired: true, maxLength: 50),
                Col("SourceWarehouseCode", "مخزن المصدر", ImportValueType.Lookup, isRequired: true, lookupKey: "warehouses", lookupUseCode: true, aliases: "المستودع المصدر"),
                Col("TargetWarehouseCode", "مخزن الهدف", ImportValueType.Lookup, isRequired: true, lookupKey: "warehouses", lookupUseCode: true, aliases: "المستودع الوجهة"),
                Col("TransferDate", "تاريخ التحويل", ImportValueType.Date, isRequired: true, aliases: "التاريخ"),
                Col("Notes", "ملاحظات", ImportValueType.Text, maxLength: 500),
                Col("ItemName", "اسم الصنف", ImportValueType.Lookup, isRequired: true, lookupKey: "items", isLineOnly: true, aliases: "الصنف"),
                Col("Quantity", "الكمية", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("Count", "العدد", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true),
                Col("UnitCost", "تكلفة الوحدة", ImportValueType.Decimal, minInclusive: 0, isLineOnly: true)
            ],
            GroupColumn: "TransferNumber",
            IgnoredHeaders: ["الإجمالي"]),
    ];

    public IReadOnlyList<ImportEntityDefinition> GetEntities() => Entities;

    public ImportEntityDefinition? FindEntity(string key) =>
        Entities.FirstOrDefault(e => e.Key == key);

    private static ImportColumnDefinition Col(
        string key, string header, ImportValueType type,
        bool isRequired = false, string? lookupKey = null, bool lookupUseCode = false,
        string? enumMap = null, decimal? minInclusive = null, decimal? maxInclusive = null,
        int? maxLength = null, bool isLineOnly = false, params string[] aliases) =>
        new(key, header, type, isRequired, lookupKey, lookupUseCode, enumMap, minInclusive, maxInclusive, maxLength, isLineOnly,
            aliases.Length == 0 ? null : aliases);

    public Task<byte[]> DownloadTemplateAsync(string entityKey)
    {
        var def = FindEntity(entityKey);
        if (def is null) return Task.FromResult(Array.Empty<byte>());

        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("البيانات");
        ws.RightToLeft = true;
        for (int c = 0; c < def.Columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = def.Columns[c].HeaderAr;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        ws.Columns().AdjustToContents();

        var guide = wb.Worksheets.Add("إرشادات");
        guide.RightToLeft = true;
        var lines = GuideLines(def);
        for (int r = 0; r < lines.Count; r++)
        {
            var cell = guide.Cell(r + 1, 1);
            cell.Value = lines[r];
            cell.Style.Alignment.WrapText = true;
        }
        guide.Column(1).Width = 120;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    public async Task<ImportPreviewViewModel> ParseAsync(string entityKey, string fileName, byte[] data)
    {
        var def = FindEntity(entityKey);
        if (def is null) return FatalVm("الوحدة غير معروفة");
        if (data is null || data.Length == 0) return FatalVm("الملف فارغ أو تعذرت قراءته");
        if (data.Length > MaxFileBytes) return FatalVm("حجم الملف يتجاوز الحد الأقصى المسموح به (25 ميجابايت)");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        List<List<string>> rows;
        try
        {
            rows = ext switch
            {
                ".xlsx" => ReadXlsx(data),
                ".csv" => ReadCsv(data),
                _ => throw new InvalidOperationException()
            };
        }
        catch
        {
            return FatalVm(ext is ".xlsx" or ".csv"
                ? "تعذر فتح الملف أو أن تنسيقه تالف"
                : "صيغة الملف غير مدعومة؛ استخدم ملف Excel (.xlsx) أو CSV (.csv)");
        }

        rows = rows.Where(r => r.Any(cell => cell.Trim().Length > 0)).ToList();
        if (rows.Count == 0) return FatalVm("الملف لا يحتوي على بيانات");

        var headers = rows[0].Select(h => h.Trim().TrimStart('\uFEFF')).ToList();

        var matched = new List<(ImportColumnDefinition Col, int Index)>();
        var unknown = new List<string>();
        for (int i = 0; i < headers.Count; i++)
        {
            var h = headers[i];
            if (h.Length == 0) continue;
            var col = def.Columns.FirstOrDefault(c => c.HeaderAr == h || (c.Aliases?.Contains(h, StringComparer.Ordinal) ?? false));
            if (col is null)
            {
                if (def.IgnoredHeaders?.Contains(h, StringComparer.Ordinal) ?? false) continue;
                unknown.Add(h);
                continue;
            }
            matched.Add((col, i));
        }
        if (matched.Count == 0)
            return FatalVm("لا يوجد تطابق بين ترويسة الملف وأعمدة القالب؛ حمّل القالب الصحيح واستخدمه");

        var dataRows = rows.Skip(1).ToList();
        if (dataRows.Count == 0) return FatalVm("الملف يحتوي على ترويسة فقط ولا توجد صفوف بيانات");
        if (dataRows.Count > MaxRows) return FatalVm($"عدد الصفوف ({dataRows.Count}) يتجاوز الحد الأقصى ({MaxRows})");

        var db = await BuildReferenceCacheAsync();

        if (IsDocumentEntity(def.Key))
            return await ParseDocumentAsync(def, matched, dataRows, db);

        var fileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSelf = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? selfColumn = SelfIdentityColumn(def.Key);

        if (selfColumn != null)
        {
            foreach (var dr in dataRows)
            {
                var v0 = BuildCells(matched, dr).GetValueOrDefault(selfColumn, "").Trim();
                if (v0.Length > 0) seenSelf.Add(NormKey(v0));
            }
        }

        var previewRows = new List<ImportPreviewRow>();
        var payloadRows = new List<ImportRowPayload>();
        int valid = 0, duplicates = 0;

        for (int idx = 0; idx < dataRows.Count; idx++)
        {
            var values = dataRows[idx];
            var rowNumber = idx + 2;
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (col, i) in matched)
            {
                var v = i < values.Count ? values[i].Trim() : "";
                if (v.Length > 0) cells[col.Key] = v;
            }

            var errors = ValidateRow(def, cells, db, seenSelf);
            if (errors.Count > 0)
            {
                previewRows.Add(new ImportPreviewRow(rowNumber, DisplayValues(matched, values), errors, null, false));
                continue;
            }

            var candidates = KeyCandidates(def, cells);
            var isDuplicate = candidates.Any(fileKeys.Contains);
            foreach (var c in candidates) fileKeys.Add(c);

            var selfRaw = selfColumn == null ? "" : cells.GetValueOrDefault(selfColumn, "").Trim();
            if (selfRaw.Length > 0) seenSelf.Add(NormKey(selfRaw));

            var exists = candidates.Any(c => db.EntityKeyExists(def.Key, c));
            var notice = isDuplicate
                ? "سطر مكرر في الملف"
                : exists ? "سيتم تحديث السجل الموجود" : "سيتم إنشاء سجل جديد";

            previewRows.Add(new ImportPreviewRow(rowNumber, DisplayValues(matched, values), [], notice, isDuplicate));
            payloadRows.Add(new ImportRowPayload(rowNumber, cells));
            valid++;
            duplicates += isDuplicate ? 1 : 0;
        }

        var vm = new ImportPreviewViewModel
        {
            EntityKey = def.Key,
            EntityNameAr = def.NameAr,
            Headers = matched.Select(m => m.Col.HeaderAr).ToList(),
            Rows = previewRows,
            Warning = unknown.Count == 0 ? null : $"تم تجاهل أعمدة غير معروفة: {string.Join("، ", unknown)}",
            TotalCount = dataRows.Count,
            ValidCount = valid,
            ErrorCount = dataRows.Count - valid,
            DuplicateCount = duplicates
        };
        vm.Payload = payloadRows.Count == 0
            ? ""
            : SerializeEnvelope(new ImportPayloadEnvelope(def.Key, payloadRows));
        if (vm.Payload.Length > 0)
        {
            vm.ApplyToken = Guid.NewGuid().ToString("N");
            _cache.Set("ImportPreview:" + vm.ApplyToken, vm.Payload, TimeSpan.FromMinutes(45));
        }
        return vm;
    }

    public async Task<ImportResult> ImportAsync(string entityKey, string payload, string applyToken)
    {
        var def = FindEntity(entityKey);
        if (def is null) return new ImportResult(false, "الوحدة غير معروفة", 0, 0, 0, 0);

        var key = "ImportPreview:" + applyToken;
        if (!_cache.TryGetValue(key, out string? cachedPayload) || string.IsNullOrEmpty(cachedPayload))
            return new ImportResult(false, "انتهت صلاحية رابط المعاينة؛ أعد رفع الملف واعاينه مجددًا قبل الاعتماد", 0, 0, 0, 0);
        _cache.Remove(key);

        ImportPayloadEnvelope? envelope = DeserializeEnvelope(cachedPayload);
        if (envelope is null || envelope.EntityKey != entityKey || envelope.Rows.Count == 0)
            return new ImportResult(false, "رابط المعاينة غير صالح أو منتهي الصلاحية؛ أعد رفع الملف واعاينه مجددًا", 0, 0, 0, 0);
        if (envelope.Rows.Count > MaxRows)
            return new ImportResult(false, $"لا يمكن استيراد أكثر من {MaxRows} صف في المرة الواحدة", 0, 0, 0, 0);

        var cache = await BuildReferenceCacheAsync();

        if (IsDocumentEntity(def.Key))
            return await ImportDocumentAsync(def, envelope, cache);

        var selfColumn = SelfIdentityColumn(def.Key);
        var applySeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (selfColumn is not null)
            foreach (var row in envelope.Rows)
            {
                var v = (row.Fields?.GetValueOrDefault(selfColumn) ?? "").Trim();
                if (v.Length > 0) applySeen.Add(NormKey(v));
            }

        string? pendingBaseCode = null;
        var unitParentLinks = new List<(Unit Child, string ParentName)>();
        int created = 0, updated = 0, failed = 0;

        await using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var row in envelope.Rows)
        {
            var cells = row.Fields ?? new Dictionary<string, string>();
            var errors = ValidateRow(def, cells, cache, applySeen);
            if (errors.Count > 0) { failed++; continue; }

            int c, u;
            if (def.Key == "suppliers") { (c, u) = ApplySupplier(cells, cache); }
            else if (def.Key == "customers") { (c, u) = ApplyCustomer(cells, cache); }
            else if (def.Key == "items") { (c, u) = ApplyItem(cells, cache); }
            else if (def.Key == "itemCategories") { (c, u) = ApplyCategory(cells, cache); }
            else if (def.Key == "itemTypes") { (c, u) = ApplyItemType(cells, cache); }
            else if (def.Key == "units") { (c, u) = ApplyUnit(cells, cache, unitParentLinks); }
            else if (def.Key == "currencies") { (c, u) = ApplyCurrency(cells, cache, ref pendingBaseCode); }
            else if (def.Key == "branches") { (c, u) = ApplyBranch(cells, cache); }
            else if (def.Key == "warehouses") { (c, u) = ApplyWarehouse(cells, cache); }
            else if (def.Key == "glAccounts") { (c, u) = ApplyAccount(def, cells, cache); }
            else { c = 0; u = 0; }

            created += c;
            updated += u;
        }

        if (pendingBaseCode is not null)
        {
            foreach (var cur in cache.CurrenciesByCode.Values)
                if (cur.IsBase) cur.IsBase = false;
            if (cache.CurrenciesByCode.TryGetValue(pendingBaseCode, out var baseCurrency))
                baseCurrency.IsBase = true;
        }

        foreach (var (child, parentName) in unitParentLinks)
            if (cache.UnitsByName.TryGetValue(NormKey(parentName), out var parent) && parent != child)
                child.ParentUnit = parent;

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            return new ImportResult(false, "تعذر حفظ البيانات؛ تحقق من الرموز والقيم المكررة في الملف ثم أعد المحاولة", created, updated, 0, failed);
        }

        var message = $"تم استيراد {created} سجل جديد و{updated} سجل محدّث"
            + (failed == 0 ? "" : $" (وتُرك {failed} صفًّا به أخطاء)");
        return new ImportResult(true, message, created, updated, 0, failed);
    }

    private static bool IsDocumentEntity(string entityKey) => entityKey switch
    {
        "payments" or "journalEntries" or "saleInvoices" or "purchaseInvoices"
            or "saleReturns" or "purchaseReturns" or "inventoryAdjustments" or "stockTransfers" => true,
        _ => false
    };

    private static Dictionary<string, string> BuildCells(
        IEnumerable<(ImportColumnDefinition Col, int Index)> matched, List<string> values)
    {
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (col, i) in matched)
        {
            var v = i < values.Count ? values[i].Trim() : "";
            if (v.Length > 0) cells[col.Key] = v;
        }
        return cells;
    }

    private static Dictionary<string, string> MergeHeaderCells(
        ImportEntityDefinition def,
        IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var headerKeys = def.Columns.Where(c => !c.IsLineOnly).Select(c => c.Key).ToList();
        foreach (var cells in rows)
            foreach (var key in headerKeys)
                if (!merged.ContainsKey(key) && cells.TryGetValue(key, out var value) && value.Trim().Length > 0)
                    merged[key] = value.Trim();
        return merged;
    }

    private static List<string> ValidateHeader(ImportEntityDefinition def, IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
        => ValidateRow(def, cells, cache, null, headerOnly: true);

    private static List<string> ValidateLine(ImportEntityDefinition def, IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
        => ValidateRow(def, cells, cache, null, lineOnly: true);

    private async Task<ImportPreviewViewModel> ParseDocumentAsync(
        ImportEntityDefinition def,
        List<(ImportColumnDefinition Col, int Index)> matched,
        List<List<string>> dataRows,
        ReferenceCache cache)
    {
        var groupCol = def.GroupColumn;
        var previewRows = new List<ImportPreviewRow>();
        var payloadRows = new List<ImportRowPayload>();
        var docRows = new List<List<(int RowNumber, List<string> Values, Dictionary<string, string> Cells)>>();

        if (groupCol is null)
        {
            for (int idx = 0; idx < dataRows.Count; idx++)
            {
                var values = dataRows[idx];
                var cells = BuildCells(matched, values);
                docRows.Add([(idx + 2, values, cells)]);
            }
        }
        else
        {
            var headerAr = def.Columns.First(c => c.Key == groupCol).HeaderAr;
            var byGroup = new Dictionary<string, List<(int RowNumber, List<string> Values, Dictionary<string, string> Cells)>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            for (int idx = 0; idx < dataRows.Count; idx++)
            {
                var values = dataRows[idx];
                var cells = BuildCells(matched, values);
                var norm = NormKey(cells.GetValueOrDefault(groupCol, "").Trim());
                if (norm.Length == 0)
                {
                    previewRows.Add(new ImportPreviewRow(idx + 2, DisplayValues(matched, values),
                        [$"الحقل «{headerAr}» مطلوب لتجميع المستند"], null, false));
                    continue;
                }
                if (!byGroup.TryGetValue(norm, out var list))
                {
                    list = [];
                    byGroup[norm] = list;
                    order.Add(norm);
                }
                list.Add((idx + 2, values, cells));
            }
            foreach (var norm in order) docRows.Add(byGroup[norm]);
        }

        int valid = 0;
        foreach (var group in docRows)
        {
            var headerCells = MergeHeaderCells(def, group.Select(g => g.Cells));
            var errors = ValidateHeader(def, headerCells, cache);
            var payloads = group.Select(g => new ImportRowPayload(g.RowNumber, g.Cells)).ToList();
            errors.AddRange(ValidateDocument(def, headerCells, payloads, cache));
            errors = errors.Distinct().ToList();

            foreach (var (rowNumber, values, cells) in group)
            {
                if (errors.Count > 0)
                {
                    previewRows.Add(new ImportPreviewRow(rowNumber, DisplayValues(matched, values), errors, null, false));
                    continue;
                }
                var lineErrors = ValidateLine(def, cells, cache);
                if (lineErrors.Count > 0)
                {
                    previewRows.Add(new ImportPreviewRow(rowNumber, DisplayValues(matched, values), lineErrors, null, false));
                    continue;
                }
                var combined = new Dictionary<string, string>(headerCells, StringComparer.Ordinal);
                foreach (var kvp in cells) combined[kvp.Key] = kvp.Value;
                payloadRows.Add(new ImportRowPayload(rowNumber, combined));
                valid++;
            }
        }

        var docPayload = payloadRows.Count == 0
            ? ""
            : SerializeEnvelope(new ImportPayloadEnvelope(def.Key, payloadRows));
        string? applyToken = null;
        if (docPayload.Length > 0)
        {
            applyToken = Guid.NewGuid().ToString("N");
            _cache.Set("ImportPreview:" + applyToken, docPayload, TimeSpan.FromMinutes(45));
        }

        return new ImportPreviewViewModel
        {
            EntityKey = def.Key,
            EntityNameAr = def.NameAr,
            Headers = matched.Select(m => m.Col.HeaderAr).ToList(),
            Rows = previewRows,
            TotalCount = dataRows.Count,
            ValidCount = valid,
            ErrorCount = dataRows.Count - valid,
            DuplicateCount = 0,
            ApplyToken = applyToken,
            Payload = docPayload
        };
    }

    private async Task<ImportResult> ImportDocumentAsync(
        ImportEntityDefinition def,
        ImportPayloadEnvelope envelope,
        ReferenceCache cache)
    {
        var groupCol = def.GroupColumn;
        var docs = new List<List<ImportRowPayload>>();
        var failed = 0;

        if (groupCol is null)
        {
            foreach (var row in envelope.Rows) docs.Add([row]);
        }
        else
        {
            var byGroup = new Dictionary<string, List<ImportRowPayload>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var row in envelope.Rows)
            {
                var norm = NormKey((row.Fields?.GetValueOrDefault(groupCol) ?? "").Trim());
                if (norm.Length == 0) { failed++; continue; }
                if (!byGroup.ContainsKey(norm)) { byGroup[norm] = []; order.Add(norm); }
                byGroup[norm].Add(row);
            }
            foreach (var norm in order) docs.Add(byGroup[norm]);
        }

        var created = 0;
        var docFailures = new List<string>();
        foreach (var doc in docs)
        {
            List<string> errors;
            if (groupCol is null)
            {
                var cells = doc[0].Fields ?? new Dictionary<string, string>();
                errors = ValidateRow(def, cells, cache, null);
                errors.AddRange(ValidateDocument(def, cells, [doc[0]], cache));
            }
            else
            {
                var header = MergeHeaderCells(def, doc.Select(d => (IReadOnlyDictionary<string, string>)(d.Fields ?? new Dictionary<string, string>())));
                errors = ValidateHeader(def, header, cache);
                errors.AddRange(ValidateDocument(def, header, doc, cache));
                foreach (var line in doc)
                    errors.AddRange(ValidateLine(def, line.Fields ?? new Dictionary<string, string>(), cache));
            }

            errors = errors.Distinct().ToList();
            if (errors.Count > 0)
            {
                failed += doc.Count;
                continue;
            }

            var (ok, error) = await ApplyDocumentAsync(def, doc, cache);
            if (ok) created++;
            else
            {
                failed += doc.Count;
                if (docFailures.Count < 5 && error is not null) docFailures.Add(error);
            }
        }

        var suffix = failed == 0 ? "" :
            $" (وتُركت {failed} {(groupCol is null ? "مستندًا" : "سطرًا")} بأخطاء"
            + (docFailures.Count == 0 ? "" : "؛ أمثلة: " + string.Join(" — ", docFailures))
            + ")";
        return new ImportResult(true, $"تم استيراد {created} مستند" + suffix, created, 0, 0, failed);
    }

    private async Task<(bool Ok, string? Error)> ApplyDocumentAsync(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows, ReferenceCache cache)
    {
        try
        {
            return def.Key switch
            {
                "payments" => await ApplyPaymentAsync(def, rows, cache),
                "journalEntries" => await ApplyJournalAsync(def, rows, cache),
                "saleInvoices" => await ApplySaleInvoiceAsync(def, rows, cache),
                "purchaseInvoices" => await ApplyPurchaseInvoiceAsync(def, rows, cache),
                "saleReturns" => await ApplySaleReturnAsync(def, rows, cache),
                "purchaseReturns" => await ApplyPurchaseReturnAsync(def, rows, cache),
                "inventoryAdjustments" => await ApplyAdjustmentAsync(def, rows, cache),
                "stockTransfers" => await ApplyTransferAsync(def, rows, cache),
                _ => (false, "نوع المستند غير معروف")
            };
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return (false, "تعارض في البيانات أثناء الحفظ؛ تأكد من عدم تكرار الأرقام");
        }
        catch (InvalidOperationException ex)
        {
            _db.ChangeTracker.Clear();
            return (false, ex.Message);
        }
    }

    private static IReadOnlyDictionary<string, string> DocumentHeader(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows)
        => def.GroupColumn is null
            ? rows[0].Fields ?? new Dictionary<string, string>()
            : MergeHeaderCells(def, rows.Select(d => (IReadOnlyDictionary<string, string>)(d.Fields ?? new Dictionary<string, string>())));

    private async Task<(bool Ok, string? Error)> ApplyPaymentAsync(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows, ReferenceCache cache)
    {
        var header = DocumentHeader(def, rows);
        var payment = new Payment
        {
            Type = (PaymentType)EnumValue(def, header, "Type", (int)PaymentType.Receipt),
            Amount = CellDecimal(header, "Amount"),
            Method = (PaymentMethod)EnumValue(def, header, "Method", (int)PaymentMethod.Cash),
            PaymentDate = CellDate(header, "PaymentDate", DateTime.Today),
            ExchangeRate = CellOptionalDecimal(header, "ExchangeRate"),
            ReferenceNumber = OptNull(header, "ReferenceNumber"),
            Notes = OptNull(header, "Notes")
        };
        var currencyRaw = header.GetValueOrDefault("CurrencyCode", "").Trim();
        if (currencyRaw.Length > 0 && cache.CurrenciesByCode.TryGetValue(NormKey(currencyRaw), out var currency))
            payment.CurrencyId = currency.Id;

        var customerRaw = header.GetValueOrDefault("PartyName", "").Trim();
        if (payment.Type == PaymentType.Receipt
            && cache.CustomersByName.TryGetValue(NormKey(customerRaw), out var customer))
            payment.CustomerId = customer.Id;
        if (payment.Type == PaymentType.Disbursement
            && cache.SuppliersByName.TryGetValue(NormKey(customerRaw), out var supplier))
            payment.SupplierId = supplier.Id;

        var result = await _payments.CreatePaymentAsync(payment, null, null);
        return (result.Success, result.Error);
    }

    private async Task<(bool Ok, string? Error)> ApplyJournalAsync(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows, ReferenceCache cache)
    {
        var header = DocumentHeader(def, rows);
        var lines = new List<JournalLine>();
        foreach (var row in rows)
        {
            var cells = row.Fields ?? new Dictionary<string, string>();
            var code = cells.GetValueOrDefault("AccountCode", "").Trim();
            if (code.Length == 0 || !cache.AccountsByCode.TryGetValue(NormKey(code), out var account)) continue;
            lines.Add(new JournalLine(account.Code, CellDecimal(cells, "Debit"), CellDecimal(cells, "Credit"),
                OptNull(cells, "EntryDescription")));
        }
        if (lines.Count == 0) return (false, "القيد لا يحتوي على أسطر صالحة");
        await _accounting.PostAsync(JournalSource.Import, 0, CellDate(header, "EntryDate", DateTime.Today),
            OptNull(header, "Description") ?? "قيد مستورد", lines.ToArray(), null);
        return (true, null);
    }

    private async Task<(bool Ok, string? Error)> ApplySaleInvoiceAsync(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows, ReferenceCache cache)
    {
        var header = DocumentHeader(def, rows);
        var customerRaw = header.GetValueOrDefault("CustomerName", "").Trim();
        if (!cache.CustomersByName.TryGetValue(NormKey(customerRaw), out var customer))
            return (false, "العميل غير موجود");

        var invoice = new SaleInvoice
        {
            CustomerId = customer.Id,
            InvoiceDate = CellDate(header, "InvoiceDate", DateTime.Today),
            PaymentTerms = (InvoicePaymentTerms)EnumValue(def, header, "PaymentTerms", (int)InvoicePaymentTerms.OnReceipt),
            Discount = CellDecimal(header, "Discount"),
            Discount2 = CellDecimal(header, "Discount2"),
            Discount3 = CellDecimal(header, "Discount3"),
            Tax = CellDecimal(header, "Tax"),
            Notes = OptNull(header, "Notes")
        };
        var currencyRaw = header.GetValueOrDefault("CurrencyCode", "").Trim();
        if (currencyRaw.Length > 0 && cache.CurrenciesByCode.TryGetValue(NormKey(currencyRaw), out var currency))
        {
            invoice.CurrencyId = currency.Id;
            invoice.ExchangeRate = CellOptionalDecimal(header, "ExchangeRate") ?? 1m;
        }

        var items = new List<SaleInvoiceItem>();
        foreach (var row in rows)
        {
            var cells = row.Fields ?? new Dictionary<string, string>();
            var item = ResolveItem(cells, cache);
            if (item is null) continue;
            items.Add(new SaleInvoiceItem
            {
                ItemId = item.Id,
                Quantity = CellDecimal(cells, "Quantity"),
                Count = CellDecimal(cells, "Count"),
                UnitPrice = CellDecimal(cells, "UnitPrice"),
                Discount = CellDecimal(cells, "ItemDiscount")
            });
        }
        if (items.Count == 0) return (false, "الفاتورة لا تحتوي على أصناف صالحة");
        return await _inventory.CreateSaleAsync(invoice, items, null, null);
    }

    private async Task<(bool Ok, string? Error)> ApplyPurchaseInvoiceAsync(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows, ReferenceCache cache)
    {
        var header = DocumentHeader(def, rows);
        var supplierRaw = header.GetValueOrDefault("SupplierName", "").Trim();
        if (!cache.SuppliersByName.TryGetValue(NormKey(supplierRaw), out var supplier))
            return (false, "المورد غير موجود");

        var invoice = new PurchaseInvoice
        {
            SupplierId = supplier.Id,
            InvoiceDate = CellDate(header, "InvoiceDate", DateTime.Today),
            PaymentTerms = (InvoicePaymentTerms)EnumValue(def, header, "PaymentTerms", (int)InvoicePaymentTerms.OnReceipt),
            Discount = CellDecimal(header, "Discount"),
            Discount2 = CellDecimal(header, "Discount2"),
            Discount3 = CellDecimal(header, "Discount3"),
            Tax = CellDecimal(header, "Tax"),
            Notes = OptNull(header, "Notes")
        };
        var currencyRaw = header.GetValueOrDefault("CurrencyCode", "").Trim();
        if (currencyRaw.Length > 0 && cache.CurrenciesByCode.TryGetValue(NormKey(currencyRaw), out var currency))
        {
            invoice.CurrencyId = currency.Id;
            invoice.ExchangeRate = CellOptionalDecimal(header, "ExchangeRate") ?? 1m;
        }

        var items = new List<PurchaseInvoiceItem>();
        foreach (var row in rows)
        {
            var cells = row.Fields ?? new Dictionary<string, string>();
            var item = ResolveItem(cells, cache);
            if (item is null) continue;
            items.Add(new PurchaseInvoiceItem
            {
                ItemId = item.Id,
                Quantity = CellDecimal(cells, "Quantity"),
                Count = CellDecimal(cells, "Count"),
                UnitPrice = CellDecimal(cells, "UnitPrice"),
                Discount = CellDecimal(cells, "ItemDiscount")
            });
        }
        if (items.Count == 0) return (false, "الفاتورة لا تحتوي على أصناف صالحة");
        return await _inventory.CreatePurchaseAsync(invoice, items, null, null);
    }

    private async Task<(bool Ok, string? Error)> ApplySaleReturnAsync(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows, ReferenceCache cache)
    {
        var header = DocumentHeader(def, rows);
        var customerRaw = header.GetValueOrDefault("CustomerName", "").Trim();
        if (!cache.CustomersByName.TryGetValue(NormKey(customerRaw), out var customer))
            return (false, "العميل غير موجود");

        var saleReturn = new SaleReturn
        {
            CustomerId = customer.Id,
            ReturnDate = CellDate(header, "ReturnDate", DateTime.Today),
            Reason = OptNull(header, "Reason")
        };
        var invoiceRaw = header.GetValueOrDefault("SaleInvoiceNumber", "").Trim();
        if (invoiceRaw.Length > 0 && cache.SaleInvoicesByNumber.TryGetValue(NormKey(invoiceRaw), out var saleInvoice))
            saleReturn.SaleInvoiceId = saleInvoice.Id;
        var currencyRaw = header.GetValueOrDefault("CurrencyCode", "").Trim();
        if (currencyRaw.Length > 0 && cache.CurrenciesByCode.TryGetValue(NormKey(currencyRaw), out var currency))
        {
            saleReturn.CurrencyId = currency.Id;
            saleReturn.ExchangeRate = CellOptionalDecimal(header, "ExchangeRate") ?? 1m;
        }

        var items = new List<SaleReturnItem>();
        foreach (var row in rows)
        {
            var cells = row.Fields ?? new Dictionary<string, string>();
            var item = ResolveItem(cells, cache);
            if (item is null) continue;
            items.Add(new SaleReturnItem
            {
                ItemId = item.Id,
                Quantity = CellDecimal(cells, "Quantity"),
                Count = CellDecimal(cells, "Count"),
                UnitPrice = CellDecimal(cells, "UnitPrice")
            });
        }
        if (items.Count == 0) return (false, "المرتجع لا يحتوي على أصناف صالحة");
        return await _inventory.CreateSaleReturnAsync(saleReturn, items, null);
    }

    private async Task<(bool Ok, string? Error)> ApplyPurchaseReturnAsync(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows, ReferenceCache cache)
    {
        var header = DocumentHeader(def, rows);
        var supplierRaw = header.GetValueOrDefault("SupplierName", "").Trim();
        if (!cache.SuppliersByName.TryGetValue(NormKey(supplierRaw), out var supplier))
            return (false, "المورد غير موجود");

        var purchaseReturn = new PurchaseReturn
        {
            SupplierId = supplier.Id,
            ReturnDate = CellDate(header, "ReturnDate", DateTime.Today),
            Reason = OptNull(header, "Reason")
        };
        var invoiceRaw = header.GetValueOrDefault("PurchaseInvoiceNumber", "").Trim();
        if (invoiceRaw.Length > 0 && cache.PurchaseInvoicesByNumber.TryGetValue(NormKey(invoiceRaw), out var purchaseInvoice))
            purchaseReturn.PurchaseInvoiceId = purchaseInvoice.Id;
        var currencyRaw = header.GetValueOrDefault("CurrencyCode", "").Trim();
        if (currencyRaw.Length > 0 && cache.CurrenciesByCode.TryGetValue(NormKey(currencyRaw), out var currency))
        {
            purchaseReturn.CurrencyId = currency.Id;
            purchaseReturn.ExchangeRate = CellOptionalDecimal(header, "ExchangeRate") ?? 1m;
        }

        var items = new List<PurchaseReturnItem>();
        foreach (var row in rows)
        {
            var cells = row.Fields ?? new Dictionary<string, string>();
            var item = ResolveItem(cells, cache);
            if (item is null) continue;
            items.Add(new PurchaseReturnItem
            {
                ItemId = item.Id,
                Quantity = CellDecimal(cells, "Quantity"),
                Count = CellDecimal(cells, "Count"),
                UnitPrice = CellDecimal(cells, "UnitPrice")
            });
        }
        if (items.Count == 0) return (false, "المرتجع لا يحتوي على أصناف صالحة");
        return await _inventory.CreatePurchaseReturnAsync(purchaseReturn, items, null);
    }

    private async Task<(bool Ok, string? Error)> ApplyAdjustmentAsync(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows, ReferenceCache cache)
    {
        var header = DocumentHeader(def, rows);
        var item = ResolveItem(header, cache);
        if (item is null) return (false, "الصنف غير موجود");
        var adjustment = new InventoryAdjustment
        {
            ItemId = item.Id,
            NewCount = CellDecimal(header, "NewCount"),
            NewQuantity = CellDecimal(header, "NewQuantity"),
            AdjustmentDate = CellDate(header, "AdjustmentDate", DateTime.Today),
            Reason = OptNull(header, "Reason")
        };
        return await _inventory.CreateAdjustmentAsync(adjustment, null);
    }

    private async Task<(bool Ok, string? Error)> ApplyTransferAsync(
        ImportEntityDefinition def, IReadOnlyList<ImportRowPayload> rows, ReferenceCache cache)
    {
        var header = DocumentHeader(def, rows);
        var srcRaw = header.GetValueOrDefault("SourceWarehouseCode", "").Trim();
        var tgtRaw = header.GetValueOrDefault("TargetWarehouseCode", "").Trim();
        var source = ResolveWarehouse(srcRaw, cache);
        if (source is null)
            return (false, "مخزن المصدر غير موجود");
        var target = ResolveWarehouse(tgtRaw, cache);
        if (target is null)
            return (false, "مخزن الهدف غير موجود");
        if (target == source)
            return (false, "لا يمكن التحويل من مستودع إلى نفسه");

        var transfer = new StockTransfer
        {
            SourceWarehouseId = source.Id,
            TargetWarehouseId = target.Id,
            TransferDate = CellDate(header, "TransferDate", DateTime.Today),
            Notes = OptNull(header, "Notes")
        };
        var items = new List<StockTransferItem>();
        foreach (var row in rows)
        {
            var cells = row.Fields ?? new Dictionary<string, string>();
            var item = ResolveItem(cells, cache);
            if (item is null) continue;
            items.Add(new StockTransferItem
            {
                ItemId = item.Id,
                Quantity = CellDecimal(cells, "Quantity"),
                Count = CellDecimal(cells, "Count"),
                UnitCost = CellDecimal(cells, "UnitCost"),
                DateReceived = transfer.TransferDate
            });
        }
        if (items.Count == 0) return (false, "التحويل لا يحتوي على أصناف صالحة");
        return await _inventory.CreateTransferAsync(transfer, items, null);
    }

    private static Item? ResolveItem(IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
    {
        var raw = cells.GetValueOrDefault("ItemName", "").Trim();
        if (raw.Length == 0) return null;
        var key = NormKey(raw);
        if (cache.ItemsByName.TryGetValue(key, out var byName)) return byName;
        if (cache.ItemsByBarcode.TryGetValue(key, out var byBarcode)) return byBarcode;
        if (cache.ItemsByCode.TryGetValue(key, out var byCode)) return byCode;
        return null;
    }

    private static Warehouse? ResolveWarehouse(string raw, ReferenceCache cache)
    {
        if (raw.Trim().Length == 0) return null;
        var key = NormKey(raw);
        if (cache.WarehousesByCode.TryGetValue(key, out var byCode)) return byCode;
        if (cache.WarehousesByName.TryGetValue(key, out var byName)) return byName;
        return null;
    }

    private static decimal? CellOptionalDecimal(IReadOnlyDictionary<string, string> cells, string key)
    {
        var raw = cells.GetValueOrDefault(key, "").Trim();
        if (raw.Length == 0) return null;
        return TryParseDecimal(raw, out var value) ? value : null;
    }

    private static decimal CellDecimal(IReadOnlyDictionary<string, string> cells, string key)
        => CellOptionalDecimal(cells, key) ?? 0m;

    private static DateTime CellDate(IReadOnlyDictionary<string, string> cells, string key, DateTime fallback)
    {
        var raw = cells.GetValueOrDefault(key, "").Trim();
        if (raw.Length == 0) return fallback;
        return TryParseDate(raw, out var value) ? value : fallback;
    }

    private static bool TryParseDate(string raw, out DateTime value)
    {
        var s = NormalizeDigits(raw.Trim());
        if (DateTime.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) return true;
        if (DateTime.TryParseExact(s, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) return true;
        if (DateTime.TryParseExact(s, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) return true;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static int? OptionalEnum(ImportEntityDefinition def, IReadOnlyDictionary<string, string> cells, string colKey)
    {
        var col = def.Columns.FirstOrDefault(c => c.Key == colKey);
        if (col is null || col.EnumMap is null) return null;
        var raw = cells.GetValueOrDefault(colKey, "").Trim();
        if (raw.Length == 0) return null;
        return TryParseEnum(raw, col, out var value) ? value : null;
    }

    private static List<string> ValidateDocument(
        ImportEntityDefinition def,
        IReadOnlyDictionary<string, string> header,
        IReadOnlyList<ImportRowPayload> rows,
        ReferenceCache cache)
    {
        var errors = new List<string>();
        switch (def.Key)
        {
            case "payments":
                var payType = OptionalEnum(def, header, "Type");
                var partyRaw = header.GetValueOrDefault("PartyName", "").Trim();
                if (payType == (int)PaymentType.Receipt)
                {
                    if (partyRaw.Length == 0) errors.Add("النوع «قبض» يتطلب تحديد العميل");
                    else if (!cache.CustomersByName.ContainsKey(NormKey(partyRaw))) errors.Add($"«{partyRaw}» ليس عميلاً موجودًا");
                }
                if (payType == (int)PaymentType.Disbursement)
                {
                    if (partyRaw.Length == 0) errors.Add("النوع «صرف» يتطلب تحديد المورد");
                    else if (!cache.SuppliersByName.ContainsKey(NormKey(partyRaw))) errors.Add($"«{partyRaw}» ليس موردًا موجودًا");
                }
                break;
            case "journalEntries":
                if (rows.Count == 0) errors.Add("القيد لا يحتوي على أسطر");
                foreach (var row in rows)
                {
                    var cells = row.Fields ?? new Dictionary<string, string>();
                    var debit = CellDecimal(cells, "Debit");
                    var credit = CellDecimal(cells, "Credit");
                    if ((debit > 0) == (credit > 0))
                        errors.Add("كل سطر في القيد يجب أن يكون مدينًا أو دائنًا وليس كلاهما");
                    if (debit < 0 || credit < 0)
                        errors.Add("لا يمكن أن يكون المبلغ سالبًا");
                }
                var totalDebit = rows.Sum(r => CellDecimal(r.Fields ?? new Dictionary<string, string>(), "Debit"));
                var totalCredit = rows.Sum(r => CellDecimal(r.Fields ?? new Dictionary<string, string>(), "Credit"));
                if (decimal.Round(totalDebit, 2) != decimal.Round(totalCredit, 2))
                    errors.Add("مجموع المدين لا يساوي مجموع الدائن في القيد");
                break;
            case "saleInvoices":
            case "purchaseInvoices":
                if (rows.Count == 0) errors.Add("الفاتورة لا تحتوي على أصناف");
                var seenItems = new HashSet<int>();
                foreach (var row in rows)
                {
                    var cells = row.Fields ?? new Dictionary<string, string>();
                    var item = ResolveItem(cells, cache);
                    if (item is not null && !seenItems.Add(item.Id))
                        errors.Add($"الصنف «{item.Name}» مكرر أكثر من مرة في الفاتورة");
                    if (CellDecimal(cells, "Quantity") < 0 || CellDecimal(cells, "Count") < 0)
                        errors.Add("الكمية والعدد لا يمكن أن يكونا سالبين");
                }
                if (InvoiceNet(header, rows) < 0)
                    errors.Add("الخصم أكبر من إجمالي الفاتورة؛ لا يمكن أن يكون الصافي سالباً");
                break;
            case "saleReturns":
            case "purchaseReturns":
                if (rows.Count == 0) errors.Add("المرتجع لا يحتوي على أصناف");
                foreach (var row in rows)
                {
                    var cells = row.Fields ?? new Dictionary<string, string>();
                    if (ResolveItem(cells, cache) is null)
                        errors.Add("لم يتم التعرف على صنف في سطر المرتجع");
                    var qty = CellDecimal(cells, "Quantity");
                    var count = CellDecimal(cells, "Count");
                    if (qty < 0 || count < 0 || (qty == 0 && count == 0))
                        errors.Add("حدد عددًا أو كمية موجبة لكل صنف في المرتجع");
                }
                break;
            case "inventoryAdjustments":
                if (ResolveItem(header, cache) is null)
                    errors.Add("حدد صنفًا للتسوية");
                if (CellDecimal(header, "NewCount") < 0 || CellDecimal(header, "NewQuantity") < 0)
                    errors.Add("العدد والكمية الجديدة لا يمكن أن يكونا سالبين");
                if (CellDecimal(header, "NewCount") == 0 && CellDecimal(header, "NewQuantity") == 0)
                    errors.Add("حدد عددًا أو كمية جديدة للتسوية");
                break;
            case "stockTransfers":
                var srcRaw = header.GetValueOrDefault("SourceWarehouseCode", "").Trim();
                var tgtRaw = header.GetValueOrDefault("TargetWarehouseCode", "").Trim();
                if (srcRaw.Length > 0 && ResolveWarehouse(srcRaw, cache) is null)
                    errors.Add($"«{srcRaw}» غير موجودة ضمن المخازن");
                if (tgtRaw.Length > 0 && ResolveWarehouse(tgtRaw, cache) is null)
                    errors.Add($"«{tgtRaw}» غير موجودة ضمن المخازن");
                if (srcRaw.Length > 0 && tgtRaw.Length > 0 && ResolveWarehouse(srcRaw, cache) is not null && ResolveWarehouse(tgtRaw, cache) == ResolveWarehouse(srcRaw, cache))
                    errors.Add("لا يمكن التحويل من مستودع إلى نفسه");
                if (rows.Count == 0) errors.Add("التحويل لا يحتوي على أصناف");
                foreach (var row in rows)
                {
                    var cells = row.Fields ?? new Dictionary<string, string>();
                    if (ResolveItem(cells, cache) is null)
                        errors.Add("لم يتم التعرف على صنف في سطر التحويل");
                    var qty = CellDecimal(cells, "Quantity");
                    var count = CellDecimal(cells, "Count");
                    if (qty < 0 || count < 0 || (qty == 0 && count == 0))
                        errors.Add("حدد عددًا أو كمية موجبة لكل صنف في التحويل");
                }
                break;
        }
        return errors;
    }

    private static decimal InvoiceNet(
        IReadOnlyDictionary<string, string> header,
        IReadOnlyList<ImportRowPayload> rows)
    {
        decimal total = 0m;
        foreach (var row in rows)
        {
            var cells = row.Fields ?? new Dictionary<string, string>();
            var qty = CellDecimal(cells, "Quantity");
            var count = CellDecimal(cells, "Count");
            total += (qty > 0 ? qty : count) * CellDecimal(cells, "UnitPrice") - CellDecimal(cells, "ItemDiscount");
        }
        return total - CellDecimal(header, "Discount") - CellDecimal(header, "Discount2")
            - CellDecimal(header, "Discount3") + CellDecimal(header, "Tax");
    }

    private static List<string> DisplayValues(List<(ImportColumnDefinition Col, int Index)> matched, List<string> values)
    {
        var result = new List<string>(matched.Count);
        foreach (var (_, i) in matched)
            result.Add(i < values.Count ? values[i] : "");
        return result;
    }

    private static string? SelfIdentityColumn(string entityKey) => entityKey switch
    {
        "units" => "Name",
        "glAccounts" => "Code",
        _ => null
    };

    private static ImportPreviewViewModel FatalVm(string message) => new() { FatalError = message };

    private static List<string> KeyCandidates(ImportEntityDefinition def, IReadOnlyDictionary<string, string> cells)
    {
        var keys = new List<string>(2);
        var primary = cells.GetValueOrDefault(def.MatchPrimaryColumn, "").Trim();
        if (primary.Length > 0) keys.Add(NormKey(primary));
        if (def.MatchFallbackColumn is not null)
        {
            var fallback = cells.GetValueOrDefault(def.MatchFallbackColumn, "").Trim();
            if (fallback.Length > 0) keys.Add(NormKey(fallback));
        }
        return keys;
    }

    private static List<string> ValidateRow(
        ImportEntityDefinition def,
        IReadOnlyDictionary<string, string> cells,
        ReferenceCache db,
        IReadOnlySet<string>? seenSelf,
        bool headerOnly = false,
        bool lineOnly = false)
    {
        var errors = new List<string>();
        foreach (var col in def.Columns)
        {
            if (headerOnly && col.IsLineOnly) continue;
            if (lineOnly && !col.IsLineOnly) continue;
            var raw = cells.GetValueOrDefault(col.Key, "").Trim();
            if (raw.Length == 0)
            {
                if (col.IsRequired) errors.Add($"الحقل «{col.HeaderAr}» مطلوب");
                continue;
            }

            switch (col.Type)
            {
                case ImportValueType.Text:
                    if (col.MaxLength.HasValue && raw.Length > col.MaxLength.Value)
                        errors.Add($"«{col.HeaderAr}» تتجاوز الحد الأقصى ({col.MaxLength}) حرفًا");
                    break;
                case ImportValueType.Integer:
                    if (!TryParseInt(raw, out _))
                        errors.Add($"«{raw}» ليست عددًا صحيحًا صالحًا");
                    else if (IsOutOfRange(col, raw))
                        errors.Add(OutOfRangeMessage(col));
                    break;
                case ImportValueType.Decimal:
                    if (!TryParseDecimal(raw, out _))
                        errors.Add($"«{raw}» ليست رقمًا صالحًا");
                    else if (IsOutOfRange(col, raw))
                        errors.Add(OutOfRangeMessage(col));
                    break;
                case ImportValueType.Bool:
                    if (!TryParseBool(raw, out _))
                        errors.Add($"«{raw}» غير صالحة؛ القيم المقبولة: نعم/لا أو 1/0 أو نشط/معطل");
                    break;
                case ImportValueType.Date:
                    if (!TryParseDate(raw, out _))
                        errors.Add($"التاريخ غير صالح: «{raw}»؛ الصيغ المقبولة: dd/MM/yyyy أو yyyy/MM/dd أو dd-MM-yyyy أو yyyy-MM-dd");
                    break;
                case ImportValueType.Enum:
                    if (!TryParseEnum(raw, col, out _))
                        errors.Add($"«{raw}» غير صالحة؛ القيم المقبولة: {EnumLabels(col)}");
                    break;
                case ImportValueType.Lookup:
                    var selfKey = NormKey(raw);
                    var known = db.LookupHas(col.LookupKey!, col.LookupUseCode, selfKey);
                    if (col.LookupKey == def.Key)
                    {
                        if (!known && (seenSelf is null || !seenSelf.Contains(selfKey)))
                            errors.Add($"«{raw}» غير موجودة في قاعدة البيانات أو في الصفوف السابقة من الملف");
                    }
                    else if (!known)
                    {
                        errors.Add($"«{raw}» غير موجودة ضمن {LookupPlural(col.LookupKey!)}");
                    }
                    break;
            }
        }
        return errors;
    }

    private static string LookupPlural(string lookupKey) => lookupKey switch
    {
        "units" => "الوحدات",
        "itemTypes" => "أنواع الأصناف",
        "categories" => "التصنيفات",
        "currencies" => "العملات",
        "glAccounts" => "الحسابات",
        "items" => "الأصناف",
        "suppliers" => "الموردون",
        "customers" => "العملاء",
        "branches" => "الفروع",
        "warehouses" => "المخازن",
        "saleInvoices" => "فواتير البيع",
        "purchaseInvoices" => "فواتير الشراء",
        _ => "السجلات"
    };

    private static bool IsOutOfRange(ImportColumnDefinition col, string raw)
    {
        if (col.MinInclusive is null && col.MaxInclusive is null) return false;
        if (col.Type == ImportValueType.Integer)
        {
            if (!int.TryParse(StripNumber(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return false;
            return col.MinInclusive.HasValue && n < col.MinInclusive.Value
                || col.MaxInclusive.HasValue && n > col.MaxInclusive.Value;
        }
        if (!decimal.TryParse(NormalizeNumber(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return false;
        return col.MinInclusive.HasValue && d < col.MinInclusive.Value
            || col.MaxInclusive.HasValue && d > col.MaxInclusive.Value;
    }

    private static string OutOfRangeMessage(ImportColumnDefinition col)
    {
        var min = col.MinInclusive;
        var max = col.MaxInclusive;
        if (min.HasValue && max.HasValue)
            return $"«{col.HeaderAr}» يجب أن تكون بين {min} و{max}";
        if (min.HasValue)
            return $"«{col.HeaderAr}» يجب ألا تقل عن {min}";
        return $"«{col.HeaderAr}» يجب ألا تزيد عن {max}";
    }

    private (int Created, int Updated) ApplySupplier(IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
    {
        var name = cells.GetValueOrDefault("Name", "").Trim();
        var codeRaw = cells.GetValueOrDefault("Code", "").Trim();
        string? code = codeRaw.Length == 0 ? null : codeRaw;

        Supplier? entity = null;
        if (code is not null && cache.SuppliersByCode.TryGetValue(NormKey(code), out var byCode)) entity = byCode;
        else if (cache.SuppliersByName.TryGetValue(NormKey(name), out var byName)) entity = byName;

        var isNew = entity is null;
        entity ??= new Supplier();
        if (isNew) _db.Suppliers.Add(entity);

        entity.Name = name;
        entity.Code = code;
        entity.Address = OptNull(cells, "Address");
        entity.Phone = OptNull(cells, "Phone");
        entity.Email = OptNull(cells, "Email");
        entity.TaxNumber = OptNull(cells, "TaxNumber");
        if (TryCellDecimal(cells, "OpeningBalance", out var openingBalance)) entity.OpeningBalance = openingBalance;
        else if (isNew) entity.OpeningBalance = 0m;
        entity.Notes = OptNull(cells, "Notes");

        var curRaw = cells.GetValueOrDefault("CurrencyCode", "").Trim();
        if (curRaw.Length == 0) entity.CurrencyId = null;
        else if (cache.CurrenciesByCode.TryGetValue(NormKey(curRaw), out var currency)) entity.CurrencyId = currency.Id;

        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        if (code is not null) cache.SuppliersByCode[NormKey(code)] = entity;
        cache.SuppliersByName[NormKey(name)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private (int Created, int Updated) ApplyCustomer(IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
    {
        var name = cells.GetValueOrDefault("Name", "").Trim();
        var codeRaw = cells.GetValueOrDefault("Code", "").Trim();
        string? code = codeRaw.Length == 0 ? null : codeRaw;

        Customer? entity = null;
        if (code is not null && cache.CustomersByCode.TryGetValue(NormKey(code), out var byCode)) entity = byCode;
        else if (cache.CustomersByName.TryGetValue(NormKey(name), out var byName)) entity = byName;

        var isNew = entity is null;
        entity ??= new Customer();
        if (isNew) _db.Customers.Add(entity);

        entity.Name = name;
        entity.Code = code;
        entity.Address = OptNull(cells, "Address");
        entity.Phone = OptNull(cells, "Phone");
        entity.Email = OptNull(cells, "Email");
        entity.TaxNumber = OptNull(cells, "TaxNumber");
        if (TryCellDecimal(cells, "OpeningBalance", out var openingBalance)) entity.OpeningBalance = openingBalance;
        else if (isNew) entity.OpeningBalance = 0m;
        entity.Notes = OptNull(cells, "Notes");

        var curRaw = cells.GetValueOrDefault("CurrencyCode", "").Trim();
        if (curRaw.Length == 0) entity.CurrencyId = null;
        else if (cache.CurrenciesByCode.TryGetValue(NormKey(curRaw), out var currency)) entity.CurrencyId = currency.Id;

        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        if (code is not null) cache.CustomersByCode[NormKey(code)] = entity;
        cache.CustomersByName[NormKey(name)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private (int Created, int Updated) ApplyItem(IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
    {
        var name = cells.GetValueOrDefault("Name", "").Trim();
        var code = OptNull(cells, "Code");
        var barcodeRaw = cells.GetValueOrDefault("Barcode", "").Trim();
        string? barcode = barcodeRaw.Length == 0 ? null : barcodeRaw;

        Item? entity = null;
        if (barcode is not null && cache.ItemsByBarcode.TryGetValue(NormKey(barcode), out var byBarcode)) entity = byBarcode;
        else if (cache.ItemsByName.TryGetValue(NormKey(name), out var byName)) entity = byName;

        var isNew = entity is null;
        entity ??= new Item();
        if (isNew) _db.Items.Add(entity);

        entity.Name = name;
        entity.Code = code;
        entity.Barcode = barcode;

        var typeRaw = cells.GetValueOrDefault("ItemTypeName", "").Trim();
        if (cache.ItemTypesByName.TryGetValue(NormKey(typeRaw), out var itemType)) entity.ItemTypeId = itemType.Id;
        var catRaw = cells.GetValueOrDefault("CategoryName", "").Trim();
        if (cache.CategoriesByName.TryGetValue(NormKey(catRaw), out var category)) entity.CategoryId = category.Id;

        var countUnitRaw = cells.GetValueOrDefault("CountUnitName", "").Trim();
        entity.CountUnitId = countUnitRaw.Length == 0
            ? null
            : cache.UnitsByName.TryGetValue(NormKey(countUnitRaw), out var countUnit) ? countUnit.Id : null;

        var qtyUnitRaw = cells.GetValueOrDefault("QuantityUnitName", "").Trim();
        entity.QuantityUnitId = qtyUnitRaw.Length == 0
            ? null
            : cache.UnitsByName.TryGetValue(NormKey(qtyUnitRaw), out var qtyUnit) ? qtyUnit.Id : null;

        if (TryCellDecimal(cells, "PurchasePrice", out var purchasePrice)) entity.PurchasePrice = purchasePrice;
        else if (isNew) entity.PurchasePrice = 0m;
        if (TryCellDecimal(cells, "SalePrice", out var salePrice)) entity.SalePrice = salePrice;
        else if (isNew) entity.SalePrice = 0m;
        if (TryCellDecimal(cells, "MinCount", out var minCount)) entity.MinCount = minCount;
        else if (isNew) entity.MinCount = 0m;
        if (TryCellDecimal(cells, "MinQuantity", out var minQuantity)) entity.MinQuantity = minQuantity;
        else if (isNew) entity.MinQuantity = 0m;
        if (isNew)
        {
            entity.CurrentCount = TryCellDecimal(cells, "CurrentCount", out var cc) ? cc : 0m;
            entity.CurrentQuantity = TryCellDecimal(cells, "CurrentQuantity", out var cq) ? cq : 0m;
        }
        entity.Notes = OptNull(cells, "Notes");

        var sellable = OptionalBool(cells, "IsSellable");
        if (sellable.HasValue) entity.IsSellable = sellable.Value;
        else if (isNew) entity.IsSellable = true;

        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        if (barcode is not null) cache.ItemsByBarcode[NormKey(barcode)] = entity;
        cache.ItemsByName[NormKey(name)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private (int Created, int Updated) ApplyCategory(IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
    {
        var name = cells.GetValueOrDefault("Name", "").Trim();
        cache.CategoriesByName.TryGetValue(NormKey(name), out var entity);
        var isNew = entity is null;
        entity ??= new ItemCategory();
        if (isNew) _db.ItemCategories.Add(entity);

        entity.Name = name;
        entity.Notes = OptNull(cells, "Notes");
        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        cache.CategoriesByName[NormKey(name)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private (int Created, int Updated) ApplyItemType(IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
    {
        var name = cells.GetValueOrDefault("Name", "").Trim();
        cache.ItemTypesByName.TryGetValue(NormKey(name), out var entity);
        var isNew = entity is null;
        entity ??= new ItemType();
        if (isNew) _db.ItemTypes.Add(entity);

        entity.Name = name;
        entity.Notes = OptNull(cells, "Notes");
        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        cache.ItemTypesByName[NormKey(name)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private (int Created, int Updated) ApplyUnit(
        IReadOnlyDictionary<string, string> cells, ReferenceCache cache, List<(Unit Child, string ParentName)> parentLinks)
    {
        var name = cells.GetValueOrDefault("Name", "").Trim();
        cache.UnitsByName.TryGetValue(NormKey(name), out var entity);
        var isNew = entity is null;
        entity ??= new Unit();
        if (isNew) _db.Units.Add(entity);

        entity.Name = name;
        entity.ShortName = OptNull(cells, "ShortName");

        var subRaw = cells.GetValueOrDefault("SubUnits", "").Trim();
        if (subRaw.Length == 0)
        {
            if (isNew) entity.SubUnits = null;
        }
        else if (int.TryParse(StripNumber(subRaw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var subVal))
        {
            entity.SubUnits = subVal;
        }

        var parentRaw = cells.GetValueOrDefault("ParentUnitName", "").Trim();
        if (parentRaw.Length == 0)
        {
            entity.ParentUnitId = null;
        }
        else if (cache.UnitsByName.TryGetValue(NormKey(parentRaw), out var parent))
        {
            if (parent != entity)
                entity.ParentUnit = parent;
            else
                entity.ParentUnitId = null;
        }
        else
        {
            parentLinks.Add((entity, parentRaw));
        }

        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        cache.UnitsByName[NormKey(name)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private (int Created, int Updated) ApplyCurrency(
        IReadOnlyDictionary<string, string> cells, ReferenceCache cache, ref string? pendingBaseCode)
    {
        var codeRaw = cells.GetValueOrDefault("Code", "").Trim();
        cache.CurrenciesByCode.TryGetValue(NormKey(codeRaw), out var entity);
        var isNew = entity is null;
        entity ??= new Currency();
        if (isNew) _db.Currencies.Add(entity);

        entity.Code = codeRaw;
        entity.Name = cells.GetValueOrDefault("Name", "").Trim();
        entity.Symbol = OptNull(cells, "Symbol");
        if (TryCellDecimal(cells, "ExchangeRate", out var exchangeRate)) entity.ExchangeRate = exchangeRate;
        else if (isNew) entity.ExchangeRate = 1m;

        var isBase = OptionalBool(cells, "IsBase");
        if (isBase.HasValue)
        {
            entity.IsBase = isBase.Value;
            if (isBase.Value) pendingBaseCode = NormKey(codeRaw);
        }
        else if (isNew)
        {
            entity.IsBase = false;
        }

        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        cache.CurrenciesByCode[NormKey(codeRaw)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private (int Created, int Updated) ApplyBranch(IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
    {
        var codeRaw = cells.GetValueOrDefault("Code", "").Trim();
        cache.BranchesByCode.TryGetValue(NormKey(codeRaw), out var entity);
        var isNew = entity is null;
        entity ??= new Branch();
        if (isNew) _db.Branches.Add(entity);

        entity.Code = codeRaw;
        entity.Name = cells.GetValueOrDefault("Name", "").Trim();
        entity.Address = OptNull(cells, "Address");
        entity.Phone = OptNull(cells, "Phone");

        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        cache.BranchesByCode[NormKey(codeRaw)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private (int Created, int Updated) ApplyWarehouse(IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
    {
        var codeRaw = cells.GetValueOrDefault("Code", "").Trim();
        cache.WarehousesByCode.TryGetValue(NormKey(codeRaw), out var entity);
        var isNew = entity is null;
        entity ??= new Warehouse();
        if (isNew) _db.Warehouses.Add(entity);

        entity.Code = codeRaw;
        entity.Name = cells.GetValueOrDefault("Name", "").Trim();

        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        cache.WarehousesByCode[NormKey(codeRaw)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private (int Created, int Updated) ApplyAccount(ImportEntityDefinition def, IReadOnlyDictionary<string, string> cells, ReferenceCache cache)
    {
        var codeRaw = cells.GetValueOrDefault("Code", "").Trim();
        cache.AccountsByCode.TryGetValue(NormKey(codeRaw), out var entity);
        var isNew = entity is null;
        entity ??= new GLAccount();
        if (isNew) _db.GLAccounts.Add(entity);

        entity.Code = codeRaw;
        entity.Name = cells.GetValueOrDefault("Name", "").Trim();
        entity.Type = (GLAccountType)EnumValue(def, cells, "Type", isNew ? (int)GLAccountType.Asset : (int)entity.Type);
        entity.NormalBalance = (NormalBalance)EnumValue(def, cells, "NormalBalance", isNew ? (int)NormalBalance.Debit : (int)entity.NormalBalance);

        var parentRaw = cells.GetValueOrDefault("ParentCode", "").Trim();
        if (parentRaw.Length == 0)
        {
            entity.ParentAccountId = null;
        }
        else if (cache.AccountsByCode.TryGetValue(NormKey(parentRaw), out var parent))
        {
            if (parent != entity)
                entity.ParentAccount = parent;
            else
                entity.ParentAccountId = null;
        }

        var active = OptionalBool(cells, "IsActive");
        if (active.HasValue) entity.IsActive = active.Value;
        else if (isNew) entity.IsActive = true;

        cache.AccountsByCode[NormKey(codeRaw)] = entity;
        return isNew ? (1, 0) : (0, 1);
    }

    private async Task<ReferenceCache> BuildReferenceCacheAsync()
    {
        var cache = new ReferenceCache();

        foreach (var u in await _db.Units.ToListAsync())
            if (!string.IsNullOrWhiteSpace(u.Name)) cache.UnitsByName[NormKey(u.Name)] = u;
        foreach (var cat in await _db.ItemCategories.ToListAsync())
            if (!string.IsNullOrWhiteSpace(cat.Name)) cache.CategoriesByName[NormKey(cat.Name)] = cat;
        foreach (var t in await _db.ItemTypes.ToListAsync())
            if (!string.IsNullOrWhiteSpace(t.Name)) cache.ItemTypesByName[NormKey(t.Name)] = t;
        foreach (var cur in await _db.Currencies.ToListAsync())
            if (!string.IsNullOrWhiteSpace(cur.Code)) cache.CurrenciesByCode[NormKey(cur.Code)] = cur;
        foreach (var b in await _db.Branches.ToListAsync())
            if (!string.IsNullOrWhiteSpace(b.Code)) cache.BranchesByCode[NormKey(b.Code)] = b;
        foreach (var w in await _db.Warehouses.ToListAsync())
        {
            if (!string.IsNullOrWhiteSpace(w.Code)) cache.WarehousesByCode[NormKey(w.Code)] = w;
            if (!string.IsNullOrWhiteSpace(w.Name)) cache.WarehousesByName[NormKey(w.Name)] = w;
        }
        foreach (var a in await _db.GLAccounts.ToListAsync())
            if (!string.IsNullOrWhiteSpace(a.Code)) cache.AccountsByCode[NormKey(a.Code)] = a;
        foreach (var s in await _db.Suppliers.ToListAsync())
        {
            if (!string.IsNullOrWhiteSpace(s.Code)) cache.SuppliersByCode[NormKey(s.Code)] = s;
            if (!string.IsNullOrWhiteSpace(s.Name)) cache.SuppliersByName[NormKey(s.Name)] = s;
        }
        foreach (var cu in await _db.Customers.ToListAsync())
        {
            if (!string.IsNullOrWhiteSpace(cu.Code)) cache.CustomersByCode[NormKey(cu.Code)] = cu;
            if (!string.IsNullOrWhiteSpace(cu.Name)) cache.CustomersByName[NormKey(cu.Name)] = cu;
        }
        foreach (var i in await _db.Items.ToListAsync())
        {
            if (!string.IsNullOrWhiteSpace(i.Code)) cache.ItemsByCode[NormKey(i.Code)] = i;
            if (!string.IsNullOrWhiteSpace(i.Barcode)) cache.ItemsByBarcode[NormKey(i.Barcode)] = i;
            if (!string.IsNullOrWhiteSpace(i.Name)) cache.ItemsByName[NormKey(i.Name)] = i;
        }
        foreach (var si in await _db.SaleInvoices.AsNoTracking().ToListAsync())
            if (!string.IsNullOrWhiteSpace(si.InvoiceNumber)) cache.SaleInvoicesByNumber[NormKey(si.InvoiceNumber)] = si;
        foreach (var pi in await _db.PurchaseInvoices.AsNoTracking().ToListAsync())
            if (!string.IsNullOrWhiteSpace(pi.InvoiceNumber)) cache.PurchaseInvoicesByNumber[NormKey(pi.InvoiceNumber)] = pi;
        return cache;
    }

    private static int EnumValue(ImportEntityDefinition def, IReadOnlyDictionary<string, string> cells, string colKey, int defaultValue)
    {
        var col = def.Columns.FirstOrDefault(c => c.Key == colKey);
        if (col is null || col.EnumMap is null) return defaultValue;
        var raw = cells.GetValueOrDefault(colKey, "").Trim();
        if (raw.Length == 0) return defaultValue;
        return TryParseEnum(raw, col, out var v) ? v : defaultValue;
    }

    private static string? OptNull(IReadOnlyDictionary<string, string> cells, string key)
    {
        var value = cells.GetValueOrDefault(key, "").Trim();
        return value.Length == 0 ? null : value;
    }

    private static bool TryCellDecimal(IReadOnlyDictionary<string, string> cells, string key, out decimal value)
    {
        var raw = cells.GetValueOrDefault(key, "").Trim();
        if (raw.Length == 0) { value = 0m; return false; }
        return decimal.TryParse(NormalizeNumber(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool? OptionalBool(IReadOnlyDictionary<string, string> cells, string key)
    {
        var raw = cells.GetValueOrDefault(key, "").Trim();
        if (raw.Length == 0) return null;
        return TryParseBool(raw, out var value) ? value : null;
    }

    private static bool TryParseBool(string raw, out bool value)
    {
        switch (raw.ToUpperInvariant())
        {
            case "نعم":
            case "TRUE":
            case "1":
            case "صحيح":
            case "نشط":
            case "نشطة":
                value = true;
                return true;
            case "لا":
            case "FALSE":
            case "0":
            case "خطأ":
            case "معطل":
            case "معطلة":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static bool TryParseEnum(string raw, ImportColumnDefinition col, out int value)
    {
        value = 0;
        var map = BuildEnumMap(col);
        var normalized = NormalizeDigits(raw.Trim());
        if (normalized.Length == 0) return false;
        if (map.TryGetValue(normalized, out var v)) { value = v; return true; }
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && map.Values.Contains(n)) { value = n; return true; }
        return false;
    }

    private static Dictionary<string, int> BuildEnumMap(ImportColumnDefinition col)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (col.Aliases is not null)
        {
            foreach (var alias in col.Aliases)
            {
                var parts = alias.Split('=', 2);
                if (parts.Length == 2 && int.TryParse(NormalizeDigits(parts[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var aliasValue))
                {
                    foreach (var label in parts[0].Split('|', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = label.Trim();
                        if (trimmed.Length > 0) result[trimmed] = aliasValue;
                    }
                    continue;
                }
                var bare = parts[0].Trim();
                if (bare.Length > 0) result[bare] = result.Count + 1;
            }
        }
        foreach (var pair in (col.EnumMap ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var labels = parts[0].Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (labels.Count == 0) continue;
            if (parts.Length == 2 && int.TryParse(NormalizeDigits(parts[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                foreach (var label in labels) result[label] = value;
            }
            else
            {
                foreach (var label in labels) result[label] = result.Count + 1;
            }
        }
        return result;
    }

    private static string EnumLabels(ImportColumnDefinition col)
    {
        var first = new Dictionary<int, string>();
        foreach (var kvp in BuildEnumMap(col))
            if (!first.ContainsKey(kvp.Value)) first[kvp.Value] = kvp.Key;
        return string.Join("، ", first.OrderBy(kv => kv.Key).Select(kv => kv.Value));
    }

    private static bool TryParseInt(string raw, out int value) =>
        int.TryParse(StripNumber(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDecimal(string raw, out decimal value) =>
        decimal.TryParse(NormalizeNumber(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static string StripNumber(string raw) =>
        NormalizeDigits(raw).Replace(" ", "").Replace(",", "");

    private static string NormalizeNumber(string raw)
    {
        var s = NormalizeDigits(raw).Replace(" ", "");
        return s.Replace(",", "");
    }

    private static string NormalizeDigits(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is >= '٠' and <= '٩')
            {
                sb.Append((char)('0' + ch - '٠'));
                continue;
            }
            if (ch is >= '۰' and <= '۹')
            {
                sb.Append((char)('0' + ch - '۰'));
                continue;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string NormKey(string value) => value.Trim().ToUpperInvariant();

    private static string SerializeEnvelope(ImportPayloadEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    private static ImportPayloadEnvelope? DeserializeEnvelope(string payload)
    {
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(payload);
            return JsonSerializer.Deserialize<ImportPayloadEnvelope>(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return null;
        }
    }

    private static List<List<string>> ReadXlsx(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow == 0) return [];

        var lastCol = ws.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
        if (lastCol == 0) return [];

        var rows = new List<List<string>>();
        for (int r = 1; r <= lastRow; r++)
        {
            var row = new List<string>(lastCol);
            for (int c = 1; c <= lastCol; c++)
                row.Add(CellText(ws.Cell(r, c)));
            rows.Add(row);
        }
        return rows;
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        var value = cell.Value;
        if (value.IsNumber) return value.GetNumber().ToString(CultureInfo.InvariantCulture);
        if (value.IsBoolean) return value.GetBoolean() ? "نعم" : "لا";
        if (value.IsDateTime) return value.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return value.GetText().Trim();
    }

    private static List<List<string>> ReadCsv(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        if (text.StartsWith('\uFEFF')) text = text[1..];
        return ParseCsv(text);
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c is '\r' or '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
                else
                {
                    field.Append(c);
                }
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    private static List<string> GuideLines(ImportEntityDefinition def)
    {
        var lines = new List<string>
        {
            $"إرشادات استيراد {def.NameAr}",
            "",
            IsDocumentEntity(def.Key)
                ? def.MatchNote + " — تُنشأ المستندات من جديد عند كل استيراد ولا تُحدَّث."
                : def.MatchNote + " — تُنشأ السجلات غير الموجودة ويُحدَّث الموجود منها.",
            "",
            "الأعمدة المطلوبة: " + string.Join("، ", def.Columns.Where(c => c.IsRequired).Select(c => c.HeaderAr)) + "."
        };

        foreach (var col in def.Columns.Where(c => c.Type == ImportValueType.Enum && c.EnumMap is not null))
            lines.Add($"قيم «{col.HeaderAr}»: {EnumLabels(col)}.");

        lines.AddRange(
        [
            "",
            "الأرقام تُكتب بأرقام لاتينية وفاصل عشري بالنقطة (مثال: 12.5)؛ الأرقام العربية مقبولة أيضًا.",
            "القيم المنطقية (مثل «نشط»): نعم/لا أو 1/0 أو نشط/معطل.",
            "ابدأ إدخال البيانات مباشرةً من الصف الثاني أسفل الترويسة ولا تعدّل صف الترويسة.",
            "الحد الأقصى لعدد الصفوف: 5000 صف (دون الترويسة).",
            "الحقول غير المطلوبة تُترك فارغة للاحتفاظ بالقيمة الافتراضية أو دون تغيير عند التحديث.",
            "عند استخدام ملف CSV احفظه بترميز UTF-8 لضمان ظهور الحروف العربية."
        ]);
        return lines;
    }

    private sealed class ReferenceCache
    {
        public readonly Dictionary<string, Unit> UnitsByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, ItemCategory> CategoriesByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, ItemType> ItemTypesByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Currency> CurrenciesByCode = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Branch> BranchesByCode = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Warehouse> WarehousesByCode = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Warehouse> WarehousesByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, GLAccount> AccountsByCode = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Supplier> SuppliersByCode = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Supplier> SuppliersByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Customer> CustomersByCode = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Customer> CustomersByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Item> ItemsByCode = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Item> ItemsByBarcode = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Item> ItemsByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, SaleInvoice> SaleInvoicesByNumber = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, PurchaseInvoice> PurchaseInvoicesByNumber = new(StringComparer.OrdinalIgnoreCase);

        public bool EntityKeyExists(string entityKey, string normKey) => entityKey switch
        {
            "suppliers" => SuppliersByCode.ContainsKey(normKey) || SuppliersByName.ContainsKey(normKey),
            "customers" => CustomersByCode.ContainsKey(normKey) || CustomersByName.ContainsKey(normKey),
            "items" => ItemsByBarcode.ContainsKey(normKey) || ItemsByName.ContainsKey(normKey),
            "itemCategories" => CategoriesByName.ContainsKey(normKey),
            "itemTypes" => ItemTypesByName.ContainsKey(normKey),
            "units" => UnitsByName.ContainsKey(normKey),
            "currencies" => CurrenciesByCode.ContainsKey(normKey),
"branches" => BranchesByCode.ContainsKey(normKey),
                    "warehouses" => WarehousesByCode.ContainsKey(normKey) || WarehousesByName.ContainsKey(normKey),
            "glAccounts" => AccountsByCode.ContainsKey(normKey),
            _ => false
        };

        public bool LookupHas(string lookupKey, bool useCode, string normKey)
        {
            if (useCode)
                return lookupKey switch
                {
                    "currencies" => CurrenciesByCode.ContainsKey(normKey),
                    "glAccounts" => AccountsByCode.ContainsKey(normKey),
                    "branches" => BranchesByCode.ContainsKey(normKey),
"warehouses" => WarehousesByCode.ContainsKey(normKey) || WarehousesByName.ContainsKey(normKey),
                    "suppliers" => SuppliersByCode.ContainsKey(normKey),
                    "customers" => CustomersByCode.ContainsKey(normKey),
                    _ => false
                };
            return lookupKey switch
            {
                "units" => UnitsByName.ContainsKey(normKey),
                "itemTypes" => ItemTypesByName.ContainsKey(normKey),
                "categories" => CategoriesByName.ContainsKey(normKey),
                "items" => ItemsByName.ContainsKey(normKey) || ItemsByBarcode.ContainsKey(normKey) || ItemsByCode.ContainsKey(normKey),
                "suppliers" => SuppliersByName.ContainsKey(normKey),
                "customers" => CustomersByName.ContainsKey(normKey),
                "parties" => CustomersByName.ContainsKey(normKey) || SuppliersByName.ContainsKey(normKey),
                "saleInvoices" => SaleInvoicesByNumber.ContainsKey(normKey),
                "purchaseInvoices" => PurchaseInvoicesByNumber.ContainsKey(normKey),
                _ => false
            };
        }
    }
}