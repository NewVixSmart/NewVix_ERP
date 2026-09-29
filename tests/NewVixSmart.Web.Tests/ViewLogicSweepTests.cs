using System.Text.RegularExpressions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Core;
using NewVixSmart.Web.ViewModels.Purchases;
using NewVixSmart.Web.ViewModels.Sales;
using NewVixSmart.Web.ViewModels.Users;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Business logic that used to be written inside Razor views, now named and tested here.
///
/// The worst offender was <c>Views/DeliveryOrders/Details.cshtml</c>, which carried a bare
/// <c>&gt; 0.005m</c> comparison deciding whether a delivery note was complete - and therefore
/// whether the rest of it could still be invoiced. A rounding tolerance with no name, in markup,
/// silently gating revenue recognition. It is now
/// <see cref="DeliveryOpenLines.HasOutstandingLines"/>, and the first tests below pin the boundary
/// it must keep: the residual at the tolerance, one storable step above it, one storable step below
/// it, and the 0.0043-unit shortfall that the 0.005m constant used to swallow.
///
/// <see cref="ViewsCarryNoBusinessLogic"/> is the regression guard: a rule that can be re-introduced
/// by pasting a number back into a <c>.cshtml</c> is not a rule, it is a habit.
/// </summary>
public sealed class ViewLogicSweepTests
{
    // ---------------------------------------------------------------------
    // Priority 1 - the delivery-note completeness tolerance.
    //
    // The business rule, as the view used to spell it out:
    //     a line is settled while `ordered - issued <= <tolerance>`, on quantity AND on count,
    //     so a residual of exactly the tolerance is settled and only a residual strictly
    //     greater than the tolerance keeps the note open.
    //
    // The tolerance is 0.00005m - half of the smallest value a decimal(18,4) quantity column can
    // hold. It was 0.005m, which was derived from the old decimal(18,2) quantity and stopped being
    // true the moment the column was widened. These tests are named after the epsilon they pin, so
    // changing the value is a deliberate, visible act rather than a silent drift.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The exact rule: a residual of precisely the tolerance is settled, because the comparison is
    /// <c>&lt;=</c>. The old markup read <c>l.Quantity - issued &gt; 0.005m</c>, so the inclusive
    /// edge has to keep being inclusive.
    /// </summary>
    [Fact]
    public void DeliveryOpenLines_ResidualExactlyAtTolerance_00005_IsSettled()
    {
        Assert.Equal(0.00005m, DeliveryOpenLines.QuantityTolerance);

        Assert.True(DeliveryOpenLines.IsLineSettled(10.00005m, 0m, 10m, 0m));
        Assert.False(DeliveryOpenLines.HasOpenQuantity(10.00005m, 0m, 10m, 0m));

        Assert.True(DeliveryOpenLines.IsLineSettled(0m, 10.00005m, 0m, 10m));
        Assert.False(DeliveryOpenLines.HasOpenCount(0m, 10.00005m, 0m, 10m));
    }

    /// <summary>
    /// One storable step above the tolerance. 0.0001 is the smallest quantity a decimal(18,4)
    /// column can hold, so this is the smallest *real* shortfall the system can hold and it must
    /// keep the note open.
    /// </summary>
    [Fact]
    public void DeliveryOpenLines_OneStorableStepAboveTolerance_00010_IsStillOutstanding()
    {
        Assert.True(DeliveryOpenLines.HasOpenQuantity(10.0001m, 0m, 10m, 0m));
        Assert.False(DeliveryOpenLines.IsLineSettled(10.0001m, 0m, 10m, 0m));

        Assert.True(DeliveryOpenLines.HasOpenCount(0m, 10.0001m, 0m, 10m));
        Assert.False(DeliveryOpenLines.IsLineSettled(0m, 10.0001m, 0m, 10m));
    }

    /// <summary>Just under the tolerance: a sub-quantum residue is noise, not a delivery.</summary>
    [Fact]
    public void DeliveryOpenLines_JustBelowTolerance_00004_IsSettled()
    {
        Assert.True(DeliveryOpenLines.IsLineSettled(10.00004m, 0m, 10m, 0m));
        Assert.False(DeliveryOpenLines.HasOpenQuantity(10.00004m, 0m, 10m, 0m));

        Assert.True(DeliveryOpenLines.IsLineSettled(0m, 10.00004m, 0m, 10m));
        Assert.False(DeliveryOpenLines.HasOpenCount(0m, 10.00004m, 0m, 10m));
    }

