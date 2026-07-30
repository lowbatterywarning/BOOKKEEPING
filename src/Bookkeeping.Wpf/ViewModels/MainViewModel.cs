using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bookkeeping.Wpf.ViewModels;

/// <summary>
/// Main navigation ViewModel. Hosts the tab-based navigation.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _windowTitle = "Bookkeeping";

    // Sub-ViewModels (lazy-loaded via DI)
    public DashboardViewModel Dashboard { get; }
    public SponsorsViewModel Sponsors { get; }
    public IncomeViewModel Income { get; }
    public ExpensesViewModel Expenses { get; }
    public ProgramsViewModel Programs { get; }
    public CashBankViewModel CashBank { get; }
    public ReportsViewModel Reports { get; }
    public SearchViewModel Search { get; }
    public BudgetViewModel Budget { get; }
    public ReconciliationViewModel Reconciliation { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private bool _isLoggedIn;

    public MainViewModel(
        DashboardViewModel dashboard,
        SponsorsViewModel sponsors,
        IncomeViewModel income,
        ExpensesViewModel expenses,
        ProgramsViewModel programs,
        CashBankViewModel cashBank,
        ReportsViewModel reports,
        SearchViewModel search,
        BudgetViewModel budget,
        ReconciliationViewModel reconciliation,
        SettingsViewModel settings)
    {
        Dashboard = dashboard;
        Sponsors = sponsors;
        Income = income;
        Expenses = expenses;
        Programs = programs;
        CashBank = cashBank;
        Reports = reports;
        Search = search;
        Budget = budget;
        Reconciliation = reconciliation;
        Settings = settings;

        // Default to dashboard
        CurrentView = Dashboard;
    }

    [RelayCommand]
    private void NavigateToDashboard() => CurrentView = Dashboard;

    [RelayCommand]
    private void NavigateToSponsors() => CurrentView = Sponsors;

    [RelayCommand]
    private void NavigateToIncome() => CurrentView = Income;

    [RelayCommand]
    private void NavigateToExpenses() => CurrentView = Expenses;

    [RelayCommand]
    private void NavigateToPrograms() => CurrentView = Programs;

    [RelayCommand]
    private void NavigateToCashBank() => CurrentView = CashBank;

    [RelayCommand]
    private void NavigateToReports() => CurrentView = Reports;

    [RelayCommand]
    private void NavigateToBudget() => CurrentView = Budget;

    [RelayCommand]
    private void NavigateToReconciliation() => CurrentView = Reconciliation;

    [RelayCommand]
    private void NavigateToSearch() => CurrentView = Search;

    [RelayCommand]
    private void NavigateToSettings() => CurrentView = Settings;
}
