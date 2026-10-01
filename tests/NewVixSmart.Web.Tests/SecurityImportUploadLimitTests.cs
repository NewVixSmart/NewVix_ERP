using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
/// refuses an oversized IFormFile before it is buffered - neither of which depends on the other. A
/// third, independent ceiling lives in ImportCenterService for callers that never pass through the
/// controller at all.
///
/// WHAT CHANGED. Preview_LimitIsNotWiderThanTheDocumentedCeiling read
/// ImportCenterController.cs and ImportCenterService.cs as text and asserted the literal
/// "_maxUploadBytes = 25L * 1024 * 1024" and "_maxFileBytes = 25 * 1024 * 1024" appeared in them.
/// That is gone. The controller's 25 MiB gate is now proven by posting a real 25 MiB + 512 byte file
/// over HTTPS and getting the refusal, and the service's own ceiling is proven by handing the real
/// service a byte[] over the limit and reading the message it returns.
///
/// THE PARSER DOUBLE. Every request here is a real multipart POST to the running host: real Kestrel,
/// real antiforgery filter, real [RequirePerm], real controller. The one thing a real parser cannot
/// show is that it was NOT reached, so these tests register a recording IImportCenterService through
/// the same DI the host uses and assert its ParseCallCount. The controller and its size gate - the
/// behaviour under test - are the production ones; only the parser behind it is observed. The test
/// that exercises the service's own ceiling resolves the real ImportCenterService instead.
/// </summary>
public sealed class SecurityImportUploadLimitTests
{
    private const long _maxUploadBytes = 25L * 1024 * 1024;

    /// <summary>Envelope room over the file ceiling, mirrored from the controller's constants.</summary>
    private const long _maxUploadRequestBytes = _maxUploadBytes + 1024 * 1024;

    private const string _username = "importuser";
    private const string _password = "Import@123456";
    private const string _entityKey = "customers";

    private const string _overCeilingMessage = "حجم الملف أكبر من الحد المسموح به (25 ميجابايت)";
    private const string _emptyFileMessage = "اختر ملفًا أولاً";
    private const string _serviceOverCeilingMessage = "حجم الملف يتجاوز الحد الأقصى المسموح به (25 ميجابايت)";

