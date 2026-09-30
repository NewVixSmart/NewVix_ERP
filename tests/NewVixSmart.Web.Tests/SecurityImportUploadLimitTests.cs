using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Import;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// The import preview used to accept any upload and then copy the file into a MemoryStream and call
/// ToArray() on it, so every byte of the request was held twice on the heap before a single row was
/// validated. With no ceiling on the endpoint, a handful of concurrent multi-gigabyte posts was
/// enough to take the process down.
///
/// The fix is two gates: the server refuses an oversized body before it is read, and the controller
/// refuses an oversized IFormFile before it is buffered - neither of which depends on the other.
/// </summary>
public sealed class SecurityImportUploadLimitTests
{
    private const long _maxUploadBytes = 25L * 1024 * 1024;

    [Fact]
    public void Preview_RefusesAnOversizedBodyBeforeItIsRead()
    {
        var preview = typeof(ImportCenterController).GetMethod(nameof(ImportCenterController.Preview))!;

        // The envelope room over the file ceiling is for the multipart boundaries and the antiforgery
        // field, so a legitimate 25 MB file is not rejected by the transport-level gate.
        var expected = _maxUploadBytes + 1024 * 1024;

        // RequestSizeLimitAttribute keeps its value in the constructor, so the metadata is read back
        // rather than a property.
        var sizeLimit = Assert.Single(preview.GetCustomAttributesData(),
            a => a.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(expected, Assert.IsType<long>(sizeLimit.ConstructorArguments[0].Value));

        var formLimits = Assert.Single(preview.GetCustomAttributes(typeof(RequestFormLimitsAttribute), false)
            .Cast<RequestFormLimitsAttribute>());
        Assert.Equal(expected, formLimits.MultipartBodyLengthLimit);
    }

    [Fact]
    public void Preview_LimitIsNotWiderThanTheDocumentedCeiling()
    {
        var source = File.ReadAllText(TestPaths.WebProjectFile("Controllers", "ImportCenterController.cs"));
        Assert.Contains("_maxUploadBytes = 25L * 1024 * 1024", source, StringComparison.Ordinal);
        // The service keeps its own identical ceiling: the controller gate is an optimisation, not
        // a replacement, and a second caller must not be able to slip past it.
        Assert.Contains("_maxFileBytes = 25 * 1024 * 1024",
            File.ReadAllText(TestPaths.WebProjectFile("Services", "ImportCenterService.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedFile_IsRefusedWithoutEverBufferingIt()
    {
        var import = new RecordingImportService();
        var controller = new ImportCenterController(import) { TempData = new FakeTempData() };
        // A FormFile that reports a length over the ceiling. The declared length is all the gate
        // needs, so this stands in for a real oversized upload without allocating one.
        var file = new FormFile(new MemoryStream(), 0, _maxUploadBytes + 1, "file", "big.xlsx")
        {
            Headers = new HeaderDictionary()
        };

        var result = await controller.Preview("customers", file);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ImportCenterController.Index), redirect.ActionName);
        Assert.Equal("حجم الملف أكبر من الحد المسموح به (25 ميجابايت)", controller.TempData["Error"]);
        // Nothing was parsed and nothing was copied: the service never saw the bytes.
        Assert.Equal(0, import.ParseCallCount);
    }

    [Fact]
    public async Task FileWithinTheCeiling_StillReachesTheParser()
    {
        var import = new RecordingImportService();
        var controller = new ImportCenterController(import) { TempData = new FakeTempData() };
        using var content = new MemoryStream([1, 2, 3, 4]);
        var file = new FormFile(content, 0, content.Length, "file", "ok.xlsx")
        {
            Headers = new HeaderDictionary()
        };

        var result = await controller.Preview("customers", file);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(1, import.ParseCallCount);
    }

    [Fact]
    public async Task EmptyFile_IsRefusedWithItsOwnMessage()
    {
        var import = new RecordingImportService();
        var controller = new ImportCenterController(import) { TempData = new FakeTempData() };
        var file = new FormFile(new MemoryStream(), 0, 0, "file", "empty.xlsx")
        {
            Headers = new HeaderDictionary()
        };

        var result = await controller.Preview("customers", file);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("اختر ملفًا أولاً", controller.TempData["Error"]);
        Assert.Equal(0, import.ParseCallCount);
    }

    private sealed class RecordingImportService : IImportCenterService
    {
        public int ParseCallCount { get; private set; }

        public IReadOnlyList<ImportEntityDefinition> GetEntities() => [_entity];

        // The controller answers NotFound for an unknown key before it ever looks at the file, so the
        // fake has to know the key the tests use.
        public ImportEntityDefinition? FindEntity(string key) =>
            key == _entity.Key ? _entity : null;

        private static readonly ImportEntityDefinition _entity = new(
            "customers", "العملاء", "وصف", "bi-people", "customers", "الكود", "Code", null, "Code", []);

        public Task<byte[]> DownloadTemplateAsync(string entityKey) => Task.FromResult(Array.Empty<byte>());

        public Task<ImportPreviewViewModel> ParseAsync(string entityKey, string fileName, byte[] data)
        {
            ParseCallCount++;
            return Task.FromResult(new ImportPreviewViewModel());
        }

        public Task<ImportResult> ImportAsync(string entityKey, string payload, string applyToken) =>
            Task.FromResult(new ImportResult(true, "ok", 0, 0, 0, 0));
    }

    // ITempDataDictionary is a Dictionary plus a handful of lifecycle methods, none of which the
    // controller touches, so they are no-ops here and the real indexer behaviour is inherited.
    private sealed class FakeTempData : Dictionary<string, object?>, ITempDataDictionary
    {
        public void Load() { }

        public void Save() { }

        public void Keep() { }

        public void Keep(string key) { }

        public object? Peek(string key) => TryGetValue(key, out var value) ? value : null;
    }
}
