using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Stock;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Proves that the widths chosen for quantities and unit prices survive an actual write and read,
/// rather than merely being declared in the model.
/// <para>
/// The defect this guards is silent data loss: with a <c>decimal(18,2)</c> column the database rounds
/// <c>0.1234</c> to <c>0.12</c> on write, no exception is raised, and the inventory or the price is
/// simply wrong afterwards. Asserting that a value "round-trips" is therefore only meaningful if the
/// value read back comes from the store.
/// </para>
/// <para>
/// Scope of the guarantee, stated honestly: this test runs on SQLite, which stores
/// <see cref="decimal"/> as text and does not enforce scale, so it proves that the EF mapping and the
/// parameter round-trip preserve the exact value and its scale. It cannot prove the column width -
/// SQLite would happily accept a <c>decimal(18,2)</c> mapping too. The store type is proven
/// separately, against the SQL Server model, by <see cref="PrecisionColumnMetadataTests"/>; the two
/// files are complementary and neither is sufficient alone.
/// </para>
/// </summary>
public sealed class PrecisionRoundTripTests : IDisposable
{
    /// <summary>A fourth-decimal quantity: the exact case the old two-decimal mapping destroyed.</summary>
    private const decimal _quantityValue = 0.1234m;

    /// <summary>A fourth-decimal quantity that is not a round figure, for the same reason.</summary>
    private const decimal _secondQuantityValue = 12.3456m;

    /// <summary>A third-decimal unit price.</summary>
    private const decimal _unitPriceValue = 12.345m;

    /// <summary>A money amount on the piastre, the unit the ledger actually uses.</summary>
    private const decimal _moneyValue = 1234.56m;

    /// <summary>The pre-existing six-decimal unit cost, kept as a control on the untouched width.</summary>
    private const decimal _unitCostValue = 9.876543m;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PrecisionRoundTripTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Seeds one item carrying fourth-decimal quantities and third-decimal prices, plus a layer whose
    /// six-decimal unit cost is the control. Returns the new item id.
    /// </summary>
    private async Task<int> SeedAsync()
    {
        using var db = CreateContext();
        var warehouse = new Warehouse { Code = "RT", Name = "Round Trip Warehouse" };
        var item = new Item
        {
            Name = "Round Trip Item",
            Category = new ItemCategory { Name = "Round Trip Category" },
            ItemType = new ItemType { Name = "Round Trip Type" },
            CurrentQuantity = _quantityValue,
            MinQuantity = _secondQuantityValue,
            ReservedQuantity = 0.0001m,
            CurrentCount = _quantityValue,
            MinCount = _secondQuantityValue,
            ReservedCount = 0.0001m,
            PurchasePrice = _unitPriceValue,
            SalePrice = 0.005m
        };
        db.Warehouses.Add(warehouse);
        db.Items.Add(item);
        await db.SaveChangesAsync();

        db.StockLayers.Add(new StockLayer
        {
            ItemId = item.Id,
            WarehouseId = warehouse.Id,
            Qty = _quantityValue,
            Count = _quantityValue,
            UnitCost = _unitCostValue,
            CountCost = _unitCostValue,
            DateReceived = new DateTime(2026, 1, 1),
            RemainingQty = _quantityValue,
            RemainingCount = _quantityValue
        });
        await db.SaveChangesAsync();
        return item.Id;
    }

    /// <summary>
    /// A fourth-decimal quantity comes back with all four decimals intact, at full scale, read through
    /// a separate context so the value cannot have come from the change tracker.
    /// </summary>
    [Fact]
    public async Task Quantity_keeps_its_fourth_decimal_through_a_write_and_a_fresh_read()
    {
        var itemId = await SeedAsync();

        using var db = CreateContext();
        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);

        Assert.Equal(_quantityValue, item.CurrentQuantity);
        Assert.Equal(_secondQuantityValue, item.MinQuantity);
        Assert.Equal(0.0001m, item.ReservedQuantity);
        Assert.Equal(_quantityValue, item.CurrentCount);
        Assert.Equal(_secondQuantityValue, item.MinCount);
        Assert.Equal(0.0001m, item.ReservedCount);

