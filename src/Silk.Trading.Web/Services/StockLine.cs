namespace Silk.Trading.Web.Services;

public sealed record StockLine(int ItemId, decimal Count, decimal Quantity);

public sealed record ConsumedCostResult(decimal QtyCost, decimal CountCost)
{
    public decimal Total => QtyCost + CountCost;
}
