using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Bookkeeping.Wpf.ViewModels;

public partial class SponsorsViewModel : ObservableValidator
{
    private readonly AppDbContext _db;

    [ObservableProperty] private ObservableCollection<SponsorDisplay> _sponsors = new();
    [ObservableProperty] private SponsorDisplay? _selectedSponsor;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _filterName = string.Empty;
    [ObservableProperty] private string _filterAddress = string.Empty;
    [ObservableProperty] private string _filterPhone = string.Empty;
    [ObservableProperty] private string _filterEmail = string.Empty;
    [ObservableProperty] private int? _filterProgramId;
    [ObservableProperty] private ObservableCollection<OrgProgram> _filterPrograms = new();
    [ObservableProperty] private bool _showInactive = true;
    [ObservableProperty] private string _sortColumn = "Name";
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string? _editAddress;
    [ObservableProperty] [CustomValidation(typeof(SponsorsViewModel), nameof(ValidatePhone))] private string? _editPhone;
    [ObservableProperty] [CustomValidation(typeof(SponsorsViewModel), nameof(ValidateEmail))] private string? _editEmail;
    [ObservableProperty] private string? _editNotes;
    [ObservableProperty] private ObservableCollection<ProgramTargetEdit> _editTargets = new();
    [ObservableProperty] private ObservableCollection<OrgProgram> _allPrograms = new();
    [ObservableProperty] private string? _editErrorMessage;

    public SponsorsViewModel(AppDbContext db) { _db = db; }

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
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(s => s.Name.ToLower().Contains(searchLower!) || (s.Email != null && s.Email.ToLower().Contains(searchLower!)) || (s.Phone != null && s.Phone.ToLower().Contains(searchLower!)));
        if (!string.IsNullOrWhiteSpace(FilterName)) query = query.Where(s => s.Name.ToLower().Contains(FilterName.ToLower()));
        if (!string.IsNullOrWhiteSpace(FilterAddress)) query = query.Where(s => s.Address != null && s.Address.ToLower().Contains(FilterAddress.ToLower()));
        if (!string.IsNullOrWhiteSpace(FilterPhone)) query = query.Where(s => s.Phone != null && s.Phone.ToLower().Contains(FilterPhone.ToLower()));
        if (!string.IsNullOrWhiteSpace(FilterEmail)) query = query.Where(s => s.Email != null && s.Email.ToLower().Contains(FilterEmail.ToLower()));
        if (!ShowInactive) query = query.Where(s => s.IsActive);

        var sponsors = await query.Include(s => s.Targets).ThenInclude(t => t.Program).OrderBy(s => s.Name).ToListAsync();
        var sponsorIds = sponsors.Select(s => s.Id).ToList();

        if (FilterPrograms.Count == 0)
            FilterPrograms = new ObservableCollection<OrgProgram>(await _db.Programs.OrderBy(p => p.Name).ToListAsync());

        var monthlyQuery = _db.Donations.Where(d => sponsorIds.Contains(d.SponsorId) && d.Date >= monthStart && d.Date < tomorrow);
        if (FilterProgramId.HasValue) monthlyQuery = monthlyQuery.Where(d => d.ProgramId == FilterProgramId.Value);
        var monthlyDonations = await monthlyQuery.GroupBy(d => d.SponsorId).Select(g => new { SponsorId = g.Key, Total = g.Sum(d => d.Amount) }).ToListAsync();

        var yearlyQuery = _db.Donations.Where(d => sponsorIds.Contains(d.SponsorId) && d.Date >= yearStart && d.Date < tomorrow);
        if (FilterProgramId.HasValue) yearlyQuery = yearlyQuery.Where(d => d.ProgramId == FilterProgramId.Value);
        var yearlyDonations = await yearlyQuery.GroupBy(d => d.SponsorId).Select(g => new { SponsorId = g.Key, Total = g.Sum(d => d.Amount) }).ToListAsync();

        var monthlyDict = monthlyDonations.ToDictionary(m => m.SponsorId, m => m.Total);
        var yearlyDict = yearlyDonations.ToDictionary(y => y.SponsorId, y => y.Total);

        var displays = sponsors.Select(s =>
        {
            var relevantTargets = s.Targets.Where(t => (t.Year == null || t.Year == today.Year) && (!FilterProgramId.HasValue || t.ProgramId == FilterProgramId.Value)).ToList();
            var totalTarget = relevantTargets.Sum(t => t.TargetAmount);
            var targetDetails = relevantTargets.Any() ? string.Join(", ", relevantTargets.Select(t => $"{t.Program.Name}: {t.TargetAmount:C}")) : "";
            return new SponsorDisplay
            {
                Id = s.Id, Name = s.Name, Address = s.Address, Phone = s.Phone, Email = s.Email, Notes = s.Notes, IsActive = s.IsActive,
                TotalTargetAmount = totalTarget, TargetDetails = targetDetails,
                DonationsThisMonth = monthlyDict.GetValueOrDefault(s.Id, 0), DonationsThisYear = yearlyDict.GetValueOrDefault(s.Id, 0)
            };
        }).ToList();

