using Bookkeeping.Core.Enums;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Bookkeeping.Services;
using Microsoft.EntityFrameworkCore;

namespace Bookkeeping.Tests;

/// <summary>
/// Unit tests for the double-entry journal engine.
/// These tests prove the ledger always balances — the most critical invariant in the system.
///
/// NOTE: The JournalEngine no longer calls SaveChangesAsync internally.
/// Tests must call SaveAsync() to persist changes before querying balances.
/// </summary>
public class JournalEngineTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly JournalEngine _engine;

    public JournalEngineTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _engine = new JournalEngine(_db);

        // Seed test data
        SeedTestDataAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    /// <summary>
    /// Helper to persist pending changes after engine operations.
    /// </summary>
    private async Task SaveAsync()
    {
        await _db.SaveChangesAsync();
    }

    private async Task SeedTestDataAsync()
    {
        // Clear existing seed data first to avoid unique constraint collisions
        // Order matters: remove dependents before principals to respect FK constraints
        _db.AppSettings.RemoveRange(_db.AppSettings);
        _db.Budgets.RemoveRange(_db.Budgets);
        _db.CashBankTransfers.RemoveRange(_db.CashBankTransfers);
        _db.Donations.RemoveRange(_db.Donations);
        _db.Expenses.RemoveRange(_db.Expenses);
        _db.JournalEntryLines.RemoveRange(_db.JournalEntryLines);
        _db.JournalEntries.RemoveRange(_db.JournalEntries);
        _db.Accounts.RemoveRange(_db.Accounts);
        _db.Programs.RemoveRange(_db.Programs);
        _db.Funds.RemoveRange(_db.Funds);
        _db.Users.RemoveRange(_db.Users);
        await _db.SaveChangesAsync();

        // Test user (needed for CreatedByUserId foreign keys)
        _db.Users.Add(new User { Id = 1, Username = "testuser", PasswordHash = "hash", FullName = "Test User", Role = UserRole.Administrator });
        await _db.SaveChangesAsync();

        // Funds
        var generalFund = new Fund { Id = 10, Name = "General Fund", IsRestricted = false, IsActive = true };
        var zekatFund = new Fund { Id = 11, Name = "Zekat Fund", IsRestricted = true, IsActive = true };
        _db.Funds.AddRange(generalFund, zekatFund);

        // Accounts
        _db.Accounts.AddRange(
            new Account { Id = 100, Code = "1000", Name = "Cash", AccountType = AccountType.Asset, IsSystem = true },
            new Account { Id = 101, Code = "1010", Name = "Bank", AccountType = AccountType.Asset, IsSystem = true },
            new Account { Id = 102, Code = "3000", Name = "Unrestricted Net Assets", AccountType = AccountType.Equity, FundId = 10, IsSystem = true },
            new Account { Id = 103, Code = "3010", Name = "Restricted Net Assets - Zekat", AccountType = AccountType.Equity, FundId = 11, IsSystem = true },
            new Account { Id = 104, Code = "4001", Name = "Donation Income - Himmet", AccountType = AccountType.Income, FundId = 10 },
            new Account { Id = 105, Code = "4002", Name = "Donation Income - Zekat", AccountType = AccountType.Income, FundId = 11 },
            new Account { Id = 106, Code = "5001", Name = "Expense - Rent", AccountType = AccountType.Expense }
        );

        // Programs (income/expense tracking)
        _db.Programs.AddRange(
            new OrgProgram { Id = 20, Name = "Himmet", FundId = 10 },
            new OrgProgram { Id = 21, Name = "Zekat", FundId = 11 },
            new OrgProgram { Id = 30, Name = "Rent", FundId = 10 }
        );

        // Sponsor
        _db.Sponsors.Add(new Sponsor { Id = 40, Name = "Test Donor" });

        await _db.SaveChangesAsync();

        // Link income accounts to programs
        var himmetProg = await _db.Programs.FindAsync(20);
        himmetProg!.IncomeAccount = await _db.Accounts.FindAsync(104);
        var zekatProg = await _db.Programs.FindAsync(21);
        zekatProg!.IncomeAccount = await _db.Accounts.FindAsync(105);

        // Link expense account to program
        var rentProg = await _db.Programs.FindAsync(30);
        rentProg!.ExpenseAccount = await _db.Accounts.FindAsync(106);

        await _db.SaveChangesAsync();
    }

    // ==================== Balancing Tests ====================

    [Fact]
    public void IsBalanced_EqualDebitsAndCredits_ReturnsTrue()
    {
        var entry = new JournalEntry
        {
            Date = DateTime.UtcNow,
            Description = "Test",
            CreatedByUserId = 1
        };
        entry.Lines.Add(new JournalEntryLine { AccountId = 100, DebitAmount = 100, CreditAmount = 0 });
        entry.Lines.Add(new JournalEntryLine { AccountId = 104, DebitAmount = 0, CreditAmount = 100 });

        Assert.True(_engine.IsBalanced(entry));
    }

    [Fact]
    public void IsBalanced_UnequalDebitsAndCredits_ReturnsFalse()
    {
        var entry = new JournalEntry
        {
            Date = DateTime.UtcNow,
            Description = "Test",
            CreatedByUserId = 1
        };
        entry.Lines.Add(new JournalEntryLine { AccountId = 100, DebitAmount = 100, CreditAmount = 0 });
        entry.Lines.Add(new JournalEntryLine { AccountId = 104, DebitAmount = 0, CreditAmount = 50 });

        Assert.False(_engine.IsBalanced(entry));
    }

    [Fact]
    public void IsBalanced_ZeroAmount_ReturnsFalse()
    {
        var entry = new JournalEntry
        {
            Date = DateTime.UtcNow,
            Description = "Test",
            CreatedByUserId = 1
        };
        entry.Lines.Add(new JournalEntryLine { AccountId = 100, DebitAmount = 0, CreditAmount = 0 });
        entry.Lines.Add(new JournalEntryLine { AccountId = 104, DebitAmount = 0, CreditAmount = 0 });

        Assert.False(_engine.IsBalanced(entry));
    }

    // ==================== Donation Tests ====================

    [Fact]
    public async Task RecordDonation_CreatesBalancedJournalEntry()
    {
        var donation = new Donation
        {
            Date = DateTime.UtcNow,
            SponsorId = 40,
            PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20, // Himmet (unrestricted)
            Amount = 500m,
            ReceiptNumber = "RCPT-001",
            CreatedByUserId = 1
        };

        var entry = await _engine.RecordDonationAsync(donation);
        await SaveAsync();

        Assert.True(_engine.IsBalanced(entry));
        Assert.Equal(2, entry.Lines.Count);

        // Verify: Debit Cash $500, Credit Income $500
        var debitLine = entry.Lines.First(l => l.DebitAmount > 0);
        var creditLine = entry.Lines.First(l => l.CreditAmount > 0);

        Assert.Equal(500m, debitLine.DebitAmount);
        Assert.Equal(500m, creditLine.CreditAmount);
        Assert.Equal(100, debitLine.AccountId); // Cash account
        Assert.Equal(104, creditLine.AccountId); // Himmet income account
    }

    [Fact]
    public async Task RecordDonation_IncludesSponsorNameInDescription()
    {
        var donation = new Donation
        {
            Date = DateTime.UtcNow,
            SponsorId = 40,
            PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20,
            Amount = 100m,
            CreatedByUserId = 1
        };

        var entry = await _engine.RecordDonationAsync(donation);
        await SaveAsync();

        // Description should contain the sponsor's name, not just the ID
        Assert.Contains("Test Donor", entry.Description);
        Assert.DoesNotContain("from 40", entry.Description);
    }

    [Fact]
    public async Task RecordDonation_RestrictedFund_TagsFundCorrectly()
    {
        var donation = new Donation
        {
            Date = DateTime.UtcNow,
            SponsorId = 40,
            PaymentMethod = PaymentMethod.Bank,
            ProgramId = 21, // Zekat (restricted)
            Amount = 1000m,
            ReceiptNumber = "RCPT-002",
            CreatedByUserId = 1
        };

        var entry = await _engine.RecordDonationAsync(donation);
        await SaveAsync();

        // Both lines should be tagged with the Zekat fund (11)
        Assert.All(entry.Lines, line => Assert.Equal(11, line.FundId));
    }

    [Fact]
    public async Task RecordDonation_BankPayment_UsesBankAccount()
    {
        var donation = new Donation
        {
            Date = DateTime.UtcNow,
            SponsorId = 40,
            PaymentMethod = PaymentMethod.Bank,
            ProgramId = 20,
            Amount = 250m,
            CreatedByUserId = 1
        };

        var entry = await _engine.RecordDonationAsync(donation);
        await SaveAsync();

        var debitLine = entry.Lines.First(l => l.DebitAmount > 0);
        Assert.Equal(101, debitLine.AccountId); // Bank account
    }

    [Fact]
    public async Task RecordDonation_ZeroAmount_ThrowsException()
    {
        var donation = new Donation
        {
            Date = DateTime.UtcNow,
            SponsorId = 40,
            PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20,
            Amount = 0m,
            CreatedByUserId = 1
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _engine.RecordDonationAsync(donation));
    }

    // ==================== Expense Tests ====================

    [Fact]
    public async Task RecordExpense_WithoutBalance_Succeeds()
    {
        // No donations recorded yet, so cash balance is $0 — transaction should still proceed
        var expense = new Expense
        {
            Date = DateTime.UtcNow,
            VendorName = "Test Vendor",
            PaymentMethod = PaymentMethod.Cash,
            ProgramId = 30,
            Amount = 200m,
            CreatedByUserId = 1
        };

        var entry = await _engine.RecordExpenseAsync(expense);
        await SaveAsync();

        Assert.True(_engine.IsBalanced(entry));
        Assert.Equal(2, entry.Lines.Count);

        // Cash balance should go negative
        var cashBalance = await _engine.GetAccountBalanceAsync(100);
        Assert.Equal(-200m, cashBalance);
    }

    [Fact]
    public async Task RecordExpense_CreatesBalancedJournalEntry()
    {
        // First add funds via donation
        await _engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.UtcNow, SponsorId = 40, PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20, Amount = 500m, CreatedByUserId = 1
        });
        await SaveAsync();

        var expense = new Expense
        {
            Date = DateTime.UtcNow,
            VendorName = "Test Vendor",
            PaymentMethod = PaymentMethod.Cash,
            ProgramId = 30, // Rent
            Amount = 200m,
            CreatedByUserId = 1
        };

        var entry = await _engine.RecordExpenseAsync(expense);
        await SaveAsync();

        Assert.True(_engine.IsBalanced(entry));
        Assert.Equal(2, entry.Lines.Count);

        // Debit Expense, Credit Cash
        var debitLine = entry.Lines.First(l => l.DebitAmount > 0);
        var creditLine = entry.Lines.First(l => l.CreditAmount > 0);

        Assert.Equal(200m, debitLine.DebitAmount);
        Assert.Equal(200m, creditLine.CreditAmount);
        Assert.Equal(106, debitLine.AccountId); // Rent expense
        Assert.Equal(100, creditLine.AccountId); // Cash
    }

    // ==================== Transfer Tests ====================

    [Fact]
    public async Task RecordTransfer_WithoutBalance_Succeeds()
    {
        var transfer = new CashBankTransfer
        {
            Date = DateTime.UtcNow,
            Direction = TransferDirection.CashToBank,
            Amount = 300m,
            CreatedByUserId = 1
        };

        var entry = await _engine.RecordTransferAsync(transfer);
        await SaveAsync();

        Assert.True(_engine.IsBalanced(entry));

        // Cash should go negative (no funds added first)
        var cashBalance = await _engine.GetAccountBalanceAsync(100);
        Assert.Equal(-300m, cashBalance);
    }

    [Fact]
    public async Task RecordTransfer_CashToBank_CreatesBalancedEntry()
    {
        // Add funds first
        await _engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.UtcNow, SponsorId = 40, PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20, Amount = 1000m, CreatedByUserId = 1
        });
        await SaveAsync();

        var transfer = new CashBankTransfer
        {
            Date = DateTime.UtcNow,
            Direction = TransferDirection.CashToBank,
            Amount = 300m,
            CreatedByUserId = 1
        };

        var entry = await _engine.RecordTransferAsync(transfer);
        await SaveAsync();

        Assert.True(_engine.IsBalanced(entry));

        // Debit Bank, Credit Cash
        var debitLine = entry.Lines.First(l => l.DebitAmount > 0);
        var creditLine = entry.Lines.First(l => l.CreditAmount > 0);

        Assert.Equal(101, debitLine.AccountId); // Bank debited
        Assert.Equal(100, creditLine.AccountId); // Cash credited
        Assert.Equal(300m, debitLine.DebitAmount);
        Assert.Equal(300m, creditLine.CreditAmount);
    }

    [Fact]
    public async Task RecordTransfer_BankToCash_CreatesBalancedEntry()
    {
        // Add funds to bank first
        await _engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.UtcNow, SponsorId = 40, PaymentMethod = PaymentMethod.Bank,
            ProgramId = 20, Amount = 1000m, CreatedByUserId = 1
        });
        await SaveAsync();

        var transfer = new CashBankTransfer
        {
            Date = DateTime.UtcNow,
            Direction = TransferDirection.BankToCash,
            Amount = 150m,
            CreatedByUserId = 1
        };

        var entry = await _engine.RecordTransferAsync(transfer);
        await SaveAsync();

        Assert.True(_engine.IsBalanced(entry));

        // Debit Cash, Credit Bank
        var debitLine = entry.Lines.First(l => l.DebitAmount > 0);
        var creditLine = entry.Lines.First(l => l.CreditAmount > 0);

        Assert.Equal(100, debitLine.AccountId); // Cash debited
        Assert.Equal(101, creditLine.AccountId); // Bank credited
    }

    [Fact]
    public async Task RecordTransfer_InvalidDirection_ThrowsException()
    {
        var transfer = new CashBankTransfer
        {
            Date = DateTime.UtcNow,
            Direction = (TransferDirection)99,
            Amount = 100m,
            CreatedByUserId = 1
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _engine.RecordTransferAsync(transfer));
    }

    // ==================== Account Balance Tests ====================

    [Fact]
    public async Task GetAccountBalance_AfterDonation_ReflectsCorrectBalance()
    {
        var donation = new Donation
        {
            Date = DateTime.UtcNow,
            SponsorId = 40,
            PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20,
            Amount = 500m,
            CreatedByUserId = 1
        };
        await _engine.RecordDonationAsync(donation);
        await SaveAsync();

        // Cash (Asset) should show +500
        var cashBalance = await _engine.GetAccountBalanceAsync(100);
        Assert.Equal(500m, cashBalance);

        // Himmet Income should show +500
        var incomeBalance = await _engine.GetAccountBalanceAsync(104);
        Assert.Equal(500m, incomeBalance);
    }

    [Fact]
    public async Task GetAccountBalance_AfterMultipleTransactions_IsCorrect()
    {
        // Donation $500 cash
        await _engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.UtcNow, SponsorId = 40, PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20, Amount = 500m, CreatedByUserId = 1
        });
        await SaveAsync();

        // Expense $200 cash
        await _engine.RecordExpenseAsync(new Expense
        {
            Date = DateTime.UtcNow, VendorName = "Vendor", PaymentMethod = PaymentMethod.Cash,
            ProgramId = 30, Amount = 200m, CreatedByUserId = 1
        });
        await SaveAsync();

        // Cash should be 500 - 200 = 300
        var cashBalance = await _engine.GetAccountBalanceAsync(100);
        Assert.Equal(300m, cashBalance);

        // Income = 500
        var incomeBalance = await _engine.GetAccountBalanceAsync(104);
        Assert.Equal(500m, incomeBalance);

        // Expense = 200
        var expenseBalance = await _engine.GetAccountBalanceAsync(106);
        Assert.Equal(200m, expenseBalance);
    }

    [Fact]
    public async Task GetAccountBalance_AfterTransfer_UpdatesBothAccounts()
    {
        // First add $1000 to cash via donation
        await _engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.UtcNow, SponsorId = 40, PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20, Amount = 1000m, CreatedByUserId = 1
        });
        await SaveAsync();

        // Transfer $400 from Cash to Bank
        await _engine.RecordTransferAsync(new CashBankTransfer
        {
            Date = DateTime.UtcNow, Direction = TransferDirection.CashToBank, Amount = 400m, CreatedByUserId = 1
        });
        await SaveAsync();

        // Cash should be 1000 - 400 = 600
        var cashBalance = await _engine.GetAccountBalanceAsync(100);
        Assert.Equal(600m, cashBalance);

        // Bank should be 0 + 400 = 400
        var bankBalance = await _engine.GetAccountBalanceAsync(101);
        Assert.Equal(400m, bankBalance);
    }

    // ==================== Fund Balance Tests ====================

    [Fact]
    public async Task GetFundBalance_UnrestrictedFund_CalculatesCorrectly()
    {
        // Donation to unrestricted (Himmet) $1000
        await _engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.UtcNow, SponsorId = 40, PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20, Amount = 1000m, CreatedByUserId = 1
        });
        await SaveAsync();

        var fundBalance = await _engine.GetFundBalanceAsync(10); // General fund
        Assert.Equal(1000m, fundBalance);
    }

    [Fact]
    public async Task GetFundBalance_RestrictedFund_CalculatesCorrectly()
    {
        // Zekat donation $500
        await _engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.UtcNow, SponsorId = 40, PaymentMethod = PaymentMethod.Bank,
            ProgramId = 21, Amount = 500m, CreatedByUserId = 1
        });
        await SaveAsync();

        var zekatBalance = await _engine.GetFundBalanceAsync(11);
        Assert.Equal(500m, zekatBalance);

        // General fund should be unaffected
        var generalBalance = await _engine.GetFundBalanceAsync(10);
        Assert.Equal(0m, generalBalance);
    }

    // ==================== Multi-Line Entry Tests ====================

    [Fact]
    public void IsBalanced_MultiLineEntry_ReturnsTrue()
    {
        var entry = new JournalEntry
        {
            Date = DateTime.UtcNow,
            Description = "Split transaction",
            CreatedByUserId = 1
        };
        // Debit two accounts, credit one
        entry.Lines.Add(new JournalEntryLine { AccountId = 100, DebitAmount = 300, CreditAmount = 0 });
        entry.Lines.Add(new JournalEntryLine { AccountId = 101, DebitAmount = 200, CreditAmount = 0 });
        entry.Lines.Add(new JournalEntryLine { AccountId = 104, DebitAmount = 0, CreditAmount = 500 });

        Assert.True(_engine.IsBalanced(entry));
    }

    // ==================== Engine Validation Tests ====================

    [Fact]
    public async Task RecordExpense_ExceedingBalance_Succeeds()
    {
        // Add $100 to cash
        await _engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.UtcNow, SponsorId = 40, PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20, Amount = 100m, CreatedByUserId = 1
        });
        await SaveAsync();

        // Spend $200 — should succeed even though balance goes negative
        var expense = new Expense
        {
            Date = DateTime.UtcNow, VendorName = "Vendor", PaymentMethod = PaymentMethod.Cash,
            ProgramId = 30, Amount = 200m, CreatedByUserId = 1
        };

        var entry = await _engine.RecordExpenseAsync(expense);
        await SaveAsync();

        Assert.True(_engine.IsBalanced(entry));

        var cashBalance = await _engine.GetAccountBalanceAsync(100);
        Assert.Equal(-100m, cashBalance);
    }

    [Fact]
    public async Task RecordTransfer_ExceedingBalance_Succeeds()
    {
        // Add $100 to cash
        await _engine.RecordDonationAsync(new Donation
        {
            Date = DateTime.UtcNow, SponsorId = 40, PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20, Amount = 100m, CreatedByUserId = 1
        });
        await SaveAsync();

        // Transfer $200 from cash to bank — should succeed even though balance goes negative
        var transfer = new CashBankTransfer
        {
            Date = DateTime.UtcNow, Direction = TransferDirection.CashToBank, Amount = 200m, CreatedByUserId = 1
        };

        var entry = await _engine.RecordTransferAsync(transfer);
        await SaveAsync();

        Assert.True(_engine.IsBalanced(entry));

        var cashBalance = await _engine.GetAccountBalanceAsync(100);
        Assert.Equal(-100m, cashBalance);
    }

    [Fact]
    public async Task RecordDonation_ZeroAmount_ThrowsWhenUnbalanced()
    {
        // The amount validation now throws ArgumentException before even reaching IsBalanced
        var donation = new Donation
        {
            Date = DateTime.UtcNow,
            SponsorId = 40,
            PaymentMethod = PaymentMethod.Cash,
            ProgramId = 20,
            Amount = 0m,
            CreatedByUserId = 1
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _engine.RecordDonationAsync(donation));
    }
}
