using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialPublicIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "SaleReturns",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "SaleInvoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "PurchaseReturns",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "PurchaseInvoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_PublicId",
                table: "SaleReturns",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_PublicId",
                table: "SaleInvoices",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_PublicId",
                table: "PurchaseReturns",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_PublicId",
                table: "PurchaseInvoices",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PublicId",
                table: "Payments",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SaleReturns_PublicId",
                table: "SaleReturns");

            migrationBuilder.DropIndex(
                name: "IX_SaleInvoices_PublicId",
                table: "SaleInvoices");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_PublicId",
                table: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_PublicId",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PublicId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SaleReturns");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Payments");
        }
    }
}
