using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Enums;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace Bookkeeping.Wpf.ViewModels;

public partial class ReconciliationViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    [ObservableProperty] private ObservableCollection<ReconciliationRow> _bankTransactions = new();
    [ObservableProperty] private ObservableCollection<BankReconciliation> _history = new();
    [ObservableProperty] private decimal _statementBalance;
    [ObservableProperty] private decimal _ledgerBalance;
    [ObservableProperty] private decimal _difference;
    [ObservableProperty] private decimal _clearedBalance;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isReconciling;

    public ReconciliationViewModel(AppDbContext db)
    {
        _db = db;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        var bankAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1010");
        if (bankAccount == null) { StatusMessage = "Bank account not found."; return; }

        // Get all bank transactions not yet cleared
        var bankLines = await _db.JournalEntryLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == bankAccount.Id)
            .OrderBy(l => l.JournalEntry.Date)
            .ToListAsync();

        // Check which are already reconciled
        var reconciledIds = await _db.ReconciledItems.Select(r => r.JournalEntryLineId).ToListAsync();

        var rows = bankLines.Select(l => new ReconciliationRow
        {
            Id = l.Id,
            Date = l.JournalEntry.Date,
            Description = l.Description ?? l.JournalEntry.Description,
            DebitAmount = l.DebitAmount,
            CreditAmount = l.CreditAmount,
            IsCleared = reconciledIds.Contains(l.Id)
        }).ToList();

        BankTransactions = new ObservableCollection<ReconciliationRow>(rows);

        // Calculate balances
        var totalDebits = bankLines.Sum(l => l.DebitAmount);
        var totalCredits = bankLines.Sum(l => l.CreditAmount);
        LedgerBalance = totalDebits - totalCredits;
        ClearedBalance = rows.Where(r => r.IsCleared).Sum(r => r.DebitAmount - r.CreditAmount);
        Difference = StatementBalance - ClearedBalance;

        // History
        History = new ObservableCollection<BankReconciliation>(
            await _db.BankReconciliations.OrderByDescending(r => r.StatementDate).Take(20).ToListAsync());
    }

    [RelayCommand]
    private void ToggleClear(ReconciliationRow? row)
    {
        if (row == null) return;
        row.IsCleared = !row.IsCleared;
        ClearedBalance = BankTransactions.Where(r => r.IsCleared).Sum(r => r.DebitAmount - r.CreditAmount);
        Difference = StatementBalance - ClearedBalance;
    }

    [RelayCommand]
    private async Task CompleteReconciliationAsync()
    {
        if (IsReconciling) return;
        if (!BankTransactions.Any(r => r.IsCleared))
        {
            StatusMessage = "Please clear at least one transaction before completing reconciliation.";
            return;
        }
        IsReconciling = true;
        StatusMessage = "Completing reconciliation...";

        var reconciliation = new BankReconciliation
        {
            StatementDate = DateTime.Today,
            StatementBalance = StatementBalance,
            LedgerBalance = LedgerBalance,
            Difference = StatementBalance - ClearedBalance,
            IsCompleted = true,
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow
        };

        _db.BankReconciliations.Add(reconciliation);
        await _db.SaveChangesAsync();

        // Save cleared items — skip any already reconciled in a previous session
        var existingReconciledIds = await _db.ReconciledItems.Select(r => r.JournalEntryLineId).ToListAsync();
        foreach (var row in BankTransactions.Where(r => r.IsCleared && !existingReconciledIds.Contains(r.Id)))
        {
            _db.ReconciledItems.Add(new ReconciledItem
            {
                BankReconciliationId = reconciliation.Id,
                JournalEntryLineId = row.Id,
                IsCleared = true
            });
        }
        await _db.SaveChangesAsync();

        StatusMessage = $"Reconciliation complete. Diff: {reconciliation.Difference:C}";
        IsReconciling = false;
        await LoadAsync();
    }

    partial void OnStatementBalanceChanged(decimal value)
    {
        Difference = value - ClearedBalance;
    }
}

public class ReconciliationRow
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public bool IsCleared { get; set; }
    public decimal Net => DebitAmount - CreditAmount;
}
