using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace NewVixSmart.Web.Extensions;

public static class CspNonce
{
    private const string ItemKey = "CspNonce";

    public static void SetCspNonce(this HttpContext context)
    {
        if (context.Items.ContainsKey(ItemKey)) return;
        context.Items[ItemKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    }

    public static string GetCspNonce(this HttpContext context)
    {
        return context.Items.TryGetValue(ItemKey, out var nonce) ? nonce?.ToString() ?? "" : "";
    }
}