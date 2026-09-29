namespace NewVixSmart.Web.Services;

public static class Code128Helper
{
    private static readonly int[] QuietZone = [0, 0, 0, 0, 0, 0, 0, 0];

    private static readonly int[][] StartCodes =
    [
        [2, 1, 2, 2, 2, 2],
        [2, 2, 2, 1, 2, 2],
        [2, 2, 2, 2, 2, 1]
    ];

    private static readonly int[][] CodeB = [
        [2, 1, 2, 2, 2, 2], [2, 2, 2, 1, 2, 2], [2, 2, 2, 2, 2, 1], [1, 2, 1, 2, 2, 3],
        [1, 2, 1, 3, 2, 2], [1, 3, 1, 2, 2, 2], [1, 2, 2, 2, 1, 3], [1, 2, 2, 3, 1, 2],
        [1, 3, 2, 2, 1, 2], [2, 2, 1, 2, 1, 3], [2, 2, 1, 3, 1, 2], [2, 3, 1, 2, 1, 2],
        [1, 1, 2, 2, 3, 2], [1, 2, 2, 1, 3, 2], [1, 2, 2, 2, 3, 1], [1, 1, 3, 2, 2, 2],
        [1, 2, 3, 1, 2, 2], [1, 2, 3, 2, 2, 1], [2, 2, 3, 2, 1, 1], [2, 2, 1, 1, 3, 2],
        [2, 2, 1, 2, 3, 1], [2, 1, 3, 2, 1, 2], [2, 2, 3, 1, 1, 2], [3, 1, 2, 1, 3, 1],
        [3, 1, 1, 2, 2, 2], [3, 2, 1, 1, 2, 2], [3, 2, 1, 2, 2, 1], [3, 1, 2, 2, 1, 2],
        [3, 2, 2, 1, 1, 2], [3, 2, 2, 2, 1, 1], [2, 1, 2, 1, 2, 3], [2, 1, 2, 3, 2, 1],
        [2, 3, 2, 1, 2, 1], [1, 1, 1, 3, 2, 3], [1, 3, 1, 1, 2, 3], [1, 3, 1, 3, 2, 1],
        [1, 1, 2, 3, 1, 3], [1, 3, 2, 1, 1, 3], [1, 3, 2, 3, 1, 1], [2, 1, 1, 3, 1, 3],
        [2, 3, 1, 1, 1, 3], [2, 3, 1, 3, 1, 1], [1, 1, 2, 1, 3, 3], [1, 1, 2, 3, 3, 1],
        [1, 3, 2, 1, 3, 1], [1, 1, 3, 1, 2, 3], [1, 1, 3, 3, 2, 1], [1, 3, 3, 1, 2, 1],
        [3, 1, 3, 1, 2, 1], [2, 1, 1, 3, 3, 1], [2, 3, 1, 1, 3, 1], [2, 1, 3, 1, 1, 3],
        [2, 1, 3, 3, 1, 1], [2, 1, 3, 1, 3, 1], [3, 1, 1, 1, 2, 3], [3, 1, 1, 3, 2, 1],
        [3, 3, 1, 1, 2, 1], [3, 1, 2, 1, 1, 3], [3, 1, 2, 3, 1, 1], [3, 3, 2, 1, 1, 1],
        [3, 1, 4, 1, 1, 1], [2, 2, 1, 4, 1, 1], [4, 3, 1, 1, 1, 1], [1, 1, 1, 2, 2, 4],
        [1, 1, 1, 4, 2, 2], [1, 2, 1, 1, 2, 4], [1, 2, 1, 4, 2, 1], [1, 4, 1, 1, 2, 2],
        [1, 4, 1, 2, 2, 1], [1, 1, 2, 2, 1, 4], [1, 1, 2, 4, 1, 2], [1, 2, 2, 1, 1, 4],
        [1, 2, 2, 4, 1, 1], [1, 4, 2, 1, 1, 2], [1, 4, 2, 2, 1, 1], [2, 4, 1, 2, 1, 1],
        [2, 2, 1, 1, 1, 4], [4, 1, 3, 1, 1, 1], [2, 4, 1, 1, 1, 2], [1, 3, 4, 1, 1, 1],
        [1, 1, 1, 2, 4, 2], [1, 2, 1, 1, 4, 2], [1, 2, 1, 2, 4, 1], [1, 1, 4, 2, 1, 2],
        [1, 2, 4, 1, 1, 2], [1, 2, 4, 2, 1, 1], [4, 1, 1, 2, 1, 2], [4, 2, 1, 1, 1, 2],
        [4, 2, 1, 2, 1, 1], [2, 1, 2, 1, 4, 1], [2, 1, 4, 1, 2, 1], [4, 1, 2, 1, 2, 1],
        [1, 1, 1, 1, 4, 3], [1, 1, 1, 3, 4, 1], [1, 3, 1, 1, 4, 1], [1, 1, 4, 1, 1, 3],
        [1, 1, 4, 3, 1, 1], [4, 1, 1, 1, 1, 3], [4, 1, 1, 3, 1, 1], [1, 1, 3, 1, 4, 1],
        [1, 1, 4, 1, 3, 1], [3, 1, 1, 1, 4, 1], [4, 1, 1, 1, 3, 1], [2, 1, 1, 4, 1, 2],
        [2, 1, 1, 2, 1, 4], [2, 1, 1, 2, 3, 2], [2, 3, 3, 1, 1, 1, 2]
    ];

