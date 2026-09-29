using System.Text.RegularExpressions;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// The Content-Security-Policy in Program.cs authorises scripts with a per-request nonce and
/// nothing else: script-src carries 'nonce-...' but never 'unsafe-inline' and never 'unsafe-eval'.
///
/// A single inline &lt;script&gt; without the nonce is therefore dead weight under that policy - the
/// browser blocks it - and the tempting fix is to add 'unsafe-inline', which would delete the
/// protection the whole policy exists for. So every inline script in every view is checked here, and
/// the external ones are checked to stay external.
/// </summary>
public sealed class SecurityViewCspNonceTests
{
    private const string NonceExpression = "@Context.GetCspNonce()";

    [Fact]
    public void EveryInlineScriptInEveryViewCarriesTheCspNonce()
    {
        var offenders = new List<string>();
        var views = 0;
        var inlineScripts = 0;

        foreach (var view in EnumerateViews())
        {
            views++;
            var markup = File.ReadAllText(view);
            foreach (Match match in Regex.Matches(markup, @"<script\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var tag = match.Value;
                // A script with src is not inline content, so the policy does not need a nonce for it.
                if (Regex.IsMatch(tag, @"\bsrc\s*=", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                inlineScripts++;
                if (!tag.Contains(NonceExpression, StringComparison.Ordinal))
                {
                    offenders.Add($"{Relative(view)}: {tag.Trim()}");
                }
            }
        }

        Assert.True(views > 0, "لم يُعثر على أي ملف view.");
        Assert.True(inlineScripts > 0, "لم يُعثر على أي script مضمّن؛ الفحص لا يعمل.");
        Assert.True(offenders.Count == 0,
            "كل script مضمّن يحتاج @Context.GetCspNonce() وإلا سيحجبه المتصفح:\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void ViewsNeverInlineAnEvilNonceOrDropTheNonceExpression()
    {
        foreach (var view in EnumerateViews())
        {
            var markup = File.ReadAllText(view);
            Assert.DoesNotContain("'unsafe-inline'", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("'unsafe-eval'", markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProgramKeepsScriptSrcNonceOnly()
    {
        var program = File.ReadAllText(WebProjectFile("Program.cs"));
        var match = Regex.Match(program, @"script-src[^;]*;");
        Assert.True(match.Success, "لم يُعثر على توجيه script-src في سياسة CSP.");

        var scriptSrc = match.Value;
        Assert.Contains("'nonce-", scriptSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", scriptSrc, StringComparison.Ordinal);
        // 'strict-dynamic' would also void the nonce allowlist, so it is called out by name.
        Assert.DoesNotContain("'strict-dynamic'", scriptSrc, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateViews()
    {
        var root = Path.Combine(WebProjectDirectory(), "Views");
        Assert.True(Directory.Exists(root), $"مجلد Views غير موجود: {root}");
        return Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string WebProjectDirectory()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "NewVixSmart.Web");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"تعذر العثور على src\\NewVixSmart.Web بالبحث الصاعد من {AppContext.BaseDirectory}.");
    }

    private static string WebProjectFile(string relative) => Path.Combine(WebProjectDirectory(), relative);

    private static string Relative(string path) =>
        Path.GetRelativePath(WebProjectDirectory(), path).Replace('\\', '/');
}