    /// <summary>
    /// The regression the widening created, stated as a test. 100.0043 units short is 0.0043 of a
    /// real, storable quantity: under the old 0.005m the line read settled, revenue was recognised
    /// on a balance that was never delivered, and 0.0043 units of stock were written off with no
    /// trace. Under decimal(18,2) that quantity could not exist, which is the only reason the old
    /// constant was harmless. It is not harmless now.
    /// </summary>
    [Fact]
    public void DeliveryOpenLines_FourthDecimalShortfall_00043_IsNotSettled_UnderTolerance00005()
    {
        Assert.True(DeliveryOpenLines.HasOpenQuantity(100m, 0m, 99.9957m, 0m),
            "0.0043 units of a storable quantity are undelivered; the line is not settled.");
        Assert.False(DeliveryOpenLines.IsLineSettled(100m, 0m, 99.9957m, 0m),
            "Recognising revenue on 0.0043 undelivered units is the bug the widening exposed.");
        Assert.False(DeliveryOpenLines.IsLineSettled(100m, 0m, 99.995m, 0m),
            "0.005 was the old tolerance; at four decimals it is an ordinary storable shortfall.");

        // Exactly the ordered quantity is settled - the other half of the same boundary.
        Assert.True(DeliveryOpenLines.IsLineSettled(100m, 0m, 100m, 0m));
        Assert.False(DeliveryOpenLines.HasOpenQuantity(100m, 0m, 100m, 0m));
        Assert.True(DeliveryOpenLines.IsLineSettled(100.0001m, 0m, 100.0001m, 0m),
            "A fully delivered fourth-decimal quantity is settled just the same.");
    }

    /// <summary>The same boundary on a count-traded line, where quantity is zero by construction.</summary>
    [Fact]
    public void DeliveryOpenLines_CountTraded_FourthDecimalShortfall_00043_IsNotSettled_UnderTolerance00005()
    {
        Assert.True(DeliveryOpenLines.HasOpenCount(0m, 100m, 0m, 99.9957m),
            "0.0043 pieces of a storable count are undelivered; the line is not settled.");
        Assert.False(DeliveryOpenLines.IsLineSettled(0m, 100m, 0m, 99.9957m));
        Assert.False(DeliveryOpenLines.IsLineSettled(0m, 100m, 0m, 99.995m));

        Assert.True(DeliveryOpenLines.IsLineSettled(0m, 100m, 0m, 100m));
        Assert.False(DeliveryOpenLines.HasOpenCount(0m, 100m, 0m, 100m));

        // One storable count step above the tolerance keeps it open, on the inclusive edge and past it.
        Assert.True(DeliveryOpenLines.IsLineSettled(0m, 100.00005m, 0m, 100m));
        Assert.False(DeliveryOpenLines.IsLineSettled(0m, 100.0001m, 0m, 100m));
    }

    [Fact]
    public void DeliveryOpenLines_ExactMatch_IsSettled()
    {
        Assert.True(DeliveryOpenLines.IsLineSettled(10m, 4m, 10m, 4m));
        Assert.False(DeliveryOpenLines.IsLineSettled(10m, 4m, 9.9999m, 4m),
            "0.0001 outstanding quantity is a storable shortfall, so the line is not settled.");
        // Quantity and count are independent dimensions: both at the tolerance settles the line,
        // and one dimension above the tolerance keeps it open.
        Assert.True(DeliveryOpenLines.IsLineSettled(10m, 4m, 9.99995m, 3.99995m));
        Assert.False(DeliveryOpenLines.IsLineSettled(10m, 4m, 9.99995m, 3.9999m));
        Assert.False(DeliveryOpenLines.IsLineSettled(10m, 4m, 10m, 3.9999m));
    }

    /// <summary>
    /// The predicate must answer the same question the inline expression answers, at every residual
    /// a decimal(18,4) pair can produce around the tolerance. The expression is reproduced here
    /// literally, so the shared predicate and a caller's own spelling of the rule cannot drift.
    /// </summary>
    [Fact]
    public void DeliveryOpenLines_IsLineSettled_AgreesWithTheInlineExpressionAtEveryBoundary()
    {
        const decimal tolerance = 0.00005m;
        Assert.Equal(tolerance, DeliveryOpenLines.QuantityTolerance);

        var residuals = new[]
        {
            -1m, 0m, 0.00001m, 0.00004m, 0.00005m, 0.00006m, 0.0001m, 0.001m, 0.0043m, 0.005m,
            0.0051m, 0.01m, 0.5m, 1m, 1000m
        };

        foreach (var residual in residuals)
        {
            const decimal ordered = 100m;
            var issued = ordered - residual;

            // What a caller evaluating the rule by hand would compute.
            var inlineOutstanding = ordered - issued > tolerance;
            var inlineSettled = !inlineOutstanding;

            Assert.Equal(inlineSettled, DeliveryOpenLines.IsLineSettled(ordered, 0m, issued, 0m));
        }
    }

