using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Services;

/// <summary>
/// Order totals and progress figures, in one named place.
///
/// The sales-order list, the sales-order details screen, both print layouts and the purchase-order
/// screens each used to re-derive these with LINQ inside Razor: seven <c>Sum</c> calls per table
/// row, a hand-rolled invoicing percentage, and the "quantity if the item is sold by quantity,
/// otherwise count" switch repeated on every line. The figures must not drift between the screens -
/// the printed order and the screen it was printed from have to agree to the piastre - so the rule
/// is stated once here and every surface calls it.
/// </summary>
public static class OrderProgress
{
    /// <summary>
    /// The measure a sales-order line is tracked in. Quantity and count are independent dimensions,
    /// not two renderings of one number, so a line traded by quantity is summarised on quantity and
    /// a line traded by count on count. This is the rule the README documents as
    /// <c>Quantity &gt; 0 ? Quantity : Count</c>, lifted out of the markup that used to spell it.
    /// </summary>
    public static decimal OrderedForDisplay(SalesOrderItem item)
        => item.Quantity > 0 ? item.Quantity : item.Count;

    /// <summary>The delivered figure on the same axis as <see cref="OrderedForDisplay"/>.</summary>
    public static decimal DeliveredForDisplay(SalesOrderItem item)
        => item.Quantity > 0 ? item.DeliveredQty : item.DeliveredCount;

    /// <summary>The invoiced figure on the same axis as <see cref="OrderedForDisplay"/>.</summary>
    public static decimal InvoicedForDisplay(SalesOrderItem item)
        => item.Quantity > 0 ? item.InvoicedQty : item.InvoicedCount;

    /// <summary>The reserved figure on the same axis as <see cref="OrderedForDisplay"/>.</summary>
    public static decimal ReservedForDisplay(SalesOrderItem item)
        => item.Quantity > 0 ? item.ReservedQty : item.ReservedCount;

    /// <summary>
    /// How much of a line has been invoiced, as a whole percentage clamped to 0-100. An over-invoiced
    /// line (a return corrected later, a rounding overshoot) still reads 100% rather than a
    /// progress bar wider than its track, and a line with nothing ordered reads 0% instead of
    /// dividing by zero.
    /// </summary>
    public static decimal InvoicingPercent(SalesOrderItem item)
    {
        var ordered = OrderedForDisplay(item);
        if (ordered <= 0) return 0m;
        return Math.Min(100m, Math.Round(InvoicedForDisplay(item) / ordered * 100m));
    }

    /// <summary>Every figure the sales-order list row needs, aggregated once.</summary>
    public static SalesOrderTotals Summarize(SalesOrder order)
    {
        var items = order.Items;
        return new SalesOrderTotals(
            OrderedQuantity: items.Sum(i => i.Quantity),
            OrderedCount: items.Sum(i => i.Count),
            DeliveredQuantity: items.Sum(i => i.DeliveredQty),
            DeliveredCount: items.Sum(i => i.DeliveredCount),
            InvoicedQuantity: items.Sum(i => i.InvoicedQty),
            InvoicedCount: items.Sum(i => i.InvoicedCount),
            Total: items.Sum(i => i.Total));
    }

    /// <summary>Every figure the purchase-order list row needs, aggregated once.</summary>
    public static PurchaseOrderTotals Summarize(PurchaseOrder order)
    {
        var items = order.Items;
        return new PurchaseOrderTotals(
            OrderedQuantity: items.Sum(i => i.Quantity),
            ReceivedQuantity: items.Sum(i => i.ReceivedQty),
            Total: items.Sum(i => i.Total));
    }

    /// <summary>
    /// Whether any line of a purchase order has come in. An order that has been received in full is
    /// invoiced from the order; one with nothing received has nothing to invoice.
    /// </summary>
    public static bool HasReceived(PurchaseOrder order)
        => order.Items.Any(i => i.ReceivedQty > 0 || i.ReceivedCount > 0);

    /// <summary>The Arabic settlement word for an invoice. Never spelled out in markup again.</summary>
    public static string SettlementLabel(SaleInvoice invoice)
        => invoice.IsPaid ? "مسدّدة" : "غير مسدّدة";
}

/// <summary>Row aggregates for one sales order.</summary>
public sealed record SalesOrderTotals(
    decimal OrderedQuantity,
    decimal OrderedCount,
    decimal DeliveredQuantity,
    decimal DeliveredCount,
    decimal InvoicedQuantity,
    decimal InvoicedCount,
    decimal Total);

/// <summary>Row aggregates for one purchase order.</summary>
public sealed record PurchaseOrderTotals(
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal Total);
