namespace NewVixSmart.Web.Services;

public sealed record MenuModule(
    string Key,
    string TitleAr,
    string Icon,
    string Controller,
    string Action,
    string Section);

public static class PermissionCatalog
{
    public const string View = "View";
    public const string Create = "Create";
    public const string Edit = "Edit";
    public const string Delete = "Delete";
    public const string Approve = "Approve";
    public const string Receive = "Receive";
    public const string Post = "Post";

    private static readonly Dictionary<string, string[]> ActionsByModule = new()
    {
        ["Items"] = [View, Create, Edit, Delete],
        ["Purchases"] = [View, Create],
        ["PurchaseOrders"] = [View, Create, Edit, Approve, Receive],
        ["PurchaseRequests"] = [View, Create, Delete],
        ["Sales"] = [View, Create],
        ["SaleReturns"] = [View, Create, Post],
        ["SalesQuotes"] = [View, Create, Delete, "Convert"],
        ["SalesOrders"] = [View, Create, Edit, Approve, "Convert"],
        ["DeliveryOrders"] = [View, Create, "Deliver"],
        ["PurchaseReturns"] = [View, Create, Post],
        ["Customers"] = [View, Create, Edit, Delete],
        ["Suppliers"] = [View, Create, Edit, Delete],
        ["Payments"] = [View, Create],
        ["Stock"] = [View],
        ["StockReport"] = [View],
        ["LowStock"] = [View],
        ["InventoryAdjustments"] = [View, Create, Delete],
        ["Warehouses"] = [View, Create, Edit, Delete],
        ["StockTransfers"] = [View, Create],
        ["Reports"] = [View, "Dashboard", "Export"],
        ["AuditLedger"] = [View, "Export"],
        ["Aging"] = [View, "Export"],
        ["Batch"] = ["SalesCreate", "AdjustmentCreate"],
        ["FiscalClose"] = ["Close", "Reopen"],
        ["ChartOfAccounts"] = [View, Create, Edit, "Deactivate"],
        ["Budgets"] = [View, "Manage"],
        ["ExportCenter"] = [View],
        ["ImportCenter"] = [View, "Import"],
        ["Settings"] = [View, Edit]
    };

    public static readonly MenuModule[] Modules =
    [
        new("Items", "الأصناف", "bi-box-seam", "Items", "Index", "commerce"),
        new("PurchaseOrders", "أوامر الشراء", "bi-clipboard-check", "PurchaseOrders", "Index", "commerce"),
        new("Purchases", "فواتير الشراء", "bi-cart-dash", "Purchases", "Index", "commerce"),
        new("PurchaseRequests", "طلبات عرض سعر", "bi-envelope-open", "PurchaseRequests", "Index", "commerce"),
        new("Sales", "المبيعات", "bi-cart-check", "Sales", "Index", "commerce"),
        new("SaleReturns", "مرتجعات البيع", "bi-arrow-return-left", "SaleReturns", "Index", "commerce"),
        new("SalesQuotes", "عروض أسعار العملاء", "bi-receipt-cutoff", "SalesQuotes", "Index", "commerce"),
        new("SalesOrders", "أوامر البيع", "bi-clipboard2-check", "SalesOrders", "Index", "commerce"),
        new("DeliveryOrders", "أذونات التسليم", "bi-truck", "DeliveryOrders", "Index", "commerce"),
        new("PurchaseReturns", "مرتجعات الشراء", "bi-arrow-return-right", "PurchaseReturns", "Index", "commerce"),
        new("Customers", "العملاء", "bi-people", "Customers", "Index", "accounts"),
        new("Suppliers", "الموردون", "bi-truck", "Suppliers", "Index", "accounts"),
        new("Payments", "المدفوعات", "bi-wallet2", "Payments", "Index", "accounts"),
        new("Stock", "حركة المخزون", "bi-boxes", "Stock", "Index", "stock"),
        new("StockReport", "تقرير المخزون", "bi-clipboard2-data", "Stock", "Report", "stock"),
        new("LowStock", "انخفاض المخزون", "bi-exclamation-triangle", "Stock", "LowStock", "stock"),
        new("InventoryAdjustments", "الجرد", "bi-clipboard-data", "InventoryAdjustments", "Index", "stock"),
        new("Warehouses", "المستودعات", "bi-building", "Warehouses", "Index", "stock"),
        new("StockTransfers", "نقل مخزون", "bi-arrow-left-right", "StockTransfers", "Index", "stock"),
        new("Reports", "التقارير", "bi-speedometer2", "Reports", "Index", "reports"),
        new("AuditLedger", "سجل التدقيق", "bi-journal-bookmark", "Reports", "AuditLedger", "reports"),
        new("Aging", "القائمة العمرية", "bi-hourglass-split", "Reports", "Aging", "reports"),
        new("Batch", "العمليات الجماعية", "bi-collection", "Batch", "Index", "operations"),
        new("FiscalClose", "الإقفال السنوي", "bi-lock", "Fiscal", "Index", "admin"),
        new("ChartOfAccounts", "مخطط الحسابات", "bi-journal-code", "Accounts", "Index", "admin"),
        new("Budgets", "الميزانيات", "bi-calendar3", "Budgets", "Index", "admin"),
        new("ExportCenter", "مركز التصدير", "bi-box-arrow-up", "ExportCenter", "Index", "admin"),
        new("ImportCenter", "مركز الاستيراد", "bi-box-arrow-in-down", "ImportCenter", "Index", "admin"),
        new("Settings", "الإعدادات", "bi-gear", "Settings", "Index", "admin")
    ];

    public static string Key(string module, string action) => $"{module}.{action}";

    public static string[] ActionsFor(string module) =>
        ActionsByModule.TryGetValue(module, out var acts) ? acts : [];

    public static string ModuleDisplayName(string permissionKey)
    {
        var dot = permissionKey.IndexOf('.');
        if (dot <= 0) return permissionKey;
        var module = permissionKey[..dot];
        var m = Modules.FirstOrDefault(x => x.Key == module);
        return m?.TitleAr ?? module;
    }
}