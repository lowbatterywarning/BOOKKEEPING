using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;

namespace Bookkeeping.Wpf.Services;

/// <summary>
/// Generates report data for all report types.
/// </summary>
public class ReportService
{
    private readonly AppDbContext _db;

    public ReportService(AppDbContext db)
    {
        _db = db;
    }

    // ---- Donation Report ----
    public async Task<List<DonationReportRow>> GetDonationReportAsync(DateTime? from, DateTime? to, int? sponsorId, int? categoryId, string? paymentMethod)
    {
        var query = _db.Donations
            .Include(d => d.Sponsor)
            .Include(d => d.DonationCategory)
            .Include(d => d.Program)
            .AsQueryable();

        if (from.HasValue) query = query.Where(d => d.Date >= from.Value);
        if (to.HasValue) query = query.Where(d => d.Date <= to.Value);
        if (sponsorId.HasValue) query = query.Where(d => d.SponsorId == sponsorId.Value);
        if (categoryId.HasValue) query = query.Where(d => d.DonationCategoryId == categoryId.Value);
        if (!string.IsNullOrEmpty(paymentMethod)) query = query.Where(d => d.PaymentMethod.ToString() == paymentMethod);

        return await query.OrderBy(d => d.Date).Select(d => new DonationReportRow
        {
            Date = d.Date,
            Sponsor = d.Sponsor.Name,
            Category = d.DonationCategory.Name,
            PaymentMethod = d.PaymentMethod.ToString(),
            Program = d.Program != null ? d.Program.Name : "",
            Amount = d.Amount,
            ReceiptNumber = d.ReceiptNumber ?? ""
        }).ToListAsync();
    }

    // ---- Expense Report ----
    public async Task<List<ExpenseReportRow>> GetExpenseReportAsync(DateTime? from, DateTime? to, int? categoryId, int? programId, string? paymentMethod)
    {
        var query = _db.Expenses
            .Include(e => e.ExpenseCategory)
            .Include(e => e.Program)
            .AsQueryable();

        if (from.HasValue) query = query.Where(e => e.Date >= from.Value);
        if (to.HasValue) query = query.Where(e => e.Date <= to.Value);
        if (categoryId.HasValue) query = query.Where(e => e.ExpenseCategoryId == categoryId.Value);
        if (programId.HasValue) query = query.Where(e => e.ProgramId == programId.Value);
        if (!string.IsNullOrEmpty(paymentMethod)) query = query.Where(e => e.PaymentMethod.ToString() == paymentMethod);

        return await query.OrderBy(e => e.Date).Select(e => new ExpenseReportRow
        {
            Date = e.Date,
            Vendor = e.VendorName,
            Category = e.ExpenseCategory.Name,
            PaymentMethod = e.PaymentMethod.ToString(),
            Program = e.Program != null ? e.Program.Name : "",
            Amount = e.Amount,
            Notes = e.Notes ?? ""
        }).ToListAsync();
    }