    /// <summary>
    /// Why the old constant had to go, pinned as an assertion rather than left in a comment. Under
    /// <c>&gt; 0.005m</c> the very residuals this suite now requires to stay open were settled; the
    /// two rule-sets genuinely disagree, so this is not a stylistic preference.
    /// </summary>
    [Fact]
    public void DeliveryOpenLines_Tolerance00005_DisagreesWithTheOld0005RuleOnStorableResiduals()
    {
        // Residuals decimal(18,4) can store. The ones at or under 0.005 are the regression set: the
        // old rule settled them, and each of them was an undelivered balance.
        var newlyStorable = new[] { 0.0001m, 0.001m, 0.0043m, 0.005m };

        foreach (var residual in newlyStorable)
        {
            const decimal ordered = 100m;
            var issued = ordered - residual;

            Assert.True(ordered - issued <= 0.005m,
                $"The old rule settled a residual of {residual} - that is the bug being pinned.");
            Assert.False(DeliveryOpenLines.IsLineSettled(ordered, 0m, issued, 0m),
                $"A residual of {residual} is a storable quantity, so the line is not settled.");
        }

        // Past the old tolerance the two rules already agreed, which is why the bug was silent.
        foreach (var residual in new[] { 0.0051m, 0.006m, 0.01m, 1m })
        {
            const decimal ordered = 100m;
            var issued = ordered - residual;
            Assert.False(DeliveryOpenLines.IsLineSettled(ordered, 0m, issued, 0m));
        }
    }

    private static DeliveryOrder Note(params (int ItemId, decimal Quantity, decimal Count)[] lines)
    {
        var note = new DeliveryOrder { Status = DeliveryOrderStatus.PartiallyIssued };
        foreach (var (itemId, quantity, count) in lines)
        {
            note.Items.Add(new DeliveryOrderItem { ItemId = itemId, Quantity = quantity, Count = count });
        }

        return note;
    }

    private static void Issue(DeliveryOrder note, DeliveryIssueStatus status,
        params (int ItemId, decimal Quantity, decimal Count)[] lines)
    {
        var issue = new DeliveryIssue { Status = status };
        foreach (var (itemId, quantity, count) in lines)
        {
            issue.Items.Add(new DeliveryIssueItem { ItemId = itemId, Quantity = quantity, Count = count });
        }

        note.Issues.Add(issue);
    }

    /// <summary>
    /// The raise-an-issue screen used to carry a private copy of the outstanding calculation that
    /// compared issued quantity only. On a quantity-traded note the two agreed by accident; these
    /// two facts pin that they now agree by construction, on both trade dimensions.
    /// </summary>
    [Fact]
    public void OpenQuantity_AgreesWithTheViewPredicateOnAQuantityTradedNote()
    {
        // Fully delivered: the figure the screen shows and the predicate the details screen uses.
        var delivered = Note((10, 100m, 0m));
        Issue(delivered, DeliveryIssueStatus.Issued, (10, 100m, 0m));
        Assert.Equal(0m, DeliveryOpenLines.OpenQuantity(delivered));
        Assert.False(DeliveryOpenLines.HasOutstandingLines(delivered));
        Assert.False(DeliveryOpenLines.CanIssueMoreLines(delivered));
        Assert.True(OpenNoteIsOffered(delivered) == DeliveryOpenLines.HasOutstandingLines(delivered));

        // Partially delivered: the screen shows what is genuinely left.
        var partial = Note((10, 100m, 0m));
        Issue(partial, DeliveryIssueStatus.Issued, (10, 60m, 0m));
        Assert.Equal(40m, DeliveryOpenLines.OpenQuantity(partial));
        Assert.True(DeliveryOpenLines.HasOutstandingLines(partial));
        Assert.True(OpenNoteIsOffered(partial) == DeliveryOpenLines.HasOutstandingLines(partial));

        // Two partial issues accumulate rather than overwrite.
        var split = Note((10, 100m, 0m));
        Issue(split, DeliveryIssueStatus.Issued, (10, 30m, 0m));
        Issue(split, DeliveryIssueStatus.Issued, (10, 20m, 0m));
        Assert.Equal(50m, DeliveryOpenLines.OpenQuantity(split));
        Assert.True(DeliveryOpenLines.HasOutstandingLines(split));
    }

    [Fact]
    public void OpenQuantity_AgreesWithTheViewPredicateOnACountTradedNote()
    {
        // A count-traded line has an ordered quantity of zero, so the screen's old quantity-only
        // calculation reported 0 outstanding for a note whose count was never issued at all, and
        // the note dropped out of the picker while the details screen still called it open.
        var unissued = Note((10, 0m, 50m));
        // The outstanding figure must come from the dimension that is actually open.
        Assert.Equal(50m, DeliveryOpenLines.OpenQuantity(unissued));
        Assert.True(DeliveryOpenLines.HasOutstandingLines(unissued));
        Assert.True(OpenNoteIsOffered(unissued) == DeliveryOpenLines.HasOutstandingLines(unissued));

        // Fully delivered by count: zero in both.
        var delivered = Note((10, 0m, 50m));
        Issue(delivered, DeliveryIssueStatus.Issued, (10, 0m, 50m));
        Assert.Equal(0m, DeliveryOpenLines.OpenQuantity(delivered));
        Assert.False(DeliveryOpenLines.HasOutstandingLines(delivered));
        Assert.False(OpenNoteIsOffered(delivered));

        // Partially delivered by count: the count residual, not the quantity one.
        var partial = Note((10, 0m, 50m));
        Issue(partial, DeliveryIssueStatus.Issued, (10, 0m, 12m));
        Assert.Equal(38m, DeliveryOpenLines.OpenQuantity(partial));
        Assert.True(DeliveryOpenLines.HasOutstandingLines(partial));

        // Quantity and count on the same note are settled independently.
        var mixed = Note((10, 8m, 0m), (11, 0m, 50m));
        Issue(mixed, DeliveryIssueStatus.Issued, (10, 8m, 0m));
        Assert.Equal(50m, DeliveryOpenLines.OpenQuantity(mixed));
        Issue(mixed, DeliveryIssueStatus.Issued, (11, 0m, 50m));
        Assert.Equal(0m, DeliveryOpenLines.OpenQuantity(mixed));
        Assert.False(DeliveryOpenLines.HasOutstandingLines(mixed));
    }

