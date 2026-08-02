using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

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

        try
        {
            switch (SelectedReportType)
            {
                case "Donation":
                    var donations = await _reportService.GetDonationReportAsync(DateFrom, DateTo, FilterSponsor?.Id, FilterDonationCategory?.Id, FilterPaymentMethod);
                    DonationReport = new ObservableCollection<Services.DonationReportRow>(donations);
                    TotalAmount = donations.Sum(d => d.Amount); RowCount = donations.Count;
                    ChartModel = BuildDonationChart(donations);
                    break;
                case "Expense":
                    var expenses = await _reportService.GetExpenseReportAsync(DateFrom, DateTo, FilterExpenseCategory?.Id, FilterProgram?.Id, FilterPaymentMethod);
                    ExpenseReport = new ObservableCollection<Services.ExpenseReportRow>(expenses);
                    TotalAmount = expenses.Sum(e => e.Amount); RowCount = expenses.Count;
                    ChartModel = BuildExpenseChart(expenses);
                    break;
                case "Program":
                    var programs = await _reportService.GetProgramReportAsync(DateFrom, DateTo);
                    ProgramReport = new ObservableCollection<Services.ProgramReportRow>(programs);
                    TotalAmount = programs.Sum(p => p.Balance); RowCount = programs.Count;
                    ChartModel = BuildProgramChart(programs);
                    break;
                case "Monthly":
                    var monthly = await _reportService.GetMonthlyReportAsync(SelectedYear);
                    MonthlyReport = new ObservableCollection<Services.MonthlyReportRow>(monthly);
                    TotalAmount = monthly.Sum(m => m.Net); RowCount = 12;
                    ChartModel = BuildMonthlyChart(monthly);
                    break;
                case "Annual":
                    AnnualReport = await _reportService.GetAnnualReportAsync(SelectedYear);
                    TotalAmount = AnnualReport.Net;
                    ChartModel = BuildAnnualChart(AnnualReport);
                    break;
                case "Fund":
                    var funds = await _reportService.GetFundReportAsync();
                    FundReport = new ObservableCollection<Services.FundReportRow>(funds);
                    TotalAmount = funds.Sum(f => f.NetBalance); RowCount = funds.Count;
                    ChartModel = BuildFundChart(funds);
                    break;
                case "Sponsor":
                    if (FilterSponsor != null)
                    {
                        var sponsorRows = await _reportService.GetSponsorReportAsync(FilterSponsor.Id);
                        SponsorReport = new ObservableCollection<Services.SponsorReportRow>(sponsorRows);
                        TotalAmount = sponsorRows.Sum(s => s.Amount); RowCount = sponsorRows.Count;
                        ChartModel = BuildSponsorChart(sponsorRows);
                    }
                    else { ChartModel = null; }
                    break;
                case "Statement of Activities":
                    IncomeStatement = await _reportService.GetIncomeStatementAsync(SelectedYear);
                    TotalAmount = IncomeStatement.NetIncome;
                    RowCount = IncomeStatement.RevenueByCategory.Count + IncomeStatement.ExpensesByCategory.Count;
                    ChartModel = BuildStatementChart(IncomeStatement);
                    break;
            }
            StatusMessage = $"Report ready — {RowCount} rows, Total: {TotalAmount:C}";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; ChartModel = null; }
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

    // ── Chart builders ──────────────────────────────────────────

    private static PlotModel BuildDonationChart(List<Services.DonationReportRow> rows)
    {
        var model = new PlotModel { Title = "Donations by Category" };
        var groups = rows.GroupBy(r => r.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(r => r.Amount) })
            .OrderBy(x => x.Total).ToList();

        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Left,
            Title = "Category",
            Key = "CategoryAxis"
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Amount ($)",
            Key = "ValueAxis"
        });

        var series = new BarSeries
        {
            Title = "Donations",
            FillColor = OxyColor.FromRgb(0x27, 0xAE, 0x60),
            XAxisKey = "ValueAxis",
            YAxisKey = "CategoryAxis"
        };
        foreach (var g in groups)
            series.Items.Add(new BarItem { Value = (double)g.Total });

        model.Series.Add(series);
        return model;
    }

    private static PlotModel BuildExpenseChart(List<Services.ExpenseReportRow> rows)
    {
        var model = new PlotModel { Title = "Expenses by Category" };
        var groups = rows.GroupBy(r => r.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(r => r.Amount) })
            .OrderBy(x => x.Total).ToList();

        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Left,
            Title = "Category",
            Key = "CategoryAxis"
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Amount ($)",
            Key = "ValueAxis"
        });

        var series = new BarSeries
        {
            Title = "Expenses",
            FillColor = OxyColor.FromRgb(0xC0, 0x39, 0x2B),
            XAxisKey = "ValueAxis",
            YAxisKey = "CategoryAxis"
        };
        foreach (var g in groups)
            series.Items.Add(new BarItem { Value = (double)g.Total });

        model.Series.Add(series);
        return model;
    }

    private static PlotModel BuildProgramChart(List<Services.ProgramReportRow> rows)
    {
        var model = new PlotModel { Title = "Program Income vs Expenses" };
        var programs = rows.Where(r => r.Income > 0 || r.Expenses > 0).ToList();
        if (programs.Count == 0) return model;

        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Left,
            Title = "Program",
            Key = "CategoryAxis"
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Amount ($)",
            Key = "ValueAxis"
        });

        var incomeSeries = new BarSeries
        {
            Title = "Income",
            FillColor = OxyColor.FromRgb(0x27, 0xAE, 0x60),
            XAxisKey = "ValueAxis",
            YAxisKey = "CategoryAxis"
        };
        var expenseSeries = new BarSeries
        {
            Title = "Expenses",
            FillColor = OxyColor.FromRgb(0xC0, 0x39, 0x2B),
            XAxisKey = "ValueAxis",
            YAxisKey = "CategoryAxis"
        };
        for (int i = 0; i < programs.Count; i++)
        {
            incomeSeries.Items.Add(new BarItem { Value = (double)programs[i].Income });
            expenseSeries.Items.Add(new BarItem { Value = (double)programs[i].Expenses });
        }
        model.Series.Add(incomeSeries);
        model.Series.Add(expenseSeries);
        return model;
    }

    private static PlotModel BuildMonthlyChart(List<Services.MonthlyReportRow> rows)
    {
        var model = new PlotModel { Title = "Monthly Income vs Expenses" };
        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Month",
            Key = "MonthAxis"
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Amount ($)",
            Key = "ValueAxis"
        });

        var incomeSeries = new LineSeries
        {
            Title = "Income",
            Color = OxyColor.FromRgb(0x27, 0xAE, 0x60),
            MarkerType = MarkerType.Circle,
            XAxisKey = "MonthAxis",
            YAxisKey = "ValueAxis"
        };
        var expenseSeries = new LineSeries
        {
            Title = "Expenses",
            Color = OxyColor.FromRgb(0xC0, 0x39, 0x2B),
            MarkerType = MarkerType.Circle,
            XAxisKey = "MonthAxis",
            YAxisKey = "ValueAxis"
        };
        var netSeries = new LineSeries
        {
            Title = "Net",
            Color = OxyColor.FromRgb(0x29, 0x80, 0xB9),
            MarkerType = MarkerType.Diamond,
            StrokeThickness = 2,
            XAxisKey = "MonthAxis",
            YAxisKey = "ValueAxis"
        };

        for (int i = 0; i < rows.Count; i++)
        {
            incomeSeries.Points.Add(new DataPoint(i, (double)rows[i].Income));
            expenseSeries.Points.Add(new DataPoint(i, (double)rows[i].Expenses));
            netSeries.Points.Add(new DataPoint(i, (double)rows[i].Net));
        }
        model.Series.Add(incomeSeries);
        model.Series.Add(expenseSeries);
        model.Series.Add(netSeries);
        return model;
    }

    private static PlotModel BuildAnnualChart(Services.AnnualReportData data)
    {
        var model = new PlotModel { Title = $"Annual Summary — {data.Year}" };
        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Left,
            Title = "",
            Key = "CategoryAxis"
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Amount ($)",
            Key = "ValueAxis"
        });

        var series = new BarSeries
        {
            Title = "Amount",
            XAxisKey = "ValueAxis",
            YAxisKey = "CategoryAxis"
        };
        series.Items.Add(new BarItem { Value = (double)data.TotalIncome, Color = OxyColor.FromRgb(0x27, 0xAE, 0x60) });
        series.Items.Add(new BarItem { Value = (double)data.TotalExpenses, Color = OxyColor.FromRgb(0xC0, 0x39, 0x2B) });
        series.Items.Add(new BarItem { Value = (double)data.Net, Color = OxyColor.FromRgb(0x29, 0x80, 0xB9) });
        model.Series.Add(series);
        return model;
    }

    private static PlotModel BuildFundChart(List<Services.FundReportRow> rows)
    {
        var model = new PlotModel { Title = "Fund Net Balances" };
        var funds = rows.Where(r => r.NetBalance != 0).ToList();
        if (funds.Count == 0) return model;

        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Left,
            Title = "Fund",
            Key = "CategoryAxis"
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Net Balance ($)",
            Key = "ValueAxis"
        });

        var series = new BarSeries
        {
            Title = "Net Balance",
            XAxisKey = "ValueAxis",
            YAxisKey = "CategoryAxis"
        };
        for (int i = 0; i < funds.Count; i++)
        {
            var val = (double)funds[i].NetBalance;
            series.Items.Add(new BarItem
            {
                Value = Math.Abs(val),
                Color = val >= 0 ? OxyColor.FromRgb(0x27, 0xAE, 0x60) : OxyColor.FromRgb(0xC0, 0x39, 0x2B)
            });
        }
        model.Series.Add(series);
        return model;
    }

    private static PlotModel BuildSponsorChart(List<Services.SponsorReportRow> rows)
    {
        var model = new PlotModel { Title = "Sponsor Donations Over Time" };
        var sorted = rows.OrderBy(r => r.Date).ToList();
        if (sorted.Count == 0) return model;

        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Left,
            Title = "Date",
            Key = "CategoryAxis"
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Amount ($)",
            Key = "ValueAxis"
        });

        var series = new BarSeries
        {
            Title = "Donation",
            FillColor = OxyColor.FromRgb(0x27, 0xAE, 0x60),
            XAxisKey = "ValueAxis",
            YAxisKey = "CategoryAxis"
        };
        for (int i = 0; i < sorted.Count; i++)
            series.Items.Add(new BarItem { Value = (double)sorted[i].Amount });

        model.Series.Add(series);
        return model;
    }

    private static PlotModel BuildStatementChart(Services.IncomeStatementData data)
    {
        var model = new PlotModel { Title = "Statement of Activities — Revenue vs Expenses" };
        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Left,
            Title = "",
            Key = "CategoryAxis"
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Amount ($)",
            Key = "ValueAxis"
        });

        var series = new BarSeries
        {
            Title = "Amount",
            XAxisKey = "ValueAxis",
            YAxisKey = "CategoryAxis"
        };
        series.Items.Add(new BarItem { Value = (double)data.TotalRevenue, Color = OxyColor.FromRgb(0x27, 0xAE, 0x60) });
        series.Items.Add(new BarItem { Value = (double)data.TotalExpenses, Color = OxyColor.FromRgb(0xC0, 0x39, 0x2B) });
        series.Items.Add(new BarItem { Value = (double)data.NetIncome, Color = OxyColor.FromRgb(0x29, 0x80, 0xB9) });
        model.Series.Add(series);
        return model;
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
}
