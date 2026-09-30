using System.Text.Json;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Configuration hardening gates (audit round 20, M-1 / M-4):
///   - M-4: the default seed usernames/passwords must never be committed in appsettings.json;
///     they live in appsettings.Development.json only, and the production startup gate in
///     Program.cs treats a missing Seed key exactly like a shipped default.
///   - M-1: forwarded headers must stay opt-in, so the rate limiter is never silently keyed
///     on an unconfigured reverse proxy.
/// These tests read the real files from disk (no configuration host) so a regression in the
/// committed JSON fails the build instead of shipping secrets.
/// </summary>
public sealed class ProductionConfigGateTests
{
    private static readonly string[] _shippedSeedDefaults = ["Admin@123", "Acc@12345", "War@12345"];

    private static readonly JsonDocumentOptions _jsonOptions = new()
    {
        // appsettings*.json are JSONC: the Web config provider and this test both skip comments.
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string WebProjectDirectory()
    {
        for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "NewVixSmart.Web");
            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"تعذر العثور على src\\NewVixSmart.Web\\appsettings.json بالبحث الصاعد من {AppContext.BaseDirectory}.");
    }

    private static string ReadSettings(string fileName) =>
        File.ReadAllText(Path.Combine(WebProjectDirectory(), fileName));

    private static JsonElement ReadSection(string fileName, string sectionName)
    {
        using var document = JsonDocument.Parse(ReadSettings(fileName), _jsonOptions);
        Assert.True(document.RootElement.TryGetProperty(sectionName, out var section),
            $"{fileName} must contain a \"{sectionName}\" section.");
        return section.Clone();
    }

    [Fact]
    public void BaseSettings_ShipsNoSeedPasswords()
    {
        var raw = ReadSettings("appsettings.json");

        foreach (var shipped in _shippedSeedDefaults)
        {
            Assert.False(raw.Contains(shipped, StringComparison.Ordinal),
                $"appsettings.json must not contain the shipped seed password \"{shipped}\".");
        }

        using var document = JsonDocument.Parse(raw, _jsonOptions);
        Assert.False(document.RootElement.TryGetProperty("Seed", out _),
            "appsettings.json must not ship a Seed section at all; the defaults belong to appsettings.Development.json.");
    }

    [Fact]
    public void DevelopmentSettings_KeepsSeedBlockSoDevSeedingWorks()
    {
        var seed = ReadSection("appsettings.Development.json", "Seed");

        var admin = seed.GetProperty("AdminPassword").GetString() ?? string.Empty;
        var accountant = seed.GetProperty("AccountantPassword").GetString() ?? string.Empty;
        var warehouse = seed.GetProperty("WarehousePassword").GetString() ?? string.Empty;

        Assert.Equal("Admin@123", admin);
        Assert.Equal("Acc@12345", accountant);
        Assert.Equal("War@12345", warehouse);
    }

    [Fact]
    public void BaseSettings_ForwardedHeadersAreOptIn()
    {
        var forwarded = ReadSection("appsettings.json", "ForwardedHeaders");

        var knownProxies = forwarded.GetProperty("KnownProxies").GetString() ?? string.Empty;
        var knownNetworks = forwarded.GetProperty("KnownNetworks").GetString() ?? string.Empty;

        Assert.False(forwarded.GetProperty("Enabled").GetBoolean(),
            "ForwardedHeaders:Enabled must default to false so no untrusted proxy is trusted implicitly.");
        Assert.True(forwarded.TryGetProperty("ForwardLimit", out _),
            "ForwardedHeaders:ForwardLimit must be present in the base settings.");
        Assert.Equal(string.Empty, knownProxies);
        Assert.Equal(string.Empty, knownNetworks);
    }

    [Fact]
    public void DevelopmentSettings_DoesNotEnableForwardedHeaders()
    {
        var forwarded = ReadSection("appsettings.Development.json", "ForwardedHeaders");

        Assert.False(forwarded.GetProperty("Enabled").GetBoolean(),
            "Development must not enable forwarded headers either; local runs have no reverse proxy.");
    }
}
