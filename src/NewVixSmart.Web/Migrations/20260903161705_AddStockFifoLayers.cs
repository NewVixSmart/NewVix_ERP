using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddStockFifoLayers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StockLayers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ItemId = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<int>(type: "int", nullable: true),
                Qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Count = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                UnitCost = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                CountCost = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                DateReceived = table.Column<DateTime>(type: "datetime2", nullable: false),
                RemainingQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                RemainingCount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockLayers", x => x.Id);
                table.ForeignKey(
                    name: "FK_StockLayers_Items_ItemId",
                    column: x => x.ItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_StockLayers_ItemId",
            table: "StockLayers",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_StockLayers_WarehouseId_ItemId",
            table: "StockLayers",
            columns: new[] { "WarehouseId", "ItemId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "StockLayers");
    }
}
