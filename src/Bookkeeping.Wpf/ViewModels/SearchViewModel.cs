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
    [ObservableProperty] private string _searchType = "All";
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty] private ObservableCollection<Donation> _donationResults = new();
    [ObservableProperty] private ObservableCollection<Expense> _expenseResults = new();
    [ObservableProperty] private ObservableCollection<Sponsor> _sponsorResults = new();

    public string[] SearchTypes => new[] { "All", "Donations", "Expenses", "Sponsors" };

    public SearchViewModel(AppDbContext db) => _db = db;

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        StatusMessage = "Searching...";

        var query = SearchQuery.Trim().ToLower();

        try
        {
            if (SearchType is "All" or "Donations")
            {
                var donations = await _db.Donations
                    .Include(d => d.Sponsor).Include(d => d.DonationCategory).Include(d => d.Program)
                    .Where(d => d.Sponsor.Name.ToLower().Contains(query)
                        || d.DonationCategory.Name.ToLower().Contains(query)
                        || (d.Notes != null && d.Notes.ToLower().Contains(query))
                        || (d.ReceiptNumber != null && d.ReceiptNumber.ToLower().Contains(query))
                        || (d.Program != null && d.Program.Name.ToLower().Contains(query))
                        || d.Amount.ToString().Contains(query))
                    .OrderByDescending(d => d.Date).Take(50).ToListAsync();
                DonationResults = new ObservableCollection<Donation>(donations);
            }

            if (SearchType is "All" or "Expenses")
            {
                var expenses = await _db.Expenses
                    .Include(e => e.ExpenseCategory).Include(e => e.Program)
                    .Where(e => e.VendorName.ToLower().Contains(query)
                        || e.ExpenseCategory.Name.ToLower().Contains(query)
                        || (e.Notes != null && e.Notes.ToLower().Contains(query))
                        || (e.Program != null && e.Program.Name.ToLower().Contains(query))
                        || e.Amount.ToString().Contains(query))
                    .OrderByDescending(e => e.Date).Take(50).ToListAsync();
                ExpenseResults = new ObservableCollection<Expense>(expenses);
            }

            if (SearchType is "All" or "Sponsors")
            {
                var sponsors = await _db.Sponsors
                    .Where(s => s.Name.ToLower().Contains(query)
                        || (s.Email != null && s.Email.ToLower().Contains(query))
                        || (s.Phone != null && s.Phone.ToLower().Contains(query))
                        || (s.Notes != null && s.Notes.ToLower().Contains(query)))
                    .OrderBy(s => s.Name).Take(50).ToListAsync();
                SponsorResults = new ObservableCollection<Sponsor>(sponsors);
            }

            var total = DonationResults.Count + ExpenseResults.Count + SponsorResults.Count;
            StatusMessage = $"Found {total} results for \"{SearchQuery}\"";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchQuery = string.Empty;
        DonationResults.Clear();
        ExpenseResults.Clear();
        SponsorResults.Clear();
        StatusMessage = null;
    }
}
