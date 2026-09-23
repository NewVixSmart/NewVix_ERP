using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddStockLayerRowVersionAndLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "StockLayers",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_InvoiceType_InvoiceId",
                table: "PaymentAllocations",
                columns: new[] { "InvoiceType", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_Source_SourceId",
                table: "JournalEntries",
                columns: new[] { "Source", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_InvoiceType_InvoiceId",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_Source_SourceId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "StockLayers");
        }
    }
}