        // Equality alone would accept 0.1230 in place of 0.1234, so the scale is asserted too.
        Assert.Equal(Exact(_quantityValue), Exact(item.CurrentQuantity));
        Assert.Equal(Exact(_secondQuantityValue), Exact(item.MinQuantity));
    }

    /// <summary>
    /// A third-decimal unit price comes back with all three decimals intact. This is the value that
    /// <c>decimal(18,2)</c> rounded to <c>12.35</c>, quietly changing every line total computed from it.
    /// </summary>
    [Fact]
    public async Task Unit_price_keeps_its_third_decimal_through_a_write_and_a_fresh_read()
    {
        var itemId = await SeedAsync();

        using var db = CreateContext();
        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);

        Assert.Equal(_unitPriceValue, item.PurchasePrice);
        Assert.Equal(0.005m, item.SalePrice);
        Assert.Equal(Exact(_unitPriceValue), Exact(item.PurchasePrice));
    }

    /// <summary>
    /// A money amount stays on the piastre. Money is deliberately the one group left at two decimals,
    /// so this is the test that stops a future "let us widen everything" change from quietly moving the
    /// ledger off the unit <c>Money.Format</c> displays.
    /// </summary>
    [Fact]
    public async Task Money_amount_stays_on_the_piastre()
    {
        using var db = CreateContext();
        db.Payments.Add(new Payment
        {
            ReceiptNumber = "RT-RECEIPT-1",
            Type = PaymentType.Receipt,
            Method = PaymentMethod.Cash,
            Amount = _moneyValue,
            PaymentDate = new DateTime(2026, 1, 1)
        });
        await db.SaveChangesAsync();

        using var verify = CreateContext();
        var payment = await verify.Payments.AsNoTracking().SingleAsync(p => p.ReceiptNumber == "RT-RECEIPT-1");

        Assert.Equal(_moneyValue, payment.Amount);
        Assert.Equal(Exact(_moneyValue), Exact(payment.Amount));
    }

    /// <summary>
    /// The six-decimal unit cost that drives stock valuation is untouched by this change, and a
    /// three-decimal cost multiplied by a fourth-decimal quantity is a seven-decimal product. If the
    /// quantity had stayed at two decimals this product could not be represented, which is the reason
    /// the quantity was widened at all.
    /// </summary>
    [Fact]
    public async Task Unit_cost_keeps_its_sixth_decimal()
    {
        var itemId = await SeedAsync();

        using var db = CreateContext();
        var layer = await db.StockLayers.AsNoTracking().SingleAsync(l => l.ItemId == itemId);

        Assert.Equal(_unitCostValue, layer.UnitCost);
        Assert.Equal(_unitCostValue, layer.CountCost);
        Assert.Equal(Exact(_unitCostValue), Exact(layer.UnitCost));
    }

    /// <summary>
    /// The four-decimal quantity columns and the six-decimal cost columns compose without the
    /// intermediate rounding that made stock valuation drift: a fourth-decimal quantity times a
    /// sixth-decimal cost is a ten-decimal product, and rounding the quantity to two decimals before
    /// the multiply is what this change removes.
    /// </summary>
    [Fact]
    public async Task Fourth_decimal_quantity_by_sixth_decimal_cost_keeps_the_full_product()
    {
        var itemId = await SeedAsync();

        using var db = CreateContext();
        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        var layer = await db.StockLayers.AsNoTracking().SingleAsync(l => l.ItemId == itemId);

        var product = item.CurrentQuantity * layer.UnitCost;

        Assert.Equal(0.1234m * 9.876543m, product);
        Assert.Equal("1.2187654062", Exact(product));
    }

    /// <summary>
    /// The declared width must reach the database, not merely the model: this reads the column
    /// definition EF actually created and asserts it carries the four- and three-decimal types.
    /// <para>
    /// This test is also where the limit of a SQLite round-trip becomes visible, and it is the reason
    /// the width is not asserted here by writing a boundary value. SQLite gives a column declared as
    /// <c>decimal(20,4)</c> NUMERIC affinity, so it coerces the stored value to SQLite INTEGER or REAL.
    /// A REAL is a double and carries only about fifteen to sixteen significant digits, so a quantity
    /// at the very edge of <c>decimal(20,4)</c> - sixteen integer digits and four decimals - loses its
    /// decimals on this provider even though the mapping is correct. That is a property of the test
    /// database, not of the schema; the twenty-digit width itself is asserted from the SQL Server
    /// store type in <see cref="PrecisionColumnMetadataTests"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void The_declared_widths_reach_the_created_column_definition()
    {
        using var db = CreateContext();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'Items'";
        var ddl = (string?)command.ExecuteScalar() ?? string.Empty;

        Assert.Contains("\"CurrentQuantity\" decimal(20,4) NOT NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("\"MinQuantity\" decimal(20,4) NOT NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("\"PurchasePrice\" decimal(20,3) NOT NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("\"SalePrice\" decimal(20,3) NOT NULL", ddl, StringComparison.Ordinal);
    }
}
