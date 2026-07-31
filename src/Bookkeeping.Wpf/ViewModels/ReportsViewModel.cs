using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;
using OxyPlot.Legends;

namespace Bookkeeping.Wpf.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly Services.ReportService _reportService;
    private readonly Services.ExportService _exportService;

    [ObservableProperty] private string _selectedReportType = "Donation";
    [ObservableProperty] private int _selectedYear = DateTime.Now.Year;
    [ObservableProperty] private DateTime? _dateFrom = new DateTime(DateTime.Now.Year, 1, 1);
    [ObservableProperty] private DateTime? _dateTo = DateTime.Now;
    [ObservableProperty] private Sponsor? _filterSponsor;
    [ObservableProperty] private DonationCategory? _filterDonationCategory;
    [ObservableProperty] private ExpenseCategory? _filterExpenseCategory;
    [ObservableProperty] private OrgProgram? _filterProgram;
    [ObservableProperty] private string? _filterPaymentMethod;

    [ObservableProperty] private ObservableCollection<Services.DonationReportRow> _donationReport = new();
    [ObservableProperty] private ObservableCollection<Services.ExpenseReportRow> _expenseReport = new();
    [ObservableProperty] private ObservableCollection<Services.ProgramReportRow> _programReport = new();
    [ObservableProperty] private ObservableCollection<Services.MonthlyReportRow> _monthlyReport = new();
    [ObservableProperty] private ObservableCollection<Services.FundReportRow> _fundReport = new();
    [ObservableProperty] private ObservableCollection<Services.SponsorReportRow> _sponsorReport = new();
    [ObservableProperty] private Services.AnnualReportData? _annualReport;
    [ObservableProperty] private Services.IncomeStatementData? _incomeStatement;

    [ObservableProperty] private ObservableCollection<Sponsor> _sponsors = new();
    [ObservableProperty] private ObservableCollection<DonationCategory> _donationCategories = new();
    [ObservableProperty] private ObservableCollection<ExpenseCategory> _expenseCategories = new();
    [ObservableProperty] private ObservableCollection<OrgProgram> _programs = new();

    [ObservableProperty] private decimal _totalAmount;
    [ObservableProperty] private int _rowCount;
    [ObservableProperty] private PlotModel? _chartModel;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isLoading;
    private bool _isLoaded;

    public string[] ReportTypes => new[] { "Donation", "Expense", "Program", "Monthly", "Annual", "Fund", "Sponsor", "Statement of Activities" };
    public string[] PaymentMethods => new[] { "", "Cash", "Bank" };

    public ReportsViewModel(AppDbContext db, Services.ReportService reportService, Services.ExportService exportService)
    {
        _db = db;
        _reportService = reportService;
        _exportService = exportService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _db.ChangeTracker.Clear();
        Sponsors = new ObservableCollection<Sponsor>(await _db.Sponsors.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync());
        DonationCategories = new ObservableCollection<DonationCategory>(await _db.DonationCategories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync());
        ExpenseCategories = new ObservableCollection<ExpenseCategory>(await _db.ExpenseCategories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync());
        Programs = new ObservableCollection<OrgProgram>(await _db.Programs.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync());
        _isLoaded = true;
        await GenerateReportAsync();
    }

    [RelayCommand]
    public async Task GenerateReportAsync()
    {
        IsLoading = true;
        StatusMessage = "Generating report...";
        ChartModel = null;

        try
        {
            switch (SelectedReportType)
            {
                case "Donation":
                    var donations = await _reportService.GetDonationReportAsync(DateFrom, DateTo, FilterSponsor?.Id, FilterDonationCategory?.Id, FilterPaymentMethod);
                    DonationReport = new ObservableCollection<Services.DonationReportRow>(donations);
                    TotalAmount = donations.Sum(d => d.Amount); RowCount = donations.Count;
                    break;
                case "Expense":
                    var expenses = await _reportService.GetExpenseReportAsync(DateFrom, DateTo, FilterExpenseCategory?.Id, FilterProgram?.Id, FilterPaymentMethod);
                    ExpenseReport = new ObservableCollection<Services.ExpenseReportRow>(expenses);
                    TotalAmount = expenses.Sum(e => e.Amount); RowCount = expenses.Count;
                    break;
                case "Program":
                    var programs = await _reportService.GetProgramReportAsync(DateFrom, DateTo);
                    ProgramReport = new ObservableCollection<Services.ProgramReportRow>(programs);
                    TotalAmount = programs.Sum(p => p.Balance); RowCount = programs.Count;
                    BuildProgramChart(programs);
                    break;
                case "Monthly":
                    var monthly = await _reportService.GetMonthlyReportAsync(SelectedYear);
                    MonthlyReport = new ObservableCollection<Services.MonthlyReportRow>(monthly);
                    TotalAmount = monthly.Sum(m => m.Net); RowCount = 12;
                    BuildMonthlyChart(monthly);
                    break;
                case "Annual":
                    AnnualReport = await _reportService.GetAnnualReportAsync(SelectedYear);
                    TotalAmount = AnnualReport.Net;
                    BuildAnnualChart(AnnualReport);
                    break;
                case "Fund":
                    var funds = await _reportService.GetFundReportAsync();
                    FundReport = new ObservableCollection<Services.FundReportRow>(funds);
                    TotalAmount = funds.Sum(f => f.NetBalance); RowCount = funds.Count;
                    BuildFundChart(funds);
                    break;
                case "Sponsor":
                    if (FilterSponsor != null)
                    {
                        var sponsorRows = await _reportService.GetSponsorReportAsync(FilterSponsor.Id);
                        SponsorReport = new ObservableCollection<Services.SponsorReportRow>(sponsorRows);
                        TotalAmount = sponsorRows.Sum(s => s.Amount); RowCount = sponsorRows.Count;
                    }
                    break;
                case "Statement of Activities":
                    IncomeStatement = await _reportService.GetIncomeStatementAsync(SelectedYear);
                    TotalAmount = IncomeStatement.NetIncome;
                    RowCount = IncomeStatement.RevenueByCategory.Count + IncomeStatement.ExpensesByCategory.Count;
                    BuildIncomeStatementChart(IncomeStatement);
                    break;
            }
            StatusMessage = $"Report ready — {RowCount} rows, Total: {TotalAmount:C}";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        var fileName = $"{SelectedReportType}_Report_{DateTime.Now:yyyyMMdd}";
        var path = Services.ExportService.ShowSaveDialog(fileName);
        if (path == null) return;
        StatusMessage = "Exporting to Excel...";
        try
        {
            switch (SelectedReportType)
            {
                case "Donation": _exportService.ExportDonationsToExcel(DonationReport, path); break;
                case "Expense": _exportService.ExportExpensesToExcel(ExpenseReport, path); break;
                case "Program": _exportService.ExportProgramToExcel(ProgramReport, path); break;
                case "Monthly": _exportService.ExportMonthlyToExcel(MonthlyReport, SelectedYear, path); break;
                case "Annual":
                    if (AnnualReport != null)
                        _exportService.ExportAnnualToExcel(AnnualReport, path);
                    break;
                case "Fund": _exportService.ExportFundToExcel(FundReport, path); break;
                case "Sponsor": _exportService.ExportSponsorToExcel(SponsorReport, FilterSponsor?.Name ?? "Sponsor", path); break;
                case "Statement of Activities":
                    if (IncomeStatement != null)
                        _exportService.ExportIncomeStatementToExcel(IncomeStatement, path);
                    break;
            }
            StatusMessage = $"Exported to {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ExportToPdfAsync()
    {
        var fileName = $"{SelectedReportType}_Report_{DateTime.Now:yyyyMMdd}";
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = fileName, DefaultExt = ".pdf", Filter = "PDF Files (*.pdf)|*.pdf" };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FileName;
        StatusMessage = "Exporting to PDF...";
        try
        {
            List<string> headers; List<List<string>> dataRows;
            switch (SelectedReportType)
            {
                case "Donation":
                    headers = new() { "Date", "Sponsor", "Category", "Method", "Program", "Amount", "Receipt #" };
                    dataRows = DonationReport.Select(r => (List<string>)new List<string> { r.Date.ToString("d"), r.Sponsor, r.Category, r.PaymentMethod, r.Program, r.Amount.ToString("N2"), r.ReceiptNumber }).ToList();
                    break;
                case "Expense":
                    headers = new() { "Date", "Vendor", "Category", "Method", "Program", "Amount", "Notes" };
                    dataRows = ExpenseReport.Select(r => (List<string>)new List<string> { r.Date.ToString("d"), r.Vendor, r.Category, r.PaymentMethod, r.Program, r.Amount.ToString("N2"), r.Notes }).ToList();
                    break;
                case "Program":
                    headers = new() { "Program", "Income", "Expenses", "Balance" };
                    dataRows = ProgramReport.Select(r => (List<string>)new List<string> { r.Program, r.Income.ToString("N2"), r.Expenses.ToString("N2"), r.Balance.ToString("N2") }).ToList();
                    break;
                case "Monthly":
                    headers = new() { "Month", "Income", "Expenses", "Net", "Cash", "Bank" };
                    dataRows = MonthlyReport.Select(r => (List<string>)new List<string> { r.Month, r.Income.ToString("N2"), r.Expenses.ToString("N2"), r.Net.ToString("N2"), r.CashBalance.ToString("N2"), r.BankBalance.ToString("N2") }).ToList();
                    break;
                case "Annual":
                    if (AnnualReport == null) { StatusMessage = "No annual report data."; return; }
                    headers = new() { "Metric", "Amount" };
                    dataRows = new List<List<string>>
                    {
                        new() { "Total Income", AnnualReport.TotalIncome.ToString("N2") },
                        new() { "Total Expenses", AnnualReport.TotalExpenses.ToString("N2") },
                        new() { "Net", AnnualReport.Net.ToString("N2") },
                        new() { "Cash Balance", AnnualReport.CashBalance.ToString("N2") },
                        new() { "Bank Balance", AnnualReport.BankBalance.ToString("N2") },
                    };
                    foreach (var c in AnnualReport.IncomeByCategory)
                        dataRows.Add(new List<string> { $"Income: {c.Category}", c.Amount.ToString("N2") });
                    foreach (var c in AnnualReport.ExpensesByCategory)
                        dataRows.Add(new List<string> { $"Expense: {c.Category}", c.Amount.ToString("N2") });
                    break;
                case "Fund":
                    headers = new() { "Fund", "Type", "Income", "Expenses", "Net Balance" };
                    dataRows = FundReport.Select(r => (List<string>)new List<string> { r.FundName, r.IsRestricted ? "Restricted" : "Unrestricted", r.Income.ToString("N2"), r.Expenses.ToString("N2"), r.NetBalance.ToString("N2") }).ToList();
                    break;
                case "Sponsor":
                    if (FilterSponsor == null) { StatusMessage = "Please select a sponsor."; return; }
                    headers = new() { "Date", "Category", "Amount", "Receipt #" };
                    dataRows = SponsorReport.Select(r => (List<string>)new List<string> { r.Date.ToString("d"), r.Category, r.Amount.ToString("N2"), r.ReceiptNumber }).ToList();
                    break;
                case "Statement of Activities":
                    if (IncomeStatement == null) { StatusMessage = "No statement data."; return; }
                    headers = new() { "Metric", "Amount" };
                    dataRows = new List<List<string>>
                    {
                        new() { "Total Revenue", IncomeStatement.TotalRevenue.ToString("N2") },
                        new() { "Total Expenses", IncomeStatement.TotalExpenses.ToString("N2") },
                        new() { "Change in Net Assets", IncomeStatement.NetIncome.ToString("N2") },
                    };
                    foreach (var c in IncomeStatement.RevenueByCategory)
                        dataRows.Add(new List<string> { $"Revenue: {c.Category}", c.Amount.ToString("N2") });
                    foreach (var c in IncomeStatement.ExpensesByCategory)
                        dataRows.Add(new List<string> { $"Expense: {c.Category}", c.Amount.ToString("N2") });
                    dataRows.Add(new List<string> { "", "" });
                    dataRows.Add(new List<string> { "Monthly Breakdown", "" });
                    foreach (var m in IncomeStatement.MonthlyBreakdown)
                        dataRows.Add(new List<string> { m.Month, $"Rev: {m.Revenue:N2} | Exp: {m.Expenses:N2} | Net: {m.Net:N2}" });
                    break;
                default: StatusMessage = "PDF export not supported for this report type."; return;
            }
            _exportService.ExportToPdf(SelectedReportType + " Report", headers, dataRows, path);
            StatusMessage = $"Exported to {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex) { StatusMessage = $"PDF export failed: {ex.Message}"; }
    }

    [RelayCommand] private void ClearFilters()
    {
        DateFrom = new DateTime(DateTime.Now.Year, 1, 1); DateTo = DateTime.Now;
        FilterSponsor = null; FilterDonationCategory = null;
        FilterExpenseCategory = null; FilterProgram = null; FilterPaymentMethod = null;
        _ = GenerateReportAsync();
    }

    partial void OnSelectedReportTypeChanged(string value)
    {
        if (_isLoaded)
            _ = GenerateReportAsync();
    }

    private void BuildProgramChart(List<Services.ProgramReportRow> data)
    {
        if (data.Count == 0) return;
        var model = new PlotModel { Title = "Program Income vs Expenses" };
        model.Legends.Add(new Legend { LegendPosition = LegendPosition.BottomCenter });
        var incomeSeries = new BarSeries { Title = "Income", FillColor = OxyColor.FromRgb(0x27, 0xae, 0x60) };
        var expenseSeries = new BarSeries { Title = "Expenses", FillColor = OxyColor.FromRgb(0xc0, 0x39, 0x2b) };
        var catAxis = new CategoryAxis { Position = AxisPosition.Bottom };
        var valueAxis = new LinearAxis { Position = AxisPosition.Left, Title = "Amount ($)" };
        foreach (var p in data) catAxis.Labels.Add(p.Program);
        model.Axes.Add(catAxis);
        model.Axes.Add(valueAxis);
        foreach (var p in data) { incomeSeries.Items.Add(new BarItem((double)p.Income)); expenseSeries.Items.Add(new BarItem((double)p.Expenses)); }
        model.Series.Add(incomeSeries); model.Series.Add(expenseSeries);
        ChartModel = model;
    }

    private void BuildMonthlyChart(List<Services.MonthlyReportRow> data)
    {
        var model = new PlotModel { Title = $"Monthly Income & Expenses — {SelectedYear}" };
        model.Legends.Add(new Legend { LegendPosition = LegendPosition.BottomCenter });
        var incomeSeries = new LineSeries { Title = "Income", Color = OxyColor.FromRgb(0x27, 0xae, 0x60), StrokeThickness = 2, MarkerType = MarkerType.Circle };
        var expenseSeries = new LineSeries { Title = "Expenses", Color = OxyColor.FromRgb(0xc0, 0x39, 0x2b), StrokeThickness = 2, MarkerType = MarkerType.Circle };
        for (int i = 0; i < data.Count; i++) { incomeSeries.Points.Add(new DataPoint(i, (double)data[i].Income)); expenseSeries.Points.Add(new DataPoint(i, (double)data[i].Expenses)); }
        model.Series.Add(incomeSeries); model.Series.Add(expenseSeries);
        var xAxis = new CategoryAxis { Position = AxisPosition.Bottom };
        foreach (var m in data) xAxis.Labels.Add(m.Month[..3]);
        model.Axes.Add(xAxis); model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Amount ($)" });
        ChartModel = model;
    }

    private void BuildAnnualChart(Services.AnnualReportData data)
    {
        var model = new PlotModel { Title = $"Income by Category — {data.Year}" };
        var pieSeries = new PieSeries { StrokeThickness = 1, InsideLabelPosition = 0.5 };
        foreach (var c in data.IncomeByCategory.Take(10))
            pieSeries.Slices.Add(new PieSlice(c.Category, (double)c.Amount) { IsExploded = false });
        model.Series.Add(pieSeries);
        ChartModel = model;
    }

    private void BuildFundChart(List<Services.FundReportRow> data)
    {
        if (data.Count == 0) return;
        var model = new PlotModel { Title = "Fund Balances" };
        var barSeries = new BarSeries { Title = "Net Balance" };
        var catAxis = new CategoryAxis { Position = AxisPosition.Bottom };
        var valueAxis = new LinearAxis { Position = AxisPosition.Left, Title = "Amount ($)" };
        foreach (var f in data)
        {
            catAxis.Labels.Add(f.FundName);
            barSeries.Items.Add(new BarItem((double)f.NetBalance));
            barSeries.Items[^1].Color = f.IsRestricted ? OxyColor.FromRgb(0xe7, 0x4c, 0x3c) : OxyColor.FromRgb(0x27, 0xae, 0x60);
        }
        model.Axes.Add(catAxis); model.Axes.Add(valueAxis); model.Series.Add(barSeries);
        ChartModel = model;
    }

    private void BuildIncomeStatementChart(Services.IncomeStatementData data)
    {
        var model = new PlotModel { Title = $"Statement of Activities — {data.Year}" };
        model.Legends.Add(new Legend { LegendPosition = LegendPosition.BottomCenter });

        var revSeries = new BarSeries { Title = "Revenue", FillColor = OxyColor.FromRgb(0x27, 0xae, 0x60) };
        var expSeries = new BarSeries { Title = "Expenses", FillColor = OxyColor.FromRgb(0xc0, 0x39, 0x2b) };
        var netSeries = new LineSeries { Title = "Net Income", Color = OxyColor.FromRgb(0x29, 0x80, 0xb9), StrokeThickness = 2, MarkerType = MarkerType.Circle };

        var catAxis = new CategoryAxis { Position = AxisPosition.Bottom };
        foreach (var m in data.MonthlyBreakdown)
        {
            catAxis.Labels.Add(m.Month[..3]);
            revSeries.Items.Add(new BarItem((double)m.Revenue));
            expSeries.Items.Add(new BarItem((double)m.Expenses));
            netSeries.Points.Add(new DataPoint(catAxis.Labels.Count - 1, (double)m.Net));
        }

        model.Axes.Add(catAxis);
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Amount ($)" });
        model.Series.Add(revSeries);
        model.Series.Add(expSeries);
        model.Series.Add(netSeries);
        ChartModel = model;
    }
}
