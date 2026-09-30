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
/// <para>
/// A quantity and a price are at 20, not at 18, and the digits are written out because the
/// regression this file exists to catch is not a wrong scale but a range that shrank. Holding
/// the precision at 18 while raising the scale cut the integer digits from 16 to 14.
/// <see cref="Quantity_and_unit_price_columns_keep_their_original_sixteen_integer_digits"/>
/// states that budget as a number in its own right.
/// </para>
/// </summary>
public sealed class PrecisionColumnMetadataTests
{
    private const string _moneyType = "decimal(18,2)";
    private const string _priceType = "decimal(20,3)";
    private const string _quantityType = "decimal(20,4)";
    private const string _costType = "decimal(18,6)";

    /// <summary>
    /// A connection string that is never opened. The model is a pure function of the provider and the
    /// <c>DbContext</c> configuration, so the store type is fully determined without a server.
    /// </summary>
    private const string _unreachableServer =
        "Server=precision-metadata-probe;Database=PrecisionMetadataProbe;Integrated Security=True;TrustServerCertificate=True";

    private static IModel SqlServerModel
    {
        get
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_unreachableServer).Options;
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
                if (clr != typeof(decimal))
                {
                    continue;
                }

                if (property.GetColumnType() is not { } columnType)
                {
                    continue;
                }

