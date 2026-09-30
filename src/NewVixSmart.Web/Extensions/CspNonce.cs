using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace NewVixSmart.Web.Extensions;

public static class CspNonce
{
    private const string _itemKey = "CspNonce";

    public static void SetCspNonce(this HttpContext context)
    {
        if (context.Items.ContainsKey(_itemKey))
        {
            return;
        }

        context.Items[_itemKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    }

    public static string GetCspNonce(this HttpContext context)
    {
        return context.Items.TryGetValue(_itemKey, out var nonce) ? nonce?.ToString() ?? "" : "";
    }
}
