using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;

namespace Bookkeeping.Wpf.ViewModels;

/// <summary>
/// ViewModel for a single dashboard tile showing a program's revenue vs expenses chart.
/// programId = null means the "ALL" aggregate tile.
/// </summary>
public partial class DashboardTileViewModel : ObservableObject
{
    private readonly Action<DashboardTileViewModel> _onTileClicked;

    [ObservableProperty] private string _title = string.Empty;

    /// <summary>Proper-cased program name (not uppercased) for use in detail windows.</summary>
    [ObservableProperty] private string _originalName = string.Empty;

    [ObservableProperty] private int? _programId;

    [ObservableProperty] private decimal _revenueMonthToDate;

    [ObservableProperty] private decimal _expensesMonthToDate;

    [ObservableProperty] private PlotModel? _chartModel;

    [ObservableProperty] private bool _isAllTile;

    public DashboardTileViewModel(Action<DashboardTileViewModel> onTileClicked)
    {
        _onTileClicked = onTileClicked;
    }

    [RelayCommand]
    private void Click()
    {
        _onTileClicked(this);
    }

    /// <summary>
    /// Builds a simple two-column chart (Revenue | Expenses) for this tile.
    /// </summary>
    public void BuildChart()
    {
        ChartModel = ChartHelper.CreateRevenueExpenseChart(
            RevenueMonthToDate, ExpensesMonthToDate,
            barWidth: 40, categoryFontSize: 11, valueAxisFormat: "#,##0");
    }
}
