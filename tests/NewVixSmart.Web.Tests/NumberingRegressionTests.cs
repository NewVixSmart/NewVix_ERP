using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class NumberingRegressionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public NumberingRegressionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static string Series() => DateTime.Now.ToString("yyyyMMdd");

    private static IEnumerable<int> Run(int from, int to)
    {
        for (int n = from; n <= to; n++)
        {
            yield return n;
        }
    }

    private static async Task<Customer> SeedCustomerAsync(AppDbContext db, string name)
    {
        var customer = new Customer { Name = name };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static async Task<Supplier> SeedSupplierAsync(AppDbContext db, string name)
    {
        var supplier = new Supplier { Name = name };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier;
    }

    private static async Task<Item> SeedItemAsync(AppDbContext db, string name)
    {
        var unit = new Unit { Name = $"قطعة {name}" };
        var item = new Item
        {
            Name = name,
            Category = new ItemCategory { Name = $"تصنيف {name}" },
            ItemType = new ItemType { Name = $"نوع {name}" },
            CountUnit = unit,
            QuantityUnit = unit,
            IsActive = true,
            CurrentQuantity = 500m,
            CurrentCount = 500m
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task SeedSalesOrderNumbersAsync(AppDbContext db, int customerId, string prefix, IEnumerable<int> sequenceValues)
    {
        db.SalesOrders.AddRange(sequenceValues.Select(n => new SalesOrder
        {
            OrderNumber = $"{prefix}{n:D3}",
            CustomerId = customerId
        }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task SeedPurchaseOrderNumbersAsync(AppDbContext db, int supplierId, string prefix, IEnumerable<int> sequenceValues)
    {
        db.PurchaseOrders.AddRange(sequenceValues.Select(n => new PurchaseOrder
        {
            OrderNumber = $"{prefix}{n:D3}",
            SupplierId = supplierId
        }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task SalesOrder_Numbering_Advances_From_999_To_1000()
    {
        using var db = CreateContext();
        var customer = await SeedCustomerAsync(db, "عميل ترقيم 999");
        var item = await SeedItemAsync(db, "صنف ترقيم 999");
        var prefix = $"SO-{Series()}-";
        await SeedSalesOrderNumbersAsync(db, customer.Id, prefix, Run(1, 999));

        var svc = new SalesOrdersService(db, new InventoryService(db, new AccountingService(db)));
        var order = new SalesOrder { CustomerId = customer.Id };
        var (ok, error) = await svc.CreateOrderAsync(order, new List<SalesOrderItem>
        {
            new SalesOrderItem { ItemId = item.Id, Quantity = 1, Count = 1, UnitPrice = 100m }
        }, "tester");

        Assert.True(ok, error);
        Assert.Equal($"{prefix}1000", order.OrderNumber);
    }

    [Fact]
    public async Task SalesOrder_Numbering_Advances_Past_Four_Digit_Sequence()
    {
        using var db = CreateContext();
        var customer = await SeedCustomerAsync(db, "عميل ترقيم 1000");
        var item = await SeedItemAsync(db, "صنف ترقيم 1000");
        var prefix = $"SO-{Series()}-";
        await SeedSalesOrderNumbersAsync(db, customer.Id, prefix, Run(1, 999).Concat(Run(1000, 1001)));

        var svc = new SalesOrdersService(db, new InventoryService(db, new AccountingService(db)));
        var order = new SalesOrder { CustomerId = customer.Id };
        var (ok, error) = await svc.CreateOrderAsync(order, new List<SalesOrderItem>
        {
            new SalesOrderItem { ItemId = item.Id, Quantity = 1, Count = 1, UnitPrice = 100m }
        }, "tester");

        Assert.True(ok, error);
        Assert.Equal($"{prefix}1002", order.OrderNumber);
    }

    [Fact]
    public async Task PurchaseOrder_Numbering_Advances_From_999_To_1000()
    {
        using var db = CreateContext();
        var supplier = await SeedSupplierAsync(db, "مورد ترقيم 999");
        var item = await SeedItemAsync(db, "صنف مشتريات 999");
        var prefix = $"PRC-{Series()}-";
        await SeedPurchaseOrderNumbersAsync(db, supplier.Id, prefix, Run(1, 999));

        var svc = new ProcurementService(db, new InventoryService(db, new AccountingService(db)));
        var order = new PurchaseOrder { SupplierId = supplier.Id };
        var (ok, error) = await svc.CreateOrderAsync(order, new List<PurchaseOrderItem>
        {
            new PurchaseOrderItem { ItemId = item.Id, Quantity = 1, Count = 1, UnitPrice = 100m }
        }, "tester");

        Assert.True(ok, error);
        Assert.Equal($"{prefix}1000", order.OrderNumber);
    }

    [Fact]
    public async Task PurchaseOrder_Numbering_Advances_Past_Four_Digit_Sequence()
    {
        using var db = CreateContext();
        var supplier = await SeedSupplierAsync(db, "مورد ترقيم 1000");
        var item = await SeedItemAsync(db, "صنف مشتريات 1000");
        var prefix = $"PRC-{Series()}-";
        await SeedPurchaseOrderNumbersAsync(db, supplier.Id, prefix, Run(1, 999).Concat(Run(1000, 1001)));

        var svc = new ProcurementService(db, new InventoryService(db, new AccountingService(db)));
        var order = new PurchaseOrder { SupplierId = supplier.Id };
        var (ok, error) = await svc.CreateOrderAsync(order, new List<PurchaseOrderItem>
        {
            new PurchaseOrderItem { ItemId = item.Id, Quantity = 1, Count = 1, UnitPrice = 100m }
        }, "tester");

        Assert.True(ok, error);
        Assert.Equal($"{prefix}1002", order.OrderNumber);
    }

    [Fact]
    public async Task StockReservation_Numbering_Ignores_Other_Series()
    {
        using var db = CreateContext();
        var item = await SeedItemAsync(db, "صنف حجز");
        var prefix = $"RSV-{Series()}-";

        db.StockReservations.AddRange(
            new StockReservation { ReservationNumber = "RSV-20200101-500", Status = StockReservationStatus.Released, ReleasedAt = DateTime.UtcNow },
            new StockReservation { ReservationNumber = $"{prefix}001", Status = StockReservationStatus.Released, ReleasedAt = DateTime.UtcNow },
            new StockReservation { ReservationNumber = $"{prefix}002", Status = StockReservationStatus.Released, ReleasedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var svc = new StockReservationsService(db);
        var (ok, error, reservation) = await svc.CreateStandaloneAsync(
            new StockReservation { Reason = "حجز مستقل" },
            new List<StockReservationLine> { new StockReservationLine { ItemId = item.Id, Quantity = 5m, Count = 5m } },
            "tester");

        Assert.True(ok, error);
        Assert.NotNull(reservation);
        Assert.Equal($"{prefix}003", reservation.ReservationNumber);
        Assert.Equal(1, await db.StockReservations.CountAsync(r => r.ReservationNumber == $"{prefix}003"));
    }

    [Fact]
    public async Task StockReservation_Numbering_Query_Is_Filtered_By_Series_Prefix()
    {
        var capture = new CommandTextCapture();
        var captureOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(capture)
            .Options;
        using var db = new AppDbContext(captureOptions);
        var item = await SeedItemAsync(db, "صنف حجز معزول");
        var prefix = $"RSV-{Series()}-";

        var svc = new StockReservationsService(db);
        var (ok, error, _) = await svc.CreateStandaloneAsync(
            new StockReservation { Reason = "حجز معزول" },
            new List<StockReservationLine> { new StockReservationLine { ItemId = item.Id, Quantity = 1m, Count = 1m } },
            "tester");

        Assert.True(ok, error);
        var numberingQuery = capture.Commands
            .First(c => c.CommandText.Contains("ReservationNumber", StringComparison.Ordinal));
        Assert.True(numberingQuery.HasPrefixParameter(prefix), numberingQuery.CommandText);
        Assert.Contains("WHERE", numberingQuery.CommandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SupplierQuote_Upsert_Updates_Existing_Row_Without_Duplicate()
    {
        using var db = CreateContext();
        var supplier = await SeedSupplierAsync(db, "مورد أسعار");
        var item = await SeedItemAsync(db, "صنف أسعار");

        var svc = new ProcurementService(db, new InventoryService(db, new AccountingService(db)));
        var (firstOk, firstError) = await svc.SaveSupplierQuoteAsync(new SupplierQuote
        {
            SupplierId = supplier.Id,
            ItemId = item.Id,
            UnitPrice = 50m,
            EffectiveDate = new DateTime(2026, 1, 10)
        });
        Assert.True(firstOk, firstError);

        var (secondOk, secondError) = await svc.SaveSupplierQuoteAsync(new SupplierQuote
        {
            SupplierId = supplier.Id,
            ItemId = item.Id,
            UnitPrice = 75m,
            EffectiveDate = new DateTime(2026, 2, 20),
            Notes = "سعر محدَّث"
        });
        Assert.True(secondOk, secondError);

        var rows = await db.SupplierQuotes.AsNoTracking()
            .Where(q => q.SupplierId == supplier.Id && q.ItemId == item.Id)
            .ToListAsync();
        var single = Assert.Single(rows);
        Assert.Equal(75m, single.UnitPrice);
        Assert.Equal(new DateTime(2026, 2, 20), single.EffectiveDate);
        Assert.Equal("سعر محدَّث", single.Notes);
    }

    [Fact]
    public async Task SupplierQuote_Upsert_Converts_Concurrent_Insert_To_Update()
    {
        var race = new CompetingQuoteInsert();
        var raceOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(race)
            .Options;

        using (var setup = CreateContext())
        {
            var supplier = await SeedSupplierAsync(setup, "مورد تسابق");
            var item = await SeedItemAsync(setup, "صنف تسابق");
            using var db = new AppDbContext(raceOptions);

            var svc = new ProcurementService(db, new InventoryService(db, new AccountingService(db)));
            var (ok, error) = await svc.SaveSupplierQuoteAsync(new SupplierQuote
            {
                SupplierId = supplier.Id,
                ItemId = item.Id,
                UnitPrice = 200m,
                EffectiveDate = new DateTime(2026, 3, 5)
            });

            Assert.True(ok, error);
            Assert.Equal(1, race.Injections);
        }

        using var verify = CreateContext();
        var rows = await verify.SupplierQuotes.AsNoTracking().ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(200m, row.UnitPrice);
    }

    private sealed class CompetingQuoteInsert : SaveChangesInterceptor
    {
        public int Injections { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var added = eventData.Context?.ChangeTracker.Entries<SupplierQuote>()
                .FirstOrDefault(e => e.State == EntityState.Added);
            if (added == null)
            {
                return result;
            }

            Injections++;
            var competing = added.Entity;
            await eventData.Context!.Database.ExecuteSqlRawAsync(
                "INSERT INTO SupplierQuotes (SupplierId, ItemId, UnitPrice, EffectiveDate) VALUES ({0}, {1}, {2}, {3})",
                [competing.SupplierId, competing.ItemId, 111m, new DateTime(2020, 1, 1)],
                cancellationToken).ConfigureAwait(false);
            return result;
        }
    }

    private sealed class CommandTextCapture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = new();

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        public override DbDataReader ReaderExecuted(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Capture(command);
            return result;
        }

        private void Capture(DbCommand command) => Commands.Add(new CapturedCommand(
            command.CommandText,
            command.Parameters.Cast<DbParameter>().Select(p => p.Value?.ToString()).ToList()));
    }

    private sealed record CapturedCommand(string CommandText, List<string?> Parameters)
    {
        public bool HasPrefixParameter(string value) =>
            Parameters.Any(p => p != null && p.StartsWith(value, StringComparison.Ordinal));
    }
}
