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
using NewVixSmart.Web.Models.Access;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Stock;
using NewVixSmart.Web.ViewModels.Users;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ListPagingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ListPagingTests()
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
    public async Task CustomersController_Index_Paginates_And_Searches()
    {
        using var db = CreateContext();
        for (int i = 1; i <= 55; i++)
        {
            db.Customers.Add(new Customer
            {
                Name = $"عميل {i:D3}",
                Code = i == 42 ? "C042" : null,
                Phone = i == 7 ? "0111234567" : null,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        var controller = new CustomersController(db, null!);

        var page1 = Assert.IsType<ViewResult>(await controller.Index(page: 1));
        var first = Assert.IsType<List<Customer>>(page1.Model);
        Assert.Equal(50, first.Count);
        Assert.Equal(55, (int)controller.ViewBag.TotalCount);
        Assert.Equal(1, (int)controller.ViewBag.Page);
        Assert.Equal(2, (int)controller.ViewBag.TotalPages);

        var page2 = Assert.IsType<ViewResult>(await controller.Index(page: 2));
        Assert.Equal(5, Assert.IsType<List<Customer>>(page2.Model).Count);
        Assert.Equal(2, (int)controller.ViewBag.Page);

        var clamped = Assert.IsType<ViewResult>(await controller.Index(page: 99));
        Assert.Equal(5, Assert.IsType<List<Customer>>(clamped.Model).Count);
        Assert.Equal(2, (int)controller.ViewBag.Page);

        var byName = Assert.IsType<ViewResult>(await controller.Index(search: "عميل 042"));
        var named = Assert.Single(Assert.IsType<List<Customer>>(byName.Model));
        Assert.Equal("عميل 042", named.Name);

        var byCode = Assert.IsType<ViewResult>(await controller.Index(search: "C042"));
        Assert.Single(Assert.IsType<List<Customer>>(byCode.Model));

        var byPhone = Assert.IsType<ViewResult>(await controller.Index(search: "0111234567"));
        Assert.Single(Assert.IsType<List<Customer>>(byPhone.Model));
    }

    [Fact]
    public async Task SalesController_Index_Paginates_And_Searches()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل مبيعات" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        for (int i = 1; i <= 60; i++)
        {
            db.SaleInvoices.Add(new SaleInvoice
            {
                InvoiceNumber = $"SI-TEST-{i:D3}",
                CustomerId = customer.Id,
                InvoiceDate = DateTime.Today.AddDays(-i),
                IsPaid = i % 2 == 0
            });
        }
        await db.SaveChangesAsync();

        var inventory = new InventoryService(db, new AccountingService(db));
        var controller = new SalesController(db, inventory);

        var first = Assert.IsType<ViewResult>(await controller.Index(page: 1));
        Assert.Equal(50, Assert.IsType<List<SaleInvoice>>(first.Model).Count);
        Assert.Equal(60, (int)controller.ViewBag.TotalCount);
        Assert.Equal(2, (int)controller.ViewBag.TotalPages);

        var second = Assert.IsType<ViewResult>(await controller.Index(page: 2));
        Assert.Equal(10, Assert.IsType<List<SaleInvoice>>(second.Model).Count);

        var searched = Assert.IsType<ViewResult>(await controller.Index(search: "SI-TEST-042"));
        var hit = Assert.Single(Assert.IsType<List<SaleInvoice>>(searched.Model));
        Assert.Equal("SI-TEST-042", hit.InvoiceNumber);
    }

    [Fact]
    public async Task AccountsController_Index_Paginates_Preserves_Filters()
    {
        using var db = CreateContext();
        for (int i = 1; i <= 61; i++)
        {
            db.GLAccounts.Add(new GLAccount
            {
                Code = $"9{i:D3}",
                Name = $"حساب {i:D3}",
                Type = GLAccountType.Asset,
                NormalBalance = NormalBalance.Debit,
                IsActive = i != 61
            });
        }
        await db.SaveChangesAsync();

        var controller = new AccountsController(db, new AccountsService(db));

        var result = Assert.IsType<ViewResult>(await controller.Index(type: GLAccountType.Asset, active: true, page: 1));
        Assert.Equal(50, Assert.IsType<List<GLAccount>>(result.Model).Count);
        Assert.Equal(60, (int)controller.ViewBag.TotalCount);
        Assert.Equal(2, (int)controller.ViewBag.TotalPages);
        Assert.Equal(GLAccountType.Asset, controller.ViewBag.TypeFilter);
        Assert.Equal(true, controller.ViewBag.ActiveFilter);
        Assert.NotNull(controller.ViewBag.Balances);

        var page2 = Assert.IsType<ViewResult>(await controller.Index(type: GLAccountType.Asset, active: true, page: 2));
        Assert.Equal(10, Assert.IsType<List<GLAccount>>(page2.Model).Count);
        Assert.All(Assert.IsType<List<GLAccount>>(page2.Model), a => Assert.NotEqual("9061", a.Code));
    }

    [Fact]
    public async Task PurchaseReturnsController_Index_Paginates_And_Searches()
    {
        using var db = CreateContext();
        var supplier = new Supplier { Name = "مورد مرتجعات" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        for (int i = 1; i <= 55; i++)
        {
            db.PurchaseReturns.Add(new PurchaseReturn
            {
                ReturnNumber = $"PRTN-{i:D3}",
                SupplierId = supplier.Id,
                ReturnDate = DateTime.Today.AddDays(-i),
                Status = i % 2 == 0 ? ReturnStatus.Posted : ReturnStatus.Draft
            });
        }
        await db.SaveChangesAsync();

        var inventory = new InventoryService(db, new AccountingService(db));
        var controller = new PurchaseReturnsController(db, inventory, new TestPermissionService());

        var first = Assert.IsType<ViewResult>(await controller.Index(page: 1));
        Assert.Equal(50, Assert.IsType<List<PurchaseReturn>>(first.Model).Count);
        Assert.Equal(55, (int)controller.ViewBag.TotalCount);

        var searched = Assert.IsType<ViewResult>(await controller.Index(search: "مورد مرتجعات"));
        Assert.Equal(50, Assert.IsType<List<PurchaseReturn>>(searched.Model).Count);
        Assert.Equal(55, (int)controller.ViewBag.TotalCount);
        Assert.Equal(2, (int)controller.ViewBag.TotalPages);

        var byNumber = Assert.IsType<ViewResult>(await controller.Index(search: "PRTN-007"));
        Assert.Single(Assert.IsType<List<PurchaseReturn>>(byNumber.Model));
    }

    [Fact]
    public async Task PaymentsController_Index_Paginates_And_Searches()
    {
        using var db = CreateContext();
        for (int i = 1; i <= 55; i++)
        {
            db.Payments.Add(new Payment
            {
                ReceiptNumber = $"PAY-T{i:D3}",
                Type = i % 2 == 0 ? PaymentType.Receipt : PaymentType.Disbursement,
                PaymentDate = DateTime.Today.AddDays(-i)
            });
        }
        await db.SaveChangesAsync();

        var controller = new PaymentsController(db, new PaymentService(db));

        var first = Assert.IsType<ViewResult>(await controller.Index(page: 1));
        Assert.Equal(50, Assert.IsType<List<Payment>>(first.Model).Count);
        Assert.Equal(55, (int)controller.ViewBag.TotalCount);
        Assert.Equal(2, (int)controller.ViewBag.TotalPages);

        var searched = Assert.IsType<ViewResult>(await controller.Index(search: "PAY-T042"));
        Assert.Single(Assert.IsType<List<Payment>>(searched.Model));
    }

    [Fact]
    public async Task StockController_Index_Paginates_Movements()
    {
        using var db = CreateContext();
        var unit = new Unit { Name = "قطعة مخزون" };
        var item = new Item
        {
            Name = "صنف حركة",
            Category = new ItemCategory { Name = "تصنيف حركة" },
            ItemType = new ItemType { Name = "نوع حركة" },
            CountUnit = unit,
            QuantityUnit = unit,
            IsActive = true
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        for (int i = 1; i <= 55; i++)
        {
            db.StockMovements.Add(new StockMovement
            {
                ItemId = item.Id,
                Type = i % 2 == 0 ? MovementType.In : MovementType.Out,
                MovementDate = DateTime.Today.AddMinutes(-i),
                Quantity = 1,
                Count = 1,
                DocumentNumber = $"STK-DOC-{i:D3}"
            });
        }
        await db.SaveChangesAsync();

        var controller = new StockController(db, new DashboardService(db));

        var first = Assert.IsType<ViewResult>(await controller.Index(null, null, page: 1));
        var vm = Assert.IsType<StockIndexViewModel>(first.Model);
        Assert.Equal(50, vm.Movements.Count);
        Assert.Equal(55, (int)controller.ViewBag.TotalCount);
        Assert.Equal(2, (int)controller.ViewBag.TotalPages);

        var filtered = Assert.IsType<ViewResult>(await controller.Index(null, MovementType.In, page: 1));
        var filteredVm = Assert.IsType<StockIndexViewModel>(filtered.Model);
        Assert.Equal(MovementType.In, filteredVm.Type);
        Assert.All(filteredVm.Movements, m => Assert.Equal(MovementType.In, m.Type));

        var searched = Assert.IsType<ViewResult>(await controller.Index(null, null, search: "STK-DOC-042"));
        Assert.Single(Assert.IsType<StockIndexViewModel>(searched.Model).Movements);
    }

    [Fact]
    public async Task UsersController_Index_BatchLoads_Roles_for_All_Users()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);

        db.Roles.AddRange(
            new IdentityRole { Name = "Admin", NormalizedName = "ADMIN" },
            new IdentityRole { Name = "Accountant", NormalizedName = "ACCOUNTANT" },
            new IdentityRole { Name = "Warehouse", NormalizedName = "WAREHOUSE" });
        await db.SaveChangesAsync();

        var manager = await um.CreateAsync(new IdentityUser { UserName = "admin-user" }, "Admin@12345");
        var accountant = await um.CreateAsync(new IdentityUser { UserName = "acct-user" }, "Acct@12345");
        var warehouse = await um.CreateAsync(new IdentityUser { UserName = "wh-user" }, "Ware@12345");
        Assert.True(manager.Succeeded && accountant.Succeeded && warehouse.Succeeded);

        var admin = await um.FindByNameAsync("admin-user");
        var acct = await um.FindByNameAsync("acct-user");
        var wh = await um.FindByNameAsync("wh-user");
        await um.AddToRoleAsync(admin!, "Admin");
        await um.AddToRoleAsync(acct!, "Accountant");
        await um.AddToRoleAsync(wh!, "Warehouse");

        var controller = new UsersController(um, db);
        var result = Assert.IsType<ViewResult>(await controller.Index());
        var items = Assert.IsType<List<UserListItemViewModel>>(result.Model);

        Assert.Equal(3, items.Count);
        var adminItem = Assert.Single(items, u => u.UserName == "admin-user");
        Assert.Contains("Admin", adminItem.Roles);
        Assert.All(adminItem.Roles, r => Assert.Equal("Admin", r));
        Assert.Single(items, u => u.UserName == "acct-user" && u.Roles.SequenceEqual(new[] { "Accountant" }));
        Assert.Single(items, u => u.UserName == "wh-user" && u.Roles.SequenceEqual(new[] { "Warehouse" }));
        Assert.Equal("admin-user", items.First().UserName);
    }

    [Fact]
    public async Task Permissions_Get_SurfacesCustomGrants_AsActions()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);

        db.Roles.AddRange(
            new IdentityRole { Name = "Admin", NormalizedName = "ADMIN" },
            new IdentityRole { Name = "Accountant", NormalizedName = "ACCOUNTANT" },
            new IdentityRole { Name = "Warehouse", NormalizedName = "WAREHOUSE" });
        await db.SaveChangesAsync();

        var created = await um.CreateAsync(new IdentityUser { UserName = "perms-user" }, "Perms@12345");
        Assert.True(created.Succeeded);
        var user = await um.FindByNameAsync("perms-user");
        await um.AddToRoleAsync(user!, "Accountant");

        db.UserPermissions.AddRange(
            new UserPermission { UserId = user!.Id, PermissionKey = "Sales.Create" },
            new UserPermission { UserId = user.Id, PermissionKey = "Batch.SalesCreate" });
        await db.SaveChangesAsync();

        var controller = new UsersController(um, db);
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());
        var result = Assert.IsType<ViewResult>(await controller.Permissions(user.Id));
        var vm = Assert.IsType<UserPermissionViewModel>(result.Model);

        var salesModule = Assert.Single(vm.Modules, m => m.Key == "Sales");
        Assert.True(salesModule.HasAction("Create"));
        Assert.False(salesModule.HasAction("Delete"));
        Assert.False(Assert.Single(vm.Modules, m => m.Key == "Purchases").HasAction("Create"));
    }

    [Fact]
    public async Task Permissions_Post_ReplacesAllCustomGrants()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);

        db.Roles.AddRange(
            new IdentityRole { Name = "Admin", NormalizedName = "ADMIN" },
            new IdentityRole { Name = "Accountant", NormalizedName = "ACCOUNTANT" },
            new IdentityRole { Name = "Warehouse", NormalizedName = "WAREHOUSE" });
        await db.SaveChangesAsync();

        var created = await um.CreateAsync(new IdentityUser { UserName = "perms-user2" }, "Perms@12345");
        Assert.True(created.Succeeded);
        var user = await um.FindByNameAsync("perms-user2");
        await um.AddToRoleAsync(user!, "Accountant");

        db.UserPermissions.AddRange(
            new UserPermission { UserId = user!.Id, PermissionKey = "Sales.Create" },
            new UserPermission { UserId = user.Id, PermissionKey = "Batch.SalesCreate" });
        await db.SaveChangesAsync();

        var controller = new UsersController(um, db);
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());
        await controller.Permissions(user.Id, new[] { "Warehouses.View", "Warehouses.Edit" });

        var remaining = await db.UserPermissions.Where(p => p.UserId == user.Id).Select(p => p.PermissionKey).ToListAsync();
        Assert.Contains("Warehouses.View", remaining);
        Assert.Contains("Warehouses.Edit", remaining);
        Assert.DoesNotContain("Sales.Create", remaining);
        Assert.DoesNotContain("Batch.SalesCreate", remaining);
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object?> _data = new();

        public IDictionary<string, object?> LoadTempData(HttpContext context) => _data;

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            foreach (var kv in values)
                _data[kv.Key] = kv.Value;
        }
    }

    private static UserManager<IdentityUser> CreateUserManager(AppDbContext db)
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());
        IdentityOptionsFactory.ApplyDefaults(options.Value);

        var store = new UserStore<IdentityUser>(db);
        return new UserManager<IdentityUser>(
            store,
            options,
            new PasswordHasher<IdentityUser>(),
            new[] { new UserValidator<IdentityUser>() },
            new[] { new PasswordValidator<IdentityUser>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<IdentityUser>>.Instance);
    }
}