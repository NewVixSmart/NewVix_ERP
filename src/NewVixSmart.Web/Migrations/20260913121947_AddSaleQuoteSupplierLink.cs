using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleQuoteSupplierLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupplierQuoteId",
                table: "SaleQuotes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleQuotes_SupplierQuoteId",
                table: "SaleQuotes",
                column: "SupplierQuoteId");

            migrationBuilder.AddForeignKey(
                name: "FK_SaleQuotes_SupplierQuotes_SupplierQuoteId",
                table: "SaleQuotes",
                column: "SupplierQuoteId",
                principalTable: "SupplierQuotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaleQuotes_SupplierQuotes_SupplierQuoteId",
                table: "SaleQuotes");

            migrationBuilder.DropIndex(
                name: "IX_SaleQuotes_SupplierQuoteId",
                table: "SaleQuotes");

            migrationBuilder.DropColumn(
                name: "SupplierQuoteId",
                table: "SaleQuotes");
        }
    }
}
