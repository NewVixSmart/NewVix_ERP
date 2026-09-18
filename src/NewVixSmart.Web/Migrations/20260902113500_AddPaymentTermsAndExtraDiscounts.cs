using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTermsAndExtraDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Discount2",
                table: "SaleInvoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Discount3",
                table: "SaleInvoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "SaleInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTerms",
                table: "SaleInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Discount2",
                table: "PurchaseInvoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Discount3",
                table: "PurchaseInvoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "PurchaseInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTerms",
                table: "PurchaseInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Discount2",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "Discount3",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "PaymentTerms",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "Discount2",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "Discount3",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "PaymentTerms",
                table: "PurchaseInvoices");
        }
    }
}