    /// <summary>
    /// The rule the raise-an-issue screen applies before offering a note: not cancelled and not
    /// invoiced, exactly as its query filters, then the shared outstanding predicate.
    /// </summary>
    private static bool OpenNoteIsOffered(DeliveryOrder note)
        => note.Status != DeliveryOrderStatus.Cancelled
           && note.SaleInvoiceId is null
           && DeliveryOpenLines.HasOutstandingLines(note);

    [Fact]
    public void HasOutstandingLines_AggregatesIssuedLinesAcrossIssuesAndSkipsCancelledOnes()
    {
        var note = Note((10, 100m, 0m), (11, 0m, 50m));
        Issue(note, DeliveryIssueStatus.Issued, (10, 40m, 0m));
        Issue(note, DeliveryIssueStatus.Issued, (10, 59.99995m, 0m));

        // Line 10 residual is 0.00005 -> at the tolerance -> settled. Line 11 has nothing issued.
        Assert.True(DeliveryOpenLines.HasOutstandingLines(note));

        Issue(note, DeliveryIssueStatus.Issued, (11, 0m, 50m));
        Assert.False(DeliveryOpenLines.HasOutstandingLines(note),
            "Both lines are now issued to within the tolerance, so the note is complete.");

        // A cancelled issue releases nothing, so re-adding the quantities under Cancelled must not
        // change the answer.
        var withCancelled = Note((10, 100m, 0m));
        Issue(withCancelled, DeliveryIssueStatus.Issued, (10, 60m, 0m));
        Issue(withCancelled, DeliveryIssueStatus.Cancelled, (10, 40m, 0m));
        Assert.True(DeliveryOpenLines.HasOutstandingLines(withCancelled),
            "A cancelled issue must not count towards what has been issued.");
    }

    [Fact]
    public void HasOutstandingLines_ExactlyAtTolerance00005AcrossIssuesIsSettled()
    {
        var note = Note((10, 100m, 0m));
        Issue(note, DeliveryIssueStatus.Issued, (10, 70m, 0m));
        Issue(note, DeliveryIssueStatus.Issued, (10, 29.99995m, 0m));
        Assert.False(DeliveryOpenLines.HasOutstandingLines(note),
            "70 + 29.99995 = 99.99995, a residual of exactly 0.00005, which `<= tolerance` settles.");
    }

    [Fact]
    public void HasOutstandingLines_OneStorableStepAboveTolerance00010AcrossIssuesIsOutstanding()
    {
        var note = Note((10, 100m, 0m));
        Issue(note, DeliveryIssueStatus.Issued, (10, 70m, 0m));
        Issue(note, DeliveryIssueStatus.Issued, (10, 29.9999m, 0m));
        Assert.True(DeliveryOpenLines.HasOutstandingLines(note),
            "70 + 29.9999 = 99.9999, a residual of 0.0001 - one storable step - so the note " +
            "still owes a delivery, and the stock for it is still owed to the customer.");
    }

    [Fact]
    public void CanIssueMoreLines_ClosesACancelledOrInvoicedNoteEvenWhenLinesAreOutstanding()
    {
        var note = Note((10, 100m, 0m));
        Issue(note, DeliveryIssueStatus.Issued, (10, 10m, 0m));

        Assert.True(DeliveryOpenLines.CanIssueMoreLines(note));

        note.Status = DeliveryOrderStatus.Cancelled;
        Assert.False(DeliveryOpenLines.CanIssueMoreLines(note));

        note.Status = DeliveryOrderStatus.PartiallyIssued;
        note.SaleInvoiceId = 42;
        Assert.False(DeliveryOpenLines.CanIssueMoreLines(note));

        note.SaleInvoiceId = null;
        Assert.True(DeliveryOpenLines.CanIssueMoreLines(note));
    }

    // ---------------------------------------------------------------------
    // Priority 2 - order totals and progress lifted out of the order views.
    // ---------------------------------------------------------------------

    [Fact]
    public void OrderedForDisplay_UsesQuantityWhenTradedByQuantityAndCountOtherwise()
    {
        // The README rule `Quantity > 0 ? Quantity : Count`, now in one named place.
        Assert.Equal(7m, OrderProgress.OrderedForDisplay(new SalesOrderItem { Quantity = 7m, Count = 3m }));
        Assert.Equal(3m, OrderProgress.OrderedForDisplay(new SalesOrderItem { Quantity = 0m, Count = 3m }));
        Assert.Equal(0m, OrderProgress.OrderedForDisplay(new SalesOrderItem()));
    }

