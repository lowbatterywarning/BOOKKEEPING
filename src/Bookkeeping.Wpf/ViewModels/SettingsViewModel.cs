using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Bookkeeping.Wpf.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppDbContext _db;
    private readonly Services.BackupService _backupService;
    private DispatcherTimer? _autoBackupTimer;

    [ObservableProperty] private string _organizationName = "My Organization";
    [ObservableProperty] private bool _autoBackupEnabled;
    [ObservableProperty] private int _autoBackupIntervalHours = 24;
    [ObservableProperty] private string _dbPath = string.Empty;
    [ObservableProperty] private string _attachmentsPath = string.Empty;
    [ObservableProperty] private string? _backupStatus;
    [ObservableProperty] private bool _isBackupInProgress;
    [ObservableProperty] private string? _lastBackupTime;

    public SettingsViewModel(AppDbContext db, Services.BackupService backupService)
    {
        _db = db;
        _backupService = backupService;
    }

    public bool IsAutoBackupScheduled => _autoBackupTimer?.IsEnabled == true;

    public async Task InitializeAsync()
    {
        await LoadAsync();
        ConfigureAutoBackup();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        OrganizationName = await GetSettingAsync("OrganizationName", "My Organization");
        bool.TryParse(await GetSettingAsync("AutoBackupEnabled", "false"), out var backupEnabled);
        AutoBackupEnabled = backupEnabled;
        int.TryParse(await GetSettingAsync("AutoBackupIntervalHours", "24"), out var backupHours);
        AutoBackupIntervalHours = backupHours > 0 ? backupHours : 24;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DbPath = Path.Combine(appData, "Bookkeeping", "bookkeeping.db");
        AttachmentsPath = Path.Combine(appData, "Bookkeeping", "attachments");
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await SetSettingAsync("OrganizationName", OrganizationName);
        await SetSettingAsync("AutoBackupEnabled", AutoBackupEnabled.ToString());
        await SetSettingAsync("AutoBackupIntervalHours", AutoBackupIntervalHours.ToString());
        ConfigureAutoBackup();
        BackupStatus = "Settings saved successfully.";
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        IsBackupInProgress = true; BackupStatus = "Creating backup...";
        try { var path = await _backupService.CreateBackupAsync(); LastBackupTime = DateTime.Now.ToString("g"); BackupStatus = $"Backup: {Path.GetFileName(path)}"; }
        catch (Exception ex) { BackupStatus = $"Backup failed: {ex.Message}"; }
        finally { IsBackupInProgress = false; }
    }

    [RelayCommand]
    private async Task BackupToLocationAsync()
    {
        var path = Services.BackupService.ShowBackupSaveDialog();
        if (path == null) return;
        IsBackupInProgress = true; BackupStatus = "Creating backup...";
        try { var temp = await _backupService.CreateBackupAsync(); File.Copy(temp, path, true); LastBackupTime = DateTime.Now.ToString("g"); BackupStatus = $"Saved: {Path.GetFileName(path)}"; }
        catch (Exception ex) { BackupStatus = $"Backup failed: {ex.Message}"; }
        finally { IsBackupInProgress = false; }
    }

    [RelayCommand]
    private async Task RestoreFromBackupAsync()
    {
        var path = Services.BackupService.ShowRestoreOpenDialog();
        if (path == null) return;
        if (MessageBox.Show("Restoring will overwrite all current data. Continue?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        BackupStatus = "Restoring...";
        try { await _backupService.RestoreFromBackupAsync(path); System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!); Application.Current.Shutdown(); }
        catch (Exception ex) { BackupStatus = $"Restore failed: {ex.Message}"; }
    }

    public void ConfigureAutoBackup()
    {
        _autoBackupTimer?.Stop();
        _autoBackupTimer = null;

        if (AutoBackupEnabled && AutoBackupIntervalHours > 0)
        {
            _autoBackupTimer = new DispatcherTimer(
                TimeSpan.FromHours(AutoBackupIntervalHours),
                DispatcherPriority.Background,
                async (_, _) =>
                {
                    try
                    {
                        await _backupService.CreateBackupAsync();
                        LastBackupTime = DateTime.Now.ToString("g");
                    }
                    catch (ObjectDisposedException)
                    {
                        // App is shutting down — stop the timer silently
                        _autoBackupTimer?.Stop();
                    }
                    catch (Exception ex)
                    {
                        BackupStatus = $"Backup failed: {ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"Auto-backup failed: {ex.Message}");
                    }
                },
                Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
            _autoBackupTimer.Start();
        }
    }

    public void Dispose() => _autoBackupTimer?.Stop();

    private async Task<string> GetSettingAsync(string key, string defaultValue)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value ?? defaultValue;
    }

    private async Task SetSettingAsync(string key, string value)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null) setting.Value = value;
        else _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        await _db.SaveChangesAsync();
    }
}
