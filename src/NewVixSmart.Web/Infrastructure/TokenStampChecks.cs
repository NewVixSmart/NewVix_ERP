using System.Security.Cryptography;
using System.Text;

namespace NewVixSmart.Web.Infrastructure;

public static class TokenStampChecks
{
    public const string StampClaimType = "stamp";

    public static bool StampMatches(string? tokenStamp, string currentStamp)
    {
        if (tokenStamp is null) return false;
        if (tokenStamp.Length != currentStamp.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(tokenStamp),
            Encoding.UTF8.GetBytes(currentStamp));
    }
}