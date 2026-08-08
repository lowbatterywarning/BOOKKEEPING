using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace Bookkeeping.Wpf.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<Donation> _donationResults = new();
    [ObservableProperty] private ObservableCollection<Expense> _expenseResults = new();
    [ObservableProperty] private string? _statusMessage;

    public SearchViewModel(AppDbContext db) { _db = db; }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) { StatusMessage = "Enter a search term."; return; }
        _db.ChangeTracker.Clear();
        var query = SearchQuery.ToLower().Trim();

        var donations = await _db.Donations.Include(d => d.Sponsor).Include(d => d.Program)
            .Where(d => d.Sponsor.Name.ToLower().Contains(query) || d.Program.Name.ToLower().Contains(query) || (d.Notes != null && d.Notes.ToLower().Contains(query)) || (d.ReceiptNumber != null && d.ReceiptNumber.ToLower().Contains(query)))
            .OrderByDescending(d => d.Date).Take(50).ToListAsync();
        DonationResults = new ObservableCollection<Donation>(donations);

        var expenses = await _db.Expenses.Include(e => e.Program)
            .Where(e => e.VendorName.ToLower().Contains(query) || e.Program.Name.ToLower().Contains(query) || (e.Notes != null && e.Notes.ToLower().Contains(query)))
            .OrderByDescending(e => e.Date).Take(50).ToListAsync();
        ExpenseResults = new ObservableCollection<Expense>(expenses);

        StatusMessage = $"Found {donations.Count} donations and {expenses.Count} expenses.";
    }

    public void ClearSearch()
    {
        SearchQuery = string.Empty;
        DonationResults = new ObservableCollection<Donation>();
        ExpenseResults = new ObservableCollection<Expense>();
        StatusMessage = null;
    }
}