    public static string GetBarcodeValue(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "NOCODE";
        }

        var clean = new string(code.Where(c => c >= 32 && c <= 126).ToArray());
        return string.IsNullOrEmpty(clean) ? "NOCODE" : clean;
    }

    public static bool IsValidCode128(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c < 32 || c > 126)
            {
                return false;
            }
        }
        return true;
    }

    public static int[] Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return QuietZone.Concat(StartCodes[1]).Concat(QuietZone).ToArray();
        }

        var result = new List<int>();
        result.AddRange(QuietZone);

        result.AddRange(StartCodes[1]);

        int checksum = 104;
        for (int i = 0; i < text.Length; i++)
        {
            int code = text[i] - 32;
            if (code < 0 || code >= CodeB.Length)
            {
                code = 0;
            }

            result.AddRange(CodeB[code]);
            checksum += code * (i + 1);
        }

        checksum %= 103;
        result.AddRange(CodeB[checksum]);

        result.AddRange([2, 3, 3, 1, 1, 1, 2]);
        result.AddRange(QuietZone);

        return result.ToArray();
    }

    public static string ToSvgPattern(int[] bars, int height = 60, int moduleWidth = 2)
    {
        var svg = new System.Text.StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{bars.Sum() * moduleWidth}\" height=\"{height}\">");
        int x = 0;
        bool black = true;
        foreach (var bar in bars)
        {
            if (black)
            {
                svg.Append($"<rect x=\"{x * moduleWidth}\" y=\"0\" width=\"{bar * moduleWidth}\" height=\"{height}\" fill=\"black\"/>");
            }
            x += bar;
            black = !black;
        }
        svg.Append("</svg>");
        return svg.ToString();
    }

    public static string ToCssStripes(int[] bars, int height = 40, int moduleWidth = 2)
    {
        var stops = new List<string>();
        int x = 0;
        bool black = true;
        foreach (var bar in bars)
        {
            int startPct = (int)((double)x / bars.Sum() * 100);
            int widthPct = (int)Math.Max(1, (double)bar / bars.Sum() * 100);
            if (black)
            {
                stops.Add($"black {startPct}% {startPct + widthPct}%");
            }
            else
            {
                stops.Add($"white {startPct}% {startPct + widthPct}%");
            }

            x += bar;
            black = !black;
        }
        return $"background: linear-gradient(to right, {string.Join(", ", stops)}); height: {height}px; width: 100%;";
    }
}
