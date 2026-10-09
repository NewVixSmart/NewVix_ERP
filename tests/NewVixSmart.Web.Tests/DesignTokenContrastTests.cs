using System.Text.RegularExpressions;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// The design-system source, not the built bundle, is where the palette is authored, so the
/// tokens are read from <c>ui/src/input.css</c>. Contrast is asserted only for pairs that the
/// design actually puts text on top of (body, labels, muted meta, primary/gold/semantic text,
/// and the button text on filled buttons). Decorative roles — like gold-on-white at low weight
/// or the bright-gold ring used only at focus — are deliberately not asserted, because a 4.5:1
/// floor there would not describe a real text pairing. Every pair must resolve to a 6-digit hex
/// present in <c>@theme</c>: a renamed or deleted token fails the test instead of being skipped.
/// </summary>
public sealed class DesignTokenContrastTests
{
    private const double _normalText = 4.5;

    private static readonly string[] _pairs =
    {
        "body text (ink / surface)",
        "labels, secondary (ink-soft / surface)",
        "muted meta (mute / surface)",
        "links, outline text (primary / surface)",
        "primary button label (surface / primary)",
        "eyebrow accent (gold-strong / surface)",
        "sidebar accent (gold-bright / sidebar-bg)",
        "sidebar body (sidebar-text / sidebar-bg)",
        "sidebar section (sidebar-section / sidebar-bg)",
        "success text (ok / surface)",
        "warning text (warn / surface)",
        "danger text (bad / surface)",
        "info text (info / surface)",
        "success button label (surface / ok)",
        "danger button label (surface / bad)",
    };

    [Fact]
    public void EveryDocumentedTokenPair_MeetsTheWcagNormalTextFloor()
    {
        var tokens = ReadThemeTokens();

        AssertContrast(tokens, "ink", "surface", _pairs[0]);
        AssertContrast(tokens, "ink-soft", "surface", _pairs[1]);
        AssertContrast(tokens, "mute", "surface", _pairs[2]);
        AssertContrast(tokens, "primary", "surface", _pairs[3]);
        AssertContrast(tokens, "surface", "primary", _pairs[4]);
        AssertContrast(tokens, "gold-strong", "surface", _pairs[5]);
        AssertContrast(tokens, "gold-bright", "sidebar-bg", _pairs[6]);
        AssertContrast(tokens, "sidebar-text", "sidebar-bg", _pairs[7]);
        AssertContrast(tokens, "sidebar-section", "sidebar-bg", _pairs[8]);
        AssertContrast(tokens, "ok", "surface", _pairs[9]);
        AssertContrast(tokens, "warn", "surface", _pairs[10]);
        AssertContrast(tokens, "bad", "surface", _pairs[11]);
        AssertContrast(tokens, "info", "surface", _pairs[12]);
        AssertContrast(tokens, "surface", "ok", _pairs[13]);
        AssertContrast(tokens, "surface", "bad", _pairs[14]);
    }

    [Fact]
    public void ThePaletteIsNotEmpty_SoTheContrastChecksAboveCannotPassVacuously()
    {
        var tokens = ReadThemeTokens();
        Assert.NotEmpty(tokens);
        Assert.Contains("primary", tokens.Keys); // lifted above because every theme must name it
    }

    private static Dictionary<string, string> ReadThemeTokens()
    {
        var css = File.ReadAllText(InputCssPath());

        var pattern = new Regex(
            @"--color-([a-z0-9-]+):\s*(#[0-9a-fA-F]{3,8})\s*;",
            RegexOptions.Compiled);

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in pattern.Matches(css))
        {
            tokens[match.Groups[1].Value] = match.Groups[2].Value.TrimStart('#');
        }

        return tokens;
    }

    private static void AssertContrast(
        IReadOnlyDictionary<string, string> tokens,
        string foreground,
        string background,
        string pairName)
    {
        Assert.True(tokens.TryGetValue(foreground, out var fg), $"token --color-{foreground} missing for {pairName}");
        Assert.True(tokens.TryGetValue(background, out var bg), $"token --color-{background} missing for {pairName}");
        var validHex = IsSixDigit(fg!, bg!);
        Assert.True(validHex, $"token(s) for {pairName} are not 6-digit hex: --color-{foreground}={fg}, --color-{background}={bg}");
        if (!validHex)
        {
            return;
        }

        var ratio = ContrastRatio(fg!, bg!);
        Assert.True(
            ratio >= _normalText,
            $"{pairName} ({fg} on {bg}) resolves to {ratio:0.00}:1, below the {_normalText:0.0}:1 normal-text floor.");
    }

    private static bool IsSixDigit(string foreground, string background) =>
        foreground.Length == 6 && background.Length == 6;

    private static double ContrastRatio(string hexA, string hexB)
    {
        var la = Luminance(hexA) + 0.05;
        var lb = Luminance(hexB) + 0.05;
        return la > lb ? la / lb : lb / la;
    }

    private static double Luminance(string hex)
    {
        var r = Channel(hex, 0);
        var g = Channel(hex, 2);
        var b = Channel(hex, 4);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double Channel(string hex, int offset)
    {
        var value = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Locates the design-system source by walking up from the test host directory. Declared in
    /// <c>SourceTextGuaranteeInventoryTests</c> as a tooling read: the guarantee here is textual
    /// (the authored tokens must keep their contrast floor), and the rendered proof is the axe
    /// run in the a11y gate.
    /// </summary>
    private static string InputCssPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ui", "src", "input.css");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "تعذر العثور على ui/src/input.css بالبحث الصاعد من " + AppContext.BaseDirectory + ".");
    }
}
