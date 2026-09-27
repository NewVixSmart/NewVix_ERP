namespace NewVixSmart.Web.Services;

using System.Globalization;

/// <summary>
/// The single source of truth for money in this system. The application is
/// single-currency: every amount in every table, journal line, report and printed
/// document is Egyptian pounds. There is deliberately no currency table, no
/// currency column and no exchange rate anywhere - if a future requirement needs
/// one, that is a schema and accounting change, not a configuration toggle.
/// </summary>
public static class Money
{
    public const string Code = "EGP";
    public const string Name = "جنيه مصري";
    public const string Symbol = "L.E";

    /// <summary>Appended after a formatted amount, e.g. "1,250.00 L.E".</summary>
    public const string Suffix = " " + Symbol;

    /// <summary>Used where a currency is named rather than an amount, e.g. "العملة: جنيه مصري".</summary>
    public const string DisplayName = Name;

    /// <summary>
    /// Formats an amount with two decimals and the currency suffix, e.g. "1,250.00 L.E".
    /// Formatting is pinned to the invariant culture so a server locale can never change
    /// the digits, separators or decimal places a user sees.
    /// </summary>
    public static string Format(decimal amount)
        => amount.ToString("N2", CultureInfo.InvariantCulture) + Suffix;
}
