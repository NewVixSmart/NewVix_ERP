using System.IO;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Keeps the theme bootstrap script single-sourced. The script that resolves the saved
/// theme mode and pins <c>data-theme</c> exists in exactly one shape - the
/// <c>_ThemeScript</c> partial - so a divergence in FOUC behaviour between layout and
/// standalone pages is a textual signal, not a re-read of three copies.
/// </summary>
public sealed class ThemeScriptPartialTests
{
    [Fact]
    public void AllThreeHostingPagesUseTheSharedPartial()
    {
        var layout = ReadView("Shared", "_Layout.cshtml");
        var login = ReadView("Account", "Login.cshtml");
        var accessDenied = ReadView("Account", "AccessDenied.cshtml");

        Assert.Contains("Html.PartialAsync(\"_ThemeScript\"", layout, System.StringComparison.Ordinal);
        Assert.Contains("Html.PartialAsync(\"_ThemeScript\"", login, System.StringComparison.Ordinal);
        Assert.Contains("Html.PartialAsync(\"_ThemeScript\"", accessDenied, System.StringComparison.Ordinal);
    }

    [Fact]
    public void StandalonePagesNoLongerDuplicateTheInlineThemeScript()
    {
        var login = ReadView("Account", "Login.cshtml");
        var accessDenied = ReadView("Account", "AccessDenied.cshtml");

        Assert.DoesNotContain("prefers-color-scheme", login, System.StringComparison.Ordinal);
        Assert.DoesNotContain("prefers-color-scheme", accessDenied, System.StringComparison.Ordinal);
    }

    private static string ReadView(string folder, string name)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "NewVixSmart.Web", "Views", folder, name);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "NewVixSmart.slnx");
            if (File.Exists(candidate))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"تعذر العثور على جذر الريبو بالبحث الصاعد من {AppContext.BaseDirectory}.");
    }
}
