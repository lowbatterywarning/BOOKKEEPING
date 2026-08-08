using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using OxyPlot;
using System.Collections.ObjectModel;
using System.Windows;

namespace Bookkeeping.Wpf.ViewModels;

/// <summary>
/// ViewModel for the program detail popup window shown when clicking a dashboard tile.
/// </summary>
public partial class ProgramDetailViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly int? _programId;
    private List<TransactionLine> _allTransactions = new();
    private bool _isLoaded;

    [ObservableProperty] private string _title = string.Empty;

    [ObservableProperty] private bool _showCurrentMonth = true;

    [ObservableProperty] private PlotModel? _chartModel;

    [ObservableProperty] private ObservableCollection<TransactionLine> _transactions = new();

    [ObservableProperty] private decimal _totalRevenue;

    [ObservableProperty] private decimal _totalExpenses;

    [ObservableProperty] private decimal _netAmount;

    /// <summary>True when the transaction queries were capped at 500 results each.</summary>
    [ObservableProperty] private bool _hasMoreTransactions;

    public string NetAmountColor => NetAmount >= 0 ? "#27ae60" : "#c0392b";

    partial void OnNetAmountChanged(decimal value) => OnPropertyChanged(nameof(NetAmountColor));

    public ProgramDetailViewModel(AppDbContext db, int? programId, string programName)
    {
        _db = db;
        _programId = programId;
        Title = programName;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        await LoadTransactionsAsync();
        _isLoaded = true;
        BuildChart();
    }

    /// <summary>Called when the month/year toggle changes.</summary>
    partial void OnShowCurrentMonthChanged(bool value)
    {
        if (!_isLoaded) return;
        var today = DateTime.Today;
        var from = value
            ? new DateTime(today.Year, today.Month, 1)
            : new DateTime(today.Year, 1, 1);
        FilterTransactions(from, today.AddDays(1));
        BuildChart();
    }

    [RelayCommand]
    private void ToggleMonth() => ShowCurrentMonth = true;

    [RelayCommand]
    private void ToggleYear() => ShowCurrentMonth = false;

    [RelayCommand]
    private void Close(Window? window)
    {
        window?.Close();
    }

    private async Task LoadTransactionsAsync()
    {
        var today = DateTime.Today;

        // Load donations
        IQueryable<Donation> donationQuery = _db.Donations
            .Include(d => d.Sponsor)
            .Include(d => d.Program);
        if (_programId.HasValue)
            donationQuery = donationQuery.Where(d => d.ProgramId == _programId.Value);

        var donations = await donationQuery
            .OrderByDescending(d => d.Date)
            .Take(500)
            .ToListAsync();

        // Load expenses
        IQueryable<Expense> expenseQuery = _db.Expenses
            .Include(e => e.Program);
        if (_programId.HasValue)
            expenseQuery = expenseQuery.Where(e => e.ProgramId == _programId.Value);

        var expenses = await expenseQuery
            .OrderByDescending(e => e.Date)
            .Take(500)
            .ToListAsync();

        HasMoreTransactions = donations.Count >= 500 || expenses.Count >= 500;

        var lines = new List<TransactionLine>();

        foreach (var d in donations)
        {
            lines.Add(new TransactionLine
            {
                Date = d.Date,
                Type = "Revenue",
                Description = d.Sponsor?.Name ?? "Unknown Sponsor",
                ProgramName = d.Program?.Name ?? "",
                Amount = d.Amount,
                IsRevenue = true,
                Notes = d.Notes,
                ReceiptNumber = d.ReceiptNumber,
            });
        }

        foreach (var e in expenses)
        {
            lines.Add(new TransactionLine
            {
                Date = e.Date,
                Type = "Expense",
                Description = e.VendorName,
                ProgramName = e.Program?.Name ?? "",
                Amount = e.Amount,
                IsRevenue = false,
                Notes = e.Notes,
            });
        }

        _allTransactions = lines.OrderByDescending(l => l.Date).ToList();

        // Filter by month or year
        var tomorrow = today.AddDays(1);
        var from = ShowCurrentMonth
            ? new DateTime(today.Year, today.Month, 1)
            : new DateTime(today.Year, 1, 1);
        FilterTransactions(from, tomorrow);
    }

    private void BuildChart()
    {
        var today = DateTime.Today;
        var start = ShowCurrentMonth
            ? new DateTime(today.Year, today.Month, 1)
            : new DateTime(today.Year, 1, 1);
        var end = today.AddDays(1);

        var revenue = _allTransactions
            .Where(t => t.IsRevenue && t.Date >= start && t.Date < end)
            .Sum(t => t.Amount);

        var expenses = _allTransactions
            .Where(t => !t.IsRevenue && t.Date >= start && t.Date < end)
            .Sum(t => t.Amount);

        TotalRevenue = revenue;
        TotalExpenses = expenses;
        NetAmount = revenue - expenses;

        var periodLabel = ShowCurrentMonth ? today.ToString("MMMM yyyy") : today.ToString("yyyy");

        ChartModel = ChartHelper.CreateRevenueExpenseChart(
            revenue, expenses,
            title: $"Revenue vs Expenses — {periodLabel}",
            barWidth: 50, categoryFontSize: 12, valueAxisFormat: "C0",
            plotMargins: new OxyThickness(40, 5, 20, 30));
    }

    private void FilterTransactions(DateTime from, DateTime to)
    {
        var filtered = _allTransactions
            .Where(t => t.Date >= from && t.Date < to)
            .OrderByDescending(t => t.Date)
            .ToList();
        Transactions = new ObservableCollection<TransactionLine>(filtered);
    }
}

/// <summary>
/// Display model for a single transaction row in the program detail list.
/// </summary>
public class TransactionLine
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsRevenue { get; set; }
    public string? Notes { get; set; }
    public string? ReceiptNumber { get; set; }

    /// <summary>Amount formatted with +/- sign and currency.</summary>
    public string DisplayAmount => IsRevenue
        ? $"+{Amount:C}"
        : $"-{Amount:C}";

    /// <summary>Colour for the amount text.</summary>
    public string AmountColor => IsRevenue ? "#27ae60" : "#c0392b";
}
