using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddItemMinStockAndBarcode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Barcode",
            table: "Items",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Barcode",
            table: "Items");
    }
}
