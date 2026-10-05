using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Enums;
using Bookkeeping.Core.Interfaces;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using System.IO;

namespace Bookkeeping.Wpf.ViewModels;

public partial class ExpensesViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly IJournalEngine _journal;
    private readonly Services.AuditService _audit;
    private readonly Services.ExportService _exportService;

    [ObservableProperty]
    private ObservableCollection<Expense> _expenses = new();
    [ObservableProperty]
    private ObservableCollection<OrgProgram> _programs = new();

    // Filters
    [ObservableProperty]
    private DateTime? _filterDateFrom;
    [ObservableProperty]
    private DateTime? _filterDateTo;
    [ObservableProperty]
    private OrgProgram? _filterProgram;

    // Add form
    [ObservableProperty]
    private bool _isAdding;
    [ObservableProperty]
    private DateTime? _newDate = DateTime.Today;
    [ObservableProperty]
    private string _newVendorName = string.Empty;
    [ObservableProperty]
    private PaymentMethod _newPaymentMethod = PaymentMethod.Cash;
    [ObservableProperty]
    private OrgProgram? _newProgram;
    [ObservableProperty]
    private decimal _newAmount;
    [ObservableProperty]
    private string? _newNotes;
    [ObservableProperty]
    private string? _newReceiptPath;
    [ObservableProperty]
    private string? _errorMessage;

    private readonly string _attachmentsFolder;

    public ExpensesViewModel(AppDbContext db, IJournalEngine journal, Services.AuditService audit, Services.ExportService exportService, string? attachmentsFolder = null)
    {
        _db = db;
        _journal = journal;
        _audit = audit;
        _exportService = exportService;
        _attachmentsFolder = attachmentsFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bookkeeping", "attachments");
        Directory.CreateDirectory(_attachmentsFolder);
    }


    private bool _editingNewAmount;
    private string _newAmountText = "0";
    public string NewAmountText
    {
        get => _newAmountText;
        set { SetProperty(ref _newAmountText, value); if (decimal.TryParse(value, out var amount)) { _editingNewAmount = true; try { NewAmount = amount; } finally { _editingNewAmount = false; } } }
    }
    partial void OnNewAmountChanged(decimal value)
    { if (_editingNewAmount) return; _newAmountText = value.ToString(System.Globalization.CultureInfo.CurrentCulture); OnPropertyChanged(nameof(NewAmountText)); }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        var query = _db.Expenses
            .Include(e => e.Program)
            .AsQueryable();

        if (FilterDateFrom.HasValue) query = query.Where(e => e.Date >= FilterDateFrom.Value);
        if (FilterDateTo.HasValue) query = query.Where(e => e.Date <= FilterDateTo.Value);
        if (FilterProgram != null) query = query.Where(e => e.ProgramId == FilterProgram.Id);

        var expenses = await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Take(200).ToListAsync();
        Expenses = new ObservableCollection<Expense>(expenses);

        PickerRefresh.Update(Programs, await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync());
    }

    [RelayCommand]
    private void ShowAddForm()
    {
        NewDate = DateTime.Today;
        NewVendorName = string.Empty;
        NewPaymentMethod = PaymentMethod.Cash;
        NewProgram = null;
        NewAmount = 0;
        NewAmountText = "0";
        NewNotes = null;
        NewReceiptPath = null;
        ErrorMessage = null;
        IsAdding = true;
    }

    [RelayCommand]
    private void CancelAdd()
    {
        IsAdding = false;
    }

    [RelayCommand]
    private void BrowseReceipt()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Receipt Attachment",
            Filter = "All Files (*.*)|*.*|Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PDF (*.pdf)|*.pdf",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Path.GetFileName(dialog.FileName)}";
            var destPath = Path.Combine(_attachmentsFolder, fileName);
            File.Copy(dialog.FileName, destPath, overwrite: true);
            NewReceiptPath = fileName;
        }
    }

    [RelayCommand]
    private async Task SaveExpenseAsync()
    {
        ErrorMessage = null;
        if (!NewDate.HasValue) { ErrorMessage = "Please enter a valid date."; return; }

        if (string.IsNullOrWhiteSpace(NewVendorName)) { ErrorMessage = "Vendor name is required."; return; }
        if (NewProgram == null) { ErrorMessage = "Please select a program."; return; }
        if (!decimal.TryParse(NewAmountText, out _) ) { ErrorMessage = "Enter a valid amount."; return; }
        if (NewAmount <= 0) { ErrorMessage = "Amount must be greater than zero."; return; }

        if (!string.IsNullOrEmpty(NewReceiptPath))
        { try { Services.ReceiptPaths.Resolve(_attachmentsFolder, NewReceiptPath); } catch (InvalidDataException ex) { ErrorMessage = ex.Message; return; } }

        var expense = new Expense
        {
            Date = NewDate.Value,
            VendorName = NewVendorName.Trim(),
            PaymentMethod = NewPaymentMethod,
            ProgramId = NewProgram.Id,
            Amount = NewAmount,
            Notes = NewNotes?.Trim(),
            ReceiptAttachmentPath = NewReceiptPath,
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var entry = await _journal.RecordExpenseAsync(expense);
            expense.JournalEntry = entry;
            _db.Expenses.Add(expense);
            await _db.SaveChangesAsync();

            _audit.LogCreate(1, "Expense", expense.Id, $"{expense.Amount:C} to {expense.VendorName}");
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            var msg = ex.Message;
            var inner = ex.InnerException;
            while (inner != null)
            {
                msg += "\n\n→ " + inner.Message;
                inner = inner.InnerException;
            }
            ErrorMessage = $"Error saving expense: {msg}";
            return;
        }

        IsAdding = false;
        try { await LoadAsync(); }
        catch (Exception ex) { ErrorMessage = $"Expense saved. Refresh failed: {ex.Message}. Reload the screen; do not save again."; }
    }

    [RelayCommand]
    private async Task DeleteExpenseAsync(Expense? expense)
    {
        if (expense == null) return;
        _db.Expenses.Remove(expense);

        var receiptPath = expense.ReceiptAttachmentPath;

        var entry = await _db.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == expense.JournalEntryId);
        if (entry != null)
        {
            _db.JournalEntryLines.RemoveRange(entry.Lines);
            _db.JournalEntries.Remove(entry);
        }

        _audit.LogDelete(1, "Expense", expense.Id, $"{expense.Amount:C} to {expense.VendorName}");
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(receiptPath))
        {
            try { var fullPath = Services.ReceiptPaths.Resolve(_attachmentsFolder, receiptPath); if (File.Exists(fullPath)) File.Delete(fullPath); }
            catch (InvalidDataException) { ErrorMessage = "Expense deleted. Its unsafe receipt path was ignored."; }
            catch (IOException ex) { ErrorMessage = $"Expense deleted. Receipt could not be removed: {ex.Message}"; }
            catch (UnauthorizedAccessException ex) { ErrorMessage = $"Expense deleted. Receipt could not be removed: {ex.Message}"; }
        }

        await LoadAsync();
    }

    [RelayCommand]
    private void OpenReceipt(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        string fullPath;
        try { fullPath = Services.ReceiptPaths.Resolve(_attachmentsFolder, path); }
        catch (InvalidDataException ex) { ErrorMessage = ex.Message; return; }
        if (File.Exists(fullPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private async Task ClearFilters()
    {
        FilterDateFrom = null;
        FilterDateTo = null;
        FilterProgram = null;
        await LoadAsync();
    }

    [RelayCommand]
    private void PrintCheck(Expense? expense)
    {
        if (expense == null) return;
        var path = Services.ExportService.ShowCheckSaveDialog(expense.VendorName);
        if (path == null) return;

        var orgName = _db.AppSettings.FirstOrDefault(s => s.Key == "OrganizationName")?.Value ?? "Organization";
        _exportService.PrintCheck(expense.VendorName, expense.Amount, expense.Notes, orgName, path);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public PaymentMethod[] PaymentMethods => Enum.GetValues<PaymentMethod>();
}
