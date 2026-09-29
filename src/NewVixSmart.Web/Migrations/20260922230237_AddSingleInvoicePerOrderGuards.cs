using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddSingleInvoicePerOrderGuards : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SaleInvoices_SalesOrderId",
            table: "SaleInvoices");

        migrationBuilder.DropIndex(
            name: "IX_PurchaseInvoices_PurchaseOrderId",
            table: "PurchaseInvoices");

        migrationBuilder.CreateIndex(
            name: "IX_SaleInvoices_SalesOrderId",
            table: "SaleInvoices",
            column: "SalesOrderId",
            unique: true,
            filter: "[SalesOrderId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoices_PurchaseOrderId",
            table: "PurchaseInvoices",
            column: "PurchaseOrderId",
            unique: true,
            filter: "[PurchaseOrderId] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SaleInvoices_SalesOrderId",
            table: "SaleInvoices");

        migrationBuilder.DropIndex(
            name: "IX_PurchaseInvoices_PurchaseOrderId",
            table: "PurchaseInvoices");

        migrationBuilder.CreateIndex(
            name: "IX_SaleInvoices_SalesOrderId",
            table: "SaleInvoices",
            column: "SalesOrderId");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoices_PurchaseOrderId",
            table: "PurchaseInvoices",
            column: "PurchaseOrderId");
    }
}