                found[$"{entity.ClrType.Name}.{property.Name}"] = columnType;
            }
        }
        return found;
    }

    private static Dictionary<string, string> MoneyColumns() => new(StringComparer.Ordinal)
    {
        ["Payment.Amount"] = _moneyType,
        ["JournalEntryLine.Debit"] = _moneyType,
        ["JournalEntryLine.Credit"] = _moneyType,
        ["BudgetLine.AnnualAmount"] = _moneyType,
        ["Supplier.OpeningBalance"] = _moneyType,
        ["Customer.OpeningBalance"] = _moneyType,
        ["SalePaymentAllocation.AllocatedAmount"] = _moneyType,
        ["PurchasePaymentAllocation.AllocatedAmount"] = _moneyType,
        ["PurchaseReturn.TotalAmount"] = _moneyType,
        ["SaleReturn.TotalAmount"] = _moneyType,

        ["PurchaseInvoice.TotalAmount"] = _moneyType,
        ["PurchaseInvoice.Discount"] = _moneyType,
        ["PurchaseInvoice.Discount2"] = _moneyType,
        ["PurchaseInvoice.Discount3"] = _moneyType,
        ["PurchaseInvoice.Tax"] = _moneyType,
        ["PurchaseInvoice.NetAmount"] = _moneyType,
        ["PurchaseInvoice.PaidAmount"] = _moneyType,

        ["SaleInvoice.TotalAmount"] = _moneyType,
        ["SaleInvoice.Discount"] = _moneyType,
        ["SaleInvoice.Discount2"] = _moneyType,
        ["SaleInvoice.Discount3"] = _moneyType,
        ["SaleInvoice.Tax"] = _moneyType,
        ["SaleInvoice.NetAmount"] = _moneyType,
        ["SaleInvoice.PaidAmount"] = _moneyType,

        ["SaleQuote.TotalAmount"] = _moneyType,
        ["SaleQuote.Discount"] = _moneyType,
        ["SaleQuote.Tax"] = _moneyType,
        ["SaleQuote.NetAmount"] = _moneyType,

        ["PurchaseInvoiceItem.Discount"] = _moneyType,
        ["SaleInvoiceItem.Discount"] = _moneyType,
    };

    private static Dictionary<string, string> UnitPriceColumns() => new(StringComparer.Ordinal)
    {
        ["Item.PurchasePrice"] = _priceType,
        ["Item.SalePrice"] = _priceType,
        ["SupplierQuote.UnitPrice"] = _priceType,
        ["PurchaseInvoiceItem.UnitPrice"] = _priceType,
        ["PurchaseOrderItem.UnitPrice"] = _priceType,
        ["PurchaseReturnItem.UnitPrice"] = _priceType,
        ["SaleInvoiceItem.UnitPrice"] = _priceType,
        ["SaleQuoteItem.UnitPrice"] = _priceType,
        ["SaleReturnItem.UnitPrice"] = _priceType,
        ["SalesOrderItem.UnitPrice"] = _priceType,
    };

    private static Dictionary<string, string> UnitCostColumns() => new(StringComparer.Ordinal)
    {
        ["StockLayer.UnitCost"] = _costType,
        ["StockLayer.CountCost"] = _costType,
        ["StockTransferItem.UnitCost"] = _costType,
    };

    private static Dictionary<string, string> QuantityColumns() => new(StringComparer.Ordinal)
    {
        ["Item.MinCount"] = _quantityType,
        ["Item.MinQuantity"] = _quantityType,
        ["Item.CurrentCount"] = _quantityType,
        ["Item.CurrentQuantity"] = _quantityType,
        ["Item.ReservedCount"] = _quantityType,
        ["Item.ReservedQuantity"] = _quantityType,

        ["PurchaseInvoiceItem.Quantity"] = _quantityType,
        ["PurchaseInvoiceItem.Count"] = _quantityType,
        ["PurchaseOrderItem.Quantity"] = _quantityType,
        ["PurchaseOrderItem.Count"] = _quantityType,
        ["PurchaseOrderItem.ReceivedQty"] = _quantityType,
        ["PurchaseOrderItem.ReceivedCount"] = _quantityType,
        ["PurchaseReturnItem.Quantity"] = _quantityType,
        ["PurchaseReturnItem.Count"] = _quantityType,

        ["SaleInvoiceItem.Quantity"] = _quantityType,
        ["SaleInvoiceItem.Count"] = _quantityType,
        ["SaleQuoteItem.Quantity"] = _quantityType,
        ["SaleQuoteItem.Count"] = _quantityType,
        ["SaleReturnItem.Quantity"] = _quantityType,
        ["SaleReturnItem.Count"] = _quantityType,

        ["SalesOrderItem.Quantity"] = _quantityType,
        ["SalesOrderItem.Count"] = _quantityType,
        ["SalesOrderItem.InvoicedQty"] = _quantityType,
        ["SalesOrderItem.InvoicedCount"] = _quantityType,
        ["SalesOrderItem.ReservedQty"] = _quantityType,
        ["SalesOrderItem.ReservedCount"] = _quantityType,
        ["SalesOrderItem.DeliveredQty"] = _quantityType,
        ["SalesOrderItem.DeliveredCount"] = _quantityType,

        ["DeliveryOrderItem.Quantity"] = _quantityType,
        ["DeliveryOrderItem.Count"] = _quantityType,
        ["DeliveryIssueItem.Quantity"] = _quantityType,
        ["DeliveryIssueItem.Count"] = _quantityType,

        ["StockReservationLine.Quantity"] = _quantityType,
        ["StockReservationLine.Count"] = _quantityType,
        ["StockReservationLine.ConsumedQuantity"] = _quantityType,
        ["StockReservationLine.ConsumedCount"] = _quantityType,

        ["StockMovement.Quantity"] = _quantityType,
        ["StockMovement.Count"] = _quantityType,
        ["StockMovement.BalanceBefore"] = _quantityType,
        ["StockMovement.BalanceAfter"] = _quantityType,
        ["StockMovement.CountBefore"] = _quantityType,
        ["StockMovement.CountAfter"] = _quantityType,

        ["StockLayer.Qty"] = _quantityType,
        ["StockLayer.Count"] = _quantityType,
        ["StockLayer.RemainingQty"] = _quantityType,
        ["StockLayer.RemainingCount"] = _quantityType,

        ["StockTransferItem.Quantity"] = _quantityType,
        ["StockTransferItem.Count"] = _quantityType,

        ["InventoryAdjustment.NewCount"] = _quantityType,
        ["InventoryAdjustment.NewQuantity"] = _quantityType,
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
                if (clr != typeof(decimal))
                {
                    continue;
                }

                if (property.GetColumnType() is null)
                {
                    continue;
                }

                var name = $"{entity.ClrType.Name}.{property.Name}";
                if (!expected.TryGetValue(name, out var want))
                {
                    continue;
                }

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
    /// The regression, stated in the unit that actually broke. A <c>decimal(18,2)</c> column holds
    /// sixteen integer digits; raising the scale to 4 while holding the precision at 18 drops that to
    /// fourteen, so a quantity that the old schema stored fine raised "Arithmetic overflow error
    /// converting numeric to data type numeric" on write. The widening was supposed to add decimals,
    /// and it did - by quietly dividing the range by a hundred. This asserts the digit budget
    /// directly, so a future change that trades integral digits for scale fails here rather than in
    /// production.
    /// <para>
    /// The budget is "at least the sixteen the piastre-grid schema had", not "exactly sixteen": at
    /// <c>decimal(20,3)</c> a price has 20 - 3 = 17 integral digits, so a price is strictly better
    /// off than before while a quantity at <c>decimal(20,4)</c> has 20 - 4 = 16 and is exactly level.
    /// Both are stated, so the two scales cannot drift into each other unnoticed.
    /// </para>
    /// </summary>
    [Fact]
    public void Quantity_and_unit_price_columns_keep_their_original_sixteen_integer_digits()
    {
        var model = SqlServerModel;

        foreach (var name in QuantityColumns().Keys)
        {
            var (precision, scale) = ParseColumnType(QuantityColumns()[name]);
            Assert.Equal(20 - 4, precision - scale);
            Assert.Equal(_quantityType, ColumnTypeOf(model, name));
        }

        foreach (var name in UnitPriceColumns().Keys)
        {
            var (precision, scale) = ParseColumnType(UnitPriceColumns()[name]);
            Assert.Equal(20 - 3, precision - scale);
            Assert.Equal(_priceType, ColumnTypeOf(model, name));
        }

        // The floor, stated over both groups: no widened column may hold fewer integer digits than
        // the decimal(18,2) schema it replaced. This is the assertion the original migration failed.
        foreach (var group in new[] { QuantityColumns(), UnitPriceColumns() })
        {
            foreach (var (name, type) in group)
            {
                var (precision, scale) = ParseColumnType(type);
                Assert.True(precision - scale >= 16,
                    $"{name} is decimal({precision},{scale}), so it holds only {precision - scale} integer "
                    + "digits where decimal(18,2) held 16. Widening the scale must not come out of the range.");
            }
        }

        // The control: money keeps exactly the 16 integral digits it always had, at 18. If the
        // quantity and price columns had kept precision 18, they would have matched this pair, and
        // nothing above would have noticed the difference in digits.
        foreach (var name in MoneyColumns().Keys)
        {
            var (precision, scale) = ParseColumnType(MoneyColumns()[name]);
            Assert.Equal(16, precision - scale);
        }
    }

    /// <summary>
    /// The ledger half of the policy: money stays on the piastre. Widening it would contradict
    /// <c>Money.Format</c>, which renders two decimals, and the 0.005 money materiality constant the
    /// payment allocator is balanced against - so a widening here is a regression, not an improvement.
    /// The precision is pinned as well as the scale: this migration widened the quantity and price
    /// columns, and money must be the control that proves it.
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
            Assert.Equal(_moneyType, ColumnTypeOf(model, name));
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
        var (entity, property) = Locate(model, qualifiedName);
        return Assert.IsType<int>(property.GetScale());
    }

    private static string ColumnTypeOf(IModel model, string qualifiedName)
    {
        var (_, property) = Locate(model, qualifiedName);
        return Assert.IsType<string>(property.GetColumnType());
    }

    private static (IEntityType Entity, IProperty Property) Locate(IModel model, string qualifiedName)
    {
        var dot = qualifiedName.IndexOf('.');
        var entityName = qualifiedName[..dot];
        var propertyName = qualifiedName[(dot + 1)..];

        var entity = Assert.Single(model.GetEntityTypes(), t => t.ClrType.Name == entityName);
        return (entity, entity.FindProperty(propertyName)!);
    }

    private static (int Precision, int Scale) ParseColumnType(string columnType)
    {
        var parts = columnType[(columnType.IndexOf('(') + 1)..^1].Split(',');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
}
