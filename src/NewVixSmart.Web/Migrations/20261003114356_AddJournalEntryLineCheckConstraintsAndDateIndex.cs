using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddJournalEntryLineCheckConstraintsAndDateIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "CK_JournalEntryLines_NonNegative",
            table: "JournalEntryLines",
            sql: "([Debit] >= 0 AND [Credit] >= 0)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_JournalEntryLines_OneSided",
            table: "JournalEntryLines",
            sql: "([Debit] = 0 AND [Credit] > 0) OR ([Debit] > 0 AND [Credit] = 0)");

        migrationBuilder.CreateIndex(
            name: "IX_JournalEntries_Date",
            table: "JournalEntries",
            column: "Date");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_JournalEntryLines_NonNegative",
            table: "JournalEntryLines");

        migrationBuilder.DropCheckConstraint(
            name: "CK_JournalEntryLines_OneSided",
            table: "JournalEntryLines");

        migrationBuilder.DropIndex(
            name: "IX_JournalEntries_Date",
            table: "JournalEntries");
    }
}
