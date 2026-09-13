using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Access;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;
using System.Security.Cryptography;

using Silk.Trading.Web.Models.Stock;

namespace Silk.Trading.Web.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var config = serviceProvider.GetRequiredService<IConfiguration>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");

        string[] roles = ["Admin", "Accountant", "Warehouse"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var adminUser = await userManager.FindByNameAsync("admin");
        if (adminUser == null)
        {
            var configuredAdminPwd = config["Seed:AdminPassword"];
            var adminPwd = !string.IsNullOrWhiteSpace(configuredAdminPwd) ? configuredAdminPwd : GenerateSecurePassword();
            adminUser = new IdentityUser { UserName = "admin", Email = "admin@silk.com", EmailConfirmed = true };
            var result = await userManager.CreateAsync(adminUser, adminPwd);
            if (!result.Succeeded && !string.IsNullOrWhiteSpace(configuredAdminPwd))
            {
                logger.LogWarning("كلمة المرور المضبوطة للمستخدم Admin لا تلبي سياسة التعقيد، جارٍ توليد كلمة مرور عشوائية");
                adminPwd = GenerateSecurePassword();
                adminUser = new IdentityUser { UserName = "admin", Email = "admin@silk.com", EmailConfirmed = true };
                result = await userManager.CreateAsync(adminUser, adminPwd);
            }
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
                if (string.IsNullOrWhiteSpace(configuredAdminPwd))
                    logger.LogInformation("مستخدم البذرة Admin: كلمة المرور = {Password}", adminPwd);
            }
        }

        await EnsureUserAsync(userManager, config, logger, "accountant", "Seed:AccountantPassword", "accountant@silk.com", "Accountant");
        await EnsureUserAsync(userManager, config, logger, "warehouse", "Seed:WarehousePassword", "warehouse@silk.com", "Warehouse");

        var db = serviceProvider.GetRequiredService<AppDbContext>();
        if (!db.ItemCategories.Any())
        {
            db.ItemCategories.AddRange(
                new ItemCategory { Name = "إلكترونيات", IsActive = true },
                new ItemCategory { Name = "ملابس", IsActive = true },
                new ItemCategory { Name = "أغذية", IsActive = true },
                new ItemCategory { Name = "مواد بناء", IsActive = true }
            );
            await db.SaveChangesAsync();
        }

        if (!db.ItemTypes.Any())
        {
            db.ItemTypes.AddRange(
                new ItemType { Name = "منتج تام", IsActive = true },
                new ItemType { Name = "مواد خام", IsActive = true }
            );
            await db.SaveChangesAsync();
        }

        if (!db.Units.Any())
        {
            db.Units.AddRange(
                new Unit { Name = "قطعة", ShortName = "قط" },
                new Unit { Name = "كيلوجرام", ShortName = "كجم" },
                new Unit { Name = "متر", ShortName = "م" },
                new Unit { Name = "صندوق", ShortName = "صن" }
            );
            await db.SaveChangesAsync();
        }

        await EnsureDefaultPermissionsAsync(serviceProvider);

        await EnsureDemoDataAsync(db, serviceProvider.GetRequiredService<Silk.Trading.Web.Services.IAccountingService>());
    }

    private static async Task EnsureDemoDataAsync(AppDbContext db, Silk.Trading.Web.Services.IAccountingService accounting)
    {
        if (!db.Warehouses.Any())
        {
            db.Warehouses.AddRange(
                new Warehouse { Code = "WH-001", Name = "المخزن الرئيسي", IsActive = true, CreatedAt = DateTime.UtcNow },
                new Warehouse { Code = "WH-002", Name = "المخزن الثانوي", IsActive = true, CreatedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
        }

        if (!db.PurchaseOrders.Any() && db.Items.Any())
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync();
            var item = await db.Items.FirstOrDefaultAsync();
            if (supplier != null && item != null)
            {
                db.PurchaseOrders.Add(new PurchaseOrder
                {
                    OrderNumber = "PRC-20260903-001",
                    SupplierId = supplier.Id,
                    OrderDate = DateTime.Today,
                    ExpectedDate = DateTime.Today.AddDays(7),
                    Status = PurchaseOrderStatus.Draft,
                    Notes = "أمر شراء تجريبي من البذرة",
                    CreatedBy = "admin",
                    CreatedAt = DateTime.UtcNow,
                    Items =
                    {
                        new PurchaseOrderItem
                        {
                            ItemId = item.Id,
                            Quantity = 10,
                            Count = 10,
                            UnitPrice = item.PurchasePrice
                        }
                    }
                });
                await db.SaveChangesAsync();
            }
        }

        if (!db.Currencies.Any())
        {
            db.Currencies.AddRange(
                new Currency { Code = "SDG", Name = "جنيه سوداني", Symbol = "ج.س", ExchangeRate = 1m, IsBase = true, IsActive = true },
                new Currency { Code = "USD", Name = "دولار أمريكي", Symbol = "$", ExchangeRate = 500m, IsBase = false, IsActive = true }
            );
            await db.SaveChangesAsync();
        }

        if (!db.Branches.Any())
        {
            db.Branches.AddRange(
                new Branch { Code = "BR-001", Name = "الفرع الرئيسي", Address = "الخرطوم", IsActive = true, CreatedAt = DateTime.UtcNow },
                new Branch { Code = "BR-002", Name = "فرع أم درمان", Address = "أم درمان", IsActive = true, CreatedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.GLAccounts.AnyAsync(a => a.Code == "4400"))
        {
            db.GLAccounts.AddRange(
                new GLAccount { Code = "4400", Name = "خسائر فروقات العملة (عملة أجنبية)", Type = GLAccountType.Expense, NormalBalance = NormalBalance.Debit, IsActive = true },
                new GLAccount { Code = "8400", Name = "أرباح فروقات العملة (عملة أجنبية)", Type = GLAccountType.Revenue, NormalBalance = NormalBalance.Credit, IsActive = true }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.FiscalPeriods.AnyAsync(fp => fp.Year == DateTime.Today.Year))
        {
            db.FiscalPeriods.Add(new FiscalPeriod { Year = DateTime.Today.Year, Name = $"سنة {DateTime.Today.Year}" });
            await db.SaveChangesAsync();
        }

        if (!await db.BudgetYears.AnyAsync(b => b.Year == DateTime.Today.Year))
        {
            db.BudgetYears.Add(new BudgetYear { Year = DateTime.Today.Year, IsActive = true, CreatedBy = "system" });
            await db.SaveChangesAsync();
        }

        if (!await db.GLAccounts.AnyAsync(a => a.Code == "3001"))
        {
            db.GLAccounts.Add(new GLAccount { Code = "3001", Name = "الأرباح المحتجزة / تجميع الإقفال", Type = GLAccountType.Equity, NormalBalance = NormalBalance.Credit, IsActive = true });
            await db.SaveChangesAsync();
        }

        if (!await db.GLAccounts.AnyAsync(a => a.Code == "5101"))
        {
            db.GLAccounts.AddRange(
                new GLAccount { Code = "5101", Name = "مرتجعات المبيعات", Type = GLAccountType.Expense, NormalBalance = NormalBalance.Debit, IsActive = true },
                new GLAccount { Code = "5102", Name = "مرتجعات المشتريات", Type = GLAccountType.Expense, NormalBalance = NormalBalance.Debit, IsActive = true }
            );
            await db.SaveChangesAsync();
        }

        if (await db.GLAccounts.AnyAsync(a => a.Code == "1000"))
            return;

        db.GLAccounts.AddRange(
            new GLAccount { Code = "1000", Name = "النقد / الصندوق", Type = GLAccountType.Asset, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "1100", Name = "البنوك / الحسابات البنكية", Type = GLAccountType.Asset, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "1200", Name = "المدينون (العملاء)", Type = GLAccountType.Asset, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "1300", Name = "المخزون", Type = GLAccountType.Asset, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "2000", Name = "الدائنون (الموردون)", Type = GLAccountType.Liability, NormalBalance = NormalBalance.Credit, IsActive = true },
            new GLAccount { Code = "3000", Name = "رأس المال", Type = GLAccountType.Equity, NormalBalance = NormalBalance.Credit, IsActive = true },
            new GLAccount { Code = "4000", Name = "إيرادات المبيعات", Type = GLAccountType.Revenue, NormalBalance = NormalBalance.Credit, IsActive = true },
            new GLAccount { Code = "4100", Name = "مرتجعات البيع", Type = GLAccountType.Revenue, NormalBalance = NormalBalance.Credit, IsActive = true },
            new GLAccount { Code = "5000", Name = "تكلفة البضاعة المباعة (COGS)", Type = GLAccountType.Expense, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "5100", Name = "مرتجعات الشراء", Type = GLAccountType.Expense, NormalBalance = NormalBalance.Debit, IsActive = true }
        );
        await db.SaveChangesAsync();

        var categories = new[]
        {
            new ItemCategory { Name = "مواد غذائية", IsActive = true },
            new ItemCategory { Name = "أدوات مكتبية", IsActive = true },
            new ItemCategory { Name = "منظفات", IsActive = true }
        };
        foreach (var category in categories)
        {
            if (!await db.ItemCategories.AnyAsync(c => c.Name == category.Name))
                db.ItemCategories.Add(category);
        }

        var types = new[]
        {
            new ItemType { Name = "منتجات", IsActive = true },
            new ItemType { Name = "خدمات", IsActive = true },
            new ItemType { Name = "مواد تعبئة", IsActive = true }
        };
        foreach (var type in types)
        {
            if (!await db.ItemTypes.AnyAsync(t => t.Name == type.Name))
                db.ItemTypes.Add(type);
        }

        var units = new[]
        {
            new Unit { Name = "لتر", ShortName = "ل" },
            new Unit { Name = "علبة", ShortName = "علبة" },
            new Unit { Name = "طن", ShortName = "طن" }
        };
        foreach (var unit in units)
        {
            if (!await db.Units.AnyAsync(u => u.Name == unit.Name))
                db.Units.Add(unit);
        }

        await db.SaveChangesAsync();

        var masterCount = await db.Units.FirstOrDefaultAsync(u => u.Name == "قطعة");
        if (masterCount != null)
        {
            foreach (var name in new[] { "علبة", "صندوق", "لتر", "طن" })
            {
                var child = await db.Units.FirstOrDefaultAsync(u => u.Name == name);
                if (child != null && child.ParentUnitId == null)
                    child.ParentUnitId = masterCount.Id;
            }
        }

        var suppliers = new[]
        {
            new Supplier { Name = "مصنع النور للأغذية", Code = "SUP-001", Address = "المنطقة الصناعية - القاهرة", Phone = "01012345678", Email = "info@alnoorfood.com", TaxNumber = "600-123-456", OpeningBalance = 0, IsActive = true },
            new Supplier { Name = "شركة الأجيال للإلكترونيات", Code = "SUP-002", Address = "شارع الجمهورية - الإسكندرية", Phone = "01098765432", Email = "sales@alajyal.com", TaxNumber = "600-789-012", OpeningBalance = 0, IsActive = true },
            new Supplier { Name = "مؤسسة البناء الحديث", Code = "SUP-003", Address = "الطريق الدائري - الجيزة", Phone = "01112345678", Email = "info@modernbuild.com", TaxNumber = "600-345-678", OpeningBalance = 0, IsActive = true },
            new Supplier { Name = "مصنع النظافة والمنظفات", Code = "SUP-004", Address = "المنطقة الصناعية - مدينة السادات", Phone = "01212345678", Email = "contact@cleanfactory.com", TaxNumber = "600-901-234", OpeningBalance = 0, IsActive = true }
        };
        foreach (var supplier in suppliers)
        {
            if (!await db.Suppliers.AnyAsync(s => s.Code == supplier.Code))
                db.Suppliers.Add(supplier);
        }

        var customers = new[]
        {
            new Customer { Name = "سوبر ماركت السلام", Code = "CUS-001", Address = "شارع السلام - القاهرة", Phone = "01011112222", Email = "elsalam@example.com", TaxNumber = "700-111-222", OpeningBalance = 0, IsActive = true },
            new Customer { Name = "مخبز المدينة", Code = "CUS-002", Address = "حي المدينة - الجيزة", Phone = "01033334444", Email = "almartabikh@example.com", TaxNumber = "700-333-444", OpeningBalance = 0, IsActive = true },
            new Customer { Name = "مطعم النخبة", Code = "CUS-003", Address = "شارع النخبة - الدقي", Phone = "01055556666", Email = "elnokhba@example.com", TaxNumber = "700-555-666", OpeningBalance = 0, IsActive = true },
            new Customer { Name = "مكتبة المستقبل", Code = "CUS-004", Address = "شارع الثورة - المنصورة", Phone = "01077778888", Email = "almostaqbal@example.com", TaxNumber = "700-777-888", OpeningBalance = 0, IsActive = true },
            new Customer { Name = "شركة الأمل للإنشاءات", Code = "CUS-005", Address = "مدينة العبور - القاهرة", Phone = "01200001111", Email = "alamal@example.com", TaxNumber = "700-999-000", OpeningBalance = 0, IsActive = true }
        };
        foreach (var customer in customers)
        {
            if (!await db.Customers.AnyAsync(c => c.Code == customer.Code))
                db.Customers.Add(customer);
        }

        var unitsByName = await db.Units.ToDictionaryAsync(u => u.Name, u => u.Id);
        var categoriesByName = await db.ItemCategories.ToDictionaryAsync(c => c.Name, c => c.Id);
        var typesByName = await db.ItemTypes.ToDictionaryAsync(t => t.Name, t => t.Id);

        var items = new[]
        {
            new { Name = "أرز بسمتي", Code = "ITM-001", Category = "مواد غذائية", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "كيلوجرام", Purchase = 28, Sale = 32, CurrentCount = 120, CurrentQuantity = 1200, MinCount = 30, MinQuantity = 300 },
            new { Name = "زيت عباد الشمس", Code = "ITM-002", Category = "مواد غذائية", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "لتر", Purchase = 85, Sale = 95, CurrentCount = 60, CurrentQuantity = 60, MinCount = 20, MinQuantity = 20 },
            new { Name = "سكر أبيض", Code = "ITM-003", Category = "مواد غذائية", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "كيلوجرام", Purchase = 22, Sale = 26, CurrentCount = 200, CurrentQuantity = 2000, MinCount = 50, MinQuantity = 500 },
            new { Name = "شاي أخضر", Code = "ITM-004", Category = "مواد غذائية", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "كيلوجرام", Purchase = 45, Sale = 52, CurrentCount = 8, CurrentQuantity = 8, MinCount = 15, MinQuantity = 15 },
            new { Name = "مسحوق غسيل", Code = "ITM-005", Category = "منظفات", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "كيلوجرام", Purchase = 55, Sale = 65, CurrentCount = 90, CurrentQuantity = 90, MinCount = 25, MinQuantity = 25 },
            new { Name = "صابون سائل للأطباق", Code = "ITM-006", Category = "منظفات", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "لتر", Purchase = 30, Sale = 38, CurrentCount = 45, CurrentQuantity = 45, MinCount = 15, MinQuantity = 15 },
            new { Name = "معطر جو", Code = "ITM-007", Category = "منظفات", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "لتر", Purchase = 40, Sale = 48, CurrentCount = 70, CurrentQuantity = 70, MinCount = 20, MinQuantity = 20 },
            new { Name = "ورق تصوير A4", Code = "ITM-008", Category = "أدوات مكتبية", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "علبة", Purchase = 65, Sale = 75, CurrentCount = 55, CurrentQuantity = 55, MinCount = 15, MinQuantity = 15 },
            new { Name = "أقلام حبر جاف", Code = "ITM-009", Category = "أدوات مكتبية", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "قطعة", Purchase = 12, Sale = 18, CurrentCount = 300, CurrentQuantity = 1800, MinCount = 80, MinQuantity = 480 },
            new { Name = "ملفات بلاستيك", Code = "ITM-010", Category = "أدوات مكتبية", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "قطعة", Purchase = 18, Sale = 25, CurrentCount = 140, CurrentQuantity = 840, MinCount = 40, MinQuantity = 240 },
            new { Name = "مروحة سقف", Code = "ITM-011", Category = "إلكترونيات", Type = "منتجات", CountUnit = "قطعة", QuantityUnit = "قطعة", Purchase = 420, Sale = 520, CurrentCount = 35, CurrentQuantity = 35, MinCount = 10, MinQuantity = 10 },
            new { Name = "لمبة LED موفرة", Code = "ITM-012", Category = "إلكترونيات", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "قطعة", Purchase = 14, Sale = 20, CurrentCount = 260, CurrentQuantity = 1560, MinCount = 60, MinQuantity = 360 },
            new { Name = "أسمنت بورتلاندي", Code = "ITM-013", Category = "مواد بناء", Type = "منتجات", CountUnit = "علبة", QuantityUnit = "كيلوجرام", Purchase = 95, Sale = 110, CurrentCount = 180, CurrentQuantity = 9000, MinCount = 50, MinQuantity = 2500 },
            new { Name = "حديد تسليح", Code = "ITM-014", Category = "مواد بناء", Type = "منتجات", CountUnit = "قطعة", QuantityUnit = "طن", Purchase = 5200, Sale = 5450, CurrentCount = 40, CurrentQuantity = 8, MinCount = 15, MinQuantity = 3 },
            new { Name = "طوب أحمر", Code = "ITM-015", Category = "مواد بناء", Type = "منتجات", CountUnit = "قطعة", QuantityUnit = "قطعة", Purchase = 180, Sale = 210, CurrentCount = 12, CurrentQuantity = 12, MinCount = 5, MinQuantity = 5 }
        };
        foreach (var it in items)
        {
            if (await db.Items.AnyAsync(i => i.Code == it.Code)) continue;
            var typeId = typesByName.TryGetValue(it.Type, out var it_t) ? it_t : default;
            var catId = categoriesByName.TryGetValue(it.Category, out var it_c) ? it_c : default;
            db.Items.Add(new Item
            {
                Name = it.Name,
                Code = it.Code,
                ItemTypeId = typeId,
                CategoryId = catId,
                CountUnitId = unitsByName.TryGetValue(it.CountUnit, out var cu) ? cu : null,
                QuantityUnitId = unitsByName.TryGetValue(it.QuantityUnit, out var qu) ? qu : null,
                PurchasePrice = it.Purchase,
                SalePrice = it.Sale,
                MinCount = it.MinCount,
                MinQuantity = it.MinQuantity,
                CurrentCount = it.CurrentCount,
                CurrentQuantity = it.CurrentQuantity,
                IsActive = true,
                IsSellable = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (!await db.Items.AnyAsync(i => i.Code == "ITM-016"))
        {
            typesByName.TryGetValue("مواد خام", out var rawTypeId);
            categoriesByName.TryGetValue("مواد غذائية", out var rawCatId);
            unitsByName.TryGetValue("علبة", out var rawCu);
            unitsByName.TryGetValue("قطعة", out var rawQu);
            db.Items.Add(new Item
            {
                Name = "أكياس تغليف بلاستيكية",
                Code = "ITM-016",
                ItemTypeId = rawTypeId,
                CategoryId = rawCatId,
                CountUnitId = rawCu != default ? rawCu : null,
                QuantityUnitId = rawQu != default ? rawQu : null,
                PurchasePrice = 15,
                SalePrice = 20,
                MinCount = 30,
                MinQuantity = 1500,
                CurrentCount = 180,
                CurrentQuantity = 9000,
                IsActive = true,
                IsSellable = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var demoItemCodes = items.Select(i => i.Code).Append("ITM-016").ToArray();
        var demoItems = await db.Items.Where(i => demoItemCodes.Contains(i.Code)).ToListAsync();
        foreach (var demoItem in demoItems)
        {
            if (await db.StockLayers.AnyAsync(sl => sl.ItemId == demoItem.Id))
                continue;

            db.StockLayers.Add(new StockLayer
            {
                ItemId = demoItem.Id,
                Qty = demoItem.CurrentQuantity,
                Count = demoItem.CurrentCount,
                UnitCost = demoItem.PurchasePrice,
                CountCost = demoItem.PurchasePrice,
                DateReceived = DateTime.Today,
                RemainingQty = demoItem.CurrentQuantity,
                RemainingCount = demoItem.CurrentCount
            });
            await accounting.RecordOpeningStockAsync(demoItem.Id, demoItem.CurrentQuantity, demoItem.CurrentCount,
                demoItem.PurchasePrice, "admin");
        }
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDefaultPermissionsAsync(IServiceProvider serviceProvider)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var db = serviceProvider.GetRequiredService<AppDbContext>();

        var users = userManager.Users.ToList();
        string[] roles = ["Admin", "Accountant", "Warehouse"];
        foreach (var user in users)
        {
            if (await userManager.IsInRoleAsync(user, "Admin")) continue;
            var hasAny = await db.UserPermissions.AnyAsync(p => p.UserId == user.Id);
            if (hasAny) continue;

            string[]? defaults = null;
            foreach (var role in roles)
            {
                if (await userManager.IsInRoleAsync(user, role))
                {
                    defaults = Silk.Trading.Web.Services.PermissionDefaults.DefaultsFor(role);
                    break;
                }
            }
            if (defaults == null || defaults.Length == 0) continue;

            db.UserPermissions.AddRange(defaults.Select(k => new UserPermission
            {
                UserId = user.Id,
                PermissionKey = k
            }));
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureUserAsync(UserManager<IdentityUser> userManager, IConfiguration config, ILogger logger, string userName, string configKey, string email, string role)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user == null)
        {
            var configuredPwd = config[configKey];
            var password = !string.IsNullOrWhiteSpace(configuredPwd) ? configuredPwd : GenerateSecurePassword();
            user = new IdentityUser { UserName = userName, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded && !string.IsNullOrWhiteSpace(configuredPwd))
            {
                logger.LogWarning("كلمة المرور المضبوطة للمستخدم {Role} لا تلبي سياسة التعقيد، جارٍ توليد كلمة مرور عشوائية", role);
                password = GenerateSecurePassword();
                user = new IdentityUser { UserName = userName, Email = email, EmailConfirmed = true };
                result = await userManager.CreateAsync(user, password);
            }
            if (result.Succeeded && string.IsNullOrWhiteSpace(configuredPwd))
                logger.LogInformation("مستخدم البذرة {Role}: كلمة المرور = {Password}", role, password);
        }
        if (!await userManager.IsInRoleAsync(user, role))
            await userManager.AddToRoleAsync(user, role);
    }

    private static string GenerateSecurePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*";
        string all = upper + lower + digits + special;
        var chars = new char[16];
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = special[RandomNumberGenerator.GetInt32(special.Length)];
        for (int i = 4; i < chars.Length; i++)
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}
