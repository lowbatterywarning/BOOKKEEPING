using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Bookkeeping.Wpf.Services;

/// <summary>
/// Handles backup and restore of the database file and attachments folder.
/// </summary>
public class BackupService
{
    private readonly string _dbPath;
    private readonly string _attachmentsPath;
    private readonly string _backupDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupService(string dbPath, string attachmentsPath, string? backupDirectory = null)
    {
        _dbPath = dbPath;
        _attachmentsPath = attachmentsPath;
        _backupDirectory = backupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bookkeeping Backups");
    }

    public async Task<string> CreateBackupAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_backupDirectory);
            var backupFile = Path.Combine(_backupDirectory, $"Bookkeeping_Backup_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.zip");
            var snapshot = backupFile + ".db";
            try
            {
                await Task.Run(() =>
                {
                    CopyDatabase(_dbPath, snapshot);
                    using var archive = ZipFile.Open(backupFile, ZipArchiveMode.Create);
                    archive.CreateEntryFromFile(snapshot, "bookkeeping.db");
                    if (Directory.Exists(_attachmentsPath))
                    {
                        foreach (var file in Directory.GetFiles(_attachmentsPath, "*", SearchOption.AllDirectories))
                        {
                            var relativePath = "attachments/" + Path.GetRelativePath(_attachmentsPath, file).Replace('\\', '/');
                            archive.CreateEntryFromFile(file, relativePath);
                        }
                    }
                });
                return backupFile;
            }
            catch
            {
                if (File.Exists(backupFile)) File.Delete(backupFile);
                throw;
            }
            finally
            {
                if (File.Exists(snapshot)) File.Delete(snapshot);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task RestoreFromBackupAsync(string backupFilePath)
    {
        await _gate.WaitAsync();
        try { await Task.Run(() => Restore(backupFilePath)); }
        finally { _gate.Release(); }
    }

    private void Restore(string backupFilePath)
    {
        // Stage on the same volume as attachments so directory swaps are atomic.
        var attachmentParent = Path.GetDirectoryName(Path.GetFullPath(_attachmentsPath))!;
        var staging = Path.Combine(attachmentParent, $".restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(backupFilePath);
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<(ZipArchiveEntry Entry, string Path)>();
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(p =>
                    p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ')
                    || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                    throw new InvalidDataException($"Unsafe backup entry: {entry.FullName}");
                if (name != "bookkeeping.db" && !name.StartsWith("attachments/", StringComparison.Ordinal))
                    throw new InvalidDataException($"Unexpected backup entry: {entry.FullName}");
                var destination = Path.GetFullPath(Path.Combine(staging, name));
                if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsafe backup entry: {entry.FullName}");
                if (!destinations.Add(destination))
                    throw new InvalidDataException($"Duplicate backup entry: {entry.FullName}");
                if (!name.EndsWith('/')) files.Add((entry, destination));
            }

            var stagedDatabase = Path.Combine(staging, "bookkeeping.db");
            if (!files.Any(f => f.Path == stagedDatabase))
                throw new InvalidDataException("The backup does not contain bookkeeping.db.");
            foreach (var file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                file.Entry.ExtractToFile(file.Path);
            }
            ValidateDatabase(stagedDatabase);

            var recoverySuffix = $".pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
            if (File.Exists(_dbPath)) CopyDatabase(_dbPath, _dbPath + recoverySuffix + ".bak");
            var stagedAttachments = Path.Combine(staging, "attachments");
            Directory.CreateDirectory(stagedAttachments);
            var recoveryAttachments = _attachmentsPath + recoverySuffix;
            var hadAttachments = Directory.Exists(_attachmentsPath);
            if (hadAttachments) Directory.Move(_attachmentsPath, recoveryAttachments);
            try
            {
                Directory.Move(stagedAttachments, _attachmentsPath);
                // Restore through SQLite, never overwrite a live database file or
                // leave an old WAL beside a replacement database.
                CopyDatabase(stagedDatabase, _dbPath);
            }
            catch
            {
                if (Directory.Exists(_attachmentsPath)) Directory.Move(_attachmentsPath, Path.Combine(staging, "failed-attachments"));
                if (hadAttachments) Directory.Move(recoveryAttachments, _attachmentsPath);
                throw;
            }
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    private static void CopyDatabase(string sourcePath, string destinationPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        source.Open();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = destinationPath, Pooling = false }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void ValidateDatabase(string dbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA quick_check";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
            throw new InvalidDataException("The backup database failed its integrity check.");
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('JournalEntries', 'Donations', 'Expenses', 'Users')";
        if (Convert.ToInt64(check.ExecuteScalar()) != 4)
            throw new InvalidDataException("The backup is not a Bookkeeping database.");
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
