using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddTypedPaymentAllocations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PaymentAllocations");

        migrationBuilder.CreateTable(
            name: "PurchasePaymentAllocations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PaymentId = table.Column<int>(type: "int", nullable: false),
                PurchaseInvoiceId = table.Column<int>(type: "int", nullable: false),
                AllocatedBaseAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                ExchangeRateAtSettlement = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                FxGain = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                FxLoss = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchasePaymentAllocations", x => x.Id);
                table.ForeignKey(
                    name: "FK_PurchasePaymentAllocations_Payments_PaymentId",
                    column: x => x.PaymentId,
                    principalTable: "Payments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PurchasePaymentAllocations_PurchaseInvoices_PurchaseInvoiceId",
                    column: x => x.PurchaseInvoiceId,
                    principalTable: "PurchaseInvoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SalePaymentAllocations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PaymentId = table.Column<int>(type: "int", nullable: false),
                SaleInvoiceId = table.Column<int>(type: "int", nullable: false),
                AllocatedBaseAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                ExchangeRateAtSettlement = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                FxGain = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                FxLoss = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalePaymentAllocations", x => x.Id);
                table.ForeignKey(
                    name: "FK_SalePaymentAllocations_Payments_PaymentId",
                    column: x => x.PaymentId,
                    principalTable: "Payments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SalePaymentAllocations_SaleInvoices_SaleInvoiceId",
                    column: x => x.SaleInvoiceId,
                    principalTable: "SaleInvoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PurchasePaymentAllocations_PaymentId_PurchaseInvoiceId",
            table: "PurchasePaymentAllocations",
            columns: new[] { "PaymentId", "PurchaseInvoiceId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PurchasePaymentAllocations_PurchaseInvoiceId",
            table: "PurchasePaymentAllocations",
            column: "PurchaseInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_SalePaymentAllocations_PaymentId_SaleInvoiceId",
            table: "SalePaymentAllocations",
            columns: new[] { "PaymentId", "SaleInvoiceId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SalePaymentAllocations_SaleInvoiceId",
            table: "SalePaymentAllocations",
            column: "SaleInvoiceId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PurchasePaymentAllocations");

        migrationBuilder.DropTable(
            name: "SalePaymentAllocations");

        migrationBuilder.CreateTable(
            name: "PaymentAllocations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PaymentId = table.Column<int>(type: "int", nullable: false),
                AllocatedBaseAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ExchangeRateAtSettlement = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                FxGain = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                FxLoss = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                InvoiceId = table.Column<int>(type: "int", nullable: false),
                InvoiceType = table.Column<short>(type: "smallint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                table.ForeignKey(
                    name: "FK_PaymentAllocations_Payments_PaymentId",
                    column: x => x.PaymentId,
                    principalTable: "Payments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentAllocations_InvoiceType_InvoiceId",
            table: "PaymentAllocations",
            columns: new[] { "InvoiceType", "InvoiceId" });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentAllocations_PaymentId_InvoiceType_InvoiceId",
            table: "PaymentAllocations",
            columns: new[] { "PaymentId", "InvoiceType", "InvoiceId" });
    }
}
