using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NewVixSmart.Web.Models.Accounting;

public class BudgetLine
{
    public int Id { get; set; }

    [Display(Name = "سنة الميزانية")]
    public int BudgetYearId { get; set; }

    [ForeignKey(nameof(BudgetYearId))]
    public BudgetYear? BudgetYear { get; set; }

    [Display(Name = "الحساب")]
    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public GLAccount? Account { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "المبلغ السنوي")]
    public decimal AnnualAmount { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
