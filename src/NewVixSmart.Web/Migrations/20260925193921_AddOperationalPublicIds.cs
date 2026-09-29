using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddOperationalPublicIds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "SalesOrders",
            type: "uniqueidentifier",
            nullable: false,
            defaultValueSql: "NEWID()");

        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "SaleQuotes",
            type: "uniqueidentifier",
            nullable: false,
            defaultValueSql: "NEWID()");

        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "PurchaseOrders",
            type: "uniqueidentifier",
            nullable: false,
            defaultValueSql: "NEWID()");

        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "Items",
            type: "uniqueidentifier",
            nullable: false,
            defaultValueSql: "NEWID()");

        migrationBuilder.AddColumn<Guid>(
            name: "PublicId",
            table: "DeliveryOrders",
            type: "uniqueidentifier",
            nullable: false,
            defaultValueSql: "NEWID()");

        migrationBuilder.CreateIndex(
            name: "IX_SalesOrders_PublicId",
            table: "SalesOrders",
            column: "PublicId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SaleQuotes_PublicId",
            table: "SaleQuotes",
            column: "PublicId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseOrders_PublicId",
            table: "PurchaseOrders",
            column: "PublicId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Items_PublicId",
            table: "Items",
            column: "PublicId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryOrders_PublicId",
            table: "DeliveryOrders",
            column: "PublicId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SalesOrders_PublicId",
            table: "SalesOrders");

        migrationBuilder.DropIndex(
            name: "IX_SaleQuotes_PublicId",
            table: "SaleQuotes");

        migrationBuilder.DropIndex(
            name: "IX_PurchaseOrders_PublicId",
            table: "PurchaseOrders");

        migrationBuilder.DropIndex(
            name: "IX_Items_PublicId",
            table: "Items");

        migrationBuilder.DropIndex(
            name: "IX_DeliveryOrders_PublicId",
            table: "DeliveryOrders");

        migrationBuilder.DropColumn(
            name: "PublicId",
            table: "SalesOrders");

        migrationBuilder.DropColumn(
            name: "PublicId",
            table: "SaleQuotes");

        migrationBuilder.DropColumn(
            name: "PublicId",
            table: "PurchaseOrders");

        migrationBuilder.DropColumn(
            name: "PublicId",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "PublicId",
            table: "DeliveryOrders");
    }
}
