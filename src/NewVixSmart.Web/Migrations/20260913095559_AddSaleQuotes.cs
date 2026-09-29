using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddSaleQuotes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SaleQuotes",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                QuoteNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CustomerId = table.Column<int>(type: "int", nullable: false),
                CurrencyId = table.Column<int>(type: "int", nullable: true),
                ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                QuoteDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Discount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Tax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                SaleInvoiceId = table.Column<int>(type: "int", nullable: true),
                ConvertedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ConvertedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleQuotes", x => x.Id);
                table.ForeignKey(
                    name: "FK_SaleQuotes_Currencies_CurrencyId",
                    column: x => x.CurrencyId,
                    principalTable: "Currencies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SaleQuotes_Customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "Customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SaleQuotes_SaleInvoices_SaleInvoiceId",
                    column: x => x.SaleInvoiceId,
                    principalTable: "SaleInvoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "SaleQuoteItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SaleQuoteId = table.Column<int>(type: "int", nullable: false),
                ItemId = table.Column<int>(type: "int", nullable: false),
                Count = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleQuoteItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_SaleQuoteItems_Items_ItemId",
                    column: x => x.ItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SaleQuoteItems_SaleQuotes_SaleQuoteId",
                    column: x => x.SaleQuoteId,
                    principalTable: "SaleQuotes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SaleQuoteItems_ItemId",
            table: "SaleQuoteItems",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_SaleQuoteItems_SaleQuoteId",
            table: "SaleQuoteItems",
            column: "SaleQuoteId");

        migrationBuilder.CreateIndex(
            name: "IX_SaleQuotes_CurrencyId",
            table: "SaleQuotes",
            column: "CurrencyId");

        migrationBuilder.CreateIndex(
            name: "IX_SaleQuotes_CustomerId",
            table: "SaleQuotes",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_SaleQuotes_QuoteNumber",
            table: "SaleQuotes",
            column: "QuoteNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SaleQuotes_SaleInvoiceId",
            table: "SaleQuotes",
            column: "SaleInvoiceId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SaleQuoteItems");

        migrationBuilder.DropTable(
            name: "SaleQuotes");
    }
}
