using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class RestrictHistoryFKs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_InventoryAdjustments_Items_ItemId",
            table: "InventoryAdjustments");

        migrationBuilder.DropForeignKey(
            name: "FK_Items_ItemCategories_CategoryId",
            table: "Items");

        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseInvoiceItems_Items_ItemId",
            table: "PurchaseInvoiceItems");

        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseInvoices_Suppliers_SupplierId",
            table: "PurchaseInvoices");

        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseReturnItems_Items_ItemId",
            table: "PurchaseReturnItems");

        migrationBuilder.DropForeignKey(
            name: "FK_SaleInvoiceItems_Items_ItemId",
            table: "SaleInvoiceItems");

        migrationBuilder.DropForeignKey(
            name: "FK_SaleInvoices_Customers_CustomerId",
            table: "SaleInvoices");

        migrationBuilder.DropForeignKey(
            name: "FK_SaleReturnItems_Items_ItemId",
            table: "SaleReturnItems");

        migrationBuilder.DropForeignKey(
            name: "FK_StockMovements_Items_ItemId",
            table: "StockMovements");

        migrationBuilder.AddForeignKey(
            name: "FK_InventoryAdjustments_Items_ItemId",
            table: "InventoryAdjustments",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Items_ItemCategories_CategoryId",
            table: "Items",
            column: "CategoryId",
            principalTable: "ItemCategories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseInvoiceItems_Items_ItemId",
            table: "PurchaseInvoiceItems",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseInvoices_Suppliers_SupplierId",
            table: "PurchaseInvoices",
            column: "SupplierId",
            principalTable: "Suppliers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseReturnItems_Items_ItemId",
            table: "PurchaseReturnItems",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_SaleInvoiceItems_Items_ItemId",
            table: "SaleInvoiceItems",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_SaleInvoices_Customers_CustomerId",
            table: "SaleInvoices",
            column: "CustomerId",
            principalTable: "Customers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_SaleReturnItems_Items_ItemId",
            table: "SaleReturnItems",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_StockMovements_Items_ItemId",
            table: "StockMovements",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_InventoryAdjustments_Items_ItemId",
            table: "InventoryAdjustments");

        migrationBuilder.DropForeignKey(
            name: "FK_Items_ItemCategories_CategoryId",
            table: "Items");

        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseInvoiceItems_Items_ItemId",
            table: "PurchaseInvoiceItems");

        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseInvoices_Suppliers_SupplierId",
            table: "PurchaseInvoices");

        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseReturnItems_Items_ItemId",
            table: "PurchaseReturnItems");

        migrationBuilder.DropForeignKey(
            name: "FK_SaleInvoiceItems_Items_ItemId",
            table: "SaleInvoiceItems");

        migrationBuilder.DropForeignKey(
            name: "FK_SaleInvoices_Customers_CustomerId",
            table: "SaleInvoices");

        migrationBuilder.DropForeignKey(
            name: "FK_SaleReturnItems_Items_ItemId",
            table: "SaleReturnItems");

        migrationBuilder.DropForeignKey(
            name: "FK_StockMovements_Items_ItemId",
            table: "StockMovements");

        migrationBuilder.AddForeignKey(
            name: "FK_InventoryAdjustments_Items_ItemId",
            table: "InventoryAdjustments",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Items_ItemCategories_CategoryId",
            table: "Items",
            column: "CategoryId",
            principalTable: "ItemCategories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseInvoiceItems_Items_ItemId",
            table: "PurchaseInvoiceItems",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseInvoices_Suppliers_SupplierId",
            table: "PurchaseInvoices",
            column: "SupplierId",
            principalTable: "Suppliers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseReturnItems_Items_ItemId",
            table: "PurchaseReturnItems",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_SaleInvoiceItems_Items_ItemId",
            table: "SaleInvoiceItems",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_SaleInvoices_Customers_CustomerId",
            table: "SaleInvoices",
            column: "CustomerId",
            principalTable: "Customers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_SaleReturnItems_Items_ItemId",
            table: "SaleReturnItems",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_StockMovements_Items_ItemId",
            table: "StockMovements",
            column: "ItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
