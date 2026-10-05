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
    [ObservableProperty] private ObservableCollection<BudgetRow> _budgetRows = new();
    [ObservableProperty] private ObservableCollection<OrgProgram> _programs = new();
    [ObservableProperty] private string? _statusMessage;

    // Totals for the footer
    [ObservableProperty] private decimal _totalBudget;
    [ObservableProperty] private decimal _totalActual;
    [ObservableProperty] private decimal _totalRemaining;

    [ObservableProperty] private string _budgetType = "Annual";
    [ObservableProperty] private int _selectedMonth = DateTime.Today.Month;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _loaded;
    public string[] BudgetTypes => ["Annual", "Monthly"];
    public int[] Months => Enumerable.Range(1, 12).ToArray();
    public bool IsMonthly => BudgetType == "Monthly";
    public string ActualHeading => IsMonthly ? "Actual through today (month)" : "Actual through today (year)";
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;
    private int? BudgetMonth => IsMonthly ? SelectedMonth : null;

    public int[] Years => Enumerable.Range(DateTime.Now.Year - 3, 7).ToArray();

    public BudgetViewModel(AppDbContext db)
    {
        _db = db;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        Programs = new ObservableCollection<OrgProgram>(await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync());
        _loaded = true;
        await RefreshSafelyAsync();
    }

    private async Task RefreshGridAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var year = SelectedYear;
            var month = BudgetMonth;
            var rows = new List<BudgetRow>();
            var start = new DateTime(year, month ?? 1, 1);
            var periodEnd = month.HasValue ? start.AddMonths(1) : start.AddYears(1);
            var end = periodEnd < DateTime.Today.AddDays(1) ? periodEnd : DateTime.Today.AddDays(1);

            var progIds = Programs.Select(p => p.Id).ToList();
            var budgets = await _db.Budgets
                .Where(b => b.Year == year && b.Month == month && b.ProgramId.HasValue && progIds.Contains(b.ProgramId.Value))
                .ToListAsync();
            var actuals = await _db.Expenses
                .Where(e => progIds.Contains(e.ProgramId) && e.Date >= start && e.Date < end)
                .GroupBy(e => e.ProgramId)
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
                    ProgramId = prog.Id,
                    Year = year,
                    Month = month,
                    Name = prog.Name,
                    BudgetAmount = budgetAmt,
                    ActualAmount = actualAmt,
                    Variance = budgetAmt - actualAmt,
                    HasBudget = budgetDict.ContainsKey(prog.Id)
                });
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
        finally { _refreshGate.Release(); }
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
    private Task SaveAllBudgetsAsync() => SaveRowsAsync(BudgetRows.ToList());

    [RelayCommand]
    private Task SaveBudgetAsync(BudgetRow? row) => row == null ? Task.CompletedTask : SaveRowsAsync([row]);

    private async Task SaveRowsAsync(List<BudgetRow> rows)
    {
        if (rows.Any(r => !decimal.TryParse(r.BudgetAmountText, out _) || r.BudgetAmount < 0))
        { StatusMessage = "Enter valid, non-negative budgets. Enter 0 to clear a budget."; return; }
        var year = SelectedYear; var month = BudgetMonth;
        if (rows.Any(r => r.Year != year || r.Month != month))
        { StatusMessage = "Wait for the selected budget period to finish loading before saving."; return; }
        await _refreshGate.WaitAsync();
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            foreach (var row in rows)
            {
                var existing = await _db.Budgets.SingleOrDefaultAsync(b => b.Year == year && b.Month == month && b.ProgramId == row.ProgramId);
                if (row.BudgetAmount == 0) { if (existing != null) _db.Budgets.Remove(existing); }
                else if (existing != null) existing.Amount = row.BudgetAmount;
                else _db.Budgets.Add(new Budget { Year = year, Month = month, Amount = row.BudgetAmount, ProgramId = row.ProgramId });
            }
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            StatusMessage = "Budgets saved. Enter 0 to clear a budget.";
        }
        catch (Exception ex) { _db.ChangeTracker.Clear(); StatusMessage = $"Could not save budgets: {ex.InnerException?.Message ?? ex.Message}"; }
        finally { _refreshGate.Release(); }
        RecalculateTotals();
    }

    private void QueueRefresh()
    {
        if (_loaded) PendingRefresh = RefreshSafelyAsync();
    }
    private async Task RefreshSafelyAsync()
    {
        try { await RefreshGridAsync(); }
        catch (Exception ex)
        {
            foreach (var row in BudgetRows) row.PropertyChanged -= OnBudgetRowChanged;
            BudgetRows.Clear(); RecalculateTotals();
            StatusMessage = $"Could not load budgets: {ex.Message}. Reload after resolving the database error.";
        }
    }
    partial void OnSelectedYearChanged(int value) => QueueRefresh();
    partial void OnSelectedMonthChanged(int value) => QueueRefresh();
    partial void OnBudgetTypeChanged(string value)
    { OnPropertyChanged(nameof(IsMonthly)); OnPropertyChanged(nameof(ActualHeading)); QueueRefresh(); }

}

public partial class BudgetRow : ObservableObject
{
    public int ProgramId { get; set; }
    public int Year { get; set; }
    public int? Month { get; set; }
    private bool _editingBudgetAmount;
    private string _budgetAmountText = "0";
    public string BudgetAmountText
    {
        get => _budgetAmountText;
        set { SetProperty(ref _budgetAmountText, value); if (decimal.TryParse(value, out var amount)) { _editingBudgetAmount = true; try { BudgetAmount = amount; } finally { _editingBudgetAmount = false; } } }
    }
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
        if (!_editingBudgetAmount) { _budgetAmountText = value.ToString(System.Globalization.CultureInfo.CurrentCulture); OnPropertyChanged(nameof(BudgetAmountText)); }
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
