using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج الدفعة المُرسلة من النموذج.
/// <para>
/// كان <c>Payment</c> نفسه هو ما يُربط، فكان المتصفح يقدر يرسل <c>Id</c> و
/// <c>PublicId</c> و<code>BranchId</code> و<code>CreatedBy</code>؛
/// <c>PaymentService</c> يكتب هذه الحقول بنفسه قبل الحفظ لكن <c>Add</c> على كيان بمفتاح
/// مرسل من المتصفح سلوك غير متوقّع. ورقم الإيصال يولّده الخادم، فكان حقلاً للعرض فقط.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class PaymentFormModel
{
    [BindNever]
    [Display(Name = "رقم الإيصال")]
    public string ReceiptNumber { get; set; } = string.Empty;

    [BindNever]
    [Display(Name = "نوع العملية")]
    public PaymentType Type { get; set; }

    [Display(Name = "العميل")]
    public int? CustomerId { get; set; }

    [Display(Name = "المورد")]
    public int? SupplierId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "المبلغ")]
    [Range(0.01, 999999999, ErrorMessage = "المبلغ لا يقبل صفراً أو سالباً أو أكبر من الحد المسموح")]
    public decimal Amount { get; set; }

    [Display(Name = "طريقة الدفع")]
    public PaymentMethod Method { get; set; }

    [Display(Name = "تاريخ الدفع")]
    [DataType(DataType.Date)]
    public DateTime PaymentDate { get; set; } = DateTime.Today;

    [StringLength(200)]
    [Display(Name = "رقم المرجع")]
    public string? ReferenceNumber { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>
    /// يبني الكيان الذي يمر إلى <c>PaymentService.CreatePaymentAsync</c>؛ النوع يحدّده
    /// المتصل لا النموذج، فالدفعة من عميل تبقى من عميل ولو وصل معها معرّف مورد.
    /// </summary>
    public Payment ToEntity(PaymentType type) => new()
    {
        Type = type,
        CustomerId = CustomerId,
        SupplierId = SupplierId,
        Amount = Amount,
        Method = Method,
        PaymentDate = PaymentDate,
        ReferenceNumber = ReferenceNumber,
        Notes = Notes
    };
}
