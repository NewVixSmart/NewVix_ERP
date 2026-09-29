using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Infrastructure;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Users;
using Xunit;

/// <summary>
/// Closes the round-20 audit items that survived re-verification. Each test names the
/// finding it defends so a future regression points straight back at the report.
/// </summary>
public sealed class AuditRound20CloseoutTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AuditRound20CloseoutTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
        SeedChartOfAccounts(db);
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    // M-6 originally guarded the reconciliation between a return's own currency and its
    // source invoice's. The ledger is single-currency (EGP), so that whole class of
    // mismatch is unrepresentable: a return can only ever book EGP, exactly like the
    // invoice it names. These two tests lock that in — one on the posting behaviour, one
    // on the model itself so multi-currency columns cannot quietly come back.
    [Fact]
    public async Task SaleReturn_Post_WithSourceInvoice_CreditsReceivable_AtInvoiceAmount()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedBasicAsync(db);
        var svc = new InventoryService(db, new AccountingService(db));
        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice,
            new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(okInv, errInv);
        Assert.True(await DeliverDirectAsync(db, invoice, itemId, 10));

        var (ok, err) = await svc.CreateSaleReturnAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id,
            CustomerId = custId,
            ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok, err);

        var saved = await db.SaleReturns.AsNoTracking().SingleAsync();
        Assert.Equal(invoice.Id, saved.SaleInvoiceId);
        Assert.Equal(320m, saved.TotalAmount);

        // The credit to receivables is the returned value itself - no re-conversion step
        // exists, so there is nothing left to disagree with the invoice.
        var credit = await db.JournalEntryLines.Include(l => l.Account)
            .Where(l => l.Account!.Code == "1200" && l.Credit > 0)
            .Select(l => l.Credit).FirstAsync();
        Assert.Equal(320m, credit);
    }

    [Fact]
    public void TransactionEntities_ExposeNoCurrencyOrRateMembers()
    {
        Type[] entities =
        [
            typeof(Payment), typeof(SalePaymentAllocation), typeof(PurchasePaymentAllocation),
            typeof(SaleInvoice), typeof(SaleReturn), typeof(SaleQuote), typeof(SalesOrder),
            typeof(PurchaseInvoice), typeof(PurchaseReturn), typeof(Customer), typeof(Supplier),
        ];
        string[] forbidden = ["Currency", "ExchangeRate", "BaseAmount", "AllocatedBaseAmount", "FxGain", "FxLoss"];

        foreach (var entity in entities)
        {
            var offenders = entity.GetProperties()
                .Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Name)
                .ToList();
            Assert.True(offenders.Count == 0,
                $"{entity.Name} must not carry multi-currency members but declares: {string.Join(", ", offenders)}");
        }
    }

    // M-2: the audit claimed SuppressImplicitRequiredAttributeForNonNullableReferenceTypes
    // stops EF treating non-nullable reference properties as required. It does not - that is
    // an MvcOptions setting, and EF derives requiredness from NRT annotations. This locks the
    // invariant the snapshot already relies on so a future refactor cannot quietly drop it.
    [Fact]
    public void Item_CategoryForeignKey_IsRequiredInEfModel()
    {
        using var db = CreateContext();
        var itemType = db.Model.FindEntityType(typeof(Item));
        Assert.NotNull(itemType);

        var fk = itemType!.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Count == 1 && f.Properties[0].Name == nameof(Item.CategoryId));
        Assert.NotNull(fk);
        Assert.False(fk!.Properties[0].IsNullable);
    }

    [Fact]
    public void StockMovement_ItemForeignKey_IsRequiredInEfModel()
    {
        using var db = CreateContext();
        var movementType = db.Model.FindEntityType(typeof(StockMovement));
        Assert.NotNull(movementType);

        var fk = movementType!.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Count == 1 && f.Properties[0].Name == nameof(StockMovement.ItemId));
        Assert.NotNull(fk);
        Assert.False(fk!.Properties[0].IsNullable);
    }

    // L-2: the admin create-user form advertised a 6-character minimum while Identity
    // enforced 8, so a 6 or 7 character password passed the form and then failed.
    [Fact]
    public void CreateUserViewModel_Password_RejectsSixCharacters()
    {
        var results = new List<ValidationResult>();
        var vm = new CreateUserViewModel { Username = "newuser", Password = "Ab1!efg" }; // 7 chars
        var valid = Validator.TryValidateObject(vm, new ValidationContext(vm), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.ErrorMessage != null && r.ErrorMessage.Contains("8 أحرف"));
    }

    [Fact]
    public void CreateUserViewModel_Password_AcceptsEightCharacters()
    {
        var results = new List<ValidationResult>();
        var vm = new CreateUserViewModel { Username = "newuser", Password = "Ab1!efgh" }; // 8 chars
        var valid = Validator.TryValidateObject(vm, new ValidationContext(vm), results, validateAllProperties: true);

        Assert.True(valid, string.Join(" | ", results.Select(r => r.ErrorMessage)));
    }

    // L-1: there was no way to revoke a former employee's access at all. Both login paths
    // already honour LockoutEnd, so ToggleDeactivated reuses the enforcement point that
    // AccountController and TokensController already trust.
    [Fact]
    public async Task ToggleDeactivated_LocksOutTarget_AndSelfIsRefused()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);

        var admin = new IdentityUser { UserName = "admin1" };
        Assert.True((await um.CreateAsync(admin, "Admin@12345")).Succeeded);
        var staff = new IdentityUser { UserName = "staff1" };
        Assert.True((await um.CreateAsync(staff, "Staff@12345")).Succeeded);

        var controller = new UsersController(um, db);

        // Acting as the admin: the admin may not revoke their own access.
        var selfResult = await InvokeToggleAsync(controller, um, db, actingAs: admin.Id, targetId: admin.Id);
        Assert.IsType<RedirectToActionResult>(selfResult);
        Assert.False(await um.IsLockedOutAsync(admin));

        // Acting as the admin: a colleague can be revoked.
        var staffResult = await InvokeToggleAsync(controller, um, db, actingAs: admin.Id, targetId: staff.Id);
        Assert.IsType<RedirectToActionResult>(staffResult);
        Assert.True(await um.IsLockedOutAsync(staff));

        // And the same action restores access, so a mistaken revocation is recoverable.
        var undoResult = await InvokeToggleAsync(controller, um, db, actingAs: admin.Id, targetId: staff.Id);
        Assert.IsType<RedirectToActionResult>(undoResult);
        Assert.False(await um.IsLockedOutAsync(staff));
    }

    [Fact]
    public async Task Index_MarksDeactivatedUsers()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);
        var staff = new IdentityUser { UserName = "staff2" };
        Assert.True((await um.CreateAsync(staff, "Staff@12345")).Succeeded);
        await um.SetLockoutEndDateAsync(staff, DateTimeOffset.MaxValue);

        var controller = new UsersController(um, db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = PrincipalFor("someone-else") }
        };

        var result = await controller.Index();
        var model = Assert.IsType<ViewResult>(result).Model;
        var list = Assert.IsAssignableFrom<IEnumerable<UserListItemViewModel>>(model!).ToList();

        Assert.Contains(list, u => u.UserName == "staff2" && u.IsDeactivated);
    }

    private static async Task<IActionResult> InvokeToggleAsync(
        UsersController controller, UserManager<IdentityUser> um, AppDbContext db, string actingAs, string targetId)
    {
        var ctx = new DefaultHttpContext { User = PrincipalFor(actingAs) };
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());
        return await controller.ToggleDeactivated(targetId);
    }

    private static ClaimsPrincipal PrincipalFor(string userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "TestAuth"));

    private static UserManager<IdentityUser> CreateUserManager(AppDbContext db)
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());
        IdentityOptionsFactory.ApplyDefaults(options.Value);
        var store = new UserStore<IdentityUser>(db);
        return new UserManager<IdentityUser>(
            store,
            options,
            new PasswordHasher<IdentityUser>(),
            [new UserValidator<IdentityUser>()],
            [new PasswordValidator<IdentityUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<IdentityUser>>.Instance);
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private static async Task<(int itemId, int customerId, int supplierId)> SeedBasicAsync(AppDbContext db)
    {
        var cat = new ItemCategory { Name = "تصنيف اختبار" };
        var type = new ItemType { Name = "نوع اختبار" };
        var unit = new Unit { Name = "قطعة" };
        db.ItemCategories.Add(cat);
        db.ItemTypes.Add(type);
        db.Units.Add(unit);

        db.Items.Add(new Item
        {
            Name = "صنف اختبار",
            Category = cat,
            ItemType = type,
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 50,
            SalePrice = 80,
            CurrentCount = 100,
            CurrentQuantity = 100
        });

        var customer = new Customer { Name = "عميل اختبار" };
        var supplier = new Supplier { Name = "مورد اختبار" };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var item = await db.Items.SingleAsync();
        return (item.Id, customer.Id, supplier.Id);
    }

    private static async Task<bool> DeliverDirectAsync(AppDbContext db, SaleInvoice invoice, int itemId, decimal qty)
    {
        var svc = new InventoryService(db, new AccountingService(db));
        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = DateTime.Today };
        var (created, _) = await svc.CreateDeliveryOrderAsync(delivery,
            [new DeliveryOrderItem { ItemId = itemId, Quantity = qty, Count = 0 }], "test");
        if (!created)
        {
            return false;
        }

        var (delivered, _) = await svc.DeliverDeliveryOrderAsync(delivery.Id, "test");
        return delivered;
    }

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1200", "المدينون", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("1000", "النقدية بالبنك", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون", GLAccountType.Liability, NormalBalance.Credit),
            ("2055", "الضريبة مستحقة", GLAccountType.Liability, NormalBalance.Credit),
            ("4000", "المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات البيع", GLAccountType.Revenue, NormalBalance.Credit),
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal });
        }
        db.SaveChanges();
    }
}
