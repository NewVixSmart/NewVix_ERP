using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Silk.Trading.Web.Models.Accounting;

public class JournalEntryLine
{
    public int Id { get; set; }

    public int JournalEntryId { get; set; }

    [ForeignKey(nameof(JournalEntryId))]
    public JournalEntry? JournalEntry { get; set; }

    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public GLAccount? Account { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Debit { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Credit { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public int? BranchId { get; set; }
}
