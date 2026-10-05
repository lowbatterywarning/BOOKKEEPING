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
    [ObservableProperty] private int _selectedResultTab;
    [ObservableProperty] private ObservableCollection<Donation> _donationResults = new();
    [ObservableProperty] private ObservableCollection<Expense> _expenseResults = new();
    [ObservableProperty] private ObservableCollection<Sponsor> _sponsorResults = new();
    [ObservableProperty] private string? _statusMessage;
    public string[] SearchTypes => ["All", "Donations", "Expenses", "Sponsors"];
    public SearchViewModel(AppDbContext db) { _db = db; }
    partial void OnSearchTypeChanged(string value) { SelectedResultTab = value == "Sponsors" ? 2 : value == "Expenses" ? 1 : 0; ClearResults(); StatusMessage = "Search type changed. Click Search."; }
    private void ClearResults() { DonationResults.Clear(); ExpenseResults.Clear(); SponsorResults.Clear(); }
    [RelayCommand]
    public async Task SearchAsync()
    {
        ClearResults();
        if (string.IsNullOrWhiteSpace(SearchQuery)) { StatusMessage = "Enter a search term."; return; }
        var query = SearchQuery.ToLower().Trim();
        var counts = new List<string>();
        if (SearchType is "All" or "Donations")
        {
            var matches = _db.Donations.AsNoTracking().Include(d => d.Sponsor).Include(d => d.Program)
                .Where(d => d.Sponsor.Name.ToLower().Contains(query) || d.Program.Name.ToLower().Contains(query) || (d.Notes != null && d.Notes.ToLower().Contains(query)) || (d.ReceiptNumber != null && d.ReceiptNumber.ToLower().Contains(query)));
            var count = await matches.CountAsync();
            DonationResults = new(await matches.OrderByDescending(d => d.Date).ThenByDescending(d => d.Id).Take(50).ToListAsync());
            counts.Add($"donations: showing {DonationResults.Count} of {count}");
        }
        if (SearchType is "All" or "Expenses")
        {
            var matches = _db.Expenses.AsNoTracking().Include(e => e.Program)
                .Where(e => e.VendorName.ToLower().Contains(query) || e.Program.Name.ToLower().Contains(query) || (e.Notes != null && e.Notes.ToLower().Contains(query)));
            var count = await matches.CountAsync();
            ExpenseResults = new(await matches.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Take(50).ToListAsync());
            counts.Add($"expenses: showing {ExpenseResults.Count} of {count}");
        }
        if (SearchType is "All" or "Sponsors")
        {
            var matches = _db.Sponsors.AsNoTracking().Where(s => s.Name.ToLower().Contains(query)
                || (s.Phone != null && s.Phone.ToLower().Contains(query)) || (s.Email != null && s.Email.ToLower().Contains(query))
                || (s.Address != null && s.Address.ToLower().Contains(query)) || (s.Notes != null && s.Notes.ToLower().Contains(query)));
            var count = await matches.CountAsync();
            SponsorResults = new(await matches.OrderBy(s => s.Name).ThenBy(s => s.Id).Take(50).ToListAsync());
            counts.Add($"sponsors: showing {SponsorResults.Count} of {count}");
        }
        StatusMessage = string.Join("; ", counts) + ". Refine the search to narrow results.";
    }
    public void ClearSearch() { SearchQuery = string.Empty; ClearResults(); StatusMessage = null; }
}
