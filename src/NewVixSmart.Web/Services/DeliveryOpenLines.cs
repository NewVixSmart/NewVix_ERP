using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Services;

/// <summary>
/// The one rule that decides whether a delivery order still owes lines, and therefore whether
/// more of it may be issued.
///
/// This used to live as a bare <c>0.005m</c> comparison written inline in a Razor view, which meant
/// the number that decides "is this delivery complete" - and therefore whether the rest of it can
/// still be invoiced - was spelled out in markup with no name. It is a quantity tolerance, not a
/// money one: the columns it compares are <c>decimal(18,4)</c> quantities and counts, while
/// <see cref="ReportService.OpenTolerance"/> gates <c>decimal(18,2)</c> money, so the two are
/// derived from two different column widths and are deliberately kept apart even where they used
/// to coincide.
///
/// The rule is <b>inclusive</b>: a residual of exactly the tolerance still counts as settled, and
/// only a residual strictly greater than the tolerance keeps the order open. That is the same
/// boundary <c>issued &gt;= ordered - tolerance</c> expresses from the other side, which is how
/// <see cref="InventoryService"/> and <see cref="DeliveriesInvoicingService"/> already read it - so
/// this class and those two now agree by construction instead of by coincidence.
/// </summary>
public static class DeliveryOpenLines
{
    /// <summary>
    /// Largest quantity/count residual, in the item's own unit, that still counts as fully issued.
    /// <para>
    /// Half of the smallest quantity the store can hold. A quantity column is
    /// <c>decimal(18,4)</c>, so the smallest residual two stored figures can differ by is 0.0001 and
    /// every residual this rule ever sees is an exact multiple of 0.0001; nothing storable is
    /// therefore small enough to hide behind 0.00005, which is the tightest tolerance that is not
    /// zero. It used to be 0.005, derived from the old <c>decimal(18,2)</c> quantity as "one
    /// hundredth of the smallest storable value" - true then, and false as soon as the column was
    /// widened to four decimals, because 0.005 had quietly become fifty storable steps: an issue of
    /// 99.9957 against an ordered 100 read as settled, recognising revenue on a balance that was
    /// never delivered and writing 0.0043 units of stock off with no trace.
    /// </para>
    /// <para>
    /// Not zero, deliberately. Exact zero is correct for a residual computed from two
    /// <c>decimal(18,4)</c> columns, but this rule also answers for values that have been through a
    /// lossy step - a ratio, a read from a provider that hands back a double, a figure a caller
    /// rounded - and a sub-quantum residue there is noise, not a delivery. Half a quantum draws the
    /// line in the only place it can be drawn: above every real quantity, below every artefact.
    /// </para>
    /// </summary>
    public const decimal QuantityTolerance = 0.00005m;

    /// <summary>
    /// Whether one ordered line has been issued in full, on both dimensions. An item may be sold by
    /// quantity, by count, or by both, so a line counts as settled only when neither dimension is
    /// still short.
    /// </summary>
    public static bool IsLineSettled(decimal orderedQuantity, decimal orderedCount,
        decimal issuedQuantity, decimal issuedCount)
        => orderedQuantity - issuedQuantity <= QuantityTolerance
           && orderedCount - issuedCount <= QuantityTolerance;

    /// <summary>Whether one ordered line still has quantity left to issue.</summary>
    public static bool HasOpenQuantity(decimal orderedQuantity, decimal orderedCount,
        decimal issuedQuantity, decimal issuedCount)
        => orderedQuantity - issuedQuantity > QuantityTolerance;

    /// <summary>Whether one ordered line still has count left to issue.</summary>
    public static bool HasOpenCount(decimal orderedQuantity, decimal orderedCount,
        decimal issuedQuantity, decimal issuedCount)
        => orderedCount - issuedCount > QuantityTolerance;

