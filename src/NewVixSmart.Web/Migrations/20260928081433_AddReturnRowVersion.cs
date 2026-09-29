using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddReturnRowVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: "PurchaseReturns",
            type: "rowversion",
            rowVersion: true,
            nullable: true);

        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: "SaleReturns",
            type: "rowversion",
            rowVersion: true,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RowVersion",
            table: "PurchaseReturns");

        migrationBuilder.DropColumn(
            name: "RowVersion",
            table: "SaleReturns");
    }
}
