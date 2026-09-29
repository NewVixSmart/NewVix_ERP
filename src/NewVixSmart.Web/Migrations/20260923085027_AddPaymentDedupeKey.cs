using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddPaymentDedupeKey : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DedupeKey",
            table: "Payments",
            type: "nvarchar(450)",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Payments_DedupeKey",
            table: "Payments",
            column: "DedupeKey",
            unique: true,
            filter: "[DedupeKey] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Payments_DedupeKey",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "DedupeKey",
            table: "Payments");
    }
}
