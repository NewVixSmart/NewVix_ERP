using System.ComponentModel.DataAnnotations;

namespace Silk.Trading.Web.Models.Accounting;

public class JournalEntry
{
    public int Id { get; set; }

    [Required(ErrorMessage = "رقم القيد مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم القيد")]
    public string EntryNumber { get; set; } = string.Empty;

    [Display(Name = "تاريخ القيد")]
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [StringLength(500)]
    [Display(Name = "البيان")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "المصدر")]
    public JournalSource Source { get; set; }

    [Display(Name = "معرف المستند المصدر")]
    public int SourceId { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "مرحّل")]
    public bool IsPosted { get; set; }

    [Display(Name = "الفرع")]
    public int? BranchId { get; set; }

    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
}
