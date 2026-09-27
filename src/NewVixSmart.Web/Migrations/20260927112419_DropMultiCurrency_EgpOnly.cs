using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// This migration is NOT purely regenerable. Three data operations have no model
    /// representation and therefore never appear in the snapshot:
    ///   1. re-denominating [Payments].[Amount] from [BaseAmount] (Up),
    ///   2. the foreign-currency guard that THROWs (Up),
    ///   3. deactivating / reactivating GL 4400 and 8400 (Up/Down).
    /// If this file is ever removed and re-scaffolded, those steps are lost silently because
    /// `has-pending-model-changes` stays clean. Keep them by hand, and keep the historical
    /// migration 20260926170013_AddSalesReservationDeliveryIssueFlow untouched.
    /// </remarks>
    public partial class DropMultiCurrency_EgpOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 0. Data safety, before any column disappears ----------------
            // At HEAD, Payments.Amount was the amount in the *transaction* currency and
            // Payments.BaseAmount the same payment in EGP, which is the only value this
            // system understands now. Re-denominate first, otherwise every historic
            // foreign receipt is silently restated as if it were already EGP.
            migrationBuilder.Sql(
                """
                UPDATE [Payments] SET [Amount] = [BaseAmount];
                """);

            // The document tables stored their value in the transaction currency and had no
            // EGP column: the ledger was posted as value x rate, so the sub-ledger and the GL
            // only agree for EGP documents. There is no lossless automatic re-denomination of
            // every money column, so fail loudly and let the operator decide. A rate of 1 or
            // NULL means the document was already EGP and needs no attention.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [SaleInvoices]      WHERE [ExchangeRate] IS NOT NULL AND [ExchangeRate] <> 1)
                 OR EXISTS (SELECT 1 FROM [PurchaseInvoices] WHERE [ExchangeRate] IS NOT NULL AND [ExchangeRate] <> 1)
                 OR EXISTS (SELECT 1 FROM [SaleReturns]      WHERE [ExchangeRate] IS NOT NULL AND [ExchangeRate] <> 1)
                 OR EXISTS (SELECT 1 FROM [PurchaseReturns]  WHERE [ExchangeRate] IS NOT NULL AND [ExchangeRate] <> 1)
                 OR EXISTS (SELECT 1 FROM [SaleQuotes]       WHERE [ExchangeRate] IS NOT NULL AND [ExchangeRate] <> 1)
                 OR EXISTS (SELECT 1 FROM [SalesOrders]      WHERE [ExchangeRate] IS NOT NULL AND [ExchangeRate] <> 1)
                    THROW 51000, N'EGP-only: this database still holds documents booked in a foreign currency. Re-denominate them (value x rate) and back-date their ExchangeRate to 1 before retrying this migration.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Currencies_CurrencyId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Currencies_CurrencyId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoices_Currencies_CurrencyId",
                table: "PurchaseInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturns_Currencies_CurrencyId",
                table: "PurchaseReturns");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleInvoices_Currencies_CurrencyId",
                table: "SaleInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleQuotes_Currencies_CurrencyId",
                table: "SaleQuotes");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleReturns_Currencies_CurrencyId",
                table: "SaleReturns");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Currencies_CurrencyId",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Currencies_CurrencyId",
                table: "Suppliers");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_CurrencyId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_CurrencyId",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SaleReturns_CurrencyId",
                table: "SaleReturns");

            migrationBuilder.DropIndex(
                name: "IX_SaleQuotes_CurrencyId",
                table: "SaleQuotes");

            migrationBuilder.DropIndex(
                name: "IX_SaleInvoices_CurrencyId",
                table: "SaleInvoices");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_CurrencyId",
                table: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_CurrencyId",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CurrencyId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CurrencyId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "SaleReturns");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "SaleReturns");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "SaleQuotes");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "SaleQuotes");

            // Renamed, not dropped: the settled amount is real data that must survive.
            migrationBuilder.RenameColumn(
                name: "AllocatedBaseAmount",
                table: "SalePaymentAllocations",
                newName: "AllocatedAmount");

            migrationBuilder.DropColumn(
                name: "ExchangeRateAtSettlement",
                table: "SalePaymentAllocations");

            migrationBuilder.DropColumn(
                name: "FxGain",
                table: "SalePaymentAllocations");

            migrationBuilder.DropColumn(
                name: "FxLoss",
                table: "SalePaymentAllocations");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "PurchaseReturns");

            // Renamed, not dropped: the settled amount is real data that must survive.
            migrationBuilder.RenameColumn(
                name: "AllocatedBaseAmount",
                table: "PurchasePaymentAllocations",
                newName: "AllocatedAmount");

            migrationBuilder.DropColumn(
                name: "ExchangeRateAtSettlement",
                table: "PurchasePaymentAllocations");

            migrationBuilder.DropColumn(
                name: "FxGain",
                table: "PurchasePaymentAllocations");

            migrationBuilder.DropColumn(
                name: "FxLoss",
                table: "PurchasePaymentAllocations");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "BaseAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "Customers");

            // The FX gain/loss accounts can still carry posted history in an existing
            // database, so retire them instead of dropping rows referenced by journal lines.
            migrationBuilder.Sql(
                """
                UPDATE [GLAccounts] SET [IsActive] = 0
                WHERE [Code] IN (N'4400', N'8400');
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// This rollback is lossy by construction: EF restores [Payments].[BaseAmount],
        /// [FxGain]/[FxLoss] and [ExchangeRateAtSettlement] as zero/NULL and recreates an
        /// empty Currencies table, because the values that used to live there were destroyed
        /// by Up. Do not roll back a production database on the assumption that it becomes
        /// correct again — take a backup and re-enter FX data if the old code is really needed.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The exact inverse would be "reactivate only what Up deactivated". A row that was
            // already inactive before this migration is indistinguishable from one Up turned off,
            // so this is the closest state-preserving form available without an audit table.
            migrationBuilder.Sql(
                """
                UPDATE [GLAccounts] SET [IsActive] = 1
                WHERE [Code] IN (N'4400', N'8400');
                """);

            migrationBuilder.RenameColumn(
                name: "AllocatedAmount",
                table: "SalePaymentAllocations",
                newName: "AllocatedBaseAmount");

            migrationBuilder.RenameColumn(
                name: "AllocatedAmount",
                table: "PurchasePaymentAllocations",
                newName: "AllocatedBaseAmount");

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "Suppliers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "SalesOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "SalesOrders",
                type: "decimal(18,6)",
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

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "SaleQuotes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "SaleQuotes",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateAtSettlement",
                table: "SalePaymentAllocations",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FxGain",
                table: "SalePaymentAllocations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FxLoss",
                table: "SalePaymentAllocations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "SaleInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "SaleInvoices",
                type: "decimal(18,6)",
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

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateAtSettlement",
                table: "PurchasePaymentAllocations",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FxGain",
                table: "PurchasePaymentAllocations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FxLoss",
                table: "PurchasePaymentAllocations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "PurchaseInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "PurchaseInvoices",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseAmount",
                table: "Payments",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Payments",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsBase = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_CurrencyId",
                table: "Suppliers",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CurrencyId",
                table: "SalesOrders",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_CurrencyId",
                table: "SaleReturns",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleQuotes_CurrencyId",
                table: "SaleQuotes",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_CurrencyId",
                table: "SaleInvoices",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_CurrencyId",
                table: "PurchaseReturns",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CurrencyId",
                table: "PurchaseInvoices",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CurrencyId",
                table: "Payments",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CurrencyId",
                table: "Customers",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                table: "Currencies",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Currencies_CurrencyId",
                table: "Customers",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Currencies_CurrencyId",
                table: "Payments",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoices_Currencies_CurrencyId",
                table: "PurchaseInvoices",
                column: "CurrencyId",
                principalTable: "Currencies",
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
                name: "FK_SaleInvoices_Currencies_CurrencyId",
                table: "SaleInvoices",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleQuotes_Currencies_CurrencyId",
                table: "SaleQuotes",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleReturns_Currencies_CurrencyId",
                table: "SaleReturns",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_Currencies_CurrencyId",
                table: "SalesOrders",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Currencies_CurrencyId",
                table: "Suppliers",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
