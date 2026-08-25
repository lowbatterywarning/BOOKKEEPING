using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Linq;

namespace Bookkeeping.Wpf.ViewModels;

public partial class ProgramsViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    [ObservableProperty]
    private ObservableCollection<ProgramDisplay> _programs = new();

    [ObservableProperty]
        private ObservableCollection<Fund> _funds = new();

    [ObservableProperty]
        private Fund? _selectedFund;

        [ObservableProperty]
        private ProgramDisplay? _selectedProgram;

        [ObservableProperty]
        private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;
    [ObservableProperty]
    private string? _editDescription;
    [ObservableProperty]
    private string? _editErrorMessage;

    public ProgramsViewModel(AppDbContext db)
    {
        _db = db;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        var programs = await _db.Programs.OrderBy(p => p.Name).ToListAsync();
        var programIds = programs.Select(p => p.Id).ToList();

                // Load funds for the picker
                var funds = await _db.Funds.OrderBy(f => f.Name).ToListAsync();
                Funds = new ObservableCollection<Fund>(funds);

                // Single grouped queries instead of per-program queries
        var incomeByProgram = await _db.Donations
            .Where(d => programIds.Contains(d.ProgramId))
            .GroupBy(d => d.ProgramId)
            .Select(g => new { ProgramId = g.Key, Total = g.Sum(d => d.Amount) })
            .ToListAsync();

        var expensesByProgram = await _db.Expenses
            .Where(e => programIds.Contains(e.ProgramId))
            .GroupBy(e => e.ProgramId)
            .Select(g => new { ProgramId = g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync();

        var incomeDict = incomeByProgram.ToDictionary(x => x.ProgramId, x => x.Total);
        var expenseDict = expensesByProgram.ToDictionary(x => x.ProgramId, x => x.Total);

        var displays = programs.Select(p =>
        {
            var income = incomeDict.GetValueOrDefault(p.Id, 0);
            var expenses = expenseDict.GetValueOrDefault(p.Id, 0);
            return new ProgramDisplay
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                IsActive = p.IsActive,
                TotalIncome = income,
                TotalExpenses = expenses,
                                Balance = income - expenses,
                                FundId = p.FundId
                            };
                        }).ToList();

        Programs = new ObservableCollection<ProgramDisplay>(displays);
    }

    [RelayCommand]
    private void AddNew()
    {
        SelectedProgram = null;
        EditName = string.Empty;
        EditDescription = null;
        EditErrorMessage = null;

            // Default to General Fund if available, otherwise first fund
            SelectedFund = Funds.FirstOrDefault(f => f.Name == "General Fund") ?? Funds.FirstOrDefault();

            IsEditing = true;
        }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedProgram == null) return;
        EditName = SelectedProgram.Name;
        EditDescription = SelectedProgram.Description;
        EditErrorMessage = null;

            // Select the fund for the existing program
            SelectedFund = Funds.FirstOrDefault(f => f.Id == SelectedProgram.FundId);

            IsEditing = true;
        }

    [RelayCommand]
    private async Task SaveAsync()
    {
        EditErrorMessage = null;
        if (string.IsNullOrWhiteSpace(EditName))
        {
            EditErrorMessage = "Program name is required.";
            return;
        }

                if (SelectedFund == null)
        {
                    EditErrorMessage = "A fund must be selected.";
                    return;
                }

                if (SelectedProgram == null)
                {
                    _db.Programs.Add(new OrgProgram
                    {
                        Name = EditName.Trim(),
                        Description = EditDescription?.Trim(),
                        FundId = SelectedFund.Id
                    });
                }
                else
                {
                    var program = await _db.Programs.FindAsync(SelectedProgram.Id);
                    if (program != null)
                    {
                        program.Name = EditName.Trim();
                        program.Description = EditDescription?.Trim();
                        program.FundId = SelectedFund.Id;
                    }
                }

        await _db.SaveChangesAsync();
        IsEditing = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(ProgramDisplay? program)
    {
        if (program == null) return;
        var entity = await _db.Programs.FindAsync(program.Id);
        if (entity != null)
        {
            entity.IsActive = !entity.IsActive;
            await _db.SaveChangesAsync();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditErrorMessage = null;
    }
}

public class ProgramDisplay
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal Balance { get; set; }
    public int FundId { get; set; }
}