    [Fact]
    public void ForDisplay_AllTrackTheSameAxisAsOrdered()
    {
        var byQuantity = new SalesOrderItem
        {
            Quantity = 10m,
            Count = 4m,
            DeliveredQty = 6m,
            DeliveredCount = 2m,
            InvoicedQty = 3m,
            InvoicedCount = 1m,
            ReservedQty = 2m,
            ReservedCount = 1m
        };
        Assert.Equal(10m, OrderProgress.OrderedForDisplay(byQuantity));
        Assert.Equal(6m, OrderProgress.DeliveredForDisplay(byQuantity));
        Assert.Equal(3m, OrderProgress.InvoicedForDisplay(byQuantity));
        Assert.Equal(2m, OrderProgress.ReservedForDisplay(byQuantity));

        var byCount = new SalesOrderItem
        {
            Quantity = 0m,
            Count = 10m,
            DeliveredQty = 6m,
            DeliveredCount = 2m,
            InvoicedQty = 3m,
            InvoicedCount = 1m,
            ReservedQty = 2m,
            ReservedCount = 1m
        };
        Assert.Equal(10m, OrderProgress.OrderedForDisplay(byCount));
        Assert.Equal(2m, OrderProgress.DeliveredForDisplay(byCount));
        Assert.Equal(1m, OrderProgress.InvoicedForDisplay(byCount));
        Assert.Equal(1m, OrderProgress.ReservedForDisplay(byCount));
    }

    [Fact]
    public void InvoicingPercent_IsZeroWhenNothingIsOrderedAndClampsAtOneHundred()
    {
        Assert.Equal(0m, OrderProgress.InvoicingPercent(new SalesOrderItem { Quantity = 0m, Count = 0m, InvoicedQty = 5m }));

        Assert.True(OrderProgress.InvoicingPercent(new SalesOrderItem { Quantity = 10m, InvoicedQty = 15m }) == 100m,
            "An over-invoiced line must not render a bar wider than its track.");
        Assert.Equal(100m, OrderProgress.InvoicingPercent(new SalesOrderItem { Quantity = 10m, InvoicedQty = 10m }));
        Assert.Equal(50m, OrderProgress.InvoicingPercent(new SalesOrderItem { Quantity = 10m, InvoicedQty = 5m }));
        Assert.Equal(0m, OrderProgress.InvoicingPercent(new SalesOrderItem { Quantity = 10m, InvoicedQty = 0m }));
    }

    [Fact]
    public void InvoicingPercent_UsesTheCountAxisForCountTradedLines()
    {
        var item = new SalesOrderItem { Quantity = 0m, Count = 200m, InvoicedQty = 0m, InvoicedCount = 50m };
        Assert.Equal(25m, OrderProgress.InvoicingPercent(item));
    }

    [Fact]
    public void Summarize_SalesOrder_MatchesTheSumsTheListRowUsedToComputeInMarkup()
    {
        var order = new SalesOrder();
        order.Items.Add(new SalesOrderItem { Quantity = 10m, Count = 2m, DeliveredQty = 4m, DeliveredCount = 1m, InvoicedQty = 3m, InvoicedCount = 1m, UnitPrice = 5m });
        order.Items.Add(new SalesOrderItem { Quantity = 20m, Count = 0m, DeliveredQty = 20m, DeliveredCount = 0m, InvoicedQty = 20m, InvoicedCount = 0m, UnitPrice = 2m });

        var t = OrderProgress.Summarize(order);

        Assert.Equal(30m, t.OrderedQuantity);
        Assert.Equal(2m, t.OrderedCount);
        Assert.Equal(24m, t.DeliveredQuantity);
        Assert.Equal(1m, t.DeliveredCount);
        Assert.Equal(23m, t.InvoicedQuantity);
        Assert.Equal(1m, t.InvoicedCount);
        // Total follows `Quantity > 0 ? Quantity : Count` per line, exactly as SalesOrderItem.Total does.
        Assert.Equal(10m * 5m + 20m * 2m, t.Total);
    }

    [Fact]
    public void Summarize_PurchaseOrder_MatchesTheSumsTheListRowUsedToComputeInMarkup()
    {
        var order = new PurchaseOrder();
        order.Items.Add(new PurchaseOrderItem { Quantity = 10m, ReceivedQty = 4m, UnitPrice = 5m });
        order.Items.Add(new PurchaseOrderItem { Quantity = 20m, ReceivedQty = 20m, UnitPrice = 2m });

        var t = OrderProgress.Summarize(order);

        Assert.Equal(30m, t.OrderedQuantity);
        Assert.Equal(24m, t.ReceivedQuantity);
        Assert.Equal(10m * 5m + 20m * 2m, t.Total);
    }