        displays = ApplySort(displays);
        Sponsors = new ObservableCollection<SponsorDisplay>(displays);
    }

    [RelayCommand] private void SortBy(string column) { if (SortColumn == column) SortDescending = !SortDescending; else { SortColumn = column; SortDescending = false; } _ = LoadAsync(); }
    [RelayCommand] private async Task ClearFilters() { SearchText = string.Empty; FilterName = string.Empty; FilterAddress = string.Empty; FilterPhone = string.Empty; FilterEmail = string.Empty; FilterProgramId = null; await LoadAsync(); }

    private List<SponsorDisplay> ApplySort(List<SponsorDisplay> list) => SortColumn switch
    {
        "Phone" => SortDescending ? list.OrderByDescending(s => s.Phone).ToList() : list.OrderBy(s => s.Phone).ToList(),
        "Email" => SortDescending ? list.OrderByDescending(s => s.Email).ToList() : list.OrderBy(s => s.Email).ToList(),
        "This Month" => SortDescending ? list.OrderByDescending(s => s.DonationsThisMonth).ToList() : list.OrderBy(s => s.DonationsThisMonth).ToList(),
        "This Year" => SortDescending ? list.OrderByDescending(s => s.DonationsThisYear).ToList() : list.OrderBy(s => s.DonationsThisYear).ToList(),
        "Target %" => SortDescending ? list.OrderByDescending(s => s.TargetProgress).ToList() : list.OrderBy(s => s.TargetProgress).ToList(),
        "Active" => SortDescending ? list.OrderByDescending(s => s.IsActive).ToList() : list.OrderBy(s => s.IsActive).ToList(),
        _ => SortDescending ? list.OrderByDescending(s => s.Name).ToList() : list.OrderBy(s => s.Name).ToList()
    };

    [RelayCommand] private async Task AddNewAsync() { AllPrograms = new ObservableCollection<OrgProgram>(await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync()); SelectedSponsor = null; ClearEditFields(); EditTargets = new ObservableCollection<ProgramTargetEdit>(AllPrograms.Select(p => new ProgramTargetEdit { ProgramId = p.Id, ProgramName = p.Name, TargetAmount = 0 })); IsEditing = true; }
    [RelayCommand] private async Task EditAsync() { if (SelectedSponsor == null) return; AllPrograms = new ObservableCollection<OrgProgram>(await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync()); var sponsor = await _db.Sponsors.Include(s => s.Targets).FirstAsync(s => s.Id == SelectedSponsor.Id); EditName = SelectedSponsor.Name; EditAddress = SelectedSponsor.Address; EditPhone = SelectedSponsor.Phone; EditEmail = SelectedSponsor.Email; EditNotes = SelectedSponsor.Notes; var targetDict = sponsor.Targets.Where(t => t.Year == null).ToDictionary(t => t.ProgramId, t => t.TargetAmount); EditTargets = new ObservableCollection<ProgramTargetEdit>(AllPrograms.Select(p => new ProgramTargetEdit { ProgramName = p.Name, ProgramId = p.Id, TargetAmount = targetDict.GetValueOrDefault(p.Id, 0) })); EditErrorMessage = null; IsEditing = true; }
    [RelayCommand]
    private async Task SaveAsync()
    {
        EditErrorMessage = null;
        if (string.IsNullOrWhiteSpace(EditName)) { EditErrorMessage = "Name is required."; return; }
        if (EditTargets.Any(t => !decimal.TryParse(t.TargetAmountText, out _))) { EditErrorMessage = "Enter a valid target amount for every program."; return; }
        if (EditTargets.Any(t => t.TargetAmount < 0)) { EditErrorMessage = "Targets cannot be negative."; return; }
        ValidateAllProperties();
        if (HasErrors)
        {
            EditErrorMessage = string.Join("\n", GetErrors("EditPhone").Cast<ValidationResult>()
                .Concat(GetErrors("EditEmail").Cast<ValidationResult>()).Select(e => e.ErrorMessage));
            return;
        }

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var sponsor = SelectedSponsor == null
                ? new Sponsor()
                : await _db.Sponsors.Include(s => s.Targets).FirstAsync(s => s.Id == SelectedSponsor.Id);
            if (SelectedSponsor == null) _db.Sponsors.Add(sponsor);

            sponsor.Name = EditName.Trim();
            sponsor.Address = EditAddress?.Trim();
            sponsor.Phone = EditPhone?.Trim();
            sponsor.Email = EditEmail?.Trim();
            sponsor.Notes = EditNotes?.Trim();
            UpdateTargets(sponsor);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            EditErrorMessage = $"Could not save sponsor: {ex.InnerException?.Message ?? ex.Message}";
            return;
        }

        IsEditing = false;
        await LoadAsync();
    }

    [RelayCommand] private async Task ToggleActiveAsync(SponsorDisplay? s) { if (s == null) return; var e = await _db.Sponsors.FindAsync(s.Id); if (e != null) { e.IsActive = !e.IsActive; await _db.SaveChangesAsync(); await LoadAsync(); } }
    [RelayCommand] private async Task DeleteAsync() { if (SelectedSponsor == null) return; var s = await _db.Sponsors.FindAsync(SelectedSponsor.Id); if (s != null) { s.IsActive = false; await _db.SaveChangesAsync(); } SelectedSponsor = null; IsEditing = false; await LoadAsync(); }
    [RelayCommand] private void CancelEdit() { IsEditing = false; EditErrorMessage = null; }

    private void ClearEditFields() { EditName = string.Empty; EditAddress = null; EditPhone = null; EditEmail = null; EditNotes = null; EditErrorMessage = null; }
    private void UpdateTargets(Sponsor sponsor)
    {
        // Only edit the timeless targets actually present in the form.
        // Inactive-program and year-specific targets retain their identities.
        foreach (var edit in EditTargets)
        {
            var existing = sponsor.Targets.SingleOrDefault(t => t.ProgramId == edit.ProgramId && t.Year == null);
            if (edit.TargetAmount <= 0)
            {
                if (existing != null) _db.SponsorTargets.Remove(existing);
            }
            else if (existing != null)
            {
                existing.TargetAmount = edit.TargetAmount;
            }
            else
            {
                sponsor.Targets.Add(new SponsorTarget { ProgramId = edit.ProgramId, TargetAmount = edit.TargetAmount });
            }
        }
    }

    public static ValidationResult ValidatePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ValidationResult.Success!;
        var phone = value.Trim();
        if (phone.Count(char.IsDigit) < 7) return new ValidationResult("Phone must contain at least 7 digits.");
        if (!Regex.IsMatch(phone, @"^\+?[\d\s\-\(\)\.]+$")) return new ValidationResult("Phone contains invalid characters.");
        if (!char.IsDigit(phone[0]) && phone[0] != '+') return new ValidationResult("Phone must start with a digit or +.");
        if (!char.IsDigit(phone[^1])) return new ValidationResult("Phone must end with a digit.");
        return ValidationResult.Success!;
    }

    public static ValidationResult ValidateEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ValidationResult.Success!;
        var email = value.Trim();
        if (email.Contains(' ')) return new ValidationResult("Email must not contain spaces.");
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || email.Count(c => c == '@') != 1) return new ValidationResult("Email must contain exactly one '@'.");
        var afterAt = email[(atIndex + 1)..];
        var lastDot = afterAt.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= afterAt.Length - 2) return new ValidationResult("Email must have a valid domain.");
        return ValidationResult.Success!;
    }
}

