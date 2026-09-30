using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
/// <remarks>
/// This migration is NOT purely regenerable. Two things here have no model representation and would
/// be lost if the file were ever removed and re-scaffolded:
///   1. the widened *precision*, which the model does express, but only as
///      <c>DecimalPrecision.MeasurePrecision</c> - re-scaffolding would emit the current widths,
///      which is correct, so this is a note rather than a hazard;
///   2. the lossy-rollback guard in <see cref="Down"/>, which is pure T-SQL and would vanish
///      silently, leaving <c>Down()</c> to round live data away with a zero exit code.
/// Keep the guard by hand, the way <c>DropMultiCurrency_EgpOnly</c> keeps its data steps.
/// </remarks>
public partial class WidenQuantityAndUnitPricePrecision : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SupplierQuotes",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockTransferItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockTransferItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockReservationLines",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockReservationLines",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ConsumedQuantity",
            table: "StockReservationLines",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ConsumedCount",
            table: "StockReservationLines",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockMovements",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "CountBefore",
            table: "StockMovements",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "CountAfter",
            table: "StockMovements",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockMovements",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "BalanceBefore",
            table: "StockMovements",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "BalanceAfter",
            table: "StockMovements",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "RemainingQty",
            table: "StockLayers",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "RemainingCount",
            table: "StockLayers",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Qty",
            table: "StockLayers",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockLayers",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SalesOrderItems",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedQty",
            table: "SalesOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedCount",
            table: "SalesOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SalesOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "InvoicedQty",
            table: "SalesOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "InvoicedCount",
            table: "SalesOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "DeliveredQty",
            table: "SalesOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "DeliveredCount",
            table: "SalesOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SalesOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleReturnItems",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleReturnItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleReturnItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleQuoteItems",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleQuoteItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleQuoteItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleInvoiceItems",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleInvoiceItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleInvoiceItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseReturnItems",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseReturnItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseReturnItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseOrderItems",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReceivedQty",
            table: "PurchaseOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReceivedCount",
            table: "PurchaseOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseInvoiceItems",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseInvoiceItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseInvoiceItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "SalePrice",
            table: "Items",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedQuantity",
            table: "Items",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedCount",
            table: "Items",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "PurchasePrice",
            table: "Items",
            type: "decimal(20,3)",
            precision: 20,
            scale: 3,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "MinQuantity",
            table: "Items",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "MinCount",
            table: "Items",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "CurrentQuantity",
            table: "Items",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "CurrentCount",
            table: "Items",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "NewQuantity",
            table: "InventoryAdjustments",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "NewCount",
            table: "InventoryAdjustments",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "DeliveryOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "DeliveryOrderItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "DeliveryIssueItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "DeliveryIssueItems",
            type: "decimal(20,4)",
            precision: 20,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)");
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// This rollback is lossy by construction, and SQL Server makes that silent. Narrowing a
    /// <c>decimal(20,4)</c> to a <c>decimal(18,2)</c> does not fail: the engine rounds, and the
    /// session exits 0. So a stock quantity of 0.0001 becomes 0.00, a movement of -7.7777 becomes
    /// -7.78, and an operator who has just rolled back a bad deploy is told the database is fine.
    /// The guard below is the deliberate <c>THROW</c> the repo already uses in
    /// <c>DropMultiCurrency_EgpOnly</c>: it runs first, so it aborts before any
    /// <c>AlterColumn</c> executes, and it names the table, the column and the value that would be
    /// destroyed so the incident is actionable rather than merely loud.
    /// </para>
    /// <para>
    /// The cost is a full clustered-index scan of each column, which no index can serve because
    /// <c>value &lt;&gt; ROUND(value, 2)</c> is not a seekable predicate. The loop stops at the
    /// first offending column, so a database that does have fractional data pays for the columns
    /// before it and nothing after; a clean database pays for all sixty.
    /// </para>
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ---- 0. Refuse a lossy rollback, before anything is altered ----------------
        migrationBuilder.Sql(
            """
            DECLARE @affected TABLE (
                [Ordinal] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [TableName] sysname NOT NULL,
                [ColumnName] sysname NOT NULL);

            INSERT INTO @affected ([TableName], [ColumnName]) VALUES
                (N'SupplierQuotes', N'UnitPrice'),
                (N'StockTransferItems', N'Quantity'),
                (N'StockTransferItems', N'Count'),
                (N'StockReservationLines', N'Quantity'),
                (N'StockReservationLines', N'Count'),
                (N'StockReservationLines', N'ConsumedQuantity'),
                (N'StockReservationLines', N'ConsumedCount'),
                (N'StockMovements', N'Quantity'),
                (N'StockMovements', N'CountBefore'),
                (N'StockMovements', N'CountAfter'),
                (N'StockMovements', N'Count'),
                (N'StockMovements', N'BalanceBefore'),
                (N'StockMovements', N'BalanceAfter'),
                (N'StockLayers', N'RemainingQty'),
                (N'StockLayers', N'RemainingCount'),
                (N'StockLayers', N'Qty'),
                (N'StockLayers', N'Count'),
                (N'SalesOrderItems', N'UnitPrice'),
                (N'SalesOrderItems', N'ReservedQty'),
                (N'SalesOrderItems', N'ReservedCount'),
                (N'SalesOrderItems', N'Quantity'),
                (N'SalesOrderItems', N'InvoicedQty'),
                (N'SalesOrderItems', N'InvoicedCount'),
                (N'SalesOrderItems', N'DeliveredQty'),
                (N'SalesOrderItems', N'DeliveredCount'),
                (N'SalesOrderItems', N'Count'),
                (N'SaleReturnItems', N'UnitPrice'),
                (N'SaleReturnItems', N'Quantity'),
                (N'SaleReturnItems', N'Count'),
                (N'SaleQuoteItems', N'UnitPrice'),
                (N'SaleQuoteItems', N'Quantity'),
                (N'SaleQuoteItems', N'Count'),
                (N'SaleInvoiceItems', N'UnitPrice'),
                (N'SaleInvoiceItems', N'Quantity'),
                (N'SaleInvoiceItems', N'Count'),
                (N'PurchaseReturnItems', N'UnitPrice'),
                (N'PurchaseReturnItems', N'Quantity'),
                (N'PurchaseReturnItems', N'Count'),
                (N'PurchaseOrderItems', N'UnitPrice'),
                (N'PurchaseOrderItems', N'ReceivedQty'),
                (N'PurchaseOrderItems', N'ReceivedCount'),
                (N'PurchaseOrderItems', N'Quantity'),
                (N'PurchaseOrderItems', N'Count'),
                (N'PurchaseInvoiceItems', N'UnitPrice'),
                (N'PurchaseInvoiceItems', N'Quantity'),
                (N'PurchaseInvoiceItems', N'Count'),
                (N'Items', N'SalePrice'),
                (N'Items', N'ReservedQuantity'),
                (N'Items', N'ReservedCount'),
                (N'Items', N'PurchasePrice'),
                (N'Items', N'MinQuantity'),
                (N'Items', N'MinCount'),
                (N'Items', N'CurrentQuantity'),
                (N'Items', N'CurrentCount'),
                (N'InventoryAdjustments', N'NewQuantity'),
                (N'InventoryAdjustments', N'NewCount'),
                (N'DeliveryOrderItems', N'Quantity'),
                (N'DeliveryOrderItems', N'Count'),
                (N'DeliveryIssueItems', N'Quantity'),
                (N'DeliveryIssueItems', N'Count');

            DECLARE @ordinal int = 1;
            DECLARE @last int = (SELECT COUNT(*) FROM @affected);
            DECLARE @tableName sysname;
            DECLARE @columnName sysname;
            DECLARE @probe nvarchar(2048);
            DECLARE @offending decimal(38,10);
            DECLARE @message nvarchar(2048) = NULL;

            WHILE @ordinal <= @last AND @message IS NULL
            BEGIN
                SELECT @tableName = [TableName], @columnName = [ColumnName]
                FROM @affected
                WHERE [Ordinal] = @ordinal;

                -- A value decimal(18,2) cannot hold exactly is one this rollback would round.
                -- The identifiers are concatenated, but they come from the fixed @affected list
                -- above and never from input, so there is nothing to inject.
                SET @probe = N'SELECT TOP (1) @value = [' + @columnName + N'] FROM [' + @tableName
                    + N'] WHERE [' + @columnName + N'] <> ROUND([' + @columnName + N'], 2);';

                SET @offending = NULL;
                EXEC sp_executesql @probe, N'@value decimal(38,10) OUTPUT', @offending OUTPUT;

                -- Still NULL means "nothing offending in this column", and it is a safe signal:
                -- a NULL row makes the predicate UNKNOWN so it is never selected, and no value
                -- that survives the narrowing is NULL either.
                IF @offending IS NOT NULL
                    SET @message = N'WidenQuantityAndUnitPricePrecision cannot be rolled back: ['
                        + @tableName + N'].[' + @columnName + N'] holds '
                        + CONVERT(nvarchar(64), CAST(@offending AS decimal(38,4)), 0)
                        + N', which decimal(18,2) cannot store - narrowing it would silently store '
                        + CONVERT(nvarchar(64), CAST(ROUND(@offending, 2) AS decimal(38,4)), 0)
                        + N' instead. Round or zero that row yourself, then retry the rollback.';

                SET @ordinal = @ordinal + 1;
            END;

            IF @message IS NOT NULL
                THROW 51000, @message, 1;
            """);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SupplierQuotes",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockTransferItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockTransferItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockReservationLines",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockReservationLines",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ConsumedQuantity",
            table: "StockReservationLines",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ConsumedCount",
            table: "StockReservationLines",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "CountBefore",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "CountAfter",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "BalanceBefore",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "BalanceAfter",
            table: "StockMovements",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "RemainingQty",
            table: "StockLayers",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "RemainingCount",
            table: "StockLayers",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Qty",
            table: "StockLayers",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "StockLayers",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedQty",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedCount",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "InvoicedQty",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "InvoicedCount",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "DeliveredQty",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "DeliveredCount",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleQuoteItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleQuoteItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleQuoteItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "SaleInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "SaleInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "SaleInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseReturnItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReceivedQty",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReceivedCount",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "UnitPrice",
            table: "PurchaseInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "PurchaseInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "PurchaseInvoiceItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "SalePrice",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedQuantity",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReservedCount",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "PurchasePrice",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,3)",
            oldPrecision: 20,
            oldScale: 3);

        migrationBuilder.AlterColumn<decimal>(
            name: "MinQuantity",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "MinCount",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "CurrentQuantity",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "CurrentCount",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "NewQuantity",
            table: "InventoryAdjustments",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "NewCount",
            table: "InventoryAdjustments",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "DeliveryOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "DeliveryOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity",
            table: "DeliveryIssueItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);

        migrationBuilder.AlterColumn<decimal>(
            name: "Count",
            table: "DeliveryIssueItems",
            type: "decimal(18,2)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(20,4)",
            oldPrecision: 20,
            oldScale: 4);
    }
}
