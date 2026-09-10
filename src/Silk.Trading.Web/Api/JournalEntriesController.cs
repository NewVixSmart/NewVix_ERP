using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Api.Dtos;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Accounting;

namespace Silk.Trading.Web.Api;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class JournalEntriesController : ControllerBase
{
    private const int PageSize = 50;
    private readonly AppDbContext _db;

    public JournalEntriesController(AppDbContext db) => _db = db;

    [HttpGet("journal-entries")]
    [ApiAuthorize("AuditLedger.View")]
    public async Task<IActionResult> GetJournalEntries(DateTime? from, DateTime? to, int? accountId, JournalSource? source, int page = 1)
    {
        page = Math.Max(1, page);
        var today = DateTime.Today;
        from = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
        to = (to ?? today).Date;

        var query = _db.JournalEntries
            .AsNoTracking()
            .Where(j => j.IsPosted && j.Date >= from.Value && j.Date <= to.Value);
        if (source is not null)
            query = query.Where(j => j.Source == source);
        if (accountId is not null)
            query = query.Where(j => j.Lines.Any(l => l.AccountId == accountId));

        var total = await query.CountAsync();
        var filteredEntryIds = query.Select(j => j.Id);
        var totalDebit = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => filteredEntryIds.Contains(l.JournalEntryId))
            .SumAsync(l => (decimal?)l.Debit) ?? 0;
        var totalCredit = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => filteredEntryIds.Contains(l.JournalEntryId))
            .SumAsync(l => (decimal?)l.Credit) ?? 0;

        var entriesRaw = await query.OrderBy(j => j.EntryNumber).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
        var entryIds = entriesRaw.Select(e => e.Id).ToArray();
        var lines = await _db.JournalEntryLines
            .AsNoTracking()
            .Include(l => l.Account)
            .Where(l => entryIds.Contains(l.JournalEntryId))
            .ToListAsync();

        var data = entriesRaw.Select(e =>
        {
            var entryLines = lines.Where(l => l.JournalEntryId == e.Id).ToList();
            return new JournalEntryResponse
            {
                Id = e.Id,
                EntryNumber = e.EntryNumber,
                Date = e.Date,
                Description = e.Description,
                Source = e.Source.GetDisplayName(),
                SourceId = e.SourceId,
                CreatedBy = e.CreatedBy,
                BranchId = e.BranchId,
                TotalDebit = entryLines.Sum(l => l.Debit),
                TotalCredit = entryLines.Sum(l => l.Credit),
                Balance = Math.Round(entryLines.Sum(l => l.Debit) - entryLines.Sum(l => l.Credit), 2),
                Lines = entryLines.Select(l => new JournalEntryLineResponse
                {
                    AccountCode = l.Account?.Code ?? "",
                    AccountName = l.Account?.Name ?? "",
                    Debit = l.Debit,
                    Credit = l.Credit,
                    Description = l.Description
                }).ToList()
            };
        }).ToList();

        return Ok(new
        {
            total,
            totalDebit,
            totalCredit,
            page,
            pageSize = PageSize,
            totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)),
            items = data
        });
    }
}