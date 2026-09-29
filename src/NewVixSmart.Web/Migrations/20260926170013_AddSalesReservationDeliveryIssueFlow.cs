using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddSalesReservationDeliveryIssueFlow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SaleInvoices_SalesOrderId",
            table: "SaleInvoices");

        migrationBuilder.AddColumn<decimal>(
            name: "DeliveredCount",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "DeliveredQty",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "ReservedCount",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "ReservedQty",
            table: "SalesOrderItems",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "PostingMode",
            table: "SaleInvoices",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<decimal>(
            name: "ReservedCount",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "ReservedQuantity",
            table: "Items",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "SalesOrderId",
            table: "DeliveryOrders",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "StockReservationId",
            table: "DeliveryOrders",
            type: "int",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "DeliveryIssues",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IssueNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                CustomerId = table.Column<int>(type: "int", nullable: false),
                SalesOrderId = table.Column<int>(type: "int", nullable: true),
                SaleInvoiceId = table.Column<int>(type: "int", nullable: true),
                IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Carrier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                TrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                IssuedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeliveryIssues", x => x.Id);
                table.ForeignKey(
                    name: "FK_DeliveryIssues_Customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "Customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DeliveryIssues_DeliveryOrders_DeliveryOrderId",
                    column: x => x.DeliveryOrderId,
                    principalTable: "DeliveryOrders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DeliveryIssues_SaleInvoices_SaleInvoiceId",
                    column: x => x.SaleInvoiceId,
                    principalTable: "SaleInvoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_DeliveryIssues_SalesOrders_SalesOrderId",
                    column: x => x.SalesOrderId,
                    principalTable: "SalesOrders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "StockReservations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ReservationNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                SalesOrderId = table.Column<int>(type: "int", nullable: true),
                CustomerId = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ReleasedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockReservations", x => x.Id);
                table.ForeignKey(
                    name: "FK_StockReservations_Customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "Customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockReservations_SalesOrders_SalesOrderId",
                    column: x => x.SalesOrderId,
                    principalTable: "SalesOrders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "DeliveryIssueItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                DeliveryIssueId = table.Column<int>(type: "int", nullable: false),
                DeliveryOrderItemId = table.Column<int>(type: "int", nullable: false),
                ItemId = table.Column<int>(type: "int", nullable: false),
                SalesOrderItemId = table.Column<int>(type: "int", nullable: true),
                Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Count = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeliveryIssueItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_DeliveryIssueItems_DeliveryIssues_DeliveryIssueId",
                    column: x => x.DeliveryIssueId,
                    principalTable: "DeliveryIssues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DeliveryIssueItems_DeliveryOrderItems_DeliveryOrderItemId",
                    column: x => x.DeliveryOrderItemId,
                    principalTable: "DeliveryOrderItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DeliveryIssueItems_Items_ItemId",
                    column: x => x.ItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DeliveryIssueItems_SalesOrderItems_SalesOrderItemId",
                    column: x => x.SalesOrderItemId,
                    principalTable: "SalesOrderItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StockReservationLines",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                StockReservationId = table.Column<int>(type: "int", nullable: false),
                ItemId = table.Column<int>(type: "int", nullable: false),
                SalesOrderItemId = table.Column<int>(type: "int", nullable: true),
                Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Count = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                ConsumedQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                ConsumedCount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockReservationLines", x => x.Id);
                table.ForeignKey(
                    name: "FK_StockReservationLines_Items_ItemId",
                    column: x => x.ItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockReservationLines_SalesOrderItems_SalesOrderItemId",
                    column: x => x.SalesOrderItemId,
                    principalTable: "SalesOrderItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockReservationLines_StockReservations_StockReservationId",
                    column: x => x.StockReservationId,
                    principalTable: "StockReservations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SaleInvoices_SalesOrderId",
            table: "SaleInvoices",
            column: "SalesOrderId",
            filter: "[SalesOrderId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryOrders_SalesOrderId",
            table: "DeliveryOrders",
            column: "SalesOrderId");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryOrders_StockReservationId",
            table: "DeliveryOrders",
            column: "StockReservationId");

        migrationBuilder.AddCheckConstraint(
            name: "CK_DeliveryOrders_SingleSource",
            table: "DeliveryOrders",
            sql: "([SalesOrderId] IS NULL OR [SaleInvoiceId] IS NULL)");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssueItems_DeliveryIssueId",
            table: "DeliveryIssueItems",
            column: "DeliveryIssueId");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssueItems_DeliveryOrderItemId",
            table: "DeliveryIssueItems",
            column: "DeliveryOrderItemId");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssueItems_ItemId",
            table: "DeliveryIssueItems",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssueItems_SalesOrderItemId",
            table: "DeliveryIssueItems",
            column: "SalesOrderItemId");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssues_CustomerId",
            table: "DeliveryIssues",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssues_DeliveryOrderId",
            table: "DeliveryIssues",
            column: "DeliveryOrderId");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssues_IssueNumber",
            table: "DeliveryIssues",
            column: "IssueNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssues_PublicId",
            table: "DeliveryIssues",
            column: "PublicId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssues_SaleInvoiceId",
            table: "DeliveryIssues",
            column: "SaleInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_DeliveryIssues_SalesOrderId",
            table: "DeliveryIssues",
            column: "SalesOrderId");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservationLines_ItemId",
            table: "StockReservationLines",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservationLines_SalesOrderItemId",
            table: "StockReservationLines",
            column: "SalesOrderItemId");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservationLines_StockReservationId",
            table: "StockReservationLines",
            column: "StockReservationId");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_CustomerId",
            table: "StockReservations",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_PublicId",
            table: "StockReservations",
            column: "PublicId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_ReservationNumber",
            table: "StockReservations",
            column: "ReservationNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_SalesOrderId",
            table: "StockReservations",
            column: "SalesOrderId");

        migrationBuilder.AddForeignKey(
            name: "FK_DeliveryOrders_SalesOrders_SalesOrderId",
            table: "DeliveryOrders",
            column: "SalesOrderId",
            principalTable: "SalesOrders",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_DeliveryOrders_StockReservations_StockReservationId",
            table: "DeliveryOrders",
            column: "StockReservationId",
            principalTable: "StockReservations",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        // Backfill: quantities already delivered under the legacy invoice-first flow
        // become DeliveredQty/DeliveredCount on the originating sales order line so the
        // new pending-delivery math starts from the truth instead of from zero.
        migrationBuilder.Sql("""
            UPDATE soi
            SET DeliveredQty = x.Qty, DeliveredCount = x.Cnt
            FROM SalesOrderItems soi
            LEFT JOIN (
                SELECT si.SalesOrderId AS OrderId, doi.ItemId AS ItemId,
                       SUM(doi.Quantity) AS Qty, SUM(doi.Count) AS Cnt
                FROM DeliveryOrders d
                INNER JOIN SaleInvoices si ON si.Id = d.SaleInvoiceId
                INNER JOIN DeliveryOrderItems doi ON doi.DeliveryOrderId = d.Id
                WHERE d.Status = 1 AND si.SalesOrderId IS NOT NULL
                GROUP BY si.SalesOrderId, doi.ItemId
            ) x ON x.OrderId = soi.SalesOrderId AND x.ItemId = soi.ItemId
            WHERE soi.DeliveredQty <> ISNULL(x.Qty, 0)
               OR soi.DeliveredCount <> ISNULL(x.Cnt, 0);
            """);

        migrationBuilder.Sql("""
            UPDATE soi
            SET ReservedQty = 0, ReservedCount = 0
            FROM SalesOrderItems soi
            WHERE soi.ReservedQty < 0 OR soi.ReservedCount < 0;

            UPDATE i
            SET ReservedQuantity = 0, ReservedCount = 0
            FROM Items i
            WHERE i.ReservedQuantity < 0 OR i.ReservedCount < 0;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_DeliveryOrders_SalesOrders_SalesOrderId",
            table: "DeliveryOrders");

        migrationBuilder.DropForeignKey(
            name: "FK_DeliveryOrders_StockReservations_StockReservationId",
            table: "DeliveryOrders");

        migrationBuilder.DropTable(
            name: "DeliveryIssueItems");

        migrationBuilder.DropTable(
            name: "StockReservationLines");

        migrationBuilder.DropTable(
            name: "DeliveryIssues");

        migrationBuilder.DropTable(
            name: "StockReservations");

        migrationBuilder.DropIndex(
            name: "IX_SaleInvoices_SalesOrderId",
            table: "SaleInvoices");

        migrationBuilder.DropIndex(
            name: "IX_DeliveryOrders_SalesOrderId",
            table: "DeliveryOrders");

        migrationBuilder.DropIndex(
            name: "IX_DeliveryOrders_StockReservationId",
            table: "DeliveryOrders");

        migrationBuilder.DropCheckConstraint(
            name: "CK_DeliveryOrders_SingleSource",
            table: "DeliveryOrders");

        migrationBuilder.DropColumn(
            name: "DeliveredCount",
            table: "SalesOrderItems");

        migrationBuilder.DropColumn(
            name: "DeliveredQty",
            table: "SalesOrderItems");

        migrationBuilder.DropColumn(
            name: "ReservedCount",
            table: "SalesOrderItems");

        migrationBuilder.DropColumn(
            name: "ReservedQty",
            table: "SalesOrderItems");

        migrationBuilder.DropColumn(
            name: "PostingMode",
            table: "SaleInvoices");

        migrationBuilder.DropColumn(
            name: "ReservedCount",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "ReservedQuantity",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "SalesOrderId",
            table: "DeliveryOrders");

        migrationBuilder.DropColumn(
            name: "StockReservationId",
            table: "DeliveryOrders");

        migrationBuilder.CreateIndex(
            name: "IX_SaleInvoices_SalesOrderId",
            table: "SaleInvoices",
            column: "SalesOrderId",
            unique: true,
            filter: "[SalesOrderId] IS NOT NULL");
    }
}
