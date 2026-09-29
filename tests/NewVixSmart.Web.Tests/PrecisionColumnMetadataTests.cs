using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NewVixSmart.Web.Data;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// The authoritative guard on decimal precision. Every mapped decimal column in the model is listed
/// here with the scale it is expected to store, and the test asserts both the explicit
/// <c>ColumnType</c> and the <c>Precision</c>/<c>Scale</c> annotations.
/// <para>
/// The model is built against the <b>SQL Server</b> provider, with no connection: the store type is a
/// provider convention, so asserting it against SQLite would prove nothing about the database the
/// system actually runs on, and - worse - SQLite does not enforce decimal scale at all, so a
/// round-trip test on SQLite alone passes happily with a <c>decimal(18,2)</c> mapping. Reading the
/// model metadata is provider-independent and cannot be satisfied by a lucky value comparison, which
/// is why this file - and not <see cref="PrecisionRoundTripTests"/> - is the check that actually
/// catches a regression.
/// </para>
/// <para>
/// The expected widths are written out literally rather than read from
/// <c>DecimalPrecision</c>. A test that took its expectation from the same constant the production
/// code configures from would agree with any width, including the old two-decimal one, and would
/// therefore be vacuous.
/// </para>
/// </summary>
public sealed class PrecisionColumnMetadataTests
{
    private const string MoneyType = "decimal(18,2)";
    private const string PriceType = "decimal(18,3)";
    private const string QuantityType = "decimal(18,4)";
    private const string CostType = "decimal(18,6)";

    /// <summary>
    /// A connection string that is never opened. The model is a pure function of the provider and the
    /// <c>DbContext</c> configuration, so the store type is fully determined without a server.
    /// </summary>
    private const string UnreachableServer =
        "Server=precision-metadata-probe;Database=PrecisionMetadataProbe;Integrated Security=True;TrustServerCertificate=True";

