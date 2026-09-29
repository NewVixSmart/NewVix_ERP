using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddImportDedupeUniqueIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Suppliers_Name",
            table: "Suppliers",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Items_Barcode",
            table: "Items",
            column: "Barcode",
            unique: true,
            filter: "[Barcode] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Items_Name",
            table: "Items",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Customers_Name",
            table: "Customers",
            column: "Name",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Suppliers_Name",
            table: "Suppliers");

        migrationBuilder.DropIndex(
            name: "IX_Items_Barcode",
            table: "Items");

        migrationBuilder.DropIndex(
            name: "IX_Items_Name",
            table: "Items");

        migrationBuilder.DropIndex(
            name: "IX_Customers_Name",
            table: "Customers");
    }
}
