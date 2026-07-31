using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bookkeeping.Wpf.ViewModels;

public partial class SponsorsViewModel : ObservableValidator
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
    [CustomValidation(typeof(SponsorsViewModel), nameof(ValidatePhone))]
    private string? _editPhone;
    [ObservableProperty]
    [CustomValidation(typeof(SponsorsViewModel), nameof(ValidateEmail))]
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

        var searchLower = SearchText?.ToLowerInvariant();
        var filterNameLower = FilterName?.ToLowerInvariant();
        var filterAddressLower = FilterAddress?.ToLowerInvariant();
        var filterPhoneLower = FilterPhone?.ToLowerInvariant();
        var filterEmailLower = FilterEmail?.ToLowerInvariant();

        // Quick search (searches name, email, phone) - case insensitive
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(s => s.Name.ToLower().Contains(searchLower!)
                || (s.Email != null && s.Email.ToLower().Contains(searchLower!))
                || (s.Phone != null && s.Phone.ToLower().Contains(searchLower!)));

        // Detailed filters - case insensitive
        if (!string.IsNullOrWhiteSpace(FilterName))
            query = query.Where(s => s.Name.ToLower().Contains(filterNameLower!));
        if (!string.IsNullOrWhiteSpace(FilterAddress))
            query = query.Where(s => s.Address != null && s.Address.ToLower().Contains(filterAddressLower!));
        if (!string.IsNullOrWhiteSpace(FilterPhone))
            query = query.Where(s => s.Phone != null && s.Phone.ToLower().Contains(filterPhoneLower!));
        if (!string.IsNullOrWhiteSpace(FilterEmail))
            query = query.Where(s => s.Email != null && s.Email.ToLower().Contains(filterEmailLower!));
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

        // Name is required
        if (string.IsNullOrWhiteSpace(EditName))
        {
            EditErrorMessage = "Name is required.";
            return;
        }

        // Run all property-level validations (phone, email custom validators)
        ValidateAllProperties();
        if (HasErrors)
        {
            EditErrorMessage = string.Join("\n",
                GetErrors("EditPhone").Cast<ValidationResult>().Select(e => e.ErrorMessage)
                .Concat(GetErrors("EditEmail").Cast<ValidationResult>().Select(e => e.ErrorMessage)));
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

    // --- Custom validation methods (called by [CustomValidation] attributes) ---

    /// <summary>
    /// Validates the phone number in real-time as the user types.
    /// </summary>
    public static ValidationResult ValidatePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Success!;

        var phone = value.Trim();
        var digitCount = phone.Count(char.IsDigit);
        if (digitCount < 7)
            return new ValidationResult("Phone number must contain at least 7 digits.");

        if (!Regex.IsMatch(phone, @"^\+?[\d\s\-\(\)\.]+$"))
            return new ValidationResult("Phone number contains invalid characters.");

        if (!char.IsDigit(phone[0]) && phone[0] != '+')
            return new ValidationResult("Phone number must start with a digit or +.");

        if (!char.IsDigit(phone[^1]))
            return new ValidationResult("Phone number must end with a digit.");

        return ValidationResult.Success!;
    }

    /// <summary>
    /// Validates the email address in real-time as the user types.
    /// </summary>
    public static ValidationResult ValidateEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Success!;

        var email = value.Trim();

        if (email.Contains(' '))
            return new ValidationResult("Email address must not contain spaces.");

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || email.Count(c => c == '@') != 1)
            return new ValidationResult("Email address must contain exactly one '@' with text before it.");

        var afterAt = email[(atIndex + 1)..];
        var lastDot = afterAt.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= afterAt.Length - 2)
            return new ValidationResult("Email address must have a valid domain (e.g. example.com).");

        return ValidationResult.Success!;
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