    private static IModel SqlServerModel
    {
        get
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(UnreachableServer).Options;
            using var context = new AppDbContext(options);
            return context.Model;
        }
    }

    private static Dictionary<string, string> MappedDecimals()
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in SqlServerModel.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clr != typeof(decimal)) continue;
                if (property.GetColumnType() is not { } columnType) continue;
                found[$"{entity.ClrType.Name}.{property.Name}"] = columnType;
            }
        }
        return found;
    }

    private static Dictionary<string, string> MoneyColumns() => new(StringComparer.Ordinal)
    {
        ["Payment.Amount"] = MoneyType,
        ["JournalEntryLine.Debit"] = MoneyType,
        ["JournalEntryLine.Credit"] = MoneyType,
        ["BudgetLine.AnnualAmount"] = MoneyType,
        ["Supplier.OpeningBalance"] = MoneyType,
        ["Customer.OpeningBalance"] = MoneyType,
        ["SalePaymentAllocation.AllocatedAmount"] = MoneyType,
        ["PurchasePaymentAllocation.AllocatedAmount"] = MoneyType,
        ["PurchaseReturn.TotalAmount"] = MoneyType,
        ["SaleReturn.TotalAmount"] = MoneyType,

        ["PurchaseInvoice.TotalAmount"] = MoneyType,
        ["PurchaseInvoice.Discount"] = MoneyType,
        ["PurchaseInvoice.Discount2"] = MoneyType,
        ["PurchaseInvoice.Discount3"] = MoneyType,
        ["PurchaseInvoice.Tax"] = MoneyType,
        ["PurchaseInvoice.NetAmount"] = MoneyType,
        ["PurchaseInvoice.PaidAmount"] = MoneyType,

        ["SaleInvoice.TotalAmount"] = MoneyType,
        ["SaleInvoice.Discount"] = MoneyType,
        ["SaleInvoice.Discount2"] = MoneyType,
        ["SaleInvoice.Discount3"] = MoneyType,
        ["SaleInvoice.Tax"] = MoneyType,
        ["SaleInvoice.NetAmount"] = MoneyType,
        ["SaleInvoice.PaidAmount"] = MoneyType,

        ["SaleQuote.TotalAmount"] = MoneyType,
        ["SaleQuote.Discount"] = MoneyType,
        ["SaleQuote.Tax"] = MoneyType,
        ["SaleQuote.NetAmount"] = MoneyType,

        ["PurchaseInvoiceItem.Discount"] = MoneyType,
        ["SaleInvoiceItem.Discount"] = MoneyType,
    };

    private static Dictionary<string, string> UnitPriceColumns() => new(StringComparer.Ordinal)
    {
        ["Item.PurchasePrice"] = PriceType,
        ["Item.SalePrice"] = PriceType,
        ["SupplierQuote.UnitPrice"] = PriceType,
        ["PurchaseInvoiceItem.UnitPrice"] = PriceType,
        ["PurchaseOrderItem.UnitPrice"] = PriceType,
        ["PurchaseReturnItem.UnitPrice"] = PriceType,
        ["SaleInvoiceItem.UnitPrice"] = PriceType,
        ["SaleQuoteItem.UnitPrice"] = PriceType,
        ["SaleReturnItem.UnitPrice"] = PriceType,
        ["SalesOrderItem.UnitPrice"] = PriceType,
    };

    private static Dictionary<string, string> UnitCostColumns() => new(StringComparer.Ordinal)
    {
        ["StockLayer.UnitCost"] = CostType,
        ["StockLayer.CountCost"] = CostType,
        ["StockTransferItem.UnitCost"] = CostType,
    };

    private static Dictionary<string, string> QuantityColumns() => new(StringComparer.Ordinal)
    {
        ["Item.MinCount"] = QuantityType,
        ["Item.MinQuantity"] = QuantityType,
        ["Item.CurrentCount"] = QuantityType,
        ["Item.CurrentQuantity"] = QuantityType,
        ["Item.ReservedCount"] = QuantityType,
        ["Item.ReservedQuantity"] = QuantityType,

        ["PurchaseInvoiceItem.Quantity"] = QuantityType,
        ["PurchaseInvoiceItem.Count"] = QuantityType,
        ["PurchaseOrderItem.Quantity"] = QuantityType,
        ["PurchaseOrderItem.Count"] = QuantityType,
        ["PurchaseOrderItem.ReceivedQty"] = QuantityType,
        ["PurchaseOrderItem.ReceivedCount"] = QuantityType,
        ["PurchaseReturnItem.Quantity"] = QuantityType,
        ["PurchaseReturnItem.Count"] = QuantityType,

        ["SaleInvoiceItem.Quantity"] = QuantityType,
        ["SaleInvoiceItem.Count"] = QuantityType,
        ["SaleQuoteItem.Quantity"] = QuantityType,
        ["SaleQuoteItem.Count"] = QuantityType,
        ["SaleReturnItem.Quantity"] = QuantityType,
        ["SaleReturnItem.Count"] = QuantityType,

        ["SalesOrderItem.Quantity"] = QuantityType,
        ["SalesOrderItem.Count"] = QuantityType,
        ["SalesOrderItem.InvoicedQty"] = QuantityType,
        ["SalesOrderItem.InvoicedCount"] = QuantityType,
        ["SalesOrderItem.ReservedQty"] = QuantityType,
        ["SalesOrderItem.ReservedCount"] = QuantityType,
        ["SalesOrderItem.DeliveredQty"] = QuantityType,
        ["SalesOrderItem.DeliveredCount"] = QuantityType,

        ["DeliveryOrderItem.Quantity"] = QuantityType,
        ["DeliveryOrderItem.Count"] = QuantityType,
        ["DeliveryIssueItem.Quantity"] = QuantityType,
        ["DeliveryIssueItem.Count"] = QuantityType,

        ["StockReservationLine.Quantity"] = QuantityType,
        ["StockReservationLine.Count"] = QuantityType,
        ["StockReservationLine.ConsumedQuantity"] = QuantityType,
        ["StockReservationLine.ConsumedCount"] = QuantityType,

        ["StockMovement.Quantity"] = QuantityType,
        ["StockMovement.Count"] = QuantityType,
        ["StockMovement.BalanceBefore"] = QuantityType,
        ["StockMovement.BalanceAfter"] = QuantityType,
        ["StockMovement.CountBefore"] = QuantityType,
        ["StockMovement.CountAfter"] = QuantityType,

        ["StockLayer.Qty"] = QuantityType,
        ["StockLayer.Count"] = QuantityType,
        ["StockLayer.RemainingQty"] = QuantityType,
        ["StockLayer.RemainingCount"] = QuantityType,

        ["StockTransferItem.Quantity"] = QuantityType,
        ["StockTransferItem.Count"] = QuantityType,

        ["InventoryAdjustment.NewCount"] = QuantityType,
        ["InventoryAdjustment.NewQuantity"] = QuantityType,
    };

    /// <summary>
    /// Every mapped decimal column is accounted for, at the width it is meant to store. A column that
    /// appears in the model but not in the expected map fails here, which is what stops a new decimal
    /// property from quietly inheriting the old two-decimal default.
    /// </summary>
    [Fact]
    public void Every_mapped_decimal_column_has_the_expected_store_type()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in new[] { MoneyColumns(), UnitPriceColumns(), UnitCostColumns(), QuantityColumns() })
        {
            foreach (var (name, type) in group)
            {
                Assert.False(expected.ContainsKey(name), $"'{name}' is listed under two different widths.");
                expected[name] = type;
            }
        }

        var actual = MappedDecimals();

        var unlisted = actual.Keys.Where(k => !expected.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.True(unlisted.Length == 0,
            "These mapped decimal columns have no declared width, so they are still on the model default: "
            + string.Join(", ", unlisted));

        var stale = expected.Keys.Where(k => !actual.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.True(stale.Length == 0,
            "These columns are expected in the model but are not mapped decimals: " + string.Join(", ", stale));

        var wrong = actual.Keys.Where(k => actual[k] != expected[k])
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => $"{k}: expected {expected[k]}, found {actual[k]}")
            .ToArray();
        Assert.True(wrong.Length == 0, "Wrong decimal store type: " + string.Join("; ", wrong));
    }

    /// <summary>
    /// The same widths, asserted on the <c>Precision</c> and <c>Scale</c> annotations rather than the
    /// store-type string. These are the values the provider turns into a parameter type and a column
    /// definition, so a mismatch here is a mismatch in the DDL even when the string happens to look
    /// right.
    /// </summary>
    [Fact]
    public void Every_mapped_decimal_column_has_the_expected_precision_and_scale()
    {
        var expected = new Dictionary<string, (int Precision, int Scale)>(StringComparer.Ordinal);
        foreach (var group in new[] { MoneyColumns(), UnitPriceColumns(), UnitCostColumns(), QuantityColumns() })
        {
            foreach (var (name, type) in group)
            {
                var (precision, scale) = ParseColumnType(type);
                expected[name] = (precision, scale);
            }
        }

        var model = SqlServerModel;
        var problems = new List<string>();

        foreach (var entity in model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clr != typeof(decimal)) continue;
                if (property.GetColumnType() is null) continue;

                var name = $"{entity.ClrType.Name}.{property.Name}";
                if (!expected.TryGetValue(name, out var want)) continue;

                var gotPrecision = property.GetPrecision();
                var gotScale = property.GetScale();
                if (gotPrecision != want.Precision || gotScale != want.Scale)
                {
                    problems.Add($"{name}: expected decimal({want.Precision},{want.Scale}), "
                        + $"annotations say ({gotPrecision?.ToString() ?? "null"},{gotScale?.ToString() ?? "null"})");
                }
            }
        }

        Assert.True(problems.Count == 0, "Precision/scale annotations disagree with the policy: "
            + string.Join("; ", problems));
    }

    /// <summary>
    /// The three-decimal defect, stated as a single readable failure: a quantity and a unit price
    /// carrying a third decimal must be storable. At the old <c>decimal(18,2)</c> this failed.
    /// </summary>
    [Fact]
    public void Quantity_and_unit_price_columns_can_store_a_third_decimal()
    {
        var model = SqlServerModel;

        foreach (var name in QuantityColumns().Keys)
        {
            Assert.True(ScaleOf(model, name) >= 3,
                $"{name} still stores two decimals, so a quantity such as 0.125 is silently rounded on write.");
        }

        foreach (var name in UnitPriceColumns().Keys)
        {
            Assert.True(ScaleOf(model, name) >= 3,
                $"{name} still stores two decimals, so a price such as 12.345 is silently rounded on write.");
        }
    }

    /// <summary>
    /// The ledger half of the policy: money stays on the piastre. Widening it would contradict
    /// <c>Money.Format</c>, which renders two decimals, and the 0.005 money materiality constant the
    /// payment allocator is balanced against - so a widening here is a regression, not an improvement.
    /// </summary>
    [Fact]
    public void Money_columns_are_never_widened_beyond_two_decimals()
    {
        var model = SqlServerModel;

        foreach (var name in MoneyColumns().Keys)
        {
            Assert.True(ScaleOf(model, name) == 2,
                $"{name} no longer stores exactly two decimals; the piastre is the smallest money unit "
                + "the system shows or reconciles.");
        }
    }

    /// <summary>
    /// The per-unit cost that drives stock valuation was already stored wider than a price, at six
    /// decimals. It is the reason a quantity could not stay at two decimals: a three-decimal cost
    /// multiplied by a three-decimal quantity is a nine-decimal product, and rounding the quantity
    /// before the multiply is what made the valuation drift. Six is left exactly as it was.
    /// </summary>
    [Fact]
    public void Unit_cost_columns_keep_their_existing_six_decimal_width()
    {
        var model = SqlServerModel;

        foreach (var name in UnitCostColumns().Keys)
        {
            Assert.True(ScaleOf(model, name) == 6, $"{name} must keep six decimals; narrowing it would lose valuation precision.");
        }
    }

    private static int ScaleOf(IModel model, string qualifiedName)
    {
        var dot = qualifiedName.IndexOf('.');
        var entityName = qualifiedName[..dot];
        var propertyName = qualifiedName[(dot + 1)..];

        var entity = Assert.Single(model.GetEntityTypes(), t => t.ClrType.Name == entityName);
        return Assert.IsType<int>(entity.FindProperty(propertyName)!.GetScale());
    }

    private static (int Precision, int Scale) ParseColumnType(string columnType)
    {
        var parts = columnType[(columnType.IndexOf('(') + 1)..^1].Split(',');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
}
