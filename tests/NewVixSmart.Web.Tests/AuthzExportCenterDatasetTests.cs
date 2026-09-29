using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Export;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// A single <c>ExportCenter.View</c> grant used to unlock the whole database: one controller, one
/// class-level filter, and a <c>key</c> query-string parameter that picked the table. Every dataset
/// is now reachable only with the owning module's <c>Export</c> key <em>and</em> that module's read
/// key, and the XLSX path is capped at the same row limit as the CSV one.
/// </summary>
public sealed class AuthzExportCenterDatasetTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AuthzExportCenterDatasetTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private ExportCenterController CreateController(AppDbContext db, params string[] keys) =>
        new(new ExportCenterService(db), new AuthzTestPermissionService(keys));

    /// <summary>The dataset -> (export key, source view key) pairs the fix is specified against.</summary>
    public static TheoryData<string, string, string> ExpectedMapping() => new()
    {
        { "suppliers", "Suppliers.Export", "Suppliers.View" },
        { "customers", "Customers.Export", "Customers.View" },
        { "items", "Items.Export", "Items.View" },
        { "item_categories", "Items.Export", "Items.View" },
        { "item_types", "Items.Export", "Items.View" },
        { "units", "Items.Export", "Items.View" },
        { "gl_accounts", "ChartOfAccounts.Export", "ChartOfAccounts.View" },
        { "branches", "Settings.Export", "Settings.View" },
        { "warehouses", "Warehouses.Export", "Warehouses.View" },
        { "payments", "Payments.Export", "Payments.View" },
        { "sale_invoices", "Sales.Export", "Sales.View" },
        { "purchase_invoices", "Purchases.Export", "Purchases.View" },
        { "sale_returns", "SaleReturns.Export", "SaleReturns.View" },
        { "purchase_returns", "PurchaseReturns.Export", "PurchaseReturns.View" },
        { "sale_quotes", "SalesQuotes.Export", "SalesQuotes.View" },
        { "stock_movements", "Stock.Export", "Stock.View" },
        { "stock_transfers", "StockTransfers.Export", "StockTransfers.View" },
        { "inventory_adjustments", "InventoryAdjustments.Export", "InventoryAdjustments.View" },
        { "journal_entries", "AuditLedger.Export", "AuditLedger.View" },
        { "fiscal_periods", "FiscalClose.Export", "FiscalClose.Close" },
        { "budgets", "Budgets.Export", "Budgets.View" },
        { "purchase_orders", "PurchaseOrders.Export", "PurchaseOrders.View" },
        { "sale_orders", "SalesOrders.Export", "SalesOrders.View" },
        { "delivery_orders", "DeliveryOrders.Export", "DeliveryOrders.View" },
        { "stock_reservations", "StockReservations.Export", "StockReservations.View" }
    };

    [Fact]
    public void EveryExportableDataset_HasAnOwningModuleMapping()
    {
        using var db = CreateContext();
        var catalog = new ExportCenterService(db).GetCatalog();

        var unmapped = catalog.Select(o => o.Key).Where(k => !ExportCenterDatasets.Map.ContainsKey(k)).ToList();
        var notInCatalog = ExportCenterDatasets.Map.Keys.Where(k => catalog.All(o => o.Key != k)).ToList();

        Assert.True(unmapped.Count == 0,
            "Exportable datasets with no permission mapping:" + Environment.NewLine + string.Join(Environment.NewLine, unmapped));
        Assert.True(notInCatalog.Count == 0,
            "Permission mappings for datasets that are not exportable:" + Environment.NewLine + string.Join(Environment.NewLine, notInCatalog));
    }

    [Theory]
    [MemberData(nameof(ExpectedMapping))]
    public void DatasetMapping_MatchesTheSpecifiedKeys(string dataset, string exportKey, string sourceViewKey)
    {
        Assert.True(ExportCenterDatasets.TryGet(dataset, out var access), $"No mapping for '{dataset}'.");
        Assert.Equal(exportKey, access.ExportKey);
        Assert.Equal(sourceViewKey, access.SourceViewKey);
    }

    [Theory]
    [MemberData(nameof(ExpectedMapping))]
    public async Task Dataset_IsReachableOnlyWithBothItsOwnKeys(string dataset, string exportKey, string sourceViewKey)
    {
        using var db = CreateContext();

        // The whole export-centre entry point on its own is not enough.
        Assert.IsType<ForbidResult>(await CreateController(db, "ExportCenter.View").ExportXlsx(dataset));

        // The export right without read access to the source module is not enough.
        Assert.IsType<ForbidResult>(await CreateController(db, exportKey).ExportXlsx(dataset));

        // Read access without the export right is not enough either.
        Assert.IsType<ForbidResult>(await CreateController(db, sourceViewKey).ExportXlsx(dataset));

        // Both together unlock it.
        Assert.IsAssignableFrom<FileContentResult>(await CreateController(db, exportKey, sourceViewKey).ExportXlsx(dataset));
    }

    [Theory]
    [MemberData(nameof(ExpectedMapping))]
    public async Task AnotherModulesExportKey_DoesNotOpenThisDataset(string dataset, string exportKey, string sourceViewKey)
    {
        using var db = CreateContext();
        var otherExportKey = ExportCenterDatasets.Map.Values
            .Select(a => a.ExportKey)
            .First(k => k != exportKey);

        // Holding a different module's export right plus this dataset's read key is still refused.
        Assert.IsType<ForbidResult>(
            await CreateController(db, otherExportKey, sourceViewKey).ExportXlsx(dataset));
    }

    [Fact]
    public async Task Csv_IsGatedByTheSameKeysAsXlsx()
    {
        using var db = CreateContext();

        Assert.IsType<ForbidResult>(await CreateController(db, "ExportCenter.View").ExportCsv("items"));
        Assert.IsType<ForbidResult>(await CreateController(db, "Items.Export").ExportCsv("items"));
        Assert.IsAssignableFrom<FileContentResult>(await CreateController(db, "Items.Export", "Items.View").ExportCsv("items"));
    }

    [Fact]
    public async Task UnknownDataset_IsNotFoundRatherThanForbidden()
    {
        using var db = CreateContext();
        var controller = CreateController(db, "ExportCenter.View", "Items.Export", "Items.View");

        Assert.IsType<NotFoundResult>(await controller.ExportXlsx("drop_table"));
        Assert.IsType<NotFoundResult>(await controller.ExportCsv("drop_table"));
    }

    [Fact]
    public async Task Index_OnlyListsDatasetsTheCallerMayExport()
    {
        using var db = CreateContext();
        var controller = CreateController(db, "ExportCenter.View", "Items.Export", "Items.View");

        var view = Assert.IsType<ViewResult>(await controller.Index());
        var vm = Assert.IsType<ExportCenterViewModel>(view.Model);

        Assert.NotEmpty(vm.Items);
        Assert.All(vm.Items, o => Assert.True(
            o.Key is "items" or "item_categories" or "item_types" or "units",
            $"'{o.Key}' was offered to a caller without its export key."));
    }

    [Fact]
    public void ExportCenter_StillRequiresTheClassLevelViewKey()
    {
        // The class-level gate stays; the per-dataset keys are enforced inside the actions.
        var gate = typeof(ExportCenterController)
            .GetCustomAttribute<RequirePermAttribute>(inherit: true)
            ?.Arguments?.FirstOrDefault() as string;

        Assert.Equal("ExportCenter.View", gate);
    }

    [Fact]
    public async Task NoDataset_IsReachableByACallerWithNoPermissionsAtAll()
    {
        using var db = CreateContext();
        var controller = CreateController(db);

        foreach (var key in ExportCenterDatasets.Map.Keys)
        {
            Assert.IsType<ForbidResult>(await controller.ExportXlsx(key));
        }
    }

    [Fact]
    public void EveryMappedExportKey_ExistsInThePermissionCatalog()
    {
        var catalog = PermissionCatalog.Modules
            .SelectMany(m => PermissionCatalog.ActionsFor(m.Key).Select(a => $"{m.Key}.{a}"))
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExportCenterDatasets.Map.Values
            .Select(a => a.ExportKey)
            .Where(k => !catalog.Contains(k))
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Export keys absent from the catalog (ungrantable, so the dataset is unreachable):" +
            Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryMappedSourceViewKey_ExistsInThePermissionCatalog()
    {
        var catalog = PermissionCatalog.Modules
            .SelectMany(m => PermissionCatalog.ActionsFor(m.Key).Select(a => $"{m.Key}.{a}"))
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExportCenterDatasets.Map.Values
            .Select(a => a.SourceViewKey)
            .Where(k => !catalog.Contains(k))
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Source view keys absent from the catalog:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryXlsxExport_CapsTheRowsItMaterialises()
    {
        // The CSV path was capped but the XLSX path was not, so one request could build the whole
        // journal in memory. Every *XlsxAsync body must take the same limit before ToListAsync.
        var source = ReadRepoFile("src/NewVixSmart.Web/Services/ExportCenterService.cs");

        var uncapped = Regex.Matches(source,
                @"public\s+async\s+Task<byte\[\]>\s+(\w*XlsxAsync)\s*\(\s*\)\s*\{(?<body>.*?)\n    \}",
                RegexOptions.Singleline)
            .Select(m => new
            {
                Method = m.Groups[1].Value,
                Body = m.Groups["body"].Value
            })
            .Where(x => !x.Body.Contains("Take(MaxExportRows)"))
            .Select(x => x.Method)
            .ToList();

        Assert.True(uncapped.Count == 0,
            "XLSX exports that materialise every row:" + Environment.NewLine + string.Join(Environment.NewLine, uncapped));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewVixSmart.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "Could not locate the repository root from " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
