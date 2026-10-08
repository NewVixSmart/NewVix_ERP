using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class BrandingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BrandingServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static BrandingService CreateService(AppDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void RenderThemeCss_EmitsSingleLightScope()
    {
        using var db = CreateContext();
        var css = CreateService(db).RenderThemeCss(new BrandingTheme());

        Assert.Contains(":root {", css, StringComparison.Ordinal);
        Assert.DoesNotContain("data-theme", css, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderThemeCss_ExposesBrandPrimaryInLightMode()
    {
        using var db = CreateContext();
        var theme = new BrandingTheme { Primary = "#be123c" };
        var css = CreateService(db).RenderThemeCss(theme);

        Assert.Contains("--color-primary: #be123c;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderThemeCss_DoesNotEmitDarkChromaRecomputation()
    {
        using var db = CreateContext();
        var theme = new BrandingTheme { Primary = "#be123c" };
        var css = CreateService(db).RenderThemeCss(theme);

        Assert.DoesNotContain("data-theme", css, StringComparison.Ordinal);
    }
}
