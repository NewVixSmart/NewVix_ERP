namespace NewVixSmart.Web.Services;

/// <summary>
/// Canonical valuation of a return document against its source invoice. Shared by the posting
/// path (<see cref="InventoryService"/>) and the receivable/payable aging reports
/// (<see cref="ReportService"/>) so the ledger and the reports can never drift apart.
/// </summary>
public static class ReturnValuation
{
    /// <summary>
    /// The receivable/supplier amount a posted return actually credited, in base currency units.
    /// This is the same figure <see cref="ReturnMirror"/> posts, so aging must use it instead of
    /// the stored gross <c>TotalAmount</c> whenever a source invoice exists.
    /// </summary>
    public static decimal ReceivableBase(decimal returnedGross, decimal invoiceGross, decimal invoiceNet)
    {
        if (returnedGross <= 0m || invoiceGross <= 0m || invoiceNet < 0m)
        {
            return returnedGross;
        }

        return decimal.Round(returnedGross * (invoiceNet / invoiceGross), 2);
    }
}

/// <summary>
/// Prorates a return document against its source invoice so the ledger reversal is the exact
/// proportional inverse of the original booking. A sale invoice is booked as
/// Dr 1200 = net, Cr 4000 = net - tax, Cr 2055 = tax; a purchase invoice as Dr 1300 = net,
/// Cr 2000 = net. A return of fraction f (returned gross / invoice gross) must therefore
/// reverse exactly that slice: receivable/supplier = f * net, contra = f * (net - tax),
/// tax = f * tax (the purchase side carries no separate tax leg, so it uses f * net whole).
/// Every component is rounded to two decimals, so the entry balances without decimal drift.
/// </summary>
public readonly record struct ReturnMirror(decimal ContraValue, decimal Tax, decimal Receivable)
{
    /// <summary>No source invoice (or nothing to prorate): f = 1, so the gross is the whole value.</summary>
    public static ReturnMirror ForGross(decimal returnedGross) => new(returnedGross, 0m, returnedGross);

    public static ReturnMirror ProratedAgainst(decimal returnedGross, decimal invoiceGross,
        decimal invoiceNet, decimal invoiceTax)
    {
        if (returnedGross <= 0m || invoiceGross <= 0m || invoiceNet < 0m)
        {
            return ForGross(returnedGross);
        }

        var fraction = returnedGross / invoiceGross;
        var net = NonNegative(decimal.Round(invoiceNet * fraction, 2));
        var tax = NonNegative(decimal.Round(invoiceTax * fraction, 2));
        if (tax < 0.005m)
        {
            tax = 0m;
        }

        var contra = decimal.Round(net - tax, 2);
        if (contra < 0m)
        {
            // A header discount larger than the tax would make the contra-revenue leg negative;
            // drop the tax leg exactly as RecordSaleDeliveryAsync does for the forward booking.
            tax = 0m;
            contra = net;
        }

        return new ReturnMirror(contra, tax, decimal.Round(contra + tax, 2));
    }

    private static decimal NonNegative(decimal amount) => amount < 0m ? 0m : amount;
}
