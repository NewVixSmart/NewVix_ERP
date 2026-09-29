using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class WidenQuantityAndUnitPricePrecision : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SupplierQuotes",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockTransferItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockTransferItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockReservationLines",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockReservationLines",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ConsumedQuantity",
            table: "StockReservationLines",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ConsumedCount",
            table: "StockReservationLines",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockMovements",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "CountBefore",
            table: "StockMovements",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "CountAfter",
            table: "StockMovements",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockMovements",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "BalanceBefore",
            table: "StockMovements",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "BalanceAfter",
            table: "StockMovements",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "RemainingQty",
            table: "StockLayers",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "RemainingCount",
            table: "StockLayers",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Qty",
            table: "StockLayers",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockLayers",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SalesOrderItems",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedQty",
            table: "SalesOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedCount",
            table: "SalesOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SalesOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "InvoicedQty",
            table: "SalesOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "InvoicedCount",
            table: "SalesOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "DeliveredQty",
            table: "SalesOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "DeliveredCount",
            table: "SalesOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SalesOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleReturnItems",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleReturnItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleReturnItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleQuoteItems",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleQuoteItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleQuoteItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleInvoiceItems",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleInvoiceItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleInvoiceItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseReturnItems",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseReturnItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseReturnItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseOrderItems",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReceivedQty",
            table: "PurchaseOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReceivedCount",
            table: "PurchaseOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseInvoiceItems",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseInvoiceItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseInvoiceItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "SalePrice",
            table: "Items",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedQuantity",
            table: "Items",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedCount",
            table: "Items",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "PurchasePrice",
            table: "Items",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "MinQuantity",
            table: "Items",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "MinCount",
            table: "Items",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "CurrentQuantity",
            table: "Items",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "CurrentCount",
            table: "Items",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "NewQuantity",
            table: "InventoryAdjustments",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "NewCount",
            table: "InventoryAdjustments",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "DeliveryOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "DeliveryOrderItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "DeliveryIssueItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "DeliveryIssueItems",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SupplierQuotes",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockTransferItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockTransferItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockReservationLines",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockReservationLines",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ConsumedQuantity",
            table: "StockReservationLines",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ConsumedCount",
            table: "StockReservationLines",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "CountBefore",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "CountAfter",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "BalanceBefore",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "BalanceAfter",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "RemainingQty",
            table: "StockLayers",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "RemainingCount",
            table: "StockLayers",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Qty",
            table: "StockLayers",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockLayers",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedQty",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedCount",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "InvoicedQty",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "InvoicedCount",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "DeliveredQty",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "DeliveredCount",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleQuoteItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleQuoteItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleQuoteItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReceivedQty",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReceivedCount",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "SalePrice",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedQuantity",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedCount",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "PurchasePrice",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,3)",
            oldPrecision: 18,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "MinQuantity",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "MinCount",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "CurrentQuantity",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "CurrentCount",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "NewQuantity",
            table: "InventoryAdjustments",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "NewCount",
            table: "InventoryAdjustments",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "DeliveryOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "DeliveryOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "DeliveryIssueItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "DeliveryIssueItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,4)",
            oldPrecision: 18,
            oldScale: 4);
    }
}
