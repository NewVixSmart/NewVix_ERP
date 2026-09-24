using System.ComponentModel.DataAnnotations.Schema;
using NewVixSmart.Web.Models.Purchases;

namespace NewVixSmart.Web.Models.Accounting;

public class PurchasePaymentAllocation
{
    public int Id { get; set; }

    public int PaymentId { get; set; }

    [ForeignKey(nameof(PaymentId))]
    public Payment? Payment { get; set; }

    public int PurchaseInvoiceId { get; set; }

    [ForeignKey(nameof(PurchaseInvoiceId))]
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AllocatedBaseAmount { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    public decimal? ExchangeRateAtSettlement { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal FxGain { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal FxLoss { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}