using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Reports used to carry <c>[RequirePerm("Reports.View")]</c> on the class. Because a class-level
/// and a method-level filter both run, every action really required <c>Reports.View</c> <em>and</em>
/// its own key - so an AuditLedger or Aging user could not open their own screen, and every report
/// action silently inherited a reports-module right it had no business requiring. Each action now
/// declares its own keys.
/// </summary>
public sealed class AuthzReportsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AuthzReportsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static ReportsController CreateController(AppDbContext db, params string[] keys) =>
        new(db,
            new ReportExportService(db),
            new FinancialReportService(db),
            new ReportService(db, new FinancialReportService(db)),
            new AuthzTestPermissionService(keys));

    private static IEnumerable<MethodInfo> ActionMethods() =>
        typeof(ReportsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>(inherit: true) is null)
            .Where(m => typeof(IActionResult).IsAssignableFrom(m.ReturnType)
                        || (m.ReturnType.IsGenericType
                            && m.ReturnType.GetGenericArguments()[0] == typeof(IActionResult)));

    private static IReadOnlyList<string> KeysOf(MethodInfo action) =>
        action.GetCustomAttributes<RequirePermAttribute>(inherit: true)
            .Select(a => a.Arguments?.FirstOrDefault() as string)
            .Where(k => k is not null)
            .Select(k => k!)
            .ToList();

    private static MethodInfo Action(string name) =>
        ActionMethods().Single(m => m.Name == name);

    [Fact]
    public void ReportsController_DeclaresNoClassLevelPermission()
    {
        var classPerm = typeof(ReportsController)
            .GetCustomAttribute<RequirePermAttribute>(inherit: true);

        Assert.True(classPerm is null,
            "A class-level [RequirePerm] ANDs with every action's own key and re-imposes the bug this fixes.");
    }

    [Fact]
    public void EveryReportsAction_CarriesItsOwnPermissionKeys()
    {
        var offenders = ActionMethods()
            .Where(m => KeysOf(m).Count == 0)
            .Select(m => m.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Reports actions with no permission key of their own:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EachReportsAction_RequiresExactlyTheExpectedKeys()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["Index"] = ["Reports.View"],
            ["Sales"] = ["Reports.View"],
            ["Purchases"] = ["Reports.View"],
            ["Payments"] = ["Reports.View"],
            ["TrialBalance"] = ["Reports.View"],
            ["IncomeStatement"] = ["Reports.View"],
            ["BalanceSheet"] = ["Reports.View"],
            ["CashFlow"] = ["Reports.View"],
            ["BudgetVariance"] = ["Reports.View"],
            ["Dashboard"] = ["Reports.Dashboard"],
            ["AuditLedger"] = ["AuditLedger.View"],
            ["AuditLedgerXlsx"] = ["AuditLedger.Export"],
            ["Aging"] = ["Aging.View"],
            ["AgingXlsx"] = ["Aging.Export"],
            ["ExportSalesCsv"] = ["Reports.Export", "Sales.View"],
            ["ExportPurchasesCsv"] = ["Reports.Export", "Purchases.View"],
            ["ExportPaymentsCsv"] = ["Reports.Export", "Payments.View"],
            ["ExportSalesXlsx"] = ["Reports.Export", "Sales.View"],
            ["ExportPurchasesXlsx"] = ["Reports.Export", "Purchases.View"],
            ["ExportPaymentsXlsx"] = ["Reports.Export", "Payments.View"],
            ["ExportItemsXlsx"] = ["Reports.Export", "Items.View"],
            ["ExportStockXlsx"] = ["Reports.Export", "Stock.View"],
            ["ExportTrialBalancePdf"] = ["Reports.Export"],
            ["ExportTrialBalanceXlsx"] = ["Reports.Export"],
            ["ExportIncomeStatementPdf"] = ["Reports.Export"],
            ["ExportIncomeStatementXlsx"] = ["Reports.Export"],
            ["ExportBalanceSheetPdf"] = ["Reports.Export"],
            ["ExportBalanceSheetXlsx"] = ["Reports.Export"],
            ["BudgetVarianceXlsx"] = ["Reports.Export", "Budgets.View"],
            ["PrintInvoice"] = ["Reports.Export"]
        };

        foreach (var (name, keys) in expected)
        {
            var wanted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var actual = KeysOf(Action(name)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(wanted, actual);
        }
    }

    [Fact]
    public async Task PrintInvoice_WithReportsExportButNoSalesView_IsForbidden()
    {
        using var db = CreateContext();
        var controller = CreateController(db, "Reports.Export", "Purchases.View");

        var result = await controller.PrintInvoice(1, "sale");

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task PrintInvoice_WithReportsExportButNoPurchasesView_IsForbidden()
    {
        using var db = CreateContext();
        var controller = CreateController(db, "Reports.Export", "Sales.View");

        var result = await controller.PrintInvoice(1, "purchase");

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task PrintInvoice_WithBothSourceKeys_PassesThePermissionCheck()
    {
        using var db = CreateContext();
        var controller = CreateController(db, "Reports.Export", "Sales.View", "Purchases.View");

        // No invoices are seeded, so a permitted request gets past the gate and then 404s.
        Assert.IsType<NotFoundResult>(await controller.PrintInvoice(1, "sale"));
        Assert.IsType<NotFoundResult>(await controller.PrintInvoice(1, "purchase"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sale")]
    [InlineData("SALE")]
    [InlineData("journal")]
    [InlineData("both")]
    public async Task PrintInvoice_WithAnUnrecognisedType_NeverFallsBackToAWeakerCheck(string type)
    {
        using var db = CreateContext();
        // Every key granted: an unknown type must still not resolve to any module.
        var controller = CreateController(db, "Reports.Export", "Sales.View", "Purchases.View", "Items.View");

        Assert.IsType<NotFoundResult>(await controller.PrintInvoice(1, type));
    }
}
