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
/// ViewModel for the sponsor detail popup window shown when clicking a sponsor tile.
/// Displays contact info, per-program donation chart, target progress bars, and donation history.
/// </summary>
public partial class SponsorDetailViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly int _sponsorId;
    private bool _isLoaded;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private bool _hasContactInfo;
    [ObservableProperty] private bool _showCurrentMonth = true;
    [ObservableProperty] private PlotModel? _chartModel;
    [ObservableProperty] private decimal _totalDonated;
    [ObservableProperty] private decimal _totalTargets;

    /// <summary>True when the sponsor has at least one target set.</summary>
    public bool HasTargets => Targets.Count > 0;

    [ObservableProperty] private ObservableCollection<TargetProgressRow> _targets = new();
    [ObservableProperty] private ObservableCollection<SponsorDonationRow> _donations = new();
    [ObservableProperty] private bool _hasMoreDonations;

    private List<SponsorDonationRow> _allDonations = new();

    public SponsorDetailViewModel(AppDbContext db, int sponsorId, string sponsorName)
    {
        _db = db;
        _sponsorId = sponsorId;
        Title = sponsorName;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        await LoadSponsorInfoAsync();
        await LoadTargetsAsync();
        await LoadDonationsAsync();
        _isLoaded = true;
        BuildChart();
    }

    partial void OnShowCurrentMonthChanged(bool value)
    {
        if (!_isLoaded) return;
        var today = DateTime.Today;
        var from = value
            ? new DateTime(today.Year, today.Month, 1)
            : new DateTime(today.Year, 1, 1);
        FilterDonations(from, today.AddDays(1));
        BuildChart();
        _ = ReloadTargetsAsync();
    }

    private async Task ReloadTargetsAsync()
    {
        try
        {
            await LoadTargetsAsync();
        }
        catch
        {
            // Silently ignore — target progress is non-critical for the toggle view
        }
    }

    [RelayCommand] private void ToggleMonth() => ShowCurrentMonth = true;
    [RelayCommand] private void ToggleYear() => ShowCurrentMonth = false;

    [RelayCommand]
    private void Close(Window? window) => window?.Close();

    private async Task LoadSponsorInfoAsync()
    {
        var sponsor = await _db.Sponsors.FindAsync(_sponsorId);
        if (sponsor == null) return;

        Email = sponsor.Email ?? "";
        Phone = sponsor.Phone ?? "";
        Address = sponsor.Address ?? "";
        HasContactInfo = !string.IsNullOrWhiteSpace(Email)
                      || !string.IsNullOrWhiteSpace(Phone)
                      || !string.IsNullOrWhiteSpace(Address);
    }

    private async Task LoadTargetsAsync()
    {
        var (from, to) = GetCurrentPeriod();

        var targets = await _db.SponsorTargets
            .Where(t => t.SponsorId == _sponsorId
                     && (t.Year == null || t.Year == DateTime.Today.Year))
            .Include(t => t.Program)
            .ToListAsync();

        var targetProgramIds = targets.Select(t => t.ProgramId).ToList();

        // Single grouped query instead of N+1
        var donatedByProgram = await _db.Donations
            .Where(d => d.SponsorId == _sponsorId
                     && targetProgramIds.Contains(d.ProgramId)
                     && d.Date >= from && d.Date < to)
            .GroupBy(d => d.ProgramId)
            .Select(g => new { ProgramId = g.Key, Total = g.Sum(d => d.Amount) })
            .ToListAsync();

        var donatedDict = donatedByProgram.ToDictionary(x => x.ProgramId, x => x.Total);

        var rows = targets.Select(t =>
        {
            var donated = donatedDict.GetValueOrDefault(t.ProgramId, 0);
            return new TargetProgressRow
            {
                ProgramName = t.Program?.Name ?? "Unknown",
                TargetAmount = t.TargetAmount,
                DonatedAmount = donated,
                ProgressPercent = t.TargetAmount > 0
                    ? (double)(donated / t.TargetAmount * 100)
                    : 0,
            };
        }).ToList();

        Targets = new ObservableCollection<TargetProgressRow>(rows);
        TotalTargets = targets.Sum(t => t.TargetAmount);
        OnPropertyChanged(nameof(HasTargets));
    }

    private (DateTime From, DateTime To) GetCurrentPeriod()
    {
        var today = DateTime.Today;
        var from = ShowCurrentMonth
            ? new DateTime(today.Year, today.Month, 1)
            : new DateTime(today.Year, 1, 1);
        return (from, today.AddDays(1));
    }

    private async Task LoadDonationsAsync()
    {
        var today = DateTime.Today;
        var yearStart = new DateTime(today.Year, 1, 1);
        var tomorrow = today.AddDays(1);
        var donations = await _db.Donations
            .AsNoTracking()
            .Where(d => d.SponsorId == _sponsorId && d.Date >= yearStart && d.Date < tomorrow)
            .Include(d => d.Program)
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.Id)
            .ToListAsync();

        _allDonations = donations.Select(d => new SponsorDonationRow
        {
            Date = d.Date,
            ProgramName = d.Program?.Name ?? "",
            Amount = d.Amount,
            PaymentMethod = d.PaymentMethod.ToString(),
            ReceiptNumber = d.ReceiptNumber,
        }).ToList();

        var from = ShowCurrentMonth
            ? new DateTime(today.Year, today.Month, 1)
            : new DateTime(today.Year, 1, 1);
        FilterDonations(from, today.AddDays(1));
    }

    private void BuildChart()
    {
        var today = DateTime.Today;
        var start = ShowCurrentMonth
            ? new DateTime(today.Year, today.Month, 1)
            : new DateTime(today.Year, 1, 1);
        var end = today.AddDays(1);

        // Group donations by program for the period
        var programDonations = _allDonations
            .Where(d => d.Date >= start && d.Date < end)
            .GroupBy(d => d.ProgramName)
            .Select(g => (ProgramName: g.Key, Amount: g.Sum(d => d.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        TotalDonated = programDonations.Sum(x => x.Amount);

        var periodLabel = ShowCurrentMonth ? today.ToString("MMMM yyyy") : today.ToString("yyyy");

        ChartModel = ChartHelper.CreatePerProgramDonationChart(
            programDonations,
            title: $"Donations by Program — {periodLabel}",
            barWidth: 50,
            plotMargins: new OxyThickness(40, 5, 20, 30));
    }

    private void FilterDonations(DateTime from, DateTime to)
    {
        var filtered = _allDonations
            .Where(d => d.Date >= from && d.Date < to)
            .OrderByDescending(d => d.Date)
            .ToList();
        HasMoreDonations = filtered.Count > 500;
        Donations = new ObservableCollection<SponsorDonationRow>(filtered.Take(500));
    }
}

public class TargetProgressRow
{
    public string ProgramName { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal DonatedAmount { get; set; }
    public double ProgressPercent { get; set; }
    public double ProgressBarWidth => Math.Min(ProgressPercent * 2.0, 200.0);
    public string ProgressLabel => $"{DonatedAmount:C} / {TargetAmount:C}";
    public string ProgressColor => ProgressPercent >= 100 ? "#27ae60" : "#2980b9";
}

public class SponsorDonationRow
{
    public DateTime Date { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? ReceiptNumber { get; set; }
    public string DisplayAmount => $"+{Amount:C}";
    public string AmountColor => "#27ae60";
}
