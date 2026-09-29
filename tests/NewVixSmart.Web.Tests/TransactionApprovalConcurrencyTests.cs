using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// مئة محاولة اعتماد متزامنة لنفس أمر البيع: الاعتماد يفتح معاملته ويغيّر حالة الأمر ثم
/// يحجز الكميات، فلا يجوز أن ينجح مرتين أو أن يتكرر الحجز أو يتكرر أي قيد محاسبي.
/// </summary>
public sealed class TransactionApprovalConcurrencyTests : IDisposable
{
    private const int Attempts = 100;
    private const int MaxParallel = 4;

    private readonly string _databasePath;
    private readonly string _connectionString;

    public TransactionApprovalConcurrencyTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nvs-approval-{Guid.NewGuid():N}.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 30
        }.ToString();

        using var setup = CreateContext();
        setup.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options);

    private static async Task<(int ItemId, int CustomerId, int OrderId)> SeedAsync(AppDbContext db)
    {
        var unit = new Unit { Name = "قطعة" };
        var category = new ItemCategory { Name = "تصنيف اعتماد" };
        var itemType = new ItemType { Name = "نوع اعتماد" };
        db.ItemCategories.Add(category);
        db.ItemTypes.Add(itemType);
        db.Units.Add(unit);
        db.Items.Add(new Item
        {
            Name = "صنف اعتماد",
            Category = category,
            ItemType = itemType,
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 40m,
            SalePrice = 90m,
            CurrentQuantity = 100m,
            CurrentCount = 0m
        });
        db.Customers.Add(new Customer { Name = "عميل اعتماد" });
        await db.SaveChangesAsync();

        var itemId = (await db.Items.SingleAsync()).Id;
        var customerId = (await db.Customers.SingleAsync()).Id;

        var orders = new SalesOrdersService(db, new InventoryService(db));
        var order = new SalesOrder { CustomerId = customerId, OrderDate = DateTime.Today };
        var (created, error) = await orders.CreateOrderAsync(order, new List<SalesOrderItem>
        {
            new() { ItemId = itemId, Quantity = 20m, UnitPrice = 90m }
        }, "tester");
        Assert.True(created, error);

        return (itemId, customerId, order.Id);
    }

    [Fact]
    public async Task ParallelApprovals_ApproveOnceWithSingleReservationAndNoDuplicateJournals()
    {
        int orderId;
        int itemId;
        using (var seed = CreateContext())
        {
            (itemId, _, orderId) = await SeedAsync(seed);
        }

        using var gate = new SemaphoreSlim(MaxParallel, MaxParallel);
        var approvals = await Task.WhenAll(Enumerable.Range(0, Attempts).Select(async _ =>
        {
            await gate.WaitAsync();
            try
            {
                using var db = CreateContext();
                var service = new SalesOrdersService(db, new InventoryService(db));
                var (ok, error) = await service.ApproveOrderAsync(orderId);
                return (Ok: ok, Error: error);
            }
            finally
            {
                gate.Release();
            }
        }));

        Assert.Equal(1, approvals.Count(a => a.Ok));
        var losers = approvals.Where(a => !a.Ok).Select(a => a.Error).Distinct().ToList();
        Assert.NotEmpty(losers);
        Assert.All(losers, error => Assert.False(string.IsNullOrWhiteSpace(error)));

        using var verify = CreateContext();
        Assert.Equal(SalesOrderStatus.Approved, (await verify.SalesOrders.FindAsync(orderId))!.Status);

        var reservations = await verify.StockReservations.AsNoTracking()
            .Where(r => r.SalesOrderId == orderId).ToListAsync();
        var reservation = Assert.Single(reservations);
        Assert.Equal(StockReservationStatus.Active, reservation.Status);
        Assert.Equal(20m, (await verify.StockReservationLines.AsNoTracking().SingleAsync(l => l.StockReservationId == reservation.Id)).Quantity);

        var item = await verify.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(20m, item.ReservedQuantity);
        Assert.Equal(80m, item.AvailableQuantity);
        Assert.Equal(100m, item.CurrentQuantity);

        var line = await verify.SalesOrderItems.AsNoTracking().SingleAsync(i => i.SalesOrderId == orderId);
        Assert.Equal(20m, line.ReservedQty);
        Assert.Equal(20m, line.PendingQty);

        // الاعتماد لا يرحّل قيودًا؛ ما يهم أنه لا تكرّر قيد واحد لمصدر واحد لو رحّلته أي خطوة لاحقة.
        var duplicateJournals = await verify.JournalEntries.AsNoTracking()
            .GroupBy(e => new { e.Source, e.SourceId })
            .Select(g => new { g.Key.Source, g.Key.SourceId, Count = g.Count() })
            .Where(x => x.Count > 1)
            .ToListAsync();
        Assert.Empty(duplicateJournals);
    }

    /// <summary>
    /// حراسة التوازي على مستوى قاعدة البيانات: سطر الأمر يحمل رمز نسخ مُعرَّفًا كConcurrency
    /// Token ويولّده مزوّد قاعدة البيانات عند كل تحديث. لا تستطيع SQLite توليد
    /// <c>rowversion</c>، لذا يتحقق هذا الاختبار من بقاء التعيين في النموذج — وهو ما يجعل
    /// التعارض يعمل على SQL Server في الإنتاج — بينما الاختبار السابق يثبت النتيجة الفعلية.
    /// </summary>
    [Fact]
    public void SalesOrderItem_RowVersion_StaysAConcurrencyToken()
    {
        using var db = CreateContext();
        var mapped = db.Model.FindEntityType(typeof(SalesOrderItem))!.FindProperty("RowVersion");
        Assert.NotNull(mapped);
        Assert.True(mapped!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, mapped.ValueGenerated);
    }
}
