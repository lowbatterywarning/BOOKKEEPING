using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Enums;
using Bookkeeping.Core.Interfaces;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace Bookkeeping.Wpf.ViewModels;

public partial class IncomeViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly IJournalEngine _journal;
    private readonly Services.AuditService _audit;

    [ObservableProperty]
    private ObservableCollection<Donation> _donations = new();

    [ObservableProperty]
    private ObservableCollection<Sponsor> _sponsors = new();
    [ObservableProperty]
    private ObservableCollection<DonationCategory> _categories = new();
    [ObservableProperty]
    private ObservableCollection<OrgProgram> _programs = new();

    // Filter
    [ObservableProperty]
    private DateTime? _filterDateFrom;
    [ObservableProperty]
    private DateTime? _filterDateTo;
    [ObservableProperty]
    private Sponsor? _filterSponsor;
    [ObservableProperty]
    private DonationCategory? _filterCategory;

    // Add form
    [ObservableProperty]
    private bool _isAdding;
    [ObservableProperty]
    private DateTime _newDate = DateTime.Today;
    [ObservableProperty]
    private Sponsor? _newSponsor;
    [ObservableProperty]
    private PaymentMethod _newPaymentMethod = PaymentMethod.Cash;
    [ObservableProperty]
    private DonationCategory? _newCategory;
    [ObservableProperty]
    private OrgProgram? _newProgram;
    [ObservableProperty]
    private decimal _newAmount;
    [ObservableProperty]
    private string? _newReceiptNumber;
    [ObservableProperty]
    private string? _newNotes;
    [ObservableProperty]
    private string? _errorMessage;

    // Add Category form
    [ObservableProperty]
    private bool _isAddingCategory;
    [ObservableProperty]
    private string _newCategoryName = string.Empty;
    [ObservableProperty]
    private string? _newCategoryDescription;
    [ObservableProperty]
    private string? _categoryErrorMessage;

    public IncomeViewModel(AppDbContext db, IJournalEngine journal, Services.AuditService audit)
    {
        _db = db;
        _journal = journal;
        _audit = audit;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        var query = _db.Donations
            .Include(d => d.Sponsor)
            .Include(d => d.DonationCategory)
            .Include(d => d.Program)
            .AsQueryable();

        if (FilterDateFrom.HasValue) query = query.Where(d => d.Date >= FilterDateFrom.Value);
        if (FilterDateTo.HasValue) query = query.Where(d => d.Date <= FilterDateTo.Value);
        if (FilterSponsor != null) query = query.Where(d => d.SponsorId == FilterSponsor.Id);
        if (FilterCategory != null) query = query.Where(d => d.DonationCategoryId == FilterCategory.Id);

        var donations = await query.OrderByDescending(d => d.Date).ThenByDescending(d => d.Id).Take(200).ToListAsync();
        Donations = new ObservableCollection<Donation>(donations);

        Sponsors = new ObservableCollection<Sponsor>(await _db.Sponsors.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync());
        Categories = new ObservableCollection<DonationCategory>(await _db.DonationCategories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync());
        Programs = new ObservableCollection<OrgProgram>(await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync());
    }

    [RelayCommand]
    private void ShowAddForm()
    {
        IsAddingCategory = false;
        NewDate = DateTime.Today;
        NewSponsor = null;
        NewPaymentMethod = PaymentMethod.Cash;
        NewCategory = null;
        NewProgram = null;
        NewAmount = 0;
        NewReceiptNumber = null;
        NewNotes = null;
        ErrorMessage = null;
        IsAdding = true;
    }

    [RelayCommand]
    private void CancelAdd()
    {
        IsAdding = false;
    }

    [RelayCommand]
    private async Task SaveDonationAsync()
    {
        ErrorMessage = null;

        if (NewSponsor == null) { ErrorMessage = "Please select a sponsor."; return; }
        if (NewCategory == null) { ErrorMessage = "Please select a donation category."; return; }
        if (NewAmount <= 0) { ErrorMessage = "Amount must be greater than zero."; return; }

        var donation = new Donation
        {
            Date = NewDate,
            SponsorId = NewSponsor.Id,
            PaymentMethod = NewPaymentMethod,
            DonationCategoryId = NewCategory.Id,
            ProgramId = NewProgram?.Id,
            Amount = NewAmount,
            ReceiptNumber = NewReceiptNumber?.Trim(),
            Notes = NewNotes?.Trim(),
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow
        };

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var entry = await _journal.RecordDonationAsync(donation);
            donation.JournalEntry = entry;
            _db.Donations.Add(donation);
            await _db.SaveChangesAsync();

            _audit.LogCreate(1, "Donation", donation.Id, $"{donation.Amount:C} from sponsor #{donation.SponsorId}");

            await transaction.CommitAsync();

            IsAdding = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            var msg = ex.Message;
            var inner = ex.InnerException;
            while (inner != null)
            {
                msg += "\n\n→ " + inner.Message;
                inner = inner.InnerException;
            }
            ErrorMessage = $"Error saving donation: {msg}";
        }
    }

    [RelayCommand]
    private async Task DeleteDonationAsync(Donation? donation)
    {
        if (donation == null) return;
        // Remove the donation first — it holds the FK to JournalEntry
        _db.Donations.Remove(donation);
        var entry = await _db.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == donation.JournalEntryId);
        if (entry != null)
        {
            _db.JournalEntryLines.RemoveRange(entry.Lines);
            _db.JournalEntries.Remove(entry);
        }
        _audit.LogDelete(1, "Donation", donation.Id, $"{donation.Amount:C}");
        await _db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearFilters()
    {
        FilterDateFrom = null;
        FilterDateTo = null;
        FilterSponsor = null;
        FilterCategory = null;
        await LoadAsync();
    }

    // ---- Category Management ----

    [RelayCommand]
    private void ShowAddCategory()
    {
        IsAdding = false;
        NewCategoryName = string.Empty;
        NewCategoryDescription = null;
        CategoryErrorMessage = null;
        IsAddingCategory = true;
    }

    [RelayCommand]
    private void CancelAddCategory()
    {
        IsAddingCategory = false;
    }

    [RelayCommand]
    private async Task SaveCategoryAsync()
    {
        CategoryErrorMessage = null;
        if (string.IsNullOrWhiteSpace(NewCategoryName))
        {
            CategoryErrorMessage = "Category name is required.";
            return;
        }

        var exists = await _db.DonationCategories.AnyAsync(c => c.Name == NewCategoryName.Trim());
        if (exists)
        {
            CategoryErrorMessage = "A category with this name already exists.";
            return;
        }

        var generalFund = await _db.Funds.FirstOrDefaultAsync(f => f.Name == "General Fund")
            ?? await _db.Funds.FirstOrDefaultAsync(f => !f.IsRestricted);
        if (generalFund == null)
        {
            CategoryErrorMessage = "No fund exists. Please create a fund first.";
            return;
        }

        // Generate the next income account code
        var maxCode = await _db.Accounts
            .Where(a => a.Code.StartsWith("4"))
            .MaxAsync(a => (string?)a.Code);
        var nextCode = string.IsNullOrEmpty(maxCode) ? "4001"
            : int.TryParse(maxCode[1..], out int suffix) && suffix < 9999
                ? $"4{(suffix + 1):D3}"
                : $"4{DateTime.UtcNow:MMddHHmm}";

        var incomeAccount = new Account
        {
            Code = nextCode,
            Name = $"Donation Income - {NewCategoryName.Trim()}",
            AccountType = AccountType.Income,
            FundId = generalFund.Id,
            IsSystem = false,
            CreatedAt = DateTime.UtcNow
        };
        _db.Accounts.Add(incomeAccount);

        var category = new DonationCategory
        {
            Name = NewCategoryName.Trim(),
            Description = NewCategoryDescription?.Trim(),
            FundId = generalFund.Id,
            IncomeAccount = incomeAccount,
            IsActive = true
        };

        _db.DonationCategories.Add(category);
        await _db.SaveChangesAsync();

        IsAddingCategory = false;
        await LoadAsync();
    }

    public PaymentMethod[] PaymentMethods => Enum.GetValues<PaymentMethod>();
}
