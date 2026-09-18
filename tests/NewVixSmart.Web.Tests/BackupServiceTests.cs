using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Infrastructure;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly List<string> _tempRoots = new();

    public BackupServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        foreach (var root in _tempRoots)
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private BackupService CreateBackupService()
    {
        var root = Path.Combine(Path.GetTempPath(), "vix-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=(localdb)\\MSSQLLocalDB;Database=NewVixSmartDb;Trusted_Connection=True;TrustServerCertificate=True"
            })
            .Build();

        var env = new FakeWebHostEnvironment { ContentRootPath = root };
        return new BackupService(config, env, NullLogger<BackupService>.Instance);
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "NewVixSmart.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private AppDbContext CreateContext() => new(_options);

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

    private static RoleManager<IdentityRole> CreateRoleManager(AppDbContext db)
    {
        var store = new RoleStore<IdentityRole>(db);
        return new RoleManager<IdentityRole>(
            store,
            Enumerable.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);
    }

    [Fact]
    public void IsValid_ValidFileName_ReturnsTrue()
        => Assert.True(BackupFileName.IsValid("VixSmart_20260916.bak"));

    [Fact]
    public void IsValid_Null_ReturnsFalse()
        => Assert.False(BackupFileName.IsValid(null));

    [Fact]
    public void IsValid_DotDotSlash_ReturnsFalse()
        => Assert.False(BackupFileName.IsValid("..\\evil.bak"));

    [Fact]
    public void IsValid_ForwardSlash_ReturnsFalse()
        => Assert.False(BackupFileName.IsValid("a/b.bak"));

    [Fact]
    public void IsValid_BackSlash_ReturnsFalse()
        => Assert.False(BackupFileName.IsValid("a\\b.bak"));

    [Fact]
    public void IsValid_TxtExtension_ReturnsFalse()
        => Assert.False(BackupFileName.IsValid("VixSmart_20260916.txt"));

    [Fact]
    public void IsValid_NoExtension_ReturnsFalse()
        => Assert.False(BackupFileName.IsValid("NewVixSmart"));

    [Fact]
    public void IsValid_DifferentCaseExtension_ReturnsTrue()
        => Assert.True(BackupFileName.IsValid("newvixsmart_BIG.BAK"));

    [Fact]
    public void IsValid_NameWithSpaces_ReturnsTrue()
        => Assert.True(BackupFileName.IsValid("New Vix Smart 2026.bak"));

    [Fact]
    public async Task InitializeAsync_FirstRun_CreatesAllSeedData()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);
        var rm = CreateRoleManager(db);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminPassword"] = "Test@Admin12345",
                ["Seed:AccountantPassword"] = "Test@Acct123456",
                ["Seed:WarehousePassword"] = "Test@Ware123456"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(um);
        services.AddSingleton(rm);
        services.AddSingleton(db);
        services.AddSingleton<IAccountingService>(new AccountingService(db));
        var provider = services.BuildServiceProvider();

        await SeedData.InitializeAsync(provider);

        Assert.NotNull(await um.FindByNameAsync("admin"));
        Assert.NotNull(await um.FindByNameAsync("accountant"));
        Assert.NotNull(await um.FindByNameAsync("warehouse"));
        Assert.True(await rm.RoleExistsAsync("Admin"));
        Assert.True(await rm.RoleExistsAsync("Accountant"));
        Assert.True(await rm.RoleExistsAsync("Warehouse"));
        Assert.NotEmpty(db.ItemCategories);
    }

    [Fact]
    public async Task InitializeAsync_AdminExists_SkipsAdditionalSeedData()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);
        var rm = CreateRoleManager(db);
        await rm.CreateAsync(new IdentityRole("Admin"));
        var admin = new IdentityUser { Id = "admin-id", UserName = "admin", Email = "admin@vix.com", EmailConfirmed = true };
        await um.CreateAsync(admin, "Adm!n123456");
        await um.AddToRoleAsync(admin, "Admin");

        IConfiguration config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(um);
        services.AddSingleton(rm);
        var provider = services.BuildServiceProvider();

        await SeedData.InitializeAsync(provider);

        Assert.Null(await um.FindByNameAsync("accountant"));
        Assert.Null(await um.FindByNameAsync("warehouse"));
        Assert.Empty(db.ItemCategories);
    }

    [Fact]
    public void BackupController_HasAdminAuthorize()
    {
        var attr = Attribute.GetCustomAttribute(typeof(BackupController), typeof(AuthorizeAttribute));
        Assert.NotNull(attr);
        Assert.Equal("Admin", ((AuthorizeAttribute)attr!).Roles);
    }

    [Fact]
    public void BackupController_PostActions_HaveValidateAntiForgeryToken()
    {
        var actions = new[] { "Create", "Delete", "Restore", "Reset" };
        var type = typeof(BackupController);
        foreach (var name in actions)
        {
            var method = type.GetMethod(name);
            Assert.NotNull(method);
            Assert.NotNull(Attribute.GetCustomAttribute(method!, typeof(HttpPostAttribute)));
            Assert.NotNull(Attribute.GetCustomAttribute(method!, typeof(ValidateAntiForgeryTokenAttribute)));
        }
    }

    [Fact]
    public void BackupController_Download_HasHttpGet()
    {
        var method = typeof(BackupController).GetMethod("Download");
        Assert.NotNull(method);
        Assert.NotNull(Attribute.GetCustomAttribute(method!, typeof(HttpGetAttribute)));
    }

    [Fact]
    public void ListBackups_EmptyDirectory_ReturnsEmpty()
    {
        var svc = CreateBackupService();
        Assert.True(Directory.Exists(svc.BackupDirectory));
        Assert.Empty(svc.ListBackups());
    }

    [Fact]
    public async Task ListBackups_ReturnsOnlyBakFiles_OrderedByNameDescending()
    {
        var svc = CreateBackupService();
        await File.WriteAllTextAsync(Path.Combine(svc.BackupDirectory, "VixSmart_20260101_000000.bak"), "a");
        await File.WriteAllTextAsync(Path.Combine(svc.BackupDirectory, "VixSmart_20260102_000000.bak"), "b");
        await File.WriteAllTextAsync(Path.Combine(svc.BackupDirectory, "notes.txt"), "ignore");

        var list = svc.ListBackups();

        Assert.Equal(2, list.Count);
        Assert.Equal("VixSmart_20260102_000000.bak", list[0].FileName);
        Assert.Equal("VixSmart_20260101_000000.bak", list[1].FileName);
        Assert.All(list, f => Assert.Equal(1, f.LengthBytes));
    }

    [Fact]
    public async Task ReadBackupBytesAsync_ReturnsFileContent()
    {
        var svc = CreateBackupService();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(Path.Combine(svc.BackupDirectory, "VixSmart_20260103_000000.bak"), bytes);

        var read = await svc.ReadBackupBytesAsync("VixSmart_20260103_000000.bak");

        Assert.Equal(bytes, read);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_RemovesIt()
    {
        var svc = CreateBackupService();
        var name = "VixSmart_20260104_000000.bak";
        await File.WriteAllTextAsync(Path.Combine(svc.BackupDirectory, name), "x");

        await svc.DeleteAsync(name);

        Assert.False(File.Exists(Path.Combine(svc.BackupDirectory, name)));
        Assert.Empty(svc.ListBackups());
    }

    [Theory]
    [InlineData("..\\evil.bak")]
    [InlineData("sub/dir.bak")]
    [InlineData("plain.txt")]
    public async Task DeleteAsync_InvalidFileName_Throws(string fileName)
    {
        var svc = CreateBackupService();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.DeleteAsync(fileName));
    }

    [Fact]
    public async Task ResolveBackupPath_MissingFile_ThrowsFileNotFound()
    {
        var svc = CreateBackupService();
        await Assert.ThrowsAsync<FileNotFoundException>(() => svc.ReadBackupBytesAsync("VixSmart_20260105_000000.bak"));
    }

    [Fact]
    public void ResolveBackupPath_ValidFile_ReturnsPathUnderBackupDirectory()
    {
        var svc = CreateBackupService();
        var name = "VixSmart_20260106_000000.bak";
        File.WriteAllText(Path.Combine(svc.BackupDirectory, name), "x");

        var resolved = svc.ResolveBackupPath(name);

        Assert.StartsWith(Path.GetFullPath(svc.BackupDirectory), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(name, resolved, StringComparison.Ordinal);
    }
}