    /// <summary>
    /// The attributes are metadata, not behaviour, so this one stays as it was: it pins the exact
    /// envelope a legitimate 25 MB file plus its multipart boundaries and antiforgery field needs,
    /// which the HTTP test below cannot state as a number. The HTTP test is what proves the server
    /// acts on it.
    /// </summary>
    [Fact]
    public void Preview_RefusesAnOversizedBodyBeforeItIsRead()
    {
        var preview = typeof(ImportCenterController).GetMethod(nameof(ImportCenterController.Preview))!;

        // RequestSizeLimitAttribute keeps its value in the constructor, so the metadata is read back
        // rather than a property.
        var sizeLimit = Assert.Single(preview.GetCustomAttributesData(),
            a => a.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.IsType<long>(sizeLimit.ConstructorArguments[0].Value);
        Assert.Equal(_maxUploadRequestBytes, Assert.IsType<long>(sizeLimit.ConstructorArguments[0].Value));

        var formLimits = Assert.Single(preview.GetCustomAttributes(typeof(RequestFormLimitsAttribute), false)
            .Cast<RequestFormLimitsAttribute>());
        Assert.Equal(_maxUploadRequestBytes, formLimits.MultipartBodyLengthLimit);
    }

    /// <summary>
    /// A body past the request envelope is refused by the server before any MVC code runs. The
    /// response is a bare 400 with no body and no content type: it is neither the application's JSON
    /// error body nor the HTML status page that a 4xx from inside MVC is re-executed into, which is
    /// how the test can tell the refusal happened at the transport rather than in a handler.
    ///
    /// Expect: 100-continue is sent because that is how a well-behaved client submits a large upload
    /// it has not been told is acceptable yet; it also makes the outcome deterministic, since the
    /// server answers from the declared length and the 27 MB is never put on the wire.
    /// </summary>
    [Fact]
    public async Task AnOversizedBody_IsRefusedByTheServerBeforeTheControllerRuns()
    {
        var recording = new RecordingImportService();
        await using var app = await StartAsync(recording);

        var antiforgery = await app.AntiforgeryTokenAsync("/ImportCenter/Index");
        var oversized = new byte[_maxUploadRequestBytes + 4096];
        using var response = await app.PostMultipartAsync(
            $"/ImportCenter/Preview?key={_entityKey}", "file", "big.xlsx", oversized, antiforgery,
            expectContinue: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Equal(0, recording.ParseCallCount);
    }

    /// <summary>
    /// A file over the 25 MiB ceiling but inside the request envelope is refused by the controller's
    /// own gate, which runs before the file is copied into memory. The user-visible half is the
    /// redirect and the Arabic message the layout renders from TempData on the page it lands on.
    /// </summary>
    [Fact]
    public async Task AFileOverTheCeilingButInsideTheEnvelope_IsRefusedWithItsMessage()
    {
        var recording = new RecordingImportService();
        await using var app = await StartAsync(recording);

        var antiforgery = await app.AntiforgeryTokenAsync("/ImportCenter/Index");
        var overCeiling = new byte[_maxUploadBytes + 512];
        using var response = await app.PostMultipartAsync(
            $"/ImportCenter/Preview?key={_entityKey}", "file", "big.xlsx", overCeiling, antiforgery);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/ImportCenter", response.Headers.Location!.ToString());
        Assert.Equal(0, recording.ParseCallCount);

        // Exactly one GET after the redirect: TempData is read-once, so a second one would find
        // nothing and the assertion would be about the test's request order, not the app's behaviour.
        using var page = await app.GetAsync("/ImportCenter");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(_overCeilingMessage, await ReadDecodedAsync(page), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyFile_IsRefusedWithItsOwnMessage()
    {
        var recording = new RecordingImportService();
        await using var app = await StartAsync(recording);

        var antiforgery = await app.AntiforgeryTokenAsync("/ImportCenter/Index");
        using var response = await app.PostMultipartAsync(
            $"/ImportCenter/Preview?key={_entityKey}", "file", "empty.xlsx", [], antiforgery);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, recording.ParseCallCount);

        using var page = await app.GetAsync("/ImportCenter");
        Assert.Contains(_emptyFileMessage, await ReadDecodedAsync(page), StringComparison.Ordinal);
    }

    /// <summary>
    /// A file inside the ceiling is not refused: it reaches the parser and the preview view is
    /// rendered from what the parser returned. The mark is the entity name the view prints, so a
    /// 200 from anywhere other than the preview would not satisfy it.
    /// </summary>
    [Fact]
    public async Task AFileWithinTheCeiling_IsHandedToTheParser()
    {
        var recording = new RecordingImportService();
        await using var app = await StartAsync(recording);

        var antiforgery = await app.AntiforgeryTokenAsync("/ImportCenter/Index");
        using var response = await app.PostMultipartAsync(
            $"/ImportCenter/Preview?key={_entityKey}", "file", "ok.xlsx", [1, 2, 3, 4], antiforgery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, recording.ParseCallCount);
        Assert.Contains(RecordingImportService.EntityNameAr, await ReadDecodedAsync(response), StringComparison.Ordinal);
    }

    /// <summary>
    /// The controller gate is an optimisation for web requests; the service keeps the identical
    /// ceiling so a second caller cannot slip past it. This hands the real service a byte[] over the
    /// limit and reads the message it returns - no HTTP, no controller, no double.
    /// </summary>
    [Fact]
    public async Task TheServiceKeepsItsOwnCeiling_ForCallersThatNeverSeeTheController()
    {
        await using var app = await StartAsync(recording: null);

        var preview = await app.InScopeAsync(services =>
            services.GetRequiredService<IImportCenterService>()
                .ParseAsync(_entityKey, "big.xlsx", new byte[_maxUploadBytes + 1]));

        Assert.Equal(_serviceOverCeilingMessage, preview.FatalError);
    }

    /// <summary>
    /// The TempData alert the layout renders goes through the default HtmlEncoder, which turns every
    /// non-Latin character into a numeric character reference. The browser sees the same message
    /// either way, so decode before comparing: asserting on the raw markup would be asserting on the
    /// encoder, not on the message.
    /// </summary>
    private static async Task<string> ReadDecodedAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private static async Task<LiveWebApp> StartAsync(RecordingImportService? recording)
    {
        var app = await LiveWebApp.StartAsync(setup =>
        {
            if (recording is not null)
            {
                // Registered after the mirrored registrations, so it is the one the controller is
                // given. A singleton because the test holds the same instance it asserts on, while
                // the host resolves it once per request.
                setup.ConfigureServices = services => services.AddSingleton<IImportCenterService>(recording);
            }
        });

        // Admin carries the role the real PermissionService short-circuits on, which is what makes
        // [RequirePerm("ImportCenter.View")] pass without inventing a permission row.
        await app.SeedUserAsync(_username, _password, LiveWebApp.AdminRole);
        // ImportCentre is an authenticated page, so the browser session has to be real before any
        // antiforgery token can be fetched from it.
        using var login = await app.LoginAsync(_username, _password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return app;
    }

    /// <summary>
    /// Stands in for the parser only, so a test can tell "the size gate refused before parsing" from
    /// "the parser ran and disliked the file". The controller, its gates and every piece of HTTP
    /// around it are the production ones.
    /// </summary>
    private sealed class RecordingImportService : IImportCenterService
    {
        public const string EntityNameAr = "العملاء";

        public int ParseCallCount { get; private set; }

        public IReadOnlyList<ImportEntityDefinition> GetEntities() => [_entity];

        // The controller answers NotFound for an unknown key before it ever looks at the file, so the
        // fake has to know the key the tests use.
        public ImportEntityDefinition? FindEntity(string key) =>
            key == _entity.Key ? _entity : null;

        private static readonly ImportEntityDefinition _entity = new(
            "customers", EntityNameAr, "وصف", "bi-people", "customers", "الكود", "Code", null, "Code", []);

        public Task<byte[]> DownloadTemplateAsync(string entityKey) => Task.FromResult(Array.Empty<byte>());

        public Task<ImportPreviewViewModel> ParseAsync(string entityKey, string fileName, byte[] data)
        {
            ParseCallCount++;
            // The preview view prints EntityNameAr, so a rendered page proves the parser's result was
            // the thing that was rendered.
            return Task.FromResult(new ImportPreviewViewModel { EntityKey = entityKey, EntityNameAr = EntityNameAr });
        }

        public Task<ImportResult> ImportAsync(string entityKey, string payload, string applyToken) =>
            Task.FromResult(new ImportResult(true, "ok", 0, 0, 0, 0));
    }
}