    [Fact]
    public void HasReceived_IsTrueWhenAnyLineHasComeInOnEitherAxis()
    {
        Assert.False(OrderProgress.HasReceived(new PurchaseOrder()));
        Assert.False(OrderProgress.HasReceived(Order(new(0m, 0m), (0m, 0m))));

        Assert.True(OrderProgress.HasReceived(Order((0m, 3m), (0m, 0m))));
        Assert.True(OrderProgress.HasReceived(Order((0m, 0m), (2m, 0m))));
        Assert.False(OrderProgress.HasReceived(Order((0m, 0m), (0m, 0m))));

        static PurchaseOrder Order(params (decimal Qty, decimal Count)[] lines)
        {
            var order = new PurchaseOrder();
            foreach (var (qty, count) in lines)
            {
                order.Items.Add(new PurchaseOrderItem { Quantity = qty, ReceivedQty = qty, ReceivedCount = count });
            }

            return order;
        }
    }

    [Fact]
    public void SettlementLabel_IsArabicForBothSettlementStates()
    {
        Assert.Equal("مسدّدة", OrderProgress.SettlementLabel(new SaleInvoice { IsPaid = true }));
        Assert.Equal("غير مسدّدة", OrderProgress.SettlementLabel(new SaleInvoice { IsPaid = false }));
    }

    // ---------------------------------------------------------------------
    // Priority 2 - ledger header totals that were LINQ inside two Razor views.
    // ---------------------------------------------------------------------

    [Fact]
    public void CustomerLedgerTotals_CountOnlyReceiptsTowardsTheReceiptsTotal()
    {
        var vm = new CustomerLedgerViewModel
        {
            Invoices = [new SaleInvoice { NetAmount = 100m }, new SaleInvoice { NetAmount = 250m }],
            Payments =
            [
                new Payment { Type = PaymentType.Receipt, Amount = 90m },
                new Payment { Type = PaymentType.Disbursement, Amount = 999m },
                new Payment { Type = PaymentType.Receipt, Amount = 10m }
            ],
            Returns = [new SaleReturn { TotalAmount = 40m }]
        };

        Assert.Equal(350m, vm.InvoicesTotal);
        Assert.Equal(100m, vm.ReceiptsTotal);
        Assert.Equal(40m, vm.ReturnsTotal);
    }

    [Fact]
    public void SupplierLedgerTotals_CountOnlyDisbursementsTowardsThePaidTotal()
    {
        var vm = new SupplierLedgerViewModel
        {
            Invoices = [new PurchaseInvoice { NetAmount = 500m }],
            Payments =
            [
                new Payment { Type = PaymentType.Disbursement, Amount = 200m },
                new Payment { Type = PaymentType.Receipt, Amount = 999m }
            ],
            Returns = [new PurchaseReturn { TotalAmount = 25m }]
        };

        Assert.Equal(500m, vm.InvoicesTotal);
        Assert.Equal(200m, vm.DisbursementsTotal);
        Assert.Equal(25m, vm.ReturnsTotal);
    }

    // ---------------------------------------------------------------------
    // Priority 2 - the unit and role filters that were LINQ inside two more views.
    // ---------------------------------------------------------------------

    [Fact]
    public void SelectableUnits_OffersRootsAndParentsButNotLeaves()
    {
        var vm = new SettingsViewModel
        {
            Units =
            [
                new Unit { Id = 1, Name = "قطعة", SubUnits = 3, ParentUnitId = null },        // root with children
                new Unit { Id = 2, Name = "كرتونة", SubUnits = 0, ParentUnitId = 1 },          // leaf
                new Unit { Id = 3, Name = "باكيت", SubUnits = null, ParentUnitId = null },       // root, no SubUnits set
                new Unit { Id = 4, Name = "جعب", SubUnits = 2, ParentUnitId = 3 }              // mid-level parent
            ]
        };

        Assert.Equal(new[] { 1, 3, 4 }, vm.SelectableUnits.Select(u => u.Id));
    }

    [Fact]
    public void RoleLabels_MapKnownRolesAndNeverMislabelAnUnknownOneAsSomebodyElsesJob()
    {
        Assert.Equal("مدير النظام", UserRoleLabels.DisplayName(UserRoleLabels.Admin));
        Assert.Equal("محاسب", UserRoleLabels.DisplayName(UserRoleLabels.Accountant));
        Assert.Equal("أمين مخزن", UserRoleLabels.DisplayName(UserRoleLabels.Warehouse));
        // The old nested ternary rendered every non-Accountant key as "أمين مخزن".
        Assert.Equal(UserRoleLabels.UnknownRole, UserRoleLabels.DisplayName("Auditor"));
        Assert.DoesNotContain("Auditor", UserRoleLabels.DisplayName("Auditor"));
    }

    [Fact]
    public void DisplayRoles_HidesAdminBecauseItAlreadyHasItsOwnBadge()
    {
        var admin = new UserListItemViewModel { Roles = [UserRoleLabels.Admin] };
        Assert.True(admin.IsAdmin);
        Assert.Empty(admin.DisplayRoles);

        var accountant = new UserListItemViewModel { Roles = [UserRoleLabels.Accountant, UserRoleLabels.Warehouse] };
        Assert.False(accountant.IsAdmin);
        Assert.Equal(new[] { UserRoleLabels.Accountant, UserRoleLabels.Warehouse }, accountant.DisplayRoles);

        Assert.Empty(new UserListItemViewModel().DisplayRoles);
    }

