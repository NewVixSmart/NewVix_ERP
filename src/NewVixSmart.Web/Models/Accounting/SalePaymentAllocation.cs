using System.ComponentModel.DataAnnotations.Schema;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Accounting;

public class SalePaymentAllocation
{
    public int Id { get; set; }

    public int PaymentId { get; set; }

    [ForeignKey(nameof(PaymentId))]
    public Payment? Payment { get; set; }

    public int SaleInvoiceId { get; set; }

    [ForeignKey(nameof(SaleInvoiceId))]
    public SaleInvoice? SaleInvoice { get; set; }

    /// <summary>
    /// Portion of the payment applied to this invoice, in EGP. The column was
    /// previously named AllocatedBaseAmount because a base-currency amount could
    /// differ from the transaction amount; with a single currency the two are
    /// identical, so it is renamed rather than dropped.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal AllocatedAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
