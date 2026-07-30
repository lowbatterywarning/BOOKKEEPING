using Bookkeeping.Core.Enums;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Bookkeeping.Services;
using Microsoft.EntityFrameworkCore;

namespace Bookkeeping.Tests;

public class IntegrationTests : IDisposable
{
    private readonly AppDbContext _db;

    public IntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private async Task SaveAsync() => await _db.SaveChangesAsync();

    [Fact]
    public async Task Schema_SponsorTargetsTable_Exists()
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SponsorTargets'";
        Assert.NotNull(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Schema_IncomeAccountId_OnDonationCategories()
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(DonationCategories)";
        var columns = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        Assert.Contains("IncomeAccountId", columns);
    }

    [Fact]
    public async Task SeedData_HasUser_Admin()
    {
        Assert.True(await _db.Users.AnyAsync(u => u.Username == "admin"));
    }

    [Fact]
    public async Task SeedData_HasAccounts_CashAndBank()
    {
        Assert.True(await _db.Accounts.AnyAsync(a => a.Code == "1000"));
        Assert.True(await _db.Accounts.AnyAsync(a => a.Code == "1010"));
        Assert.True(await _db.Accounts.AnyAsync(a => a.Code == "3000"));
    }

    [Fact]
    public async Task CanAdd_SponsorTarget_EndToEnd()
    {
        // Save fund first to get its ID
        var zekatFund = new Fund { Name = "Zekat Fund", IsRestricted = true };
        _db.Funds.Add(zekatFund);
        await SaveAsync();

        var incAcct = new Account { Code = "4001", Name = "Income - Zekat", AccountType = AccountType.Income, FundId = zekatFund.Id };
        _db.Accounts.Add(incAcct);
        await SaveAsync();

        var cat = new DonationCategory { Name = "Zekat", FundId = zekatFund.Id, IncomeAccount = incAcct };
        _db.DonationCategories.Add(cat);
        var sponsor = new Sponsor { Name = "Donor" };
        _db.Sponsors.Add(sponsor);
        await SaveAsync();

        _db.SponsorTargets.Add(new SponsorTarget { SponsorId = sponsor.Id, DonationCategoryId = cat.Id, TargetAmount = 500 });
        await SaveAsync();

        var target = await _db.SponsorTargets.FirstAsync(t => t.SponsorId == sponsor.Id);
        Assert.Equal(500m, target.TargetAmount);
    }

    [Fact]
    public async Task FullFlow_Donate_Expense_Transfer_VerifyBalances()
    {
        var engine = new JournalEngine(_db);

        // Save fund first to get its ID
        var fund = new Fund { Name = "Test Fund", IsRestricted = false };
        _db.Funds.Add(fund);
        await SaveAsync();

        var incAcct = new Account { Code = "4001", Name = "Income", AccountType = AccountType.Income, FundId = fund.Id };
        var expAcct = new Account { Code = "5001", Name = "Expense", AccountType = AccountType.Expense, FundId = fund.Id };
        _db.Accounts.AddRange(incAcct, expAcct);
        await SaveAsync();

        var dCat = new DonationCategory { Name = "Giving", FundId = fund.Id, IncomeAccount = incAcct };
        var eCat = new ExpenseCategory { Name = "Costs", FundId = fund.Id, ExpenseAccount = expAcct };
        _db.DonationCategories.Add(dCat);
        _db.ExpenseCategories.Add(eCat);
        var sponsor = new Sponsor { Name = "Donor" };
        _db.Sponsors.Add(sponsor);
        await SaveAsync();

        // Beginning balance $10,000 cash
        await engine.RecordBeginningBalanceAsync("1000", 10000m, 1);
        await SaveAsync();

        // Donate $3,000
        await engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.Today, SponsorId = sponsor.Id, PaymentMethod = PaymentMethod.Cash,
            DonationCategoryId = dCat.Id, Amount = 3000m, CreatedByUserId = 1
        });
        await SaveAsync();

        // Expense $1,000
        await engine.RecordExpenseAsync(new Expense
        {
            Date = DateTime.Today, VendorName = "Vendor", PaymentMethod = PaymentMethod.Cash,
            ExpenseCategoryId = eCat.Id, Amount = 1000m, CreatedByUserId = 1
        });
        await SaveAsync();

        // Transfer $2,000 to bank
        await engine.RecordTransferAsync(new CashBankTransfer
        {
            Date = DateTime.Today, Direction = "CashToBank", Amount = 2000m, CreatedByUserId = 1
        });
        await SaveAsync();

        // Verify
        var cashBal = await engine.GetAccountBalanceAsync(1);
        var bankBal = await engine.GetAccountBalanceAsync(2);
        var fundBal = await engine.GetFundBalanceAsync(fund.Id);

        Assert.Equal(10000m, cashBal);   // 10000 + 3000 - 1000 - 2000
        Assert.Equal(2000m, bankBal);     // 0 + 2000
        Assert.Equal(2000m, fundBal);     // 3000 income - 1000 expense (opening balance went to General Fund)

        // Ledger must balance
        var totals = await _db.JournalEntryLines
            .GroupBy(l => 1)
            .Select(g => new { Debits = g.Sum(l => l.DebitAmount), Credits = g.Sum(l => l.CreditAmount) })
            .FirstAsync();
        Assert.Equal(totals.Debits, totals.Credits);
    }
}
