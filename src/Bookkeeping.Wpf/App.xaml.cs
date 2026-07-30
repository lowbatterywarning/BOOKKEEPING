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
        if (Services is IDisposable disposable)
            disposable.Dispose();
        base.OnExit(e);
    }
}

