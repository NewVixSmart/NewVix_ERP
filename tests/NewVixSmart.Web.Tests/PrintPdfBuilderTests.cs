using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class PrintPdfBuilderTests
{
    [Fact]
    public void AmountInWords_UnderBillion_StillWorks()
    {
        var words = PrintPdfBuilder.AmountInWords(495.50m);
        Assert.Contains("فقط", words);
        Assert.DoesNotContain("مليار", words);
    }

    public static IEnumerable<object[]> HugeAmounts =>
        new object[][]
        {
            new object[] { 1_000_000_000m },
            new object[] { 2_000_000_000m },
            new object[] { 3_000_000_000m },
            new object[] { 1_000_500_000.75m },
            new object[] { 150_000_000_000m }
        };

    [Theory]
    [MemberData(nameof(HugeAmounts))]
    public void AmountInWords_HugeAmounts_DoNotThrow(decimal whole)
    {
        var words = PrintPdfBuilder.AmountInWords(whole);
        Assert.Contains("مليار", words);
        Assert.Contains("فقط", words);
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(-1_250.75)]
    public void AmountInWords_Negative_PrefixedWithMinusAndKeepsMagnitude(decimal value)
    {
        var words = PrintPdfBuilder.AmountInWords(value);
        Assert.StartsWith("ناقص", words);
        Assert.Equal(PrintPdfBuilder.AmountInWords(Math.Abs(value)), words["ناقص ".Length..]);
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(0)]
    [InlineData(1200.40)]
    public void AmountInWords_AlwaysEndsWithFaqat(decimal value)
    {
        Assert.EndsWith("فقط", PrintPdfBuilder.AmountInWords(value));
    }
}
