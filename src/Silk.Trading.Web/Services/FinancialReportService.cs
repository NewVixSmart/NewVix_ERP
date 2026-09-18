using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.ViewModels.Reports;

namespace Silk.Trading.Web.Services;

public class FinancialReportService : IFinancialReportService
{
    private readonly AppDbContext _db;

    public FinancialReportService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<TrialBalanceReportViewModel> TrialBalanceAsync(DateTime asOf)
    {
        var asOfDate = asOf.Date;

        var accounts = await _db.GLAccounts
            .AsNoTracking()
            .OrderBy(a => a.Code)
            .ToListAsync();

        var entryIds = _db.JournalEntries
            .AsNoTracking()
            .Where(j => j.IsPosted && j.Date <= asOfDate)
            .Select(j => j.Id);

        var lines = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => entryIds.Contains(l.JournalEntryId))
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(l => (decimal?)l.Debit) ?? 0m, Credit = g.Sum(l => (decimal?)l.Credit) ?? 0m })
            .ToListAsync();

        var byAccount = lines.ToDictionary(x => x.AccountId, x => x);

        var rows = new List<TrialBalanceRowViewModel>();
        foreach (var a in accounts)
        {
            byAccount.TryGetValue(a.Id, out var activity);
            var debit = activity?.Debit ?? 0m;
            var credit = activity?.Credit ?? 0m;
            var signed = a.NormalBalance == NormalBalance.Debit ? debit - credit : credit - debit;

            if (!a.IsActive && debit == 0m && credit == 0m) continue;

            rows.Add(new TrialBalanceRowViewModel
            {
                Code = a.Code,
                Name = a.Name,
                Debit = debit,
                Credit = credit,
                Balance = signed
            });
        }

        return new TrialBalanceReportViewModel
        {
            AsOf = asOfDate,
            Rows = rows,
            TotalDebit = rows.Sum(r => r.Debit),
            TotalCredit = rows.Sum(r => r.Credit)
        };
    }

    public async Task<IncomeStatementReportViewModel> IncomeStatementAsync(DateTime from, DateTime to)
    {
        var fromDate = from.Date;
        var toDate = to.Date;

        var activity = await GetAccountActivityAsync(fromDate, toDate, GLAccountType.Revenue, GLAccountType.Expense);

        var vm = new IncomeStatementReportViewModel { From = fromDate, To = toDate };

        foreach (var line in activity)
        {
            var signed = line.NormalBalance == NormalBalance.Debit ? line.Debit - line.Credit : line.Credit - line.Debit;

            if (line.Type == GLAccountType.Revenue)
            {
                if (signed >= 0)
                {
                    vm.RevenueLines.Add(new IncomeStatementLineViewModel { Code = line.Code, Name = line.Name, Amount = signed });
                    vm.TotalRevenue += signed;
                }
                else
                {
                    vm.ContraRevenueLines.Add(new IncomeStatementLineViewModel { Code = line.Code, Name = line.Name, Amount = -signed });
                    vm.TotalContraRevenue += -signed;
                }
            }
            else
            {
                if (signed >= 0)
                {
                    vm.ExpenseLines.Add(new IncomeStatementLineViewModel { Code = line.Code, Name = line.Name, Amount = signed });
                    vm.TotalExpenses += signed;
                }
                else
                {
                    vm.ContraExpenseLines.Add(new IncomeStatementLineViewModel { Code = line.Code, Name = line.Name, Amount = -signed });
                    vm.TotalContraExpenses += -signed;
                }
            }
        }

        return vm;
    }

    public async Task<BalanceSheetReportViewModel> BalanceSheetAsync(DateTime asOf)
    {
        var asOfDate = asOf.Date;

        var activity = await GetAccountActivityAsync(null, asOfDate, GLAccountType.Asset, GLAccountType.Liability, GLAccountType.Equity);

        var vm = new BalanceSheetReportViewModel
        {
            AsOf = asOfDate,
            Assets = new BalanceSheetSectionViewModel { Title = "الأصول" },
            Liabilities = new BalanceSheetSectionViewModel { Title = "الخصوم" },
            Equity = new BalanceSheetSectionViewModel { Title = "حقوق الملكية" }
        };

        foreach (var line in activity)
        {
            var amount = line.Type == GLAccountType.Asset
                ? line.Debit - line.Credit
                : line.Credit - line.Debit;
            if (amount == 0) continue;

            var item = new BalanceSheetLineViewModel { Code = line.Code, Name = line.Name, Amount = amount };

            switch (line.Type)
            {
                case GLAccountType.Asset:
                    vm.Assets.Lines.Add(item);
                    vm.Assets.Total += amount;
                    break;
                case GLAccountType.Liability:
                    vm.Liabilities.Lines.Add(item);
                    vm.Liabilities.Total += amount;
                    break;
                case GLAccountType.Equity:
                    vm.Equity.Lines.Add(item);
                    vm.Equity.Total += amount;
                    break;
            }
        }

        // الأرباح المحتجزة المرحّلة من إقفالات السنوات السابقة تُعرض ضمن حقوق الملكية،
        // ويُقصر بند "صافي الدخل (الفترة)" على الفترات المفتوحة فقط حفاظًا على توازن الميزانية.
        var closedRetained = await ClosedRetainedEarningsAsync(asOfDate);
        if (closedRetained != 0m)
        {
            var retainedLine = vm.Equity.Lines.FirstOrDefault(l => l.Code == "3001");
            if (retainedLine is null)
            {
                vm.Equity.Lines.Add(new BalanceSheetLineViewModel { Code = "3001", Name = "الأرباح المحتجزة", Amount = closedRetained });
                vm.Equity.Lines.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));
            }
            else
            {
                retainedLine.Amount += closedRetained;
            }
            vm.Equity.Total += closedRetained;
        }

        vm.NetIncome = await NetIncomeAsOfAsync(asOfDate, closedRetained);

        return vm;
    }

    private async Task<decimal> ClosedRetainedEarningsAsync(DateTime asOfDate)
    {
        var retained = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => l.JournalEntry!.IsPosted
                && l.JournalEntry.Source == JournalSource.YearEndClose
                && l.JournalEntry.Date <= asOfDate
                && l.Account!.Code == "3001")
            .GroupBy(l => 1)
            .Select(g => new
            {
                Debit = g.Sum(l => (decimal?)l.Debit) ?? 0m,
                Credit = g.Sum(l => (decimal?)l.Credit) ?? 0m
            })
            .FirstOrDefaultAsync();

        return retained is null ? 0m : retained.Credit - retained.Debit;
    }

    private async Task<decimal> NetIncomeAsOfAsync(DateTime asOfDate, decimal closedRetained = 0m)
    {
        var activity = await GetAccountActivityAsync(null, asOfDate, GLAccountType.Revenue, GLAccountType.Expense);
        decimal revenue = 0, expense = 0;

        foreach (var line in activity)
        {
            var signed = line.NormalBalance == NormalBalance.Debit ? line.Debit - line.Credit : line.Credit - line.Debit;
            if (line.Type == GLAccountType.Revenue) revenue += signed;
            else expense += signed;
        }

        return revenue - expense - closedRetained;
    }

    public async Task<IReadOnlyList<PlAccountActivity>> GetYearlyPlActivityAsync(int year)
    {
        var activity = await GetAccountActivityAsync(new DateTime(year, 1, 1), new DateTime(year, 12, 31), GLAccountType.Revenue, GLAccountType.Expense);
        return activity.Select(a => new PlAccountActivity(a.Code, a.Name, a.Type, a.NormalBalance, a.Debit, a.Credit)).ToList();
    }

    public async Task<(decimal Debit, decimal Credit)> GetAccountYearlyActivityAsync(int accountId, int year)
    {
        var fromDate = new DateTime(year, 1, 1);
        var toDate = new DateTime(year, 12, 31);

        var entryIds = _db.JournalEntries
            .AsNoTracking()
            .Where(j => j.IsPosted && j.Source != JournalSource.YearEndClose && j.Date >= fromDate && j.Date <= toDate)
            .Select(j => j.Id);

        var result = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => entryIds.Contains(l.JournalEntryId) && l.AccountId == accountId)
            .GroupBy(l => 1)
            .Select(g => new { Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .FirstOrDefaultAsync();

        return (result?.Debit ?? 0m, result?.Credit ?? 0m);
    }

    private async Task<List<AccountActivity>> GetAccountActivityAsync(DateTime? from, DateTime to, params GLAccountType[] types)
    {
        var query = _db.GLAccounts
            .AsNoTracking()
            .Where(a => types.Contains(a.Type));

        var accounts = await query.OrderBy(a => a.Code).ToListAsync();

        var entryQuery = _db.JournalEntries.AsNoTracking().Where(j => j.IsPosted && j.Source != JournalSource.YearEndClose && j.Date <= to);
        if (from is not null)
        {
            var fromDate = from.Value.Date;
            entryQuery = entryQuery.Where(j => j.Date >= fromDate);
        }
        var entryIds = entryQuery.Select(j => j.Id);

        var activity = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => entryIds.Contains(l.JournalEntryId))
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(l => (decimal?)l.Debit) ?? 0m, Credit = g.Sum(l => (decimal?)l.Credit) ?? 0m })
            .ToListAsync();

        var byAccount = activity.ToDictionary(x => x.AccountId, x => x);

        var result = new List<AccountActivity>();
        foreach (var a in accounts)
        {
            byAccount.TryGetValue(a.Id, out var act);
            if (!a.IsActive && (act?.Debit ?? 0m) == 0m && (act?.Credit ?? 0m) == 0m) continue;
            result.Add(new AccountActivity(a.Code, a.Name, a.Type, a.NormalBalance, act?.Debit ?? 0m, act?.Credit ?? 0m));
        }

        return result;
    }

    private readonly record struct AccountActivity(string Code, string Name, GLAccountType Type, NormalBalance NormalBalance, decimal Debit, decimal Credit);
}
