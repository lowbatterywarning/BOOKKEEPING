using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace Bookkeeping.Wpf.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    [ObservableProperty]
    private decimal _cashBalance;

    [ObservableProperty]
    private decimal _bankBalance;

    [ObservableProperty]
    private decimal _totalIncomeMonthToDate;

    [ObservableProperty]
    private decimal _totalExpensesMonthToDate;

    [ObservableProperty]
    private decimal _surplusDeficitMonthToDate;

    [ObservableProperty]
    private ObservableCollection<JournalEntry> _recentTransactions = new();

    public DashboardViewModel(AppDbContext db)
    {
        _db = db;
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        _db.ChangeTracker.Clear();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var tomorrow = today.AddDays(1);

        // Cash and bank balances — single query per account
        var cashAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1000");
        var bankAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1010");

        if (cashAccount != null)
        {
            var debits = await _db.JournalEntryLines
                .Where(l => l.AccountId == cashAccount.Id)
                .SumAsync(l => l.DebitAmount);
            var credits = await _db.JournalEntryLines
                .Where(l => l.AccountId == cashAccount.Id)
                .SumAsync(l => l.CreditAmount);
            CashBalance = debits - credits;
        }

        if (bankAccount != null)
        {
            var debits = await _db.JournalEntryLines
                .Where(l => l.AccountId == bankAccount.Id)
                .SumAsync(l => l.DebitAmount);
            var credits = await _db.JournalEntryLines
                .Where(l => l.AccountId == bankAccount.Id)
                .SumAsync(l => l.CreditAmount);
            BankBalance = debits - credits;
        }

        // Income month-to-date
        TotalIncomeMonthToDate = await _db.Donations
            .Where(d => d.Date >= monthStart && d.Date < tomorrow)
            .SumAsync(d => d.Amount);

        // Expenses month-to-date
        TotalExpensesMonthToDate = await _db.Expenses
            .Where(e => e.Date >= monthStart && e.Date < tomorrow)
            .SumAsync(e => e.Amount);

        SurplusDeficitMonthToDate = TotalIncomeMonthToDate - TotalExpensesMonthToDate;

        // Recent transactions
        var recent = await _db.JournalEntries
            .OrderByDescending(j => j.Date)
            .Take(10)
            .ToListAsync();
        RecentTransactions = new ObservableCollection<JournalEntry>(recent);
    }
}
