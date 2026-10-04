using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;

namespace Bookkeeping.Wpf.ViewModels;

/// <summary>
/// ViewModel for the main dashboard. Loads program revenue/expense summaries
/// and presents them as interactive tiles with column charts.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    [ObservableProperty] private ObservableCollection<DashboardTileViewModel> _tiles = new();

    /// <summary>Number of tile columns to display, computed from the available width.</summary>
    [ObservableProperty] private int _tileColumns = 3;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string? _errorMessage;

    /// <summary>True when there are no active programs to display.</summary>
    [ObservableProperty] private bool _isEmpty;

    /// <summary>Friendly message shown in the empty state.</summary>
    [ObservableProperty] private string _emptyMessage = string.Empty;

    public DashboardViewModel(AppDbContext db)
    {
        _db = db;
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        IsEmpty = false;
        EmptyMessage = string.Empty;

        try
        {
            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var tomorrow = today.AddDays(1);

            // Load all active programs
            var programs = await _db.Programs
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

            // Single grouped query for MTD revenue per program
            var revenueByProgram = await _db.Donations
                .AsNoTracking()
                .Where(d => d.Date >= monthStart && d.Date < tomorrow)
                .GroupBy(d => d.ProgramId)
                .Select(g => new { ProgramId = g.Key, Total = g.Sum(d => d.Amount) })
                .ToListAsync();

            // Single grouped query for MTD expenses per program
            var expensesByProgram = await _db.Expenses
                .AsNoTracking()
                .Where(e => e.Date >= monthStart && e.Date < tomorrow)
                .GroupBy(e => e.ProgramId)
                .Select(g => new { ProgramId = g.Key, Total = g.Sum(e => e.Amount) })
                .ToListAsync();

            var revenueDict = revenueByProgram.ToDictionary(x => x.ProgramId, x => x.Total);
            var expenseDict = expensesByProgram.ToDictionary(x => x.ProgramId, x => x.Total);

            // Calculate ALL totals
            var allRevenue = revenueByProgram.Sum(x => x.Total);
            var allExpenses = expensesByProgram.Sum(x => x.Total);

            var tileList = new List<DashboardTileViewModel>();

            // 1. "ALL" tile first — aggregates everything
            var allTile = new DashboardTileViewModel(OnTileClicked)
            {
                Title = "ALL PROGRAMS",
                OriginalName = "All Programs",
                ProgramId = null,
                IsAllTile = true,
                RevenueMonthToDate = allRevenue,
                ExpensesMonthToDate = allExpenses,
            };
            allTile.BuildChart();
            tileList.Add(allTile);

            // 2. One tile per active program
            foreach (var program in programs)
            {
                var rev = revenueDict.GetValueOrDefault(program.Id, 0);
                var exp = expenseDict.GetValueOrDefault(program.Id, 0);

                var tile = new DashboardTileViewModel(OnTileClicked)
                {
                    Title = program.Name.ToUpperInvariant(),
                    OriginalName = program.Name,
                    ProgramId = program.Id,
                    IsAllTile = false,
                    RevenueMonthToDate = rev,
                    ExpensesMonthToDate = exp,
                };
                tile.BuildChart();
                tileList.Add(tile);
            }

            Tiles = new ObservableCollection<DashboardTileViewModel>(tileList);
        }
        catch (Exception ex)
        {
            Tiles = new ObservableCollection<DashboardTileViewModel>();
            ErrorMessage = $"Failed to load dashboard: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Handles a tile click — opens the ProgramDetailWindow for the selected program.
    /// The detail window shares this VM's DbContext, which is safe because ShowDialog()
    /// is modal (only one detail window open at a time) and both sides call Clear() before use.
    /// </summary>
    private void OnTileClicked(DashboardTileViewModel tile)
    {
        try
        {
            var programName = tile.IsAllTile ? "All Programs" : tile.OriginalName;
            var detailVm = new ProgramDetailViewModel(_db, tile.ProgramId, programName);

            var window = new Views.ProgramDetailWindow
            {
                DataContext = detailVm,
                Owner = Application.Current.MainWindow,
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open program details:\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

