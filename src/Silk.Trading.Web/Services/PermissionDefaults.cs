namespace Silk.Trading.Web.Services;

public static class PermissionDefaults
{
    public static string[] DefaultsFor(string roleName) => roleName switch
    {
        "Accountant" =>
        [
            "Items.View", "Items.Create", "Items.Edit",
            "PurchaseOrders.View", "PurchaseOrders.Create", "PurchaseOrders.Edit", "PurchaseOrders.Approve", "PurchaseOrders.Receive",
            "PurchaseRequests.View", "PurchaseRequests.Create",
            "Purchases.View", "Purchases.Create",
            "Sales.View", "Sales.Create",
            "SaleReturns.View", "SaleReturns.Create", "SaleReturns.Post",
            "PurchaseReturns.View", "PurchaseReturns.Create", "PurchaseReturns.Post",
            "Customers.View", "Customers.Create", "Customers.Edit", "Customers.Delete",
            "Suppliers.View", "Suppliers.Create", "Suppliers.Edit", "Suppliers.Delete",
            "Payments.View", "Payments.Create",
            "Warehouses.View", "Warehouses.Create", "Warehouses.Edit", "Warehouses.Delete",
            "StockTransfers.View", "StockTransfers.Create",
            "Reports.View", "Reports.Dashboard", "Reports.Export",
            "AuditLedger.View", "AuditLedger.Export",
            "Shipments.View", "Shipments.Create", "Shipments.Edit",
            "Batch.SalesCreate", "Batch.AdjustmentCreate",
            "FiscalClose.Close",
            "ChartOfAccounts.View",
            "Budgets.View", "Budgets.Manage"
        ],
        "Warehouse" =>
        [
            "Items.View", "Items.Create", "Items.Edit",
            "PurchaseOrders.View", "PurchaseOrders.Create", "PurchaseOrders.Edit", "PurchaseOrders.Approve", "PurchaseOrders.Receive",
            "PurchaseRequests.View", "PurchaseRequests.Create",
            "Purchases.View", "Purchases.Create",
            "Sales.View", "Sales.Create",
            "SaleReturns.View", "SaleReturns.Create", "SaleReturns.Post",
            "PurchaseReturns.View", "PurchaseReturns.Create", "PurchaseReturns.Post",
            "Customers.View", "Customers.Create", "Customers.Edit", "Customers.Delete",
            "Suppliers.View", "Suppliers.Create", "Suppliers.Edit", "Suppliers.Delete",
            "Stock.View", "StockReport.View",
            "InventoryAdjustments.View", "InventoryAdjustments.Create",
            "Warehouses.View", "Warehouses.Create", "Warehouses.Edit",
            "StockTransfers.View", "StockTransfers.Create",
            "Shipments.View", "Shipments.Create", "Shipments.Edit",
            "Batch.AdjustmentCreate"
        ],
        _ => []
    };
}