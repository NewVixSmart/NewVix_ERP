using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NewVixSmart.Web.Extensions;

public static class ModelStateExtensions
{
    public static void IgnoreEmptyLineItemRows(this ModelStateDictionary ms)
    {
        var keys = ms.Keys.ToList();
        foreach (var k in keys)
        {
            var isItemIdKey = (k.StartsWith("Items[") || k.StartsWith("items[")) && k.Contains("].ItemId");
            if (!isItemIdKey) continue;

            var entry = ms[k];
            if (entry == null) continue;
            var emptyAttempt = string.IsNullOrWhiteSpace(entry.AttemptedValue) || entry.AttemptedValue == "undefined";
            if (!emptyAttempt) continue;

            var prefix = k.Substring(0, k.IndexOf(']') + 1);
            foreach (var key in keys.Where(x => x.StartsWith(prefix)))
                ms.Remove(key);
        }
    }
}