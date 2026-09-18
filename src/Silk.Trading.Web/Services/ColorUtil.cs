using System.Globalization;

namespace Silk.Trading.Web.Services;

public static class ColorUtil
{
    public static byte[] HexToRgb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3)
            h = string.Concat(h.Select(c => char.ToString(c) + char.ToString(c)));
        if (h.Length != 6 || !h.All(Uri.IsHexDigit))
            throw new FormatException("invalid hex");
        return
        [
            Convert.ToByte(h[..2], 16),
            Convert.ToByte(h.Substring(2, 2), 16),
            Convert.ToByte(h.Substring(4, 2), 16)
        ];
    }

    public static string RgbToHex(int r, int g, int b) =>
        $"#{r:x2}{g:x2}{b:x2}";

    public static string Blend(string hexA, string hexB, double weightOfB)
    {
        var a = HexToRgb(hexA);
        var b = HexToRgb(hexB);
        var w = Math.Clamp(weightOfB, 0, 1);
        var r = (int)Math.Round(a[0] + (b[0] - a[0]) * w);
        var g = (int)Math.Round(a[1] + (b[1] - a[1]) * w);
        var bl = (int)Math.Round(a[2] + (b[2] - a[2]) * w);
        return RgbToHex(r, g, bl);
    }

    public static string Lighten(string hex, double amount) => Blend(hex, "#ffffff", amount);

    public static string Darken(string hex, double amount) => Blend(hex, "#000000", amount);

    public static double RelativeLuminance(string hex)
    {
        var c = HexToRgb(hex).Select(v => v / 255d).ToArray();
        var linear = c.Select(v => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4)).ToArray();
        return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
    }

    public static bool IsLight(string hex) => RelativeLuminance(hex) > 0.55;

    public static string RgbList(string hex)
    {
        var c = HexToRgb(hex);
        return $"{c[0]}, {c[1]}, {c[2]}";
    }
}