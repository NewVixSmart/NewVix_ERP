using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Stock;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class StockReportTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public StockReportTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    [Fact]
    public async Task StockReport_Totals_AreCompanyWide_NotFilteredSubset()
    {
        using var db = CreateContext();
        var catA = new ItemCategory { Name = "فئة أ", IsActive = true };
        var catB = new ItemCategory { Name = "فئة ب", IsActive = true };
        var type = new ItemType { Name = "نوع" };
        db.ItemCategories.AddRange(catA, catB);
        db.ItemTypes.Add(type);
        await db.SaveChangesAsync();

        db.Items.AddRange(
            new Item { Name = "صنف أ1", Code = "I-A1", CategoryId = catA.Id, ItemTypeId = type.Id, IsActive = true, CurrentCount = 5, CurrentQuantity = 5, PurchasePrice = 10m, SalePrice = 15m },
            new Item { Name = "صنف أ2", Code = "I-A2", CategoryId = catA.Id, ItemTypeId = type.Id, IsActive = true, CurrentCount = 3, CurrentQuantity = 3, PurchasePrice = 20m, SalePrice = 25m },
            new Item { Name = "صنف ب1", Code = "I-B1", CategoryId = catB.Id, ItemTypeId = type.Id, IsActive = true, CurrentCount = 2, CurrentQuantity = 2, PurchasePrice = 100m, SalePrice = 120m }
        );
        await db.SaveChangesAsync();

        var controller = new StockController(db, new DashboardService(db));
        var result = Assert.IsType<ViewResult>(await controller.Report(catA.Id, false));
        var vm = Assert.IsType<StockReportViewModel>(result.Model);

        Assert.Equal(2, vm.TotalItems);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(10m, vm.TotalCount);
        Assert.Equal(10m, vm.TotalQuantity);
        Assert.Equal(310m, vm.TotalValue);
    }

    [Fact]
    public async Task StockReport_LowOnlyFilter_StillReportsCompanyWideTotals()
    {
        using var db = CreateContext();
        var cat = new ItemCategory { Name = "فئة", IsActive = true };
        var type = new ItemType { Name = "نوع" };
        var unit = new Unit { Name = "قطعة" };
        db.ItemCategories.Add(cat);
        db.ItemTypes.Add(type);
        db.Units.Add(unit);
        await db.SaveChangesAsync();

        db.Items.AddRange(
            new Item { Name = "عالٍ", Code = "I-HI", CategoryId = cat.Id, ItemTypeId = type.Id, CountUnitId = unit.Id, IsActive = true, CurrentCount = 100, CurrentQuantity = 100, MinCount = 10, MinQuantity = 10, PurchasePrice = 1m, SalePrice = 2m },
            new Item { Name = "منخفض", Code = "I-LO", CategoryId = cat.Id, ItemTypeId = type.Id, CountUnitId = unit.Id, IsActive = true, CurrentCount = 2, CurrentQuantity = 2, MinCount = 5, MinQuantity = 5, PurchasePrice = 40m, SalePrice = 80m }
        );
        await db.SaveChangesAsync();

        var controller = new StockController(db, new DashboardService(db));
        var result = Assert.IsType<ViewResult>(await controller.Report(null, lowOnly: true));
        var vm = Assert.IsType<StockReportViewModel>(result.Model);

        Assert.Single(vm.Items);
        Assert.Equal(102m, vm.TotalCount);
        Assert.Equal(180m, vm.TotalValue);
    }
}
