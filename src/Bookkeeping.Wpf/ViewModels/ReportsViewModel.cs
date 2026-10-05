using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace Bookkeeping.Wpf.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private int _reportVersion;
    private readonly Services.ReportService _reportService;
    private readonly Services.ExportService _exportService;

    [ObservableProperty] private string _selectedReportType = "Donation";
    [ObservableProperty] private DateTime? _dateFrom = new DateTime(DateTime.Now.Year, 1, 1);
    [ObservableProperty] private DateTime? _dateTo = DateTime.Now;
    [ObservableProperty] private Sponsor? _filterSponsor;
    [ObservableProperty] private OrgProgram? _filterProgram;
    [ObservableProperty] private string? _filterPaymentMethod;

    [ObservableProperty] private ObservableCollection<Services.DonationReportRow> _donationReport = new();
    [ObservableProperty] private ObservableCollection<Services.ExpenseReportRow> _expenseReport = new();
    [ObservableProperty] private ObservableCollection<Sponsor> _sponsors = new();
    [ObservableProperty] private ObservableCollection<OrgProgram> _programs = new();
    [ObservableProperty] private decimal _totalAmount;
    [ObservableProperty] private int _rowCount;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isLoading;

    public string[] ReportTypes => new[] { "Donation", "Expense" };
    public string[] PaymentMethods => new[] { "", "Cash", "Bank" };

    public ReportsViewModel(AppDbContext db, Services.ReportService reportService, Services.ExportService exportService)
    {
        _db = db;
        _reportService = reportService;
        _exportService = exportService;
    }

    partial void OnSelectedReportTypeChanged(string value)
    {
        _reportVersion++;
        DonationReport.Clear(); ExpenseReport.Clear(); TotalAmount = 0; RowCount = 0;
        StatusMessage = "Report type changed. Click Generate to refresh.";
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        PickerRefresh.Update(Sponsors, await _db.Sponsors.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync());
        PickerRefresh.Update(Programs, await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync());
        await GenerateReportAsync();
    }

    [RelayCommand]
    public async Task GenerateReportAsync()
    {
        var version = ++_reportVersion;
        var reportType = SelectedReportType;
        IsLoading = true;
        StatusMessage = "Generating report...";
        try
        {
            if (reportType == "Donation")
            {
                var donations = await _reportService.GetDonationReportAsync(DateFrom, DateTo, FilterSponsor?.Id, FilterProgram?.Id, FilterPaymentMethod);
                if (version != _reportVersion) return;
                DonationReport = new ObservableCollection<Services.DonationReportRow>(donations);
                TotalAmount = donations.Sum(d => d.Amount); RowCount = donations.Count;
            }
            else
            {
                var expenses = await _reportService.GetExpenseReportAsync(DateFrom, DateTo, FilterProgram?.Id, FilterPaymentMethod);
                if (version != _reportVersion) return;
                ExpenseReport = new ObservableCollection<Services.ExpenseReportRow>(expenses);
                TotalAmount = expenses.Sum(e => e.Amount); RowCount = expenses.Count;
            }
            StatusMessage = $"Report generated: {RowCount} rows, {TotalAmount:C} total.";
        }
        catch (Exception ex) { if (version == _reportVersion) StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ClearFilters()
    {
        FilterSponsor = null;
        FilterProgram = null;
        FilterPaymentMethod = null;
        await LoadAsync();
    }
}
