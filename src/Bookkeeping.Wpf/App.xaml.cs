using System.Windows;
using Bookkeeping.Wpf.Services;
using Bookkeeping.Wpf.ViewModels;
using Bookkeeping.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace Bookkeeping.Wpf;

public partial class App : Application
{
    internal static IServiceProvider? Services { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch unhandled exceptions on the dispatcher thread
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Unhandled error:\n\n{args.Exception}", "Bookkeeping Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bookkeeping");
            Directory.CreateDirectory(appDataPath);
            var dbPath = Path.Combine(appDataPath, "bookkeeping.db");

            var services = new ServiceCollection();
            services.AddBookkeepingServices(dbPath);
            Services = services.BuildServiceProvider();

            using var scope = Services.CreateScope();
            var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
            await initializer.InitializeAsync();

            // Keep a rolling snapshot of the database while the app runs.
            Services.GetRequiredService<AutoBackupService>().Start();

            // No login — single-user, single-laptop, go straight to the app
            var mainWindow = new MainWindow(Services.GetRequiredService<MainViewModel>());
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup error:\n\n{ex}", "Bookkeeping Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Final snapshot so the auto-backup is at most one session old.
        try
        {
            Services?.GetRequiredService<AutoBackupService>().CreateSnapshotAsync().Wait(TimeSpan.FromSeconds(10));
        }
        catch { /* never block exit on a backup failure */ }

        if (Services is IDisposable disposable)
            disposable.Dispose();
        base.OnExit(e);
    }
}

