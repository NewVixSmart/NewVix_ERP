using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewVixSmart.Web.Api;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ApiControllerSecurityTests
{
    private static readonly Type[] ApiControllers =
    [
        typeof(ItemsController),
        typeof(CustomersController),
        typeof(SuppliersController),
        typeof(SalesController),
        typeof(PurchasesController),
        typeof(PaymentsController),
        typeof(JournalEntriesController),
        typeof(TokensController)
    ];

    [Fact]
    public void AllApiControllers_IgnoreAntiforgeryToken()
    {
        foreach (var type in ApiControllers)
            Assert.NotNull(Attribute.GetCustomAttribute(type, typeof(IgnoreAntiforgeryTokenAttribute)));
    }

    [Fact]
    public void ApiControllers_UseJwtBearer_ExceptAnonymousTokenEndpoint()
    {
        foreach (var type in ApiControllers)
        {
            var auth = Attribute.GetCustomAttribute(type, typeof(AuthorizeAttribute)) as AuthorizeAttribute;
            if (type == typeof(TokensController))
            {
                Assert.Null(auth);
                continue;
            }
            Assert.NotNull(auth);
            Assert.Equal(JwtBearerDefaults.AuthenticationScheme, auth!.AuthenticationSchemes);
        }
    }

    [Fact]
    public void ApiControllers_AreApiControllers()
    {
        foreach (var type in ApiControllers)
            Assert.NotNull(Attribute.GetCustomAttribute(type, typeof(ApiControllerAttribute)));
    }
}