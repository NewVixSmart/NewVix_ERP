using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NewVixSmart.Web.Models.Accounting;

public enum PaymentAllocationInvoiceType
{
    [Display(Name = "مبيعات")]
    Sales = 1,
    [Display(Name = "مشتريات")]
    Purchases = 2
}

public class PaymentAllocation
{
    public int Id { get; set; }

    public int PaymentId { get; set; }

    [ForeignKey(nameof(PaymentId))]
    public Payment? Payment { get; set; }

    public PaymentAllocationInvoiceType InvoiceType { get; set; }

    public int InvoiceId { get; set; }

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