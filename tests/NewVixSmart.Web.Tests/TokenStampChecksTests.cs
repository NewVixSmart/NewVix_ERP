using NewVixSmart.Web.Infrastructure;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class TokenStampChecksTests
{
    [Fact]
    public void StampMatches_SameValue_True()
        => Assert.True(TokenStampChecks.StampMatches("abc123", "abc123"));

    [Fact]
    public void StampMatches_DifferentValue_False()
        => Assert.False(TokenStampChecks.StampMatches("abc123", "xyz999"));

    [Fact]
    public void StampMatches_NullClaim_False()
        => Assert.False(TokenStampChecks.StampMatches(null, "abc123"));

    [Fact]
    public void StampMatches_DifferentLength_False()
        => Assert.False(TokenStampChecks.StampMatches("short", "a-much-longer-value"));

    [Fact]
    public void StampMatches_EmptyValues_True()
        => Assert.True(TokenStampChecks.StampMatches(string.Empty, string.Empty));

    [Fact]
    public void StampMatches_SameLengthDifferentBytes_False()
        => Assert.False(TokenStampChecks.StampMatches("aaaa", "bbbb"));
}
