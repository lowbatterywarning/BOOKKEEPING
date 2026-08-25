using System.IO;
using Bookkeeping.Core.Interfaces;
using Bookkeeping.Data;
using Bookkeeping.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bookkeeping.Wpf.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBookkeepingServices(this IServiceCollection services, string dbPath)
    {
        // Paths
        var attachmentsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "attachments");
        Directory.CreateDirectory(attachmentsPath);

        // Database
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Core services
        services.AddScoped<IJournalEngine, JournalEngine>();
        services.AddScoped<ReportService>();
        services.AddScoped<ExportService>();
        services.AddScoped<AuditService>();
        services.AddScoped<BackupService>(_ => new BackupService(dbPath, attachmentsPath));
        services.AddSingleton<AutoBackupService>(_ => new AutoBackupService(dbPath));
        services.AddScoped<DatabaseInitializer>();

        // ViewModels
        services.AddTransient<ViewModels.DashboardViewModel>();
        services.AddTransient<ViewModels.SponsorsViewModel>();
        services.AddTransient<ViewModels.SponsorDashboardViewModel>();
        services.AddTransient<ViewModels.IncomeViewModel>();
        services.AddTransient<ViewModels.ExpensesViewModel>();
        services.AddTransient<ViewModels.ProgramsViewModel>();
        services.AddTransient<ViewModels.CashBankViewModel>();
        services.AddTransient<ViewModels.ReportsViewModel>();
        services.AddTransient<ViewModels.SearchViewModel>();
        services.AddTransient<ViewModels.BudgetViewModel>();
        services.AddTransient<ViewModels.SettingsViewModel>();
        services.AddTransient<ViewModels.MainViewModel>();

        return services;
    }
}
