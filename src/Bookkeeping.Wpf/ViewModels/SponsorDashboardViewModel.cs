using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;

namespace Bookkeeping.Wpf.ViewModels;

/// <summary>
/// ViewModel for the sponsor dashboard. Loads all active sponsors with their
/// month-to-date donations and target summaries, presented as interactive tiles.
/// </summary>
public partial class SponsorDashboardViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    [ObservableProperty] private ObservableCollection<SponsorTileViewModel> _tiles = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _emptyMessage = string.Empty;

    public SponsorDashboardViewModel(AppDbContext db)
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
            _db.ChangeTracker.Clear();
            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var tomorrow = today.AddDays(1);

            // Load active sponsors
            var sponsors = await _db.Sponsors
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync();

            if (sponsors.Count == 0)
            {
                IsEmpty = true;
                EmptyMessage = "No active sponsors found.\nGo to Sponsors in the sidebar to add one.";
                Tiles = new ObservableCollection<SponsorTileViewModel>();
                return;
            }

            var sponsorIds = sponsors.Select(s => s.Id).ToList();

            // Load targets with program names
            var targets = await _db.SponsorTargets
                .Where(t => sponsorIds.Contains(t.SponsorId)
                         && (t.Year == null || t.Year == today.Year))
                .Include(t => t.Program)
                .ToListAsync();

            // Load MTD donations grouped by sponsor + program
            var donations = await _db.Donations
                .Where(d => d.Date >= monthStart && d.Date < tomorrow
                         && sponsorIds.Contains(d.SponsorId))
                .Include(d => d.Program)
                .ToListAsync();

            var tileList = new List<SponsorTileViewModel>();

            foreach (var sponsor in sponsors)
            {
                // MTD donations for this sponsor, grouped by program
                var sponsorDonations = donations
                    .Where(d => d.SponsorId == sponsor.Id)
                    .GroupBy(d => d.Program?.Name ?? "Unknown")
                    .Select(g => (ProgramName: g.Key, Amount: g.Sum(d => d.Amount)))
                    .OrderByDescending(x => x.Amount)
                    .ToList();

                var totalDonated = sponsorDonations.Sum(x => x.Amount);

                // Targets for this sponsor
                var sponsorTargets = targets
                    .Where(t => t.SponsorId == sponsor.Id)
                    .ToList();

                var totalTarget = sponsorTargets.Sum(t => t.TargetAmount);

                var tile = new SponsorTileViewModel(OnTileClicked)
                {
                    Title = sponsor.Name.ToUpperInvariant(),
                    OriginalName = sponsor.Name,
                    SponsorId = sponsor.Id,
                    TotalDonatedMonthToDate = totalDonated,
                    TotalTargetAmount = totalTarget,
                    TargetCount = sponsorTargets.Count,
                };

                // Build chart (top 5 programs)
                var topPrograms = sponsorDonations.Take(5).ToList();
                var overflow = Math.Max(0, sponsorDonations.Count - 5);
                tile.BuildChart(topPrograms, overflow);

                tileList.Add(tile);
            }

            Tiles = new ObservableCollection<SponsorTileViewModel>(tileList);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load sponsor dashboard: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Handles a tile click — opens the SponsorDetailWindow for the selected sponsor.
    /// The detail window shares this VM's DbContext, which is safe because ShowDialog()
    /// is modal (only one detail window open at a time) and both sides call Clear() before use.
    /// </summary>
    private void OnTileClicked(SponsorTileViewModel tile)
    {
        try
        {
            var detailVm = new SponsorDetailViewModel(_db, tile.SponsorId, tile.OriginalName);

            var window = new Views.SponsorDetailWindow
            {
                DataContext = detailVm,
                Owner = Application.Current.MainWindow,
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open sponsor details:\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
