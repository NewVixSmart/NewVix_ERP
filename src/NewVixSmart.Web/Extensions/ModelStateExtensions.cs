using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NewVixSmart.Web.Extensions;

public static class ModelStateExtensions
{
    private const string _genericMessage = "قيمة غير صالحة";

    /// <summary>
    /// Drops a line-item row whose only evidence of being real is a blank <c>ItemId</c>. The grid
    /// posts one row per visible line, and a row the user never touched arrives as blank; its
    /// <c>[Required]</c> error is noise, not a defect to report. A row with a real <c>ItemId</c>
    /// keeps every one of its errors, including the blank <c>ItemId</c> one.
    /// </summary>
    public static void IgnoreEmptyLineItemRows(this ModelStateDictionary ms)
    {
        var keys = ms.Keys.ToList();
        foreach (var k in keys)
        {
            var isItemIdKey = (k.StartsWith("Items[") || k.StartsWith("items[")) && k.Contains("].ItemId");
            if (!isItemIdKey)
            {
                continue;
            }

            var entry = ms[k];
            if (entry == null)
            {
                continue;
            }

            var emptyAttempt = string.IsNullOrWhiteSpace(entry.AttemptedValue) || entry.AttemptedValue == "undefined";
            if (!emptyAttempt)
            {
                continue;
            }

            var prefix = k.Substring(0, k.IndexOf(']') + 1);
            foreach (var key in keys.Where(x => x.StartsWith(prefix)))
            {
                ms.Remove(key);
            }
        }
    }

    /// <summary>
    /// The messages attached to one line-item row, keyed by its <c>items[n].</c> prefix, in posting
    /// order and de-duplicated. Drives <c>_LineItemErrors.cshtml</c>, which puts them in the row and
    /// points the row's <c>aria-describedby</c> at them.
    /// </summary>
    public static IReadOnlyList<string> LineItemErrorMessages(this ModelStateDictionary ms, string keyPrefix)
        => ms.Where(entry => entry.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
            .SelectMany(entry => entry.Value!.Errors)
            .Select(Describe)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .ToList();

    /// <summary>
    /// The messages that no visible field is showing: everything outside the line-item grid, minus
    /// the keys the current view renders beside an input (<c>ViewData["ErrorKeysWithVisibleField"]</c>).
    /// This is the last net under a silent failure - a field error with no <c>asp-validation-for</c>
    /// lands here instead of nowhere.
    /// </summary>
    public static IReadOnlyList<string> UnhandledFieldErrorMessages(this ModelStateDictionary ms, IReadOnlyCollection<string>? renderedKeys = null)
    {
        var rendered = renderedKeys ?? Array.Empty<string>();

        return ms.Where(entry => !string.IsNullOrEmpty(entry.Key)
                && !entry.Key.StartsWith("items[", StringComparison.OrdinalIgnoreCase)
                && !rendered.Any(key => key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(entry => entry.Value!.Errors)
            .Select(Describe)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Prefers the operator-facing message and falls back to the exception text, then to a
    /// non-empty generic. Never returns an empty string: an empty error message renders as nothing,
    /// which is the defect being fixed.
    /// </summary>
    private static string Describe(ModelError error)
        => !string.IsNullOrWhiteSpace(error.ErrorMessage)
            ? error.ErrorMessage
            : (!string.IsNullOrWhiteSpace(error.Exception?.Message)
                ? error.Exception.Message
                : _genericMessage);
}