    // ---------------------------------------------------------------------
    // The source-level regression guard.
    // ---------------------------------------------------------------------

    [Fact]
    public void NoViewCarriesABareRoundingTolerance()
    {
        // `<script>` and `<style>` bodies are exempt here too: the numbers in them are CSS and
        // JavaScript constants (a 0.05 contrast guard, a 0.05 zoom step), not an EGLP quantity
        // tolerance. The negative lookbehind keeps 1.05, 10.05 and 10.0005 out - a tolerance is a
        // value of its own, never the tail of a longer number.
        var offenders = Offenders(markup => GuardRegex.Tolerance.IsMatch(markup), exemptScriptBodies: true);

        Assert.True(offenders.Count == 0,
            "A rounding tolerance in a Razor view decides a business outcome with no name. " +
            "Call the named predicate in Services/ instead:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void NoViewRunsLinqOverAnEntityCollectionInMarkup()
    {
        // `.Sum(` and `.Where(` over a loaded navigation collection is the aggregation the views
        // used to do per table row. `<script>` bodies are exempt: the projection there marshals an
        // already-built payload into the page's JSON bootstrap block, it computes nothing.
        var offenders = Offenders(
            markup => GuardRegex.LinqOverEntityCollection.IsMatch(markup),
            exemptScriptBodies: true);

        Assert.True(offenders.Count == 0,
            "Move the aggregation to a service method or a computed view-model property:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The screens that decide whether a delivery still has lines left used to each hold their own
    /// copy of the settled/open-line calculation - one of them compared issued quantity only, so it
    /// disagreed with the other about a count-traded line. Both copies are gone; this pins that
    /// neither can grow back, and that the tolerance keeps exactly one home.
    ///
    /// Note the reconstruction check is deliberately narrow: it looks for an <em>in-memory</em> copy
    /// of the issued-per-item total. A <c>.GroupBy</c> that EF runs in SQL is a different mechanism
    /// and cannot call an in-memory predicate, so it is not a second copy of the rule.
    /// </summary>
    [Theory]
    [InlineData("DeliveryIssuesController.cs")]
    [InlineData("DeliveryOrdersController.cs")]
    public void NoDeliveryControllerCarriesACopyOfTheSettledRule(string fileName)
    {
        var path = Path.Combine(WebProjectDirectory(), "Controllers", fileName);
        Assert.True(File.Exists(path), $"لم يُعثر على وحدة التحكّم: {path}");

        var offenders = ControllerRuleOffenders(File.ReadAllText(path));

        Assert.True(offenders.Count == 0,
            $"يجب أن تشتقّ {fileName} قاعدة الاستحقاق من DeliveryOpenLines: " +
            string.Join("، ", offenders));
    }

    /// <summary>
    /// The three ways a controller re-acquires its own copy of the rule. Extracted so the self-check
    /// can run the same detection over a known-good body and prove it stays quiet.
    /// </summary>
    private static List<string> ControllerRuleOffenders(string source)
    {
        var offenders = new List<string>();
        if (GuardRegex.Tolerance.IsMatch(source))
        {
            offenders.Add("عتبة تقريب مكتوبة مباشرةً (0.005 أو 0.00005)");
        }

        if (GuardRegex.ReconstructsIssuedTotals.IsMatch(source))
        {
            offenders.Add("إعادة بناء مجموع الصادر لكل صنف داخل الوحدة");
        }

        if (!source.Contains("DeliveryOpenLines.", StringComparison.Ordinal))
        {
            offenders.Add("لا استدعاء لقاعدة DeliveryOpenLines المشتركة");
        }

        return offenders;
    }

    /// <summary>
    /// The guard has to be able to fail, or it proves nothing. These two facts run the same matchers
    /// the sweep uses, so a sweep that passes only because the pattern silently stopped matching is
    /// caught here instead of in production.
    /// </summary>
    [Fact]
    public void TheGuardPatternsStillMatchTheCodeTheyAreMeantToCatch()
    {
        var tolerance = GuardRegex.Tolerance.IsMatch("@if (residual > 0.005m) { open = true; }");
        var quantityTolerance = GuardRegex.Tolerance.IsMatch("@if (residual > 0.00005m) { open = true; }");
        var linq = GuardRegex.LinqOverEntityCollection.IsMatch("@{ var t = Model.Items.Sum(i => i.Total); }");
        var reconstructs = GuardRegex.ReconstructsIssuedTotals.IsMatch(
            "issued = note.Issues.SelectMany(i => i.Items).GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));");

        Assert.True(tolerance, "لم يعد نمط فحص عتبة التقريب يطابق ما وُضع لأجله.");
        Assert.True(quantityTolerance, "لم يعد النمط يطابق عتبة الكمية 0.00005m بعد تغيّرها.");
        Assert.True(linq, "لم يعد نمط فحص LINQ يطابق ما وُضع لأجله.");
        Assert.True(reconstructs, "لم يعد نمط فحص إعادة بناء مجموع الصادر يطابق ما وُضع لأجله.");

        // And it must not fire on the things views legitimately contain.
        Assert.False(GuardRegex.Tolerance.IsMatch("@Model.Amount.ToString(\"N2\")"),
            "A formatted amount is not a tolerance.");
        Assert.False(GuardRegex.Tolerance.IsMatch("scale: 1.05"),
            "The guard must not read a quantity like 1.05 as a 0.005 tolerance.");
        Assert.False(GuardRegex.Tolerance.IsMatch("qty: 10.0005"),
            "The guard must not read a fourth-decimal quantity as a tolerance.");
        Assert.False(GuardRegex.ReconstructsIssuedTotals.IsMatch(
                "return notes.Where(DeliveryOpenLines.HasOutstandingLines).ToList();"),
            "Delegating to the shared rule is exactly what the controller should do.");

        // The controller guard must stay quiet on a body that already delegates, or it would fail
        // the build rather than catch a regression.
        Assert.Empty(ControllerRuleOffenders(
            "return notes.Where(DeliveryOpenLines.HasOutstandingLines).ToList();"));
        Assert.Empty(ControllerRuleOffenders(
            "}).Where(i => DeliveryOpenLines.HasResidualLeft(i.Quantity, i.Count)).ToList();"));
        // ...and it must still speak up on a body that re-derives the rule.
        var reported = string.Join("، ", ControllerRuleOffenders("if (i.Quantity > 0.005m) keep = true;"));
        Assert.Contains("عتبة تقريب", reported, StringComparison.Ordinal);
    }

    /// <summary>The same two expressions the sweep runs, so the self-check and the sweep cannot drift.</summary>
    private static class GuardRegex
    {
        /// <summary>
        /// A bare rounding-tolerance literal. The negative lookbehind keeps 1.05 and 10.05 out: a
        /// tolerance is a value of its own, never the tail of a longer number. Both tolerances the
        /// system actually uses are matched - the 0.005 money materiality and the 0.00005 quantity
        /// one - because a copy of either rule pasted into markup is the same defect, and because
        /// the quantity tolerance moved when the column widened.
        /// </summary>
        public static readonly Regex Tolerance =
            new(@"(?<![\d.])(?:0\.00005|0\.005)m?\b", RegexOptions.Compiled);

        public static readonly Regex LinqOverEntityCollection = new(@"\.(Sum|Where)\s*\(", RegexOptions.Compiled);

        /// <summary>
        /// A hand-rolled issued-per-item total - the shape a copy of the settled rule takes when it
        /// is rebuilt where it is used rather than asked for. Matching the accumulator on its own
        /// name keeps an honest <c>notes.Where(...)</c> out of the way.
        /// </summary>
        public static readonly Regex ReconstructsIssuedTotals =
            new(@"\.GroupBy\s*\(\s*l\s*=>\s*l\.(ItemId|Quantity|Count)\b", RegexOptions.Compiled);
    }

    /// <summary>
    /// Every view whose markup (optionally minus its script bodies) trips <paramref name="offender"/>,
    /// reported as file:line so the failure names a place to go.
    /// </summary>
    private static List<string> Offenders(Func<string, bool> offender, bool exemptScriptBodies = false)
    {
        var hits = new List<string>();
        var views = 0;

        foreach (var view in EnumerateViews())
        {
            views++;
            var markup = exemptScriptBodies ? StripScriptBodies(File.ReadAllText(view)) : File.ReadAllText(view);
            if (!offender(markup))
            {
                continue;
            }

            foreach (var (number, line) in markup.Split('\n').Select((l, i) => (i + 1, l)))
            {
                if (offender(line))
                {
                    hits.Add($"{Relative(view)}:{number}: {line.Trim()}");
                }
            }
        }

        Assert.True(views > 0, "لم يُعثر على أي ملف view؛ الفحص لا يعمل.");
        return hits;
    }

    /// <summary>
    /// Blanks out <c>&lt;script&gt;…&lt;/script&gt;</c> and <c>&lt;style&gt;…&lt;/style&gt;</c> bodies while
    /// keeping every line number in the file, so a hit still points at the right line.
    /// </summary>
    private static string StripScriptBodies(string markup)
        => Regex.Replace(markup, @"<(?<tag>script|style)\b[^>]*>.*?</\k<tag>>",
            m => Regex.Replace(m.Value, @"[^\r\n]", " "), RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static IEnumerable<string> EnumerateViews()
    {
        var root = Path.Combine(WebProjectDirectory(), "Views");
        Assert.True(Directory.Exists(root), $"مجلد Views غير موجود: {root}");
        return Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string WebProjectDirectory()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "NewVixSmart.Web");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"تعذر العثور على src\\NewVixSmart.Web بالبحث الصاعد من {AppContext.BaseDirectory}.");
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(WebProjectDirectory(), path).Replace('\\', '/');
}
