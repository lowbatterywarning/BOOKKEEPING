using System.IO;
using System.IO.Compression;
using Bookkeeping.Data;
using Microsoft.Win32;

namespace Bookkeeping.Wpf.Services;

/// <summary>
/// Handles backup and restore of the database file and attachments folder.
/// </summary>
public class BackupService
{
    private readonly string _dbPath;
    private readonly string _attachmentsPath;

    public BackupService(string dbPath, string attachmentsPath)
    {
        _dbPath = dbPath;
        _attachmentsPath = attachmentsPath;
    }

    public async Task<string> CreateBackupAsync()
    {
        var backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Bookkeeping Backups");
        Directory.CreateDirectory(backupDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFile = Path.Combine(backupDir, $"Bookkeeping_Backup_{timestamp}.zip");

        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(backupFile, ZipArchiveMode.Create);

            // Add database file
            if (File.Exists(_dbPath))
                archive.CreateEntryFromFile(_dbPath, "bookkeeping.db");

            // Add attachments folder
            if (Directory.Exists(_attachmentsPath))
            {
                foreach (var file in Directory.GetFiles(_attachmentsPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = "attachments/" + Path.GetRelativePath(_attachmentsPath, file);
                    archive.CreateEntryFromFile(file, relativePath);
                }
            }
        });

        return backupFile;
    }

    public async Task RestoreFromBackupAsync(string backupFilePath)
    {
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(backupFilePath);

            // Restore database
            var dbEntry = archive.GetEntry("bookkeeping.db");
            if (dbEntry != null)
            {
                // Backup current db before overwriting
                var currentBackup = _dbPath + ".pre_restore.bak";
                if (File.Exists(_dbPath))
                    File.Copy(_dbPath, currentBackup, overwrite: true);

                dbEntry.ExtractToFile(_dbPath, overwrite: true);
            }

            // Restore attachments (preserve folder structure)
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("attachments/") && !string.IsNullOrEmpty(entry.Name))
                {
                    var relativePath = entry.FullName["attachments/".Length..];
                    var destPath = Path.Combine(_attachmentsPath, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
        });

        // Re-open the database connection will happen on next app start
    }

    public static string? ShowBackupSaveDialog()
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"Bookkeeping_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            DefaultExt = ".zip",
            Filter = "ZIP files (*.zip)|*.zip"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public static string? ShowRestoreOpenDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Backup File to Restore",
            Filter = "ZIP files (*.zip)|*.zip",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
