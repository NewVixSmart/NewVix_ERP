using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Core;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Detail actions took <c>Details(string id)</c> but fell back to <c>int.TryParse</c>, so a caller
/// holding the module's view key could walk <c>/Items/Details/1</c>, <c>/Items/Details/2</c> and so
/// on. The public id is now the only accepted key.
///
/// The same shape applied to a second overposting hole: a new item's reserved balances and owning
/// branch are server-owned, and must never come from the form.
/// </summary>
public sealed class AuthzPublicIdAndItemBindingTests
{
    public static TheoryData<string, string> DetailsControllers() => new()
    {
        { "src/NewVixSmart.Web/Controllers/ItemsController.cs", "ItemsController" },
        { "src/NewVixSmart.Web/Controllers/SalesController.cs", "SalesController" },
        { "src/NewVixSmart.Web/Controllers/SalesOrdersController.cs", "SalesOrdersController" },
        { "src/NewVixSmart.Web/Controllers/SalesQuotesController.cs", "SalesQuotesController" },
        { "src/NewVixSmart.Web/Controllers/PurchasesController.cs", "PurchasesController" },
        { "src/NewVixSmart.Web/Controllers/PaymentsController.cs", "PaymentsController" },
        { "src/NewVixSmart.Web/Controllers/PurchaseOrdersController.cs", "PurchaseOrdersController" },
        { "src/NewVixSmart.Web/Controllers/PurchaseReturnsController.cs", "PurchaseReturnsController" },
        { "src/NewVixSmart.Web/Controllers/SaleReturnsController.cs", "SaleReturnsController" },
        { "src/NewVixSmart.Web/Controllers/DeliveryOrdersController.cs", "DeliveryOrdersController" }
    };

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewVixSmart.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "Could not locate the repository root from " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Extracts the body of a method by brace matching from its signature.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Signature not found: {signature}");

        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException("Unbalanced braces after " + signature);
    }

    [Theory]
    [MemberData(nameof(DetailsControllers))]
    public void DetailsAction_HasNoNumericIdFallback(string path, string controller)
    {
        var body = MethodBody(ReadRepoFile(path), "Task<IActionResult> Details(string id)");

        Assert.True(!body.Contains("int.TryParse", StringComparison.Ordinal),
            $"{controller}.Details still accepts a numeric id, so /Details/1 walks the table.");
        Assert.True(!body.Contains(".Id ==", StringComparison.Ordinal),
            $"{controller}.Details still queries by internal Id.");

        // The public id must be resolved, and a value that is not a guid must be refused outright.
        Assert.Contains("Guid.TryParse", body);
        Assert.Contains("return NotFound();", body);
    }

    [Theory]
    [MemberData(nameof(DetailsControllers))]
    public void NoInternalRedirect_PassesANumericIdToDetails(string path, string controller)
    {
        var source = ReadRepoFile(path);

        // The id has to be a public one: either the entity's own PublicId, or the value a helper
        // resolved it to. Anything that bottoms out in a bare `Id` hands over the primary key.
        static bool IsPublicId(string expression)
        {
            var chain = expression.Split('.');
            if (chain.Length > 1 && chain[^1] == "Value")
            {
                chain = chain[..^1]; // publicId.Value is still the public id
            }

            var name = chain[^1];
            return name.Equals("PublicId", StringComparison.Ordinal)
                || name.Equals("publicId", StringComparison.Ordinal);
        }

        var offending = Regex.Matches(source,
                @"RedirectToAction\(nameof\(Details\)\s*,\s*new\s*\{\s*id\s*=\s*(?<expr>[A-Za-z0-9_.]+)\s*\}\s*\)")
            .Select(m => m.Groups["expr"].Value)
            .Where(expr => !IsPublicId(expr))
            .Distinct()
            .ToList();

        Assert.True(offending.Count == 0,
            $"{controller} redirects to Details with a numeric id: {string.Join(", ", offending)}");
    }

    [Fact]
    public void ItemReservedBalancesAndBranch_AreNeverModelBound()
    {
        foreach (var property in new[] { nameof(Item.ReservedQuantity), nameof(Item.ReservedCount), nameof(Item.BranchId) })
        {
            var attr = typeof(Item).GetProperty(property)!
                .GetCustomAttributes(typeof(BindNeverAttribute), inherit: true);

            Assert.True(attr.Length > 0,
                $"Item.{property} is bindable, so a crafted create request can set it directly.");
        }
    }

    [Fact]
    public void ItemsCreate_ResetsServerOwnedFieldsOnBothGetAndPost()
    {
        var source = ReadRepoFile("src/NewVixSmart.Web/Controllers/ItemsController.cs");

        // The GET view model and the POST handler must both zero the reserved balances and clear the
        // branch; [BindNever] alone would leave a re-displayed form showing the posted values.
        var get = MethodBody(source, "private static Item NewItem()");
        var post = MethodBody(source, "public async Task<IActionResult> Create(Item item)");

        foreach (var field in new[] { "ReservedQuantity", "ReservedCount", "BranchId" })
        {
            Assert.Contains(field + " =", get, StringComparison.Ordinal);
            Assert.Contains("item." + field + " =", post, StringComparison.Ordinal);
        }
    }
}
