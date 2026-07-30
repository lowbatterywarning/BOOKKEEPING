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
    private ObservableCollection<ExpenseCategory> _categories = new();
    [ObservableProperty]
    private ObservableCollection<OrgProgram> _programs = new();

    // Filters
    [ObservableProperty]
    private DateTime? _filterDateFrom;
    [ObservableProperty]
    private DateTime? _filterDateTo;
    [ObservableProperty]
    private ExpenseCategory? _filterCategory;
    [ObservableProperty]
    private OrgProgram? _filterProgram;

    // Add form
    [ObservableProperty]
    private bool _isAdding;
    [ObservableProperty]
    private DateTime _newDate = DateTime.Today;
    [ObservableProperty]
    private string _newVendorName = string.Empty;
    [ObservableProperty]
    private PaymentMethod _newPaymentMethod = PaymentMethod.Cash;
    [ObservableProperty]
    private ExpenseCategory? _newCategory;
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

    // Add Category form
    [ObservableProperty]
    private bool _isAddingCategory;
    [ObservableProperty]
    private string _newCategoryName = string.Empty;
    [ObservableProperty]
    private string? _newCategoryDescription;
    [ObservableProperty]
    private string? _categoryErrorMessage;

    private readonly string _attachmentsFolder;

    public ExpensesViewModel(AppDbContext db, IJournalEngine journal, Services.AuditService audit, Services.ExportService exportService)
    {
        _db = db;
        _journal = journal;
        _audit = audit;
        _exportService = exportService;
        _attachmentsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bookkeeping", "attachments");
        Directory.CreateDirectory(_attachmentsFolder);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        var query = _db.Expenses
            .Include(e => e.ExpenseCategory)
            .Include(e => e.Program)
            .AsQueryable();

        if (FilterDateFrom.HasValue) query = query.Where(e => e.Date >= FilterDateFrom.Value);
        if (FilterDateTo.HasValue) query = query.Where(e => e.Date <= FilterDateTo.Value);
        if (FilterCategory != null) query = query.Where(e => e.ExpenseCategoryId == FilterCategory.Id);
        if (FilterProgram != null) query = query.Where(e => e.ProgramId == FilterProgram.Id);

        var expenses = await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Take(200).ToListAsync();
        Expenses = new ObservableCollection<Expense>(expenses);

        Categories = new ObservableCollection<ExpenseCategory>(await _db.ExpenseCategories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync());
        Programs = new ObservableCollection<OrgProgram>(await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync());
    }

    [RelayCommand]
    private void ShowAddForm()
    {
        IsAddingCategory = false;
        NewDate = DateTime.Today;
        NewVendorName = string.Empty;
        NewPaymentMethod = PaymentMethod.Cash;
        NewCategory = null;
        NewProgram = null;
        NewAmount = 0;
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

        if (string.IsNullOrWhiteSpace(NewVendorName)) { ErrorMessage = "Vendor name is required."; return; }
        if (NewCategory == null) { ErrorMessage = "Please select an expense category."; return; }
        if (NewAmount <= 0) { ErrorMessage = "Amount must be greater than zero."; return; }

        var expense = new Expense
        {
            Date = NewDate,
            VendorName = NewVendorName.Trim(),
            PaymentMethod = NewPaymentMethod,
            ExpenseCategoryId = NewCategory.Id,
            ProgramId = NewProgram?.Id,
            Amount = NewAmount,
            Notes = NewNotes?.Trim(),
            ReceiptAttachmentPath = NewReceiptPath,
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow
        };

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var entry = await _journal.RecordExpenseAsync(expense);
            expense.JournalEntry = entry;
            _db.Expenses.Add(expense);
            await _db.SaveChangesAsync();

            _audit.LogCreate(1, "Expense", expense.Id, $"{expense.Amount:C} to {expense.VendorName}");

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
            ErrorMessage = $"Error saving expense: {msg}";
        }
    }

    [RelayCommand]
    private async Task DeleteExpenseAsync(Expense? expense)
    {
        if (expense == null) return;
        var entry = await _db.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == expense.JournalEntryId);
        if (entry != null)
        {
            _db.JournalEntryLines.RemoveRange(entry.Lines);
            _db.JournalEntries.Remove(entry);
        }

        if (!string.IsNullOrEmpty(expense.ReceiptAttachmentPath))
        {
            var fullPath = Path.Combine(_attachmentsFolder, expense.ReceiptAttachmentPath);
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }

        _db.Expenses.Remove(expense);
        _audit.LogDelete(1, "Expense", expense.Id, $"{expense.Amount:C} to {expense.VendorName}");
        await _db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenReceipt(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var fullPath = Path.Combine(_attachmentsFolder, path);
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
        FilterCategory = null;
        FilterProgram = null;
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

        var exists = await _db.ExpenseCategories.AnyAsync(c => c.Name == NewCategoryName.Trim());
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

        // Generate the next expense account code
        var maxCode = await _db.Accounts
            .Where(a => a.Code.StartsWith("5"))
            .MaxAsync(a => (string?)a.Code);
        var nextCode = string.IsNullOrEmpty(maxCode) ? "5001"
            : int.TryParse(maxCode[1..], out int suffix) && suffix < 9999
                ? $"5{(suffix + 1):D3}"
                : $"5{DateTime.UtcNow:MMddHHmm}";

        var expenseAccount = new Account
        {
            Code = nextCode,
            Name = $"Expense - {NewCategoryName.Trim()}",
            AccountType = AccountType.Expense,
            FundId = generalFund.Id,
            IsSystem = false,
            CreatedAt = DateTime.UtcNow
        };
        _db.Accounts.Add(expenseAccount);

        var category = new ExpenseCategory
        {
            Name = NewCategoryName.Trim(),
            Description = NewCategoryDescription?.Trim(),
            FundId = generalFund.Id,
            ExpenseAccount = expenseAccount,
            IsActive = true
        };

        _db.ExpenseCategories.Add(category);
        await _db.SaveChangesAsync();

        IsAddingCategory = false;
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
