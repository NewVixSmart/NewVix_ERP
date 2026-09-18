using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Services;

public class FiscalService : IFiscalService
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;
    private readonly IFinancialReportService _financial;

    public FiscalService(AppDbContext db, IAccountingService accounting, IFinancialReportService financial)
    {
        _db = db;
        _accounting = accounting;
        _financial = financial;
    }

    public async Task<FiscalPeriod?> GetPeriodAsync(int year)
        => await _db.FiscalPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Year == year);

    public async Task EnsurePeriodAsync(int year)
    {
        if (!await _db.FiscalPeriods.AnyAsync(p => p.Year == year))
        {
            _db.FiscalPeriods.Add(new FiscalPeriod { Year = year, Name = $"سنة {year}" });
            await _db.SaveChangesAsync();
        }
    }

    public Task<bool> IsClosedAsync(DateTime date)
        => _db.FiscalPeriods.AsNoTracking().AnyAsync(p => p.Year == date.Year && p.IsClosed);

    public async Task ValidateBudgetWriteAsync(int year)
    {
        if (await _db.FiscalPeriods.AsNoTracking().AnyAsync(p => p.Year == year && p.IsClosed))
            throw new InvalidOperationException($"السنة المالية {year} مغلقة — لا يمكن إنشاء أو تعديل ميزانيتها");
    }

    public async Task<FiscalCloseSummary> CloseYearAsync(int year, string? user)
    {
        var period = await _db.FiscalPeriods.FirstOrDefaultAsync(p => p.Year == year);
        if (period == null)
            throw new InvalidOperationException($"السنة المالية {year} غير موجودة — أنشئها أولاً");

        if (period.IsClosed)
            throw new InvalidOperationException($"السنة المالية {year} مغلقة بالفعل");

        var latestYear = await _db.FiscalPeriods.MaxAsync(p => p.Year);
        if (year < latestYear)
            throw new InvalidOperationException($"لا يمكن إغلاق سنة {year} لأن سنة {latestYear} أحدث — أغلق الأحدث أولاً");

        var retained = await _db.GLAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == "3001");
        if (retained == null || !retained.IsActive)
            throw new InvalidOperationException("حساب الأرباح المحتجزة 3001 غير موجود في مخطط الحسابات");

        var activity = await _financial.GetYearlyPlActivityAsync(year);
        var closeDate = new DateTime(year, 12, 31);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            int posted = 0;
            decimal netIncome = 0;
            foreach (var line in activity)
            {
                var signed = decimal.Round(
                    line.NormalBalance == NormalBalance.Debit ? line.Debit - line.Credit : line.Credit - line.Debit, 2);
                if (signed == 0) continue;

                var isCreditNormal = line.Type == GLAccountType.Revenue;
                JournalLine[] pair = isCreditNormal
                    ? signed > 0
                        ? [new JournalLine(line.Code, signed, 0), new JournalLine("3001", 0, signed)]
                        : [new JournalLine(line.Code, 0, -signed), new JournalLine("3001", -signed, 0)]
                    : signed > 0
                        ? [new JournalLine("3001", signed, 0), new JournalLine(line.Code, 0, signed)]
                        : [new JournalLine("3001", 0, -signed), new JournalLine(line.Code, -signed, 0)];

                await _accounting.PostAsync(JournalSource.YearEndClose, period.Id, closeDate,
                    $"إقفال سنوي — ترحيل رصيد {line.Name} إلى الأرباح المحتجزة", pair, user);

                netIncome += isCreditNormal ? signed : -signed;
                posted++;
            }

            _db.ChangeTracker.Clear();

            var toClose = await _db.FiscalPeriods.SingleAsync(p => p.Year == year);
            toClose.IsClosed = true;
            toClose.ClosedById = user;
            toClose.ClosedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            return new FiscalCloseSummary(year, posted, decimal.Round(netIncome, 2), posted);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<FiscalReopenSummary> ReopenYearAsync(int year, string? user)
    {
        var period = await _db.FiscalPeriods.FirstOrDefaultAsync(p => p.Year == year);
        if (period == null)
            throw new InvalidOperationException($"السنة المالية {year} غير موجودة");
        if (!period.IsClosed)
            throw new InvalidOperationException($"السنة المالية {year} غير مغلقة — يمكن إعادة فتح سنة مغلقة فقط");

        var latestYear = await _db.FiscalPeriods.MaxAsync(p => p.Year);
        if (year < latestYear)
            throw new InvalidOperationException($"لا يمكن إعادة فتح سنة {year} لأن سنة {latestYear} أحدث — أعد فتح الأحدث أولاً");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var entries = await _db.JournalEntries
                .Where(j => j.Source == JournalSource.YearEndClose && j.SourceId == period.Id)
                .Include(j => j.Lines)
                .ToListAsync();

            decimal net = 0;
            if (entries.Count > 0)
            {
                var retained = await _db.GLAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == "3001");
                if (retained != null)
                {
                    var ids = entries.Select(e => e.Id).ToList();
                    decimal credit = await _db.JournalEntryLines
                            .Where(l => ids.Contains(l.JournalEntryId) && l.AccountId == retained.Id)
                            .SumAsync(l => (decimal?)l.Credit) ?? 0m;
                    decimal debit = await _db.JournalEntryLines
                        .Where(l => ids.Contains(l.JournalEntryId) && l.AccountId == retained.Id)
                        .SumAsync(l => (decimal?)l.Debit) ?? 0m;
                    net = credit - debit;
                }

                var lines = entries.SelectMany(e => e.Lines).ToList();
                _db.JournalEntryLines.RemoveRange(lines);
                _db.JournalEntries.RemoveRange(entries);
                await _db.SaveChangesAsync();
            }

            period.IsClosed = false;
            period.ClosedById = null;
            period.ClosedAt = null;
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            return new FiscalReopenSummary(year, entries.Count, decimal.Round(net, 2));
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}