public class SponsorDisplay
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public decimal TotalTargetAmount { get; set; }
    public string TargetDetails { get; set; } = "";
    public decimal DonationsThisMonth { get; set; }
    public decimal DonationsThisYear { get; set; }
    public decimal TargetProgress => TotalTargetAmount > 0 ? Math.Min(100, Math.Round(DonationsThisYear / TotalTargetAmount * 100, 0)) : 0;
    public string TargetDisplay => TotalTargetAmount > 0 ? $"{DonationsThisYear:C} / {TotalTargetAmount:C}" : $"{DonationsThisYear:C}";
}

public partial class ProgramTargetEdit : ObservableObject
{
    public int ProgramId { get; set; }
    public string ProgramName { get; set; } = "";
    [ObservableProperty] private decimal _targetAmount;

    private bool _editingTargetAmount;
    private string _targetAmountText = "0";
    public string TargetAmountText
    {
        get => _targetAmountText;
        set { SetProperty(ref _targetAmountText, value); if (decimal.TryParse(value, out var amount)) { _editingTargetAmount = true; try { TargetAmount = amount; } finally { _editingTargetAmount = false; } } }
    }
    partial void OnTargetAmountChanged(decimal value)
    { if (_editingTargetAmount) return; _targetAmountText = value.ToString(System.Globalization.CultureInfo.CurrentCulture); OnPropertyChanged(nameof(TargetAmountText)); }

}
