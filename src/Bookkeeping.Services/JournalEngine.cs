using Bookkeeping.Core.Enums;
using Bookkeeping.Core.Interfaces;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;

namespace Bookkeeping.Services;

/// <summary>
/// Core double-entry bookkeeping engine.
/// Every financial transaction flows through this engine to guarantee the ledger always balances.
///
/// IMPORTANT: This engine does NOT call SaveChangesAsync. The caller is responsible for
/// wrapping calls in a transaction and saving the unit of work atomically.
/// </summary>
public class JournalEngine : IJournalEngine
{
    private readonly AppDbContext _db;

    // Well-known account codes
    private const string CashAccountCode = "1000";
    private const string BankAccountCode = "1010";
    private const string UnrestrictedEquityCode = "3000";

    public JournalEngine(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Record a donation: Debit Cash/Bank, Credit the income account for the donation category.
    /// Caller must call SaveChangesAsync to persist.
    /// </summary>
    public async Task<JournalEntry> RecordDonationAsync(Donation donation)
    {
        if (donation.Amount <= 0)
            throw new ArgumentException("Donation amount must be greater than zero.", nameof(donation));

        // Find the asset account (Cash or Bank)
        var assetAccount = await GetAssetAccountAsync(donation.PaymentMethod);

        // Find the income account for this donation category
        var incomeAccount = await GetOrCreateIncomeAccountAsync(donation.DonationCategoryId);

        // Load the category with fund and sponsor name for the description
        var category = await _db.DonationCategories
            .Include(dc => dc.Fund)
            .FirstAsync(dc => dc.Id == donation.DonationCategoryId);

        var sponsorName = await _db.Sponsors
            .Where(s => s.Id == donation.SponsorId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync() ?? $"Sponsor #{donation.SponsorId}";

        // Create the journal entry
        var entry = new JournalEntry
        {
            Date = donation.Date,
            Description = $"Donation - {category.Name} from {sponsorName}",
            Reference = donation.ReceiptNumber,
            CreatedByUserId = donation.CreatedByUserId,
            CreatedAt = DateTime.UtcNow
        };

        entry.Lines.Add(new JournalEntryLine
        {
            AccountId = assetAccount.Id,
            DebitAmount = donation.Amount,
            CreditAmount = 0,
            FundId = category.FundId,
            Description = $"Debit: {assetAccount.Name}"
        });

        entry.Lines.Add(new JournalEntryLine
        {
            AccountId = incomeAccount.Id,
            DebitAmount = 0,
            CreditAmount = donation.Amount,
            FundId = category.FundId,
            Description = $"Credit: {incomeAccount.Name} ({category.Name})"
        });

        if (!IsBalanced(entry))
            throw new InvalidOperationException("Journal entry is not balanced! Debits must equal credits.");

        _db.JournalEntries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Record an expense: Debit the expense account, Credit Cash/Bank.
    /// Validates sufficient balance before proceeding.
    /// Caller must call SaveChangesAsync to persist.
    /// </summary>
    public async Task<JournalEntry> RecordExpenseAsync(Expense expense)
    {
        if (expense.Amount <= 0)
            throw new ArgumentException("Expense amount must be greater than zero.", nameof(expense));

        // Find the asset account (Cash or Bank)
        var assetAccount = await GetAssetAccountAsync(expense.PaymentMethod);

        // Validate sufficient balance
        var assetBalance = await GetAccountBalanceInternalAsync(assetAccount);
        if (assetBalance < expense.Amount)
            throw new InvalidOperationException(
                $"Insufficient {assetAccount.Name} balance. Available: {assetBalance:C}, Required: {expense.Amount:C}.");

        // Find the expense account for this expense category
        var expenseAccount = await GetOrCreateExpenseAccountAsync(expense.ExpenseCategoryId);

        // Determine fund from the expense category
        var category = await _db.ExpenseCategories
            .Include(ec => ec.Fund)
            .FirstAsync(ec => ec.Id == expense.ExpenseCategoryId);

        int? fundId = category.FundId;

        // Create the journal entry
        var entry = new JournalEntry
        {
            Date = expense.Date,
            Description = $"Expense - {category.Name} to {expense.VendorName}",
            Reference = null,
            CreatedByUserId = expense.CreatedByUserId,
            CreatedAt = DateTime.UtcNow
        };

        entry.Lines.Add(new JournalEntryLine
        {
            AccountId = expenseAccount.Id,
            DebitAmount = expense.Amount,
            CreditAmount = 0,
            FundId = fundId,
            Description = $"Debit: {expenseAccount.Name}"
        });

        entry.Lines.Add(new JournalEntryLine
        {
            AccountId = assetAccount.Id,
            DebitAmount = 0,
            CreditAmount = expense.Amount,
            FundId = fundId,
            Description = $"Credit: {assetAccount.Name}"
        });

        if (!IsBalanced(entry))
            throw new InvalidOperationException("Journal entry is not balanced! Debits must equal credits.");

        _db.JournalEntries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Record a cash-to-bank or bank-to-cash transfer.
    /// Validates sufficient source balance before proceeding.
    /// Caller must call SaveChangesAsync to persist.
    /// </summary>
    public async Task<JournalEntry> RecordTransferAsync(CashBankTransfer transfer)
    {
        if (transfer.Amount <= 0)
            throw new ArgumentException("Transfer amount must be greater than zero.", nameof(transfer));

        var cashAccount = await _db.Accounts.FirstAsync(a => a.Code == CashAccountCode);
        var bankAccount = await _db.Accounts.FirstAsync(a => a.Code == BankAccountCode);

        Account debitAccount, creditAccount;
        string directionDesc;

        if (transfer.Direction == "CashToBank")
        {
            debitAccount = bankAccount;
            creditAccount = cashAccount;
            directionDesc = "Cash → Bank";

            var cashBalance = await GetAccountBalanceInternalAsync(cashAccount);
            if (cashBalance < transfer.Amount)
                throw new InvalidOperationException(
                    $"Insufficient Cash balance. Available: {cashBalance:C}, Required: {transfer.Amount:C}.");
        }
        else if (transfer.Direction == "BankToCash")
        {
            debitAccount = cashAccount;
            creditAccount = bankAccount;
            directionDesc = "Bank → Cash";

            var bankBalance = await GetAccountBalanceInternalAsync(bankAccount);
            if (bankBalance < transfer.Amount)
                throw new InvalidOperationException(
                    $"Insufficient Bank balance. Available: {bankBalance:C}, Required: {transfer.Amount:C}.");
        }
        else
        {
            throw new ArgumentException($"Invalid transfer direction: {transfer.Direction}. Must be 'CashToBank' or 'BankToCash'.");
        }

        var entry = new JournalEntry
        {
            Date = transfer.Date,
            Description = $"Transfer: {directionDesc}",
            Reference = null,
            CreatedByUserId = transfer.CreatedByUserId,
            CreatedAt = DateTime.UtcNow
        };

        entry.Lines.Add(new JournalEntryLine
        {
            AccountId = debitAccount.Id,
            DebitAmount = transfer.Amount,
            CreditAmount = 0,
            Description = $"Debit: {debitAccount.Name}"
        });

        entry.Lines.Add(new JournalEntryLine
        {
            AccountId = creditAccount.Id,
            DebitAmount = 0,
            CreditAmount = transfer.Amount,
            Description = $"Credit: {creditAccount.Name}"
        });

        if (!IsBalanced(entry))
            throw new InvalidOperationException("Journal entry is not balanced! Debits must equal credits.");

        _db.JournalEntries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Get the current balance of an account.
    /// For Asset/Expense accounts: Sum(Debits) - Sum(Credits)
    /// For Liability/Equity/Income accounts: Sum(Credits) - Sum(Debits)
    /// </summary>
    public async Task<decimal> GetAccountBalanceAsync(int accountId)
    {
        var account = await _db.Accounts.FindAsync(accountId)
            ?? throw new ArgumentException($"Account {accountId} not found.");

        return await GetAccountBalanceInternalAsync(account);
    }

    /// <summary>
    /// Get the net asset balance of a fund using full fund accounting.
    /// Computes as: Fund Equity + Fund Income - Fund Expenses.
    /// </summary>
    public async Task<decimal> GetFundBalanceAsync(int fundId)
    {
        // Single query: aggregate all fund-tagged journal lines by account type
        var totals = await _db.JournalEntryLines
            .Where(l => l.FundId == fundId)
            .GroupBy(l => l.Account.AccountType)
            .Select(g => new
            {
                AccountType = g.Key,
                TotalDebits = g.Sum(l => l.DebitAmount),
                TotalCredits = g.Sum(l => l.CreditAmount)
            })
            .ToListAsync();

        decimal fundIncome = 0, fundExpenses = 0, fundEquity = 0;

        foreach (var t in totals)
        {
            switch (t.AccountType)
            {
                case AccountType.Income:
                    fundIncome = t.TotalCredits - t.TotalDebits;
                    break;
                case AccountType.Expense:
                    fundExpenses = t.TotalDebits - t.TotalCredits;
                    break;
                case AccountType.Equity:
                    fundEquity = t.TotalCredits - t.TotalDebits;
                    break;
            }
        }

        // Fund net position = opening equity + income - expenses
        return fundEquity + fundIncome - fundExpenses;
    }

    /// <summary>
    /// Record a beginning/opening balance for an asset account (Cash or Bank).
    /// Debits the asset, credits the appropriate equity account based on the asset's fund.
    /// </summary>
    public async Task<JournalEntry> RecordBeginningBalanceAsync(string accountCode, decimal amount, int createdByUserId)
    {
        if (amount <= 0)
            throw new ArgumentException("Beginning balance amount must be greater than zero.", nameof(amount));

        var assetAccount = await _db.Accounts.FirstAsync(a => a.Code == accountCode);

        // Determine the correct equity account: use the asset's fund if set, otherwise unrestricted
        Account equityAccount;
        if (assetAccount.FundId.HasValue)
        {
            equityAccount = await _db.Accounts.FirstOrDefaultAsync(a =>
                a.AccountType == AccountType.Equity && a.FundId == assetAccount.FundId.Value)
                ?? throw new InvalidOperationException(
                    $"No equity account found for fund {assetAccount.FundId}. Cannot record beginning balance.");
        }
        else
        {
            equityAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == UnrestrictedEquityCode)
                ?? throw new InvalidOperationException(
                    $"Equity account ({UnrestrictedEquityCode}) not found. Cannot record beginning balance.");
        }

        var entry = new JournalEntry
        {
            Date = DateTime.Today,
            Description = $"Beginning Balance - {assetAccount.Name}",
            Reference = "OPEN",
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow
        };

        entry.Lines.Add(new JournalEntryLine
        {
            AccountId = assetAccount.Id,
            DebitAmount = amount,
            CreditAmount = 0,
            FundId = assetAccount.FundId,
            Description = $"Opening balance for {assetAccount.Name}"
        });

        entry.Lines.Add(new JournalEntryLine
        {
            AccountId = equityAccount.Id,
            DebitAmount = 0,
            CreditAmount = amount,
            FundId = equityAccount.FundId,
            Description = $"Opening balance equity for {assetAccount.Name}"
        });

        if (!IsBalanced(entry))
            throw new InvalidOperationException("Beginning balance entry is not balanced.");

        _db.JournalEntries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Validate that total debits equal total credits.
    /// </summary>
    public bool IsBalanced(JournalEntry entry)
    {
        decimal totalDebits = entry.Lines.Sum(l => l.DebitAmount);
        decimal totalCredits = entry.Lines.Sum(l => l.CreditAmount);
        return totalDebits == totalCredits && totalDebits > 0;
    }

    // ---- Private helpers ----

    /// <summary>
    /// Internal balance calculation that accepts an already-loaded account entity
    /// to avoid a redundant DB round-trip.
    /// </summary>
    private async Task<decimal> GetAccountBalanceInternalAsync(Account account)
    {
        var totals = await _db.JournalEntryLines
            .Where(l => l.AccountId == account.Id)
            .GroupBy(l => 1)
            .Select(g => new {
                TotalDebits = g.Sum(l => l.DebitAmount),
                TotalCredits = g.Sum(l => l.CreditAmount)
            })
            .FirstOrDefaultAsync();

        decimal totalDebits = totals?.TotalDebits ?? 0;
        decimal totalCredits = totals?.TotalCredits ?? 0;

        return account.AccountType switch
        {
            AccountType.Asset or AccountType.Expense => totalDebits - totalCredits,
            AccountType.Liability or AccountType.Equity or AccountType.Income => totalCredits - totalDebits,
            _ => throw new ArgumentOutOfRangeException(nameof(account.AccountType))
        };
    }

    private async Task<Account> GetAssetAccountAsync(PaymentMethod paymentMethod)
    {
        var code = paymentMethod == PaymentMethod.Cash ? CashAccountCode : BankAccountCode;
        return await _db.Accounts.FirstAsync(a => a.Code == code);
    }

    private async Task<Account> GetOrCreateIncomeAccountAsync(int donationCategoryId)
    {
        var category = await _db.DonationCategories
            .Include(dc => dc.IncomeAccount)
            .FirstAsync(dc => dc.Id == donationCategoryId);

        if (category.IncomeAccount != null)
            return category.IncomeAccount;

        // Create the income account for this category
        var nextCode = await GetNextAccountCodeAsync("4");
        var account = new Account
        {
            Code = nextCode,
            Name = $"Donation Income - {category.Name}",
            AccountType = AccountType.Income,
            FundId = category.FundId,
            IsSystem = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Accounts.Add(account);
        return account;
    }

    private async Task<Account> GetOrCreateExpenseAccountAsync(int expenseCategoryId)
    {
        var category = await _db.ExpenseCategories
            .Include(ec => ec.ExpenseAccount)
            .FirstAsync(ec => ec.Id == expenseCategoryId);

        if (category.ExpenseAccount != null)
            return category.ExpenseAccount;

        // Create the expense account for this category
        var nextCode = await GetNextAccountCodeAsync("5");
        var account = new Account
        {
            Code = nextCode,
            Name = $"Expense - {category.Name}",
            AccountType = AccountType.Expense,
            FundId = category.FundId,
            IsSystem = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Accounts.Add(account);
        return account;
    }

    private async Task<string> GetNextAccountCodeAsync(string prefix)
    {
        var maxCode = await _db.Accounts
            .Where(a => a.Code.StartsWith(prefix))
            .MaxAsync(a => (string?)a.Code);

        if (string.IsNullOrEmpty(maxCode))
            return $"{prefix}001";

        // Parse the numeric suffix of the existing code and increment within the same prefix
        var suffixStr = maxCode[prefix.Length..];
        if (int.TryParse(suffixStr, out int suffix) && suffix < 999)
            return $"{prefix}{(suffix + 1):D3}";

        // Fallback: use timestamp-based suffix
        return $"{prefix}{DateTime.UtcNow:MMddHHmm}";
    }
}
