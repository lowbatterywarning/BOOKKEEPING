using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Enums;
using Bookkeeping.Core.Interfaces;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace Bookkeeping.Wpf.ViewModels;

public partial class CashBankViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly IJournalEngine _journal;

    // Cash summary
    [ObservableProperty] private decimal _cashBeginningBalance;
    [ObservableProperty] private decimal _cashIncome;
    [ObservableProperty] private decimal _cashExpenses;
    [ObservableProperty] private decimal _cashTransfersIn;
    [ObservableProperty] private decimal _cashTransfersOut;
    [ObservableProperty] private decimal _cashEndingBalance;
    [ObservableProperty] private ObservableCollection<LedgerLine> _cashTransactions = new();

    // Bank summary
    [ObservableProperty] private decimal _bankBeginningBalance;
    [ObservableProperty] private decimal _bankIncome;
    [ObservableProperty] private decimal _bankExpenses;
    [ObservableProperty] private decimal _bankTransfersIn;
    [ObservableProperty] private decimal _bankTransfersOut;
    [ObservableProperty] private decimal _bankEndingBalance;
    [ObservableProperty] private ObservableCollection<LedgerLine> _bankTransactions = new();

    // Transfer form
    [ObservableProperty] private bool _isTransferring;
    [ObservableProperty] private DateTime _transferDate = DateTime.Today;
    [ObservableProperty] private string _transferDirection = "CashToBank";
    [ObservableProperty] private decimal _transferAmount;
    [ObservableProperty] private string? _transferNotes;
    [ObservableProperty] private string? _transferError;

    // Beginning balance form
    [ObservableProperty] private bool _isSettingBeginningBalance;
    [ObservableProperty] private string _beginningBalanceAccount = "Cash";
    [ObservableProperty] private DateTime _beginningBalanceDate = DateTime.Today;
    [ObservableProperty] private decimal _beginningBalanceAmount;
    [ObservableProperty] private string? _beginningBalanceError;

    public CashBankViewModel(AppDbContext db, IJournalEngine journal)
    {
        _db = db;
        _journal = journal;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        var cashAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1000");
        var bankAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1010");
        if (cashAccount == null || bankAccount == null) return;

        // Load current year's journal entry lines for cash (include navigation for robust categorization)
        var yearStart = new DateTime(DateTime.Today.Year, 1, 1);
        var cashLines = await _db.JournalEntryLines
            .Include(l => l.JournalEntry).ThenInclude(j => j.Donation)
            .Include(l => l.JournalEntry).ThenInclude(j => j.Expense)
            .Include(l => l.JournalEntry).ThenInclude(j => j.CashBankTransfer)
            .Where(l => l.AccountId == cashAccount.Id && l.JournalEntry.Date >= yearStart)
            .OrderBy(l => l.JournalEntry.Date).ThenBy(l => l.JournalEntry.Id)
            .ToListAsync();

        // Load current year's journal entry lines for bank
        var bankLines = await _db.JournalEntryLines
            .Include(l => l.JournalEntry).ThenInclude(j => j.Donation)
            .Include(l => l.JournalEntry).ThenInclude(j => j.Expense)
            .Include(l => l.JournalEntry).ThenInclude(j => j.CashBankTransfer)
            .Where(l => l.AccountId == bankAccount.Id && l.JournalEntry.Date >= yearStart)
            .OrderBy(l => l.JournalEntry.Date).ThenBy(l => l.JournalEntry.Id)
            .ToListAsync();

        // Process cash
        var cashLedger = BuildLedgerLines(cashLines);
        CashTransactions = new ObservableCollection<LedgerLine>(cashLedger);
        CashBeginningBalance = cashLedger.Where(l => l.Type == "Beginning").Sum(l => l.Amount);
        CashIncome = cashLedger.Where(l => l.Type == "Donation").Sum(l => l.Amount);
        CashExpenses = cashLedger.Where(l => l.Type == "Expense").Sum(l => l.Amount);
        CashTransfersIn = cashLedger.Where(l => l.Type == "Transfer In").Sum(l => l.Amount);
        CashTransfersOut = cashLedger.Where(l => l.Type == "Transfer Out").Sum(l => l.Amount);
        CashEndingBalance = CashBeginningBalance + CashIncome - CashExpenses + CashTransfersIn - CashTransfersOut;

        // Process bank
        var bankLedger = BuildLedgerLines(bankLines);
        BankTransactions = new ObservableCollection<LedgerLine>(bankLedger);
        BankBeginningBalance = bankLedger.Where(l => l.Type == "Beginning").Sum(l => l.Amount);
        BankIncome = bankLedger.Where(l => l.Type == "Donation").Sum(l => l.Amount);
        BankExpenses = bankLedger.Where(l => l.Type == "Expense").Sum(l => l.Amount);
        BankTransfersIn = bankLedger.Where(l => l.Type == "Transfer In").Sum(l => l.Amount);
        BankTransfersOut = bankLedger.Where(l => l.Type == "Transfer Out").Sum(l => l.Amount);
        BankEndingBalance = BankBeginningBalance + BankIncome - BankExpenses + BankTransfersIn - BankTransfersOut;
    }

    private List<LedgerLine> BuildLedgerLines(List<JournalEntryLine> lines)
    {
        var result = new List<LedgerLine>();
        decimal runningBalance = 0;

        foreach (var line in lines)
        {
            var entry = line.JournalEntry;
            var isDebit = line.DebitAmount > 0;
            var amount = isDebit ? line.DebitAmount : line.CreditAmount;
            var sign = isDebit ? 1 : -1;

            // Robust categorization via entity relationships, not string matching
            string type;
            if (entry.Reference == "OPEN")
                type = "Beginning";
            else if (entry.Donation != null)
                type = "Donation";
            else if (entry.Expense != null)
                type = "Expense";
            else if (entry.CashBankTransfer != null)
                type = isDebit ? "Transfer In" : "Transfer Out";
            else
                type = isDebit ? "Deposit" : "Withdrawal";

            runningBalance += amount * sign;

            result.Add(new LedgerLine
            {
                Date = entry.Date,
                Description = line.Description ?? entry.Description,
                Reference = entry.Reference ?? "",
                Type = type,
                Debit = isDebit ? amount : 0,
                Credit = isDebit ? 0 : amount,
                Balance = runningBalance
            });
        }

        return result;
    }

    // ---- Transfer ----

    [RelayCommand]
    private void ShowTransferForm()
    {
        TransferDate = DateTime.Today;
        TransferDirection = "CashToBank";
        TransferAmount = 0;
        TransferNotes = null;
        TransferError = null;
        IsTransferring = true;
    }

    [RelayCommand]
    private void CancelTransfer()
    {
        IsTransferring = false;
    }

    [RelayCommand]
    private async Task ExecuteTransferAsync()
    {
        TransferError = null;
        if (TransferAmount <= 0)
        {
            TransferError = "Amount must be greater than zero.";
            return;
        }

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var transfer = new CashBankTransfer
            {
                Date = TransferDate,
                Direction = TransferDirection == "CashToBank" ? Core.Enums.TransferDirection.CashToBank : Core.Enums.TransferDirection.BankToCash,
                Amount = TransferAmount,
                Notes = TransferNotes?.Trim(),
                CreatedByUserId = 1,
                CreatedAt = DateTime.UtcNow
            };

            var entry = await _journal.RecordTransferAsync(transfer);
            transfer.JournalEntry = entry;
            _db.CashBankTransfers.Add(transfer);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            IsTransferring = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            var msg = ex.Message;
            var inner = ex.InnerException;
            while (inner != null)
            {
                msg += "\n\n→ " + inner.Message;
                inner = inner.InnerException;
            }
            TransferError = $"Transfer failed: {msg}";
        }
    }

    // ---- Beginning Balance ----

    [RelayCommand]
    private void ShowBeginningBalanceForm()
    {
        BeginningBalanceAccount = "Cash";
        BeginningBalanceDate = DateTime.Today;
        BeginningBalanceAmount = 0;
        BeginningBalanceError = null;
        IsSettingBeginningBalance = true;
    }

    [RelayCommand]
    private void CancelBeginningBalance()
    {
        IsSettingBeginningBalance = false;
    }

    [RelayCommand]
    private async Task SetBeginningBalanceAsync()
    {
        BeginningBalanceError = null;
        if (BeginningBalanceAmount <= 0)
        {
            BeginningBalanceError = "Amount must be greater than zero.";
            return;
        }

        var accountCode = BeginningBalanceAccount == "Cash" ? "1000" : "1010";

        // Prevent duplicate beginning balances for the same account
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == accountCode);
        if (account != null)
        {
            var hasExisting = await _db.JournalEntries
                .AnyAsync(j => j.Reference == "OPEN" && j.Lines.Any(l => l.AccountId == account.Id));
            if (hasExisting)
            {
                BeginningBalanceError = $"A beginning balance for {BeginningBalanceAccount} already exists. Delete the existing one first.";
                return;
            }
        }

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            await _journal.RecordBeginningBalanceAsync(accountCode, BeginningBalanceAmount, BeginningBalanceDate, 1);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            IsSettingBeginningBalance = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            var msg = ex.Message;
            var inner = ex.InnerException;
            while (inner != null)
            {
                msg += "\n\n→ " + inner.Message;
                inner = inner.InnerException;
            }
            BeginningBalanceError = $"Error: {msg}";
        }
    }
}

public class LedgerLine
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public decimal Amount => Debit > 0 ? Debit : Credit;
}
