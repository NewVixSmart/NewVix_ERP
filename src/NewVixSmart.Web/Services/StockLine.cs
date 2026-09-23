namespace NewVixSmart.Web.Services;

public sealed record StockLine(int ItemId, decimal Count, decimal Quantity);

public sealed record ConsumedCostResult(decimal QtyCost, decimal CountCost, decimal DominantTotal)
{
    // Quantity-primary valuation (H-2): when a line carries Quantity, Count is display-only.
    // DominantTotal never sums the two dimensions; for multi-item documents it sums each
    // line's dominant-dimension cost, so mixed qty/count items are still both priced.
    public decimal Total => DominantTotal;
}
