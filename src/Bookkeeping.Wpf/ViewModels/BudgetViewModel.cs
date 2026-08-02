using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace Bookkeeping.Wpf.ViewModels;

public partial class BudgetViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    [ObservableProperty] private int _selectedYear = DateTime.Now.Year;
    [ObservableProperty] private string _budgetType = "Category"; // Category or Program
    [ObservableProperty] private ObservableCollection<BudgetRow> _budgetRows = new();
    [ObservableProperty] private ObservableCollection<ExpenseCategory> _categories = new();
    [ObservableProperty] private ObservableCollection<OrgProgram> _programs = new();
    [ObservableProperty] private string? _statusMessage;

    // Totals for the footer
    [ObservableProperty] private decimal _totalBudget;
    [ObservableProperty] private decimal _totalActual;
    [ObservableProperty] private decimal _totalRemaining;

    public string[] BudgetTypes => new[] { "Category", "Program" };
    public int[] Years => Enumerable.Range(DateTime.Now.Year - 3, 7).ToArray();

    public BudgetViewModel(AppDbContext db)
    {
        _db = db;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        Categories = new ObservableCollection<ExpenseCategory>(await _db.ExpenseCategories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync());
        Programs = new ObservableCollection<OrgProgram>(await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync());
        await RefreshGridAsync();
    }

    private async Task RefreshGridAsync()
    {
        var rows = new List<BudgetRow>();

        if (BudgetType == "Category")
        {
            var catIds = Categories.Select(c => c.Id).ToList();
            // Single query for all budgets
            var budgets = await _db.Budgets
                .Where(b => b.Year == SelectedYear && b.Month == null && b.ExpenseCategoryId.HasValue && catIds.Contains(b.ExpenseCategoryId.Value))
                .ToListAsync();
            // Single query for all actuals
            var actuals = await _db.Expenses
                .Where(e => catIds.Contains(e.ExpenseCategoryId) && e.Date.Year == SelectedYear)
                .GroupBy(e => e.ExpenseCategoryId)
                .Select(g => new { CategoryId = g.Key, Total = g.Sum(e => e.Amount) })
                .ToListAsync();

            var budgetDict = budgets.Where(b => b.ExpenseCategoryId.HasValue)
                .ToDictionary(b => b.ExpenseCategoryId!.Value, b => b.Amount);
            var actualDict = actuals.ToDictionary(a => a.CategoryId, a => a.Total);

            foreach (var cat in Categories)
            {
                var budgetAmt = budgetDict.GetValueOrDefault(cat.Id, 0);
                var actualAmt = actualDict.GetValueOrDefault(cat.Id, 0);
                rows.Add(new BudgetRow
                {
                    Name = cat.Name,
                    BudgetAmount = budgetAmt,
                    ActualAmount = actualAmt,
                    Variance = budgetAmt - actualAmt,
                    HasBudget = budgetDict.ContainsKey(cat.Id)
                });
            }
        }
        else
        {
            var progIds = Programs.Select(p => p.Id).ToList();
            var budgets = await _db.Budgets
                .Where(b => b.Year == SelectedYear && b.Month == null && b.ProgramId.HasValue && progIds.Contains(b.ProgramId.Value))
                .ToListAsync();
            var actuals = await _db.Expenses
                .Where(e => e.ProgramId.HasValue && progIds.Contains(e.ProgramId.Value) && e.Date.Year == SelectedYear)
                .GroupBy(e => e.ProgramId!.Value)
                .Select(g => new { ProgramId = g.Key, Total = g.Sum(e => e.Amount) })
                .ToListAsync();

            var budgetDict = budgets.Where(b => b.ProgramId.HasValue)
                .ToDictionary(b => b.ProgramId!.Value, b => b.Amount);
            var actualDict = actuals.ToDictionary(a => a.ProgramId, a => a.Total);

            foreach (var prog in Programs)
            {
                var budgetAmt = budgetDict.GetValueOrDefault(prog.Id, 0);
                var actualAmt = actualDict.GetValueOrDefault(prog.Id, 0);
                rows.Add(new BudgetRow
                {
                    Name = prog.Name,
                    BudgetAmount = budgetAmt,
                    ActualAmount = actualAmt,
                    Variance = budgetAmt - actualAmt,
                    HasBudget = budgetDict.ContainsKey(prog.Id)
                });
            }
        }

        // Unsubscribe old rows to prevent memory leaks
        foreach (var oldRow in BudgetRows)
            oldRow.PropertyChanged -= OnBudgetRowChanged;

        BudgetRows = new ObservableCollection<BudgetRow>(rows);

        // Subscribe to each row's changes to update totals in real time
        foreach (var row in rows)
            row.PropertyChanged += OnBudgetRowChanged;

        RecalculateTotals();
    }

    private void OnBudgetRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        TotalBudget = BudgetRows.Sum(r => r.BudgetAmount);
        TotalActual = BudgetRows.Sum(r => r.ActualAmount);
        TotalRemaining = TotalBudget - TotalActual;
    }

    [RelayCommand]
    private async Task SaveAllBudgetsAsync()
    {
        foreach (var row in BudgetRows)
            await PrepareBudgetSaveAsync(row);
        await _db.SaveChangesAsync();
        StatusMessage = "All budgets saved.";
        RecalculateTotals();
    }

    [RelayCommand]
    private async Task SaveBudgetAsync(BudgetRow? row)
    {
        if (row == null) return;
        await PrepareBudgetSaveAsync(row);
        await _db.SaveChangesAsync();
        StatusMessage = $"Budget for {row.Name} saved: {row.BudgetAmount:C}";
    }

    private async Task PrepareBudgetSaveAsync(BudgetRow? row)
    {
        if (row == null) return;

        int? categoryId = null;
        int? programId = null;

        if (BudgetType == "Category")
        {
            var cat = Categories.FirstOrDefault(c => c.Name == row.Name);
            if (cat == null) return;
            categoryId = cat.Id;
        }
        else
        {
            var prog = Programs.FirstOrDefault(p => p.Name == row.Name);
            if (prog == null) return;
            programId = prog.Id;
        }

        var existing = await _db.Budgets.FirstOrDefaultAsync(b =>
            b.Year == SelectedYear && b.Month == null && b.ExpenseCategoryId == categoryId && b.ProgramId == programId);

        if (existing != null)
        {
            if (row.BudgetAmount <= 0)
                _db.Budgets.Remove(existing);
            else
                existing.Amount = row.BudgetAmount;
        }
        else if (row.BudgetAmount > 0)
        {
            _db.Budgets.Add(new Budget { Year = SelectedYear, Amount = row.BudgetAmount, ExpenseCategoryId = categoryId, ProgramId = programId });
        }
    }

    partial void OnSelectedYearChanged(int value)
    {
        _ = RefreshGridAsync().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
                StatusMessage = $"Error: {t.Exception.InnerException?.Message ?? t.Exception.Message}";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
    partial void OnBudgetTypeChanged(string value)
    {
        _ = RefreshGridAsync().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
                StatusMessage = $"Error: {t.Exception.InnerException?.Message ?? t.Exception.Message}";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}

public partial class BudgetRow : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private decimal _budgetAmount;

    [ObservableProperty]
    private decimal _actualAmount;

    [ObservableProperty]
    private decimal _variance;

    [ObservableProperty]
    private bool _hasBudget;

    public decimal PercentUsed => BudgetAmount > 0 ? Math.Round(ActualAmount / BudgetAmount * 100, 1) : 0;
    public decimal Remaining => BudgetAmount - ActualAmount;
    public string Status => !HasBudget ? "No Budget" :
        Variance < 0 ? "Over Budget" :
        Variance == 0 ? "On Track" : "Under Budget";
    public string StatusColor => !HasBudget ? "#95a5a6" :
        Variance < 0 ? "#c0392b" :
        Variance == 0 ? "#f39c12" : "#27ae60";

    partial void OnBudgetAmountChanged(decimal value)
    {
        Variance = value - ActualAmount;
        HasBudget = value > 0;
        OnPropertyChanged(nameof(PercentUsed));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusColor));
    }

    partial void OnActualAmountChanged(decimal value)
    {
        Variance = BudgetAmount - value;
        OnPropertyChanged(nameof(PercentUsed));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusColor));
    }

    partial void OnVarianceChanged(decimal value)
    {
        OnPropertyChanged(nameof(PercentUsed));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusColor));
    }
}
