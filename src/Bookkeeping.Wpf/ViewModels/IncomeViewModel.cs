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
    private ObservableCollection<OrgProgram> _programs = new();

    // Filter
    [ObservableProperty]
    private DateTime? _filterDateFrom;
    [ObservableProperty]
    private DateTime? _filterDateTo;
    [ObservableProperty]
    private Sponsor? _filterSponsor;
    [ObservableProperty]
    private OrgProgram? _filterProgram;

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
    private OrgProgram? _newProgram;
    [ObservableProperty]
    private decimal _newAmount;
    [ObservableProperty]
    private string? _newReceiptNumber;
    [ObservableProperty]
    private string? _newNotes;
    [ObservableProperty]
    private string? _errorMessage;



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
            .Include(d => d.Program)
            .AsQueryable();

        if (FilterDateFrom.HasValue) query = query.Where(d => d.Date >= FilterDateFrom.Value);
        if (FilterDateTo.HasValue) query = query.Where(d => d.Date <= FilterDateTo.Value);
        if (FilterSponsor != null) query = query.Where(d => d.SponsorId == FilterSponsor.Id);
        if (FilterProgram != null) query = query.Where(d => d.ProgramId == FilterProgram.Id);

        var donations = await query.OrderByDescending(d => d.Date).ThenByDescending(d => d.Id).Take(200).ToListAsync();
        Donations = new ObservableCollection<Donation>(donations);

        Sponsors = new ObservableCollection<Sponsor>(await _db.Sponsors.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync());
        Programs = new ObservableCollection<OrgProgram>(await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync());
    }

    [RelayCommand]
    private void ShowAddForm()
    {
        NewDate = DateTime.Today;
        NewSponsor = null;
        NewPaymentMethod = PaymentMethod.Cash;
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
        if (NewProgram == null) { ErrorMessage = "Please select a program."; return; }
        if (NewAmount <= 0) { ErrorMessage = "Amount must be greater than zero."; return; }

        var donation = new Donation
        {
            Date = NewDate,
            SponsorId = NewSponsor.Id,
            PaymentMethod = NewPaymentMethod,
            ProgramId = NewProgram.Id,
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
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            IsAdding = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();
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
        FilterProgram = null;
        await LoadAsync();
    }

    public PaymentMethod[] PaymentMethods => Enum.GetValues<PaymentMethod>();
}
