using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Bookkeeping.Wpf.ViewModels;

public partial class SponsorsViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    [ObservableProperty]
    private ObservableCollection<SponsorDisplay> _sponsors = new();

    [ObservableProperty]
    private SponsorDisplay? _selectedSponsor;

    // Search / Filters
    [ObservableProperty]
    private string _searchText = string.Empty;
    [ObservableProperty]
    private string _filterName = string.Empty;
    [ObservableProperty]
    private string _filterAddress = string.Empty;
    [ObservableProperty]
    private string _filterPhone = string.Empty;
    [ObservableProperty]
    private string _filterEmail = string.Empty;
    [ObservableProperty]
    private bool _showInactive = true;

    // Sorting
    [ObservableProperty]
    private string _sortColumn = "Name";
    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private bool _isEditing;

    // Edit fields
    [ObservableProperty]
    private string _editName = string.Empty;
    [ObservableProperty]
    private string? _editAddress;
    [ObservableProperty]
    private string? _editPhone;
    [ObservableProperty]
    private string? _editEmail;
    [ObservableProperty]
    private string? _editNotes;
    [ObservableProperty]
    private ObservableCollection<CategoryTargetEdit> _editTargets = new();
    [ObservableProperty]
    private ObservableCollection<DonationCategory> _allCategories = new();
    [ObservableProperty]
    private string? _editErrorMessage;

    public SponsorsViewModel(AppDbContext db)
    {
        _db = db;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var yearStart = new DateTime(today.Year, 1, 1);
        var tomorrow = today.AddDays(1);

        var query = _db.Sponsors.AsQueryable();

        // Quick search (searches name, email, phone)
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(s => s.Name.Contains(SearchText) || (s.Email != null && s.Email.Contains(SearchText)) || (s.Phone != null && s.Phone.Contains(SearchText)));

        // Detailed filters
        if (!string.IsNullOrWhiteSpace(FilterName))
            query = query.Where(s => s.Name.Contains(FilterName));
        if (!string.IsNullOrWhiteSpace(FilterAddress))
            query = query.Where(s => s.Address != null && s.Address.Contains(FilterAddress));
        if (!string.IsNullOrWhiteSpace(FilterPhone))
            query = query.Where(s => s.Phone != null && s.Phone.Contains(FilterPhone));
        if (!string.IsNullOrWhiteSpace(FilterEmail))
            query = query.Where(s => s.Email != null && s.Email.Contains(FilterEmail));
        if (!ShowInactive)
            query = query.Where(s => s.IsActive);

        var sponsors = await query
            .Include(s => s.Targets).ThenInclude(t => t.DonationCategory)
            .OrderBy(s => s.Name).ToListAsync();
        var sponsorIds = sponsors.Select(s => s.Id).ToList();

        // Single query: all donations this month grouped by sponsor
        var monthlyDonations = await _db.Donations
            .Where(d => sponsorIds.Contains(d.SponsorId) && d.Date >= monthStart && d.Date < tomorrow)
            .GroupBy(d => d.SponsorId)
            .Select(g => new { SponsorId = g.Key, Total = g.Sum(d => d.Amount) })
            .ToListAsync();

        // Single query: all donations this year grouped by sponsor
        var yearlyDonations = await _db.Donations
            .Where(d => sponsorIds.Contains(d.SponsorId) && d.Date >= yearStart && d.Date < tomorrow)
            .GroupBy(d => d.SponsorId)
            .Select(g => new { SponsorId = g.Key, Total = g.Sum(d => d.Amount) })
            .ToListAsync();

        var monthlyDict = monthlyDonations.ToDictionary(m => m.SponsorId, m => m.Total);
        var yearlyDict = yearlyDonations.ToDictionary(y => y.SponsorId, y => y.Total);

        var displays = sponsors.Select(s =>
        {
            var totalTarget = s.Targets.Sum(t => t.TargetAmount);
            var targetDetails = s.Targets.Any()
                ? string.Join(", ", s.Targets.Select(t => $"{t.DonationCategory.Name}: {t.TargetAmount:C}"))
                : "";
            return new SponsorDisplay
            {
                Id = s.Id,
                Name = s.Name,
                Address = s.Address,
                Phone = s.Phone,
                Email = s.Email,
                Notes = s.Notes,
                IsActive = s.IsActive,
                TotalTargetAmount = totalTarget,
                TargetDetails = targetDetails,
                DonationsThisMonth = monthlyDict.GetValueOrDefault(s.Id, 0),
                DonationsThisYear = yearlyDict.GetValueOrDefault(s.Id, 0)
            };
        }).ToList();

        // Apply in-memory sorting
        displays = ApplySort(displays);

        Sponsors = new ObservableCollection<SponsorDisplay>(displays);
    }

    [RelayCommand]
    private void SortBy(string column)
    {
        if (SortColumn == column)
            SortDescending = !SortDescending;
        else
        {
            SortColumn = column;
            SortDescending = false;
        }
        _ = LoadAsync();
    }

    private List<SponsorDisplay> ApplySort(List<SponsorDisplay> list)
    {
        var sorted = SortColumn switch
        {
            "Phone" => SortDescending ? list.OrderByDescending(s => s.Phone).ToList() : list.OrderBy(s => s.Phone).ToList(),
            "Email" => SortDescending ? list.OrderByDescending(s => s.Email).ToList() : list.OrderBy(s => s.Email).ToList(),
            "This Month" => SortDescending ? list.OrderByDescending(s => s.DonationsThisMonth).ToList() : list.OrderBy(s => s.DonationsThisMonth).ToList(),
            "This Year" => SortDescending ? list.OrderByDescending(s => s.DonationsThisYear).ToList() : list.OrderBy(s => s.DonationsThisYear).ToList(),
            "Target %" => SortDescending ? list.OrderByDescending(s => s.TargetProgress).ToList() : list.OrderBy(s => s.TargetProgress).ToList(),
            "Active" => SortDescending ? list.OrderByDescending(s => s.IsActive).ToList() : list.OrderBy(s => s.IsActive).ToList(),
            _ => SortDescending ? list.OrderByDescending(s => s.Name).ToList() : list.OrderBy(s => s.Name).ToList()
        };
        return sorted;
    }

    [RelayCommand]
    private async Task AddNewAsync()
    {
        AllCategories = new ObservableCollection<DonationCategory>(
            await _db.DonationCategories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync());
        SelectedSponsor = null;
        EditName = string.Empty;
        EditAddress = null;
        EditPhone = null;
        EditEmail = null;
        EditNotes = null;
        EditTargets = new ObservableCollection<CategoryTargetEdit>(
            AllCategories.Select(c => new CategoryTargetEdit { CategoryName = c.Name, CategoryId = c.Id, TargetAmount = 0 }));
        EditErrorMessage = null;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (SelectedSponsor == null) return;
        AllCategories = new ObservableCollection<DonationCategory>(
            await _db.DonationCategories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync());

        var sponsor = await _db.Sponsors.Include(s => s.Targets).FirstAsync(s => s.Id == SelectedSponsor.Id);
        EditName = SelectedSponsor.Name;
        EditAddress = SelectedSponsor.Address;
        EditPhone = SelectedSponsor.Phone;
        EditEmail = SelectedSponsor.Email;
        EditNotes = SelectedSponsor.Notes;

        var targetDict = sponsor.Targets.ToDictionary(t => t.DonationCategoryId, t => t.TargetAmount);
        EditTargets = new ObservableCollection<CategoryTargetEdit>(
            AllCategories.Select(c => new CategoryTargetEdit
            {
                CategoryName = c.Name,
                CategoryId = c.Id,
                TargetAmount = targetDict.GetValueOrDefault(c.Id, 0)
            }));
        EditErrorMessage = null;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        EditErrorMessage = null;
        if (string.IsNullOrWhiteSpace(EditName))
        {
            EditErrorMessage = "Name is required.";
            return;
        }

        if (SelectedSponsor == null)
        {
            var sponsor = new Sponsor
            {
                Name = EditName.Trim(),
                Address = EditAddress?.Trim(),
                Phone = EditPhone?.Trim(),
                Email = EditEmail?.Trim(),
                Notes = EditNotes?.Trim()
            };
            _db.Sponsors.Add(sponsor);
            await _db.SaveChangesAsync();

            // Save targets for new sponsor
            await SaveTargetsAsync(sponsor.Id);
        }
        else
        {
            var sponsor = await _db.Sponsors.Include(s => s.Targets).FirstAsync(s => s.Id == SelectedSponsor.Id);
            sponsor.Name = EditName.Trim();
            sponsor.Address = EditAddress?.Trim();
            sponsor.Phone = EditPhone?.Trim();
            sponsor.Email = EditEmail?.Trim();
            sponsor.Notes = EditNotes?.Trim();

            // Remove old targets, save new ones
            _db.SponsorTargets.RemoveRange(sponsor.Targets);
            await _db.SaveChangesAsync();
            await SaveTargetsAsync(sponsor.Id);
        }

        IsEditing = false;
        await LoadAsync();
    }

    private async Task SaveTargetsAsync(int sponsorId)
    {
        var targetsToSave = EditTargets
            .Where(t => t.TargetAmount > 0)
            .Select(t => new SponsorTarget
            {
                SponsorId = sponsorId,
                DonationCategoryId = t.CategoryId,
                TargetAmount = t.TargetAmount
            });
        _db.SponsorTargets.AddRange(targetsToSave);
        await _db.SaveChangesAsync();
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(SponsorDisplay? sponsor)
    {
        if (sponsor == null) return;
        var entity = await _db.Sponsors.FindAsync(sponsor.Id);
        if (entity != null)
        {
            entity.IsActive = !entity.IsActive;
            await _db.SaveChangesAsync();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedSponsor == null) return;

        var sponsor = await _db.Sponsors.FindAsync(SelectedSponsor.Id);
        if (sponsor != null)
        {
            sponsor.IsActive = false;
            await _db.SaveChangesAsync();
        }

        SelectedSponsor = null;
        IsEditing = false;
        await LoadAsync();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditErrorMessage = null;
    }
}

public class SponsorDisplay
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public decimal TotalTargetAmount { get; set; }
    public string TargetDetails { get; set; } = "";
    public decimal DonationsThisMonth { get; set; }
    public decimal DonationsThisYear { get; set; }
    public decimal TargetProgress => TotalTargetAmount > 0
        ? Math.Min(100, Math.Round(DonationsThisYear / TotalTargetAmount * 100, 0))
        : 0;
    public string TargetDisplay => TotalTargetAmount > 0
        ? $"{DonationsThisYear:C} / {TotalTargetAmount:C}"
        : $"{DonationsThisYear:C}";
}

public class CategoryTargetEdit
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public decimal TargetAmount { get; set; }
}
