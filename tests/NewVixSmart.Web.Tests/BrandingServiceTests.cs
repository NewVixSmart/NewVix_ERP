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
    public void RenderThemeCss_EmitsBothThemeScopes()
    {
        using var db = CreateContext();
        var css = CreateService(db).RenderThemeCss(new BrandingTheme());

        Assert.Contains(":root[data-theme=\"light\"] {", css, StringComparison.Ordinal);
        Assert.Contains("html:root[data-theme=\"dark\"] {", css, StringComparison.Ordinal);
        Assert.DoesNotContain(":root {", css, StringComparison.Ordinal);
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
    public void RenderThemeCss_RecomputesBrandPrimaryForDarkMode()
    {
        using var db = CreateContext();
        var theme = new BrandingTheme { Primary = "#be123c" };
        var css = CreateService(db).RenderThemeCss(theme);
        var darkBlock = css.Substring(css.IndexOf("html:root[data-theme=\"dark\"] {", StringComparison.Ordinal));
        var darkPrimaryLine = darkBlock
            .Split('\n')
            .First(l => l.Contains("--color-primary:", StringComparison.Ordinal));

        Assert.NotEqual("--color-primary: #be123c;", darkPrimaryLine.Trim());
    }

    [Fact]
    public void RenderThemeCss_DarkBlockDoesNotOverrideCarbonSurfaces()
    {
        using var db = CreateContext();
        var css = CreateService(db).RenderThemeCss(new BrandingTheme());
        var darkBlock = css.Substring(css.IndexOf("html:root[data-theme=\"dark\"] {", StringComparison.Ordinal));

        Assert.DoesNotContain("--color-bg-page:", darkBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("--bs-body-bg:", darkBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("--color-sidebar-bg:", darkBlock, StringComparison.Ordinal);
    }
}
