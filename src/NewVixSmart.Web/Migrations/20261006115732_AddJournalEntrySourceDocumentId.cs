using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewVixSmart.Web.Migrations;

/// <inheritdoc />
public partial class AddJournalEntrySourceDocumentId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SourceDocumentId",
            table: "JournalEntries",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_JournalEntries_SourceDocumentId",
            table: "JournalEntries",
            column: "SourceDocumentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_JournalEntries_SourceDocumentId",
            table: "JournalEntries");

        migrationBuilder.DropColumn(
            name: "SourceDocumentId",
            table: "JournalEntries");
    }
}
