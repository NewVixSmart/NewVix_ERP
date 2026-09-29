using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddWarehousesAndStockTransfer : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Warehouses",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Warehouses", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "StockTransfers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TransferNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                SourceWarehouseId = table.Column<int>(type: "int", nullable: false),
                TargetWarehouseId = table.Column<int>(type: "int", nullable: false),
                TransferDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockTransfers", x => x.Id);
                table.ForeignKey(
                    name: "FK_StockTransfers_Warehouses_SourceWarehouseId",
                    column: x => x.SourceWarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockTransfers_Warehouses_TargetWarehouseId",
                    column: x => x.TargetWarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StockTransferItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                StockTransferId = table.Column<int>(type: "int", nullable: false),
                ItemId = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Count = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                UnitCost = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                DateReceived = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockTransferItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_StockTransferItems_Items_ItemId",
                    column: x => x.ItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockTransferItems_StockTransfers_StockTransferId",
                    column: x => x.StockTransferId,
                    principalTable: "StockTransfers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_StockTransferItems_ItemId",
            table: "StockTransferItems",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_StockTransferItems_StockTransferId",
            table: "StockTransferItems",
            column: "StockTransferId");

        migrationBuilder.CreateIndex(
            name: "IX_StockTransfers_SourceWarehouseId",
            table: "StockTransfers",
            column: "SourceWarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_StockTransfers_TargetWarehouseId",
            table: "StockTransfers",
            column: "TargetWarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_StockTransfers_TransferNumber",
            table: "StockTransfers",
            column: "TransferNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Warehouses_Code",
            table: "Warehouses",
            column: "Code",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "StockTransferItems");

        migrationBuilder.DropTable(
            name: "StockTransfers");

        migrationBuilder.DropTable(
            name: "Warehouses");
    }
}