    /// <summary>
    /// Whether a residual - the amount still owed on a line - is above the tolerance on either
    /// dimension. This is <see cref="IsLineSettled"/> for callers that have already reduced a line to
    /// what is left instead of holding ordered and issued figures side by side, so the same
    /// <see cref="QuantityTolerance"/> answers both shapes and the two cannot drift.
    /// </summary>
    public static bool HasResidualLeft(decimal remainingQuantity, decimal remainingCount)
        => remainingQuantity > QuantityTolerance || remainingCount > QuantityTolerance;

    /// <summary>
    /// The issued quantity and count per item across every non-cancelled issue attached to
    /// <paramref name="order"/>. A cancelled issue releases nothing, so it is excluded here exactly
    /// as it is excluded when the delivery status itself is recomputed.
    /// </summary>
    public static Dictionary<int, (decimal Quantity, decimal Count)> IssuedByItem(DeliveryOrder order)
        => order.Issues
            .Where(i => i.Status != DeliveryIssueStatus.Cancelled)
            .SelectMany(i => i.Items)
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => (Quantity: g.Sum(l => l.Quantity), Count: g.Sum(l => l.Count)));

    /// <summary>Whether every ordered line of <paramref name="order"/> has been issued in full.</summary>
    public static bool IsFullyIssued(DeliveryOrder order)
    {
        if (order.Items.Count == 0) return true;

        var issued = IssuedByItem(order);
        return order.Items.All(l =>
        {
            var got = issued.TryGetValue(l.ItemId, out var v) ? v : default;
            return IsLineSettled(l.Quantity, l.Count, got.Quantity, got.Count);
        });
    }

    /// <summary>
    /// How much of <paramref name="order"/> is still to be issued, for surfaces that show a
    /// remaining figure - the "متبقٍ" column on the raise-an-issue screen, for instance.
    ///
    /// This used to be a private method on the delivery issues controller that subtracted issued
    /// quantity and never looked at count. Because a count-traded line has an ordered quantity of
    /// zero, that version reported 0 outstanding for a note whose count was entirely unissued, so
    /// the screen offered a note the details screen had already called complete. Deriving the
    /// figure from <see cref="IsLineSettled"/> is what makes the two agree: a line contributes only
    /// if it is not settled, and it contributes the residual of the dimension that is actually
    /// open, so the figure is greater than zero exactly when <see cref="HasOutstandingLines"/> is
    /// true.
    ///
    /// The residual is reported in each line's own unit, so on a note that mixes quantity-traded and
    /// count-traded items the two are added together. That is inherent to showing a single figure
    /// and is inherited from the original label; use <see cref="HasOutstandingLines"/> for the
    /// decision itself rather than reading a number.
    /// </summary>
    public static decimal OpenQuantity(DeliveryOrder order)
    {
        var issued = IssuedByItem(order);
        decimal open = 0m;

        foreach (var line in order.Items)
        {
            var got = issued.TryGetValue(line.ItemId, out var v) ? v : default;
            if (IsLineSettled(line.Quantity, line.Count, got.Quantity, got.Count)) continue;

            open += HasOpenQuantity(line.Quantity, line.Count, got.Quantity, got.Count)
                ? Math.Max(0m, line.Quantity - got.Quantity)
                : Math.Max(0m, line.Count - got.Count);
        }

        return open;
    }

    /// <summary>
    /// Whether anything is still outstanding on <paramref name="order"/> - the predicate a surface
    /// must ask before offering to issue or invoice the rest of it.
    /// </summary>
    public static bool HasOutstandingLines(DeliveryOrder order) => !IsFullyIssued(order);

    /// <summary>
    /// Whether a further delivery issue may be raised against <paramref name="order"/>. A cancelled
    /// note and an already-invoiced note are both closed for issuing regardless of what is still
    /// outstanding, so this is the gate the details screen used to assemble by hand.
    /// </summary>
    public static bool CanIssueMoreLines(DeliveryOrder order)
        => HasOutstandingLines(order)
           && order.SaleInvoiceId is null
           && order.Status != DeliveryOrderStatus.Cancelled;
}
