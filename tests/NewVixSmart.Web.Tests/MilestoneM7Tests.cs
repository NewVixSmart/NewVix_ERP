using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class MilestoneM7Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public MilestoneM7Tests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private ReportService CreateReportService(AppDbContext db) => new(db, new FinancialReportService(db));

    private async Task<(Unit unit, ItemCategory category, ItemType type)> SeedLookupsAsync(AppDbContext db)
    {
        var unit = new Unit { Name = "قطعة" };
        var category = new ItemCategory { Name = "تصنيف اختبار" };
        var type = new ItemType { Name = "نوع اختبار" };
        db.Units.Add(unit);
        db.ItemCategories.Add(category);
        db.ItemTypes.Add(type);
        await db.SaveChangesAsync();
        return (unit, category, type);
    }

    // ---------- Code128Helper ----------

    [Fact]
    public void GetBarcodeValue_PassesThroughValidCharacters()
    {
        Assert.Equal("ABC-123", Code128Helper.GetBarcodeValue("ABC-123"));
    }

    [Fact]
    public void GetBarcodeValue_FallsBackForNullOrEmpty()
    {
        Assert.Equal("NOCODE", Code128Helper.GetBarcodeValue(null));
        Assert.Equal("NOCODE", Code128Helper.GetBarcodeValue(""));
        Assert.Equal("NOCODE", Code128Helper.GetBarcodeValue("أبجد")); // Arabic has no ASCII chars
    }

    [Fact]
    public void IsValidCode128_RejectsNonAscii()
    {
        Assert.True(Code128Helper.IsValidCode128("ITM00000001"));
        Assert.False(Code128Helper.IsValidCode128("أبج"));
    }

    [Fact]
    public void Encode_ProducesNonEmptyPatternWithQuietZones()
    {
        var bars = Code128Helper.Encode("ITM00000001");
        Assert.NotNull(bars);
        Assert.True(bars.Length > 20);
        // first and last 8 entries are the quiet zones (zeros)
        Assert.Equal(0, bars[0]);
        Assert.Equal(0, bars[^1]);
    }

    [Fact]
    public void ToSvgPattern_ProducesValidSvg()
    {
        var bars = Code128Helper.Encode("STK42");
        var svg = Code128Helper.ToSvgPattern(bars, 50, 2);
        Assert.Contains("xmlns", svg);
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
    }

    // ---------- Item barcode ----------

    [Fact]
    public void ItemBarcodeFallback_IsNonEmptyValidCode128()
    {
        // Controller auto-fill produces an item code or an ITM-padded id as the barcode.
        var id = 7;
        var code = "SKU-77";
        var generated = string.IsNullOrWhiteSpace(code) ? $"ITM{id:D8}" : code;
        Assert.False(string.IsNullOrWhiteSpace(generated));
        Assert.True(Code128Helper.IsValidCode128(generated));
        Assert.True(Code128Helper.Encode(generated).Length > 0);

        var fallbackWithoutCode = $"ITM{id:D8}";
        Assert.True(Code128Helper.IsValidCode128(fallbackWithoutCode));
        Assert.True(Code128Helper.Encode(fallbackWithoutCode).Length > 0);
    }

    // ---------- Dashboard low-stock ----------

    [Fact]
    public async Task GetDashboardAsync_FlagsLowStockItems_WithMinStock()
    {
        using var db = CreateContext();
        var (unit, category, type) = await SeedLookupsAsync(db);

        db.Items.Add(new Item
        {
            Name = "منخفض",
            Code = "LOW",
            Barcode = "LOW",
            CountUnitId = unit.Id,
            CategoryId = category.Id,
            ItemTypeId = type.Id,
            CurrentCount = 5,
            MinCount = 10,
            IsActive = true
        });
        db.Items.Add(new Item
        {
            Name = "كافي",
            Code = "OK",
            Barcode = "OK",
            CountUnitId = unit.Id,
            CategoryId = category.Id,
            ItemTypeId = type.Id,
            CurrentCount = 50,
            MinCount = 10,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var svc = CreateReportService(db);
        var vm = await svc.GetDashboardAsync();

        Assert.Contains(vm.LowStockItems, i => i.Code == "LOW");
        Assert.DoesNotContain(vm.LowStockItems, i => i.Code == "OK");
    }

    // ---------- XLSX exports ----------

    [Fact]
    public async Task ExportItemsXlsxAsync_ProducesNonEmptyWorkbook()
    {
        using var db = CreateContext();
        var (unit, category, type) = await SeedLookupsAsync(db);
        db.Items.Add(new Item
        {
            Name = "أ",
            Code = "X1",
            Barcode = "X1",
            CountUnitId = unit.Id,
            CategoryId = category.Id,
            ItemTypeId = type.Id
        });
        await db.SaveChangesAsync();

        var svc = CreateReportService(db);
        var bytes = await svc.ExportItemsXlsxAsync();

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        // XLSX magic bytes
        Assert.Equal(0x50, bytes[0]); // 'P'
        Assert.Equal(0x4B, bytes[1]); // 'K'
    }

    [Fact]
    public async Task ExportStockXlsxAsync_WorksWithLowOnlyFlag()
    {
        using var db = CreateContext();
        var (unit, category, type) = await SeedLookupsAsync(db);
        db.Items.Add(new Item
        {
            Name = "م",
            Code = "S1",
            Barcode = "S1",
            CountUnitId = unit.Id,
            CategoryId = category.Id,
            ItemTypeId = type.Id,
            CurrentCount = 7,
            MinCount = 3
        });
        await db.SaveChangesAsync();

        var svc = CreateReportService(db);
        var bytes = await svc.ExportStockXlsxAsync(lowOnly: true);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }
}