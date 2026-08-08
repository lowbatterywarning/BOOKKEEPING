using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;

namespace Bookkeeping.Wpf.ViewModels;

/// <summary>
/// ViewModel for a single sponsor dashboard tile showing donations per program
/// and target summary.
/// </summary>
public partial class SponsorTileViewModel : ObservableObject
{
    private readonly Action<SponsorTileViewModel> _onTileClicked;

    [ObservableProperty] private string _title = string.Empty;

    /// <summary>Proper-cased sponsor name (not uppercased) for use in detail windows.</summary>
    [ObservableProperty] private string _originalName = string.Empty;

    [ObservableProperty] private int _sponsorId;
    [ObservableProperty] private decimal _totalDonatedMonthToDate;
    [ObservableProperty] private decimal _totalTargetAmount;
    [ObservableProperty] private int _targetCount;
    [ObservableProperty] private PlotModel? _chartModel;

    /// <summary>Human-readable target summary for the tile footer.</summary>
    public string TargetSummary => TargetCount > 0
        ? $"{TotalTargetAmount:C} across {TargetCount}"
        : "No targets";

    /// <summary>Number of additional programs beyond the 5 shown in the chart.</summary>
    [ObservableProperty] private int _overflowProgramCount;

    /// <summary>Label shown when the chart is truncated (e.g. "+3 more").</summary>
    public string OverflowLabel => OverflowProgramCount > 0
        ? $"+{OverflowProgramCount} more"
        : string.Empty;

    public SponsorTileViewModel(Action<SponsorTileViewModel> onTileClicked)
    {
        _onTileClicked = onTileClicked;
    }

    [RelayCommand]
    private void Click()
    {
        _onTileClicked(this);
    }

    partial void OnTotalTargetAmountChanged(decimal value) => OnPropertyChanged(nameof(TargetSummary));
    partial void OnTargetCountChanged(int value) => OnPropertyChanged(nameof(TargetSummary));
    partial void OnOverflowProgramCountChanged(int value) => OnPropertyChanged(nameof(OverflowLabel));

    /// <summary>
    /// Builds the per-program donation chart for this tile (top 5 programs).
    /// </summary>
    public void BuildChart(
        List<(string ProgramName, decimal Amount)> programDonations,
        int overflowCount)
    {
        OverflowProgramCount = overflowCount;
        ChartModel = ChartHelper.CreatePerProgramDonationChart(programDonations, compactLabels: true);
    }
}