    // ---- Program Report ----
    public async Task<List<ProgramReportRow>> GetProgramReportAsync(DateTime? from, DateTime? to)
    {
        var programs = await _db.Programs.OrderBy(p => p.Name).ToListAsync();
        var programIds = programs.Select(p => p.Id).ToList();

        // Single grouped query for income
        var incomeQuery = _db.Donations.Where(d => d.ProgramId.HasValue && programIds.Contains(d.ProgramId.Value));
        if (from.HasValue) incomeQuery = incomeQuery.Where(d => d.Date >= from.Value);
        if (to.HasValue) incomeQuery = incomeQuery.Where(d => d.Date <= to.Value);
        var incomeByProgram = await incomeQuery
            .GroupBy(d => d.ProgramId!.Value)
            .Select(g => new { ProgramId = g.Key, Total = g.Sum(d => d.Amount) })
            .ToListAsync();

        // Single grouped query for expenses
        var expenseQuery = _db.Expenses.Where(e => e.ProgramId.HasValue && programIds.Contains(e.ProgramId.Value));
        if (from.HasValue) expenseQuery = expenseQuery.Where(e => e.Date >= from.Value);
        if (to.HasValue) expenseQuery = expenseQuery.Where(e => e.Date <= to.Value);
        var expensesByProgram = await expenseQuery
            .GroupBy(e => e.ProgramId!.Value)
            .Select(g => new { ProgramId = g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync();

        var incomeDict = incomeByProgram.ToDictionary(x => x.ProgramId, x => x.Total);
        var expenseDict = expensesByProgram.ToDictionary(x => x.ProgramId, x => x.Total);

        return programs.Select(p => new ProgramReportRow
        {
            Program = p.Name,
            Income = incomeDict.GetValueOrDefault(p.Id, 0),
            Expenses = expenseDict.GetValueOrDefault(p.Id, 0),
            Balance = incomeDict.GetValueOrDefault(p.Id, 0) - expenseDict.GetValueOrDefault(p.Id, 0)
        }).ToList();
    }

    // ---- Monthly Summary Report ----
    public async Task<List<MonthlyReportRow>> GetMonthlyReportAsync(int year)
    {
        var result = new List<MonthlyReportRow>();

        var cashAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1000");
        var bankAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1010");

        // Fetch all monthly income/expenses in a single set of queries
        var monthlyIncome = await _db.Donations
            .Where(d => d.Date.Year == year)
            .GroupBy(d => d.Date.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(d => d.Amount) })
            .ToListAsync();

        var monthlyExpenses = await _db.Expenses
            .Where(e => e.Date.Year == year)
            .GroupBy(e => e.Date.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync();

        // Pre-compute running cash/bank balances by fetching all lines up to year-end
        // then computing cumulative balances per month in memory
        Dictionary<int, decimal> cashBalByMonth = new();
        Dictionary<int, decimal> bankBalByMonth = new();

        if (cashAccount != null)
        {
            var cashLines = await _db.JournalEntryLines
                .Where(l => l.AccountId == cashAccount.Id && l.JournalEntry.Date.Year <= year)
                .Select(l => new { l.DebitAmount, l.CreditAmount, Month = l.JournalEntry.Date.Month, Year = l.JournalEntry.Date.Year })
                .ToListAsync();

            // Group by (Year, Month) for cumulative balance
            var cashMonthly = cashLines
                .GroupBy(l => new { l.Year, l.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new { g.Key.Year, g.Key.Month, Net = g.Sum(l => l.DebitAmount - l.CreditAmount) })
                .ToList();

            decimal runningCash = 0;
            foreach (var cm in cashMonthly)
            {
                runningCash += cm.Net;
                if (cm.Year == year)
                    cashBalByMonth[cm.Month] = runningCash;
            }
        }

        if (bankAccount != null)
        {
            var bankLines = await _db.JournalEntryLines
                .Where(l => l.AccountId == bankAccount.Id && l.JournalEntry.Date.Year <= year)
                .Select(l => new { l.DebitAmount, l.CreditAmount, Month = l.JournalEntry.Date.Month, Year = l.JournalEntry.Date.Year })
                .ToListAsync();

            var bankMonthly = bankLines
                .GroupBy(l => new { l.Year, l.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new { g.Key.Year, g.Key.Month, Net = g.Sum(l => l.DebitAmount - l.CreditAmount) })
                .ToList();

            decimal runningBank = 0;
            foreach (var bm in bankMonthly)
            {
                runningBank += bm.Net;
                if (bm.Year == year)
                    bankBalByMonth[bm.Month] = runningBank;
            }
        }

        for (int month = 1; month <= 12; month++)
        {
            var income = monthlyIncome.FirstOrDefault(m => m.Month == month)?.Total ?? 0;
            var expenses = monthlyExpenses.FirstOrDefault(m => m.Month == month)?.Total ?? 0;
            cashBalByMonth.TryGetValue(month, out var cashBal);
            bankBalByMonth.TryGetValue(month, out var bankBal);

            result.Add(new MonthlyReportRow
            {
                Month = new DateTime(year, month, 1).ToString("MMMM"),
                Income = income,
                Expenses = expenses,
                Net = income - expenses,
                CashBalance = cashBal,
                BankBalance = bankBal
            });
        }
        return result;
    }

    // ---- Annual Report ----
    public async Task<AnnualReportData> GetAnnualReportAsync(int year)
    {
        var from = new DateTime(year, 1, 1);
        var to = new DateTime(year, 12, 31);

        var totalIncome = await _db.Donations.Where(d => d.Date >= from && d.Date <= to).SumAsync(d => d.Amount);
        var totalExpenses = await _db.Expenses.Where(e => e.Date >= from && e.Date <= to).SumAsync(e => e.Amount);

        // Income by category
        var incomeByCategory = await _db.Donations
            .Where(d => d.Date >= from && d.Date <= to)
            .GroupBy(d => d.DonationCategory.Name)
            .Select(g => new CategorySummary { Category = g.Key, Amount = g.Sum(d => d.Amount) })
            .OrderByDescending(c => c.Amount).ToListAsync();

        // Expenses by category
        var expensesByCategory = await _db.Expenses
            .Where(e => e.Date >= from && e.Date <= to)
            .GroupBy(e => e.ExpenseCategory.Name)
            .Select(g => new CategorySummary { Category = g.Key, Amount = g.Sum(e => e.Amount) })
            .OrderByDescending(c => c.Amount).ToListAsync();

        // Cash/Bank year-end balances (single query per account with combined sum)
        var cashAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1000");
        var bankAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1010");
        decimal cashBal = 0, bankBal = 0;

        if (cashAccount != null)
        {
            var cashTotals = await _db.JournalEntryLines
                .Where(l => l.AccountId == cashAccount.Id && l.JournalEntry.Date <= to)
                .GroupBy(l => 1)
                .Select(g => new { Debits = g.Sum(l => l.DebitAmount), Credits = g.Sum(l => l.CreditAmount) })
                .FirstOrDefaultAsync();
            cashBal = (cashTotals?.Debits ?? 0) - (cashTotals?.Credits ?? 0);
        }

        if (bankAccount != null)
        {
            var bankTotals = await _db.JournalEntryLines
                .Where(l => l.AccountId == bankAccount.Id && l.JournalEntry.Date <= to)
                .GroupBy(l => 1)
                .Select(g => new { Debits = g.Sum(l => l.DebitAmount), Credits = g.Sum(l => l.CreditAmount) })
                .FirstOrDefaultAsync();
            bankBal = (bankTotals?.Debits ?? 0) - (bankTotals?.Credits ?? 0);
        }

        return new AnnualReportData
        {
            Year = year,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            Net = totalIncome - totalExpenses,
            IncomeByCategory = incomeByCategory,
            ExpensesByCategory = expensesByCategory,
            CashBalance = cashBal,
            BankBalance = bankBal
        };
    }

    // ---- Fund Report (Restricted vs Unrestricted) ----
    public async Task<List<FundReportRow>> GetFundReportAsync()
    {
        var funds = await _db.Funds.OrderBy(f => f.Name).ToListAsync();
        var fundIds = funds.Select(f => f.Id).ToList();

        // Single grouped query for income
        var incomeByFund = await _db.JournalEntryLines
            .Where(l => l.FundId.HasValue && fundIds.Contains(l.FundId.Value) && l.Account.AccountType == Core.Enums.AccountType.Income)
            .GroupBy(l => l.FundId!.Value)
            .Select(g => new { FundId = g.Key, Total = g.Sum(l => l.CreditAmount) })
            .ToListAsync();

        // Single grouped query for expenses
        var expensesByFund = await _db.JournalEntryLines
            .Where(l => l.FundId.HasValue && fundIds.Contains(l.FundId.Value) && l.Account.AccountType == Core.Enums.AccountType.Expense)
            .GroupBy(l => l.FundId!.Value)
            .Select(g => new { FundId = g.Key, Total = g.Sum(l => l.DebitAmount) })
            .ToListAsync();

        var incomeDict = incomeByFund.ToDictionary(x => x.FundId, x => x.Total);
        var expenseDict = expensesByFund.ToDictionary(x => x.FundId, x => x.Total);

        return funds.Select(fund => new FundReportRow
        {
            FundName = fund.Name,
            IsRestricted = fund.IsRestricted,
            Income = incomeDict.GetValueOrDefault(fund.Id, 0),
            Expenses = expenseDict.GetValueOrDefault(fund.Id, 0),
            NetBalance = incomeDict.GetValueOrDefault(fund.Id, 0) - expenseDict.GetValueOrDefault(fund.Id, 0)
        }).ToList();
    }

    // ---- Sponsor Report ----
    public async Task<List<SponsorReportRow>> GetSponsorReportAsync(int sponsorId)
    {
        return await _db.Donations
            .Where(d => d.SponsorId == sponsorId)
            .Include(d => d.DonationCategory)
            .OrderBy(d => d.Date)
            .Select(d => new SponsorReportRow
            {
                Date = d.Date,
                Category = d.DonationCategory.Name,
                Amount = d.Amount,
                ReceiptNumber = d.ReceiptNumber ?? ""
            }).ToListAsync();
    }

    // ---- Statement of Activities (Non-Profit P&L) ----
    public async Task<IncomeStatementData> GetIncomeStatementAsync(int year)
    {
        var from = new DateTime(year, 1, 1);
        var to = new DateTime(year, 12, 31);

        // Revenue by category — top N + "Other" for small items
        var allRevenue = await _db.Donations
            .Where(d => d.Date >= from && d.Date <= to)
            .GroupBy(d => d.DonationCategory.Name)
            .Select(g => new CategorySummary { Category = g.Key, Amount = g.Sum(d => d.Amount) })
            .OrderByDescending(c => c.Amount).ToListAsync();

        var revenue = CollapseSmallCategories(allRevenue, 8);
        var totalRevenue = allRevenue.Sum(r => r.Amount);

        // Expenses by category — top N + "Other" for small items
        var allExpenses = await _db.Expenses
            .Where(e => e.Date >= from && e.Date <= to)
            .GroupBy(e => e.ExpenseCategory.Name)
            .Select(g => new CategorySummary { Category = g.Key, Amount = g.Sum(e => e.Amount) })
            .OrderByDescending(c => c.Amount).ToListAsync();

        var expenses = CollapseSmallCategories(allExpenses, 8);
        var totalExpenses = allExpenses.Sum(e => e.Amount);

        // Revenue by fund (restricted vs unrestricted)
        var revenueByFund = await _db.Donations
            .Where(d => d.Date >= from && d.Date <= to)
            .GroupBy(d => d.DonationCategory.Fund.Name)
            .Select(g => new CategorySummary { Category = g.Key, Amount = g.Sum(d => d.Amount) })
            .OrderByDescending(c => c.Amount).ToListAsync();

        // Expenses by fund
        var expensesByFund = await _db.Expenses
            .Where(e => e.Date >= from && e.Date <= to)
            .GroupBy(e => e.ExpenseCategory.Fund != null ? e.ExpenseCategory.Fund.Name : "General Fund")
            .Select(g => new CategorySummary { Category = g.Key, Amount = g.Sum(e => e.Amount) })
            .OrderByDescending(c => c.Amount).ToListAsync();

        // Monthly breakdown
        var monthlyRevenue = await _db.Donations
            .Where(d => d.Date.Year == year)
            .GroupBy(d => d.Date.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(d => d.Amount) })
            .ToListAsync();

        var monthlyExpenses = await _db.Expenses
            .Where(e => e.Date.Year == year)
            .GroupBy(e => e.Date.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync();

        var monthlyRows = new List<IncomeStatementMonthlyRow>();
        for (int month = 1; month <= 12; month++)
        {
            monthlyRows.Add(new IncomeStatementMonthlyRow
            {
                Month = new DateTime(year, month, 1).ToString("MMMM"),
                Revenue = monthlyRevenue.FirstOrDefault(r => r.Month == month)?.Total ?? 0,
                Expenses = monthlyExpenses.FirstOrDefault(e => e.Month == month)?.Total ?? 0,
                Net = (monthlyRevenue.FirstOrDefault(r => r.Month == month)?.Total ?? 0)
                    - (monthlyExpenses.FirstOrDefault(e => e.Month == month)?.Total ?? 0)
            });
        }

        return new IncomeStatementData
        {
            Year = year,
            TotalRevenue = totalRevenue,
            TotalExpenses = totalExpenses,
            NetIncome = totalRevenue - totalExpenses,
            RevenueByCategory = revenue,
            ExpensesByCategory = expenses,
            RevenueByFund = revenueByFund,
            ExpensesByFund = expensesByFund,
            MonthlyBreakdown = monthlyRows
        };
    }
    /// <summary>
    /// Groups categories beyond the topN into a single "Other" line.
    /// </summary>
    private static List<CategorySummary> CollapseSmallCategories(List<CategorySummary> all, int topN)
    {
        if (all.Count <= topN) return all;

        var top = all.Take(topN).ToList();
        var otherTotal = all.Skip(topN).Sum(c => c.Amount);
        if (otherTotal > 0)
            top.Add(new CategorySummary { Category = "Other", Amount = otherTotal });
        return top;
    }
}

// ---- Report Row Models ----

public class DonationReportRow
{
    public DateTime Date { get; set; }
    public string Sponsor { get; set; } = "";
    public string Category { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string Program { get; set; } = "";
    public decimal Amount { get; set; }
    public string ReceiptNumber { get; set; } = "";
}

public class ExpenseReportRow
{
    public DateTime Date { get; set; }
    public string Vendor { get; set; } = "";
    public string Category { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string Program { get; set; } = "";
    public decimal Amount { get; set; }
    public string Notes { get; set; } = "";
}

public class ProgramReportRow
{
    public string Program { get; set; } = "";
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Balance { get; set; }
}

public class MonthlyReportRow
{
    public string Month { get; set; } = "";
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net { get; set; }
    public decimal CashBalance { get; set; }
    public decimal BankBalance { get; set; }
}

public class AnnualReportData
{
    public int Year { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal Net { get; set; }
    public decimal CashBalance { get; set; }
    public decimal BankBalance { get; set; }
    public List<CategorySummary> IncomeByCategory { get; set; } = new();
    public List<CategorySummary> ExpensesByCategory { get; set; } = new();
}

public class CategorySummary
{
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
}

public class FundReportRow
{
    public string FundName { get; set; } = "";
    public bool IsRestricted { get; set; }
    public string TypeDisplay => IsRestricted ? "Restricted" : "Unrestricted";
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetBalance { get; set; }
}

public class SponsorReportRow
{
    public DateTime Date { get; set; }
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public string ReceiptNumber { get; set; } = "";
}

public class IncomeStatementData
{
    public int Year { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetIncome { get; set; }
    public List<CategorySummary> RevenueByCategory { get; set; } = new();
    public List<CategorySummary> ExpensesByCategory { get; set; } = new();
    public List<CategorySummary> RevenueByFund { get; set; } = new();
    public List<CategorySummary> ExpensesByFund { get; set; } = new();
    public List<IncomeStatementMonthlyRow> MonthlyBreakdown { get; set; } = new();
}

public class IncomeStatementMonthlyRow
{
    public string Month { get; set; } = "";
    public decimal Revenue { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net { get; set; }
}
