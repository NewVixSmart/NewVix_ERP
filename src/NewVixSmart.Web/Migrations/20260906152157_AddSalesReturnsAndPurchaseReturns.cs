using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddSalesReturnsAndPurchaseReturns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "BranchId",
            table: "SaleReturns",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CurrencyId",
            table: "SaleReturns",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ExchangeRate",
            table: "SaleReturns",
            type: "decimal(18,6)",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "PostedAt",
            table: "SaleReturns",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PostedBy",
            table: "SaleReturns",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Status",
            table: "SaleReturns",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "BranchId",
            table: "PurchaseReturns",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CurrencyId",
            table: "PurchaseReturns",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ExchangeRate",
            table: "PurchaseReturns",
            type: "decimal(18,6)",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "PostedAt",
            table: "PurchaseReturns",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PostedBy",
            table: "PurchaseReturns",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Status",
            table: "PurchaseReturns",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_SaleReturns_BranchId",
            table: "SaleReturns",
            column: "BranchId");

        migrationBuilder.CreateIndex(
            name: "IX_SaleReturns_CurrencyId",
            table: "SaleReturns",
            column: "CurrencyId");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseReturns_BranchId",
            table: "PurchaseReturns",
            column: "BranchId");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseReturns_CurrencyId",
            table: "PurchaseReturns",
            column: "CurrencyId");

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseReturns_Branches_BranchId",
            table: "PurchaseReturns",
            column: "BranchId",
            principalTable: "Branches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseReturns_Currencies_CurrencyId",
            table: "PurchaseReturns",
            column: "CurrencyId",
            principalTable: "Currencies",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_SaleReturns_Branches_BranchId",
            table: "SaleReturns",
            column: "BranchId",
            principalTable: "Branches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_SaleReturns_Currencies_CurrencyId",
            table: "SaleReturns",
            column: "CurrencyId",
            principalTable: "Currencies",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseReturns_Branches_BranchId",
            table: "PurchaseReturns");

        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseReturns_Currencies_CurrencyId",
            table: "PurchaseReturns");

        migrationBuilder.DropForeignKey(
            name: "FK_SaleReturns_Branches_BranchId",
            table: "SaleReturns");

        migrationBuilder.DropForeignKey(
            name: "FK_SaleReturns_Currencies_CurrencyId",
            table: "SaleReturns");

        migrationBuilder.DropIndex(
            name: "IX_SaleReturns_BranchId",
            table: "SaleReturns");

        migrationBuilder.DropIndex(
            name: "IX_SaleReturns_CurrencyId",
            table: "SaleReturns");

        migrationBuilder.DropIndex(
            name: "IX_PurchaseReturns_BranchId",
            table: "PurchaseReturns");

        migrationBuilder.DropIndex(
            name: "IX_PurchaseReturns_CurrencyId",
            table: "PurchaseReturns");

        migrationBuilder.DropColumn(
            name: "BranchId",
            table: "SaleReturns");

        migrationBuilder.DropColumn(
            name: "CurrencyId",
            table: "SaleReturns");

        migrationBuilder.DropColumn(
            name: "ExchangeRate",
            table: "SaleReturns");

        migrationBuilder.DropColumn(
            name: "PostedAt",
            table: "SaleReturns");

        migrationBuilder.DropColumn(
            name: "PostedBy",
            table: "SaleReturns");

        migrationBuilder.DropColumn(
            name: "Status",
            table: "SaleReturns");

        migrationBuilder.DropColumn(
            name: "BranchId",
            table: "PurchaseReturns");

        migrationBuilder.DropColumn(
            name: "CurrencyId",
            table: "PurchaseReturns");

        migrationBuilder.DropColumn(
            name: "ExchangeRate",
            table: "PurchaseReturns");

        migrationBuilder.DropColumn(
            name: "PostedAt",
            table: "PurchaseReturns");

        migrationBuilder.DropColumn(
            name: "PostedBy",
            table: "PurchaseReturns");

        migrationBuilder.DropColumn(
            name: "Status",
            table: "PurchaseReturns");
    }
}
