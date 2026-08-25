using System.IO;
using Microsoft.Data.Sqlite;

namespace Bookkeeping.Wpf.Services;

/// <summary>
/// Keeps a single rolling snapshot of the database (bookkeeping.autobackup.db),
/// refreshed periodically and on app exit. Uses SQLite's online backup API so
/// the snapshot is always consistent even if taken mid-use.
/// </summary>
public class AutoBackupService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly string _dbPath;
    private readonly string _snapshotPath;
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _busy;

    public AutoBackupService(string dbPath)
    {
        _dbPath = dbPath;
        _snapshotPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "bookkeeping.autobackup.db");
    }

    public string SnapshotPath => _snapshotPath;

    public void Start()
    {
        if (_timer != null) return;
        _timer = new Timer(async _ => await CreateSnapshotAsync(), null, Interval, Interval);
    }

    public async Task CreateSnapshotAsync()
    {
        if (!File.Exists(_dbPath)) return;
        lock (_gate)
        {
            if (_busy) return;
            _busy = true;
        }

        try
        {
            await Task.Run(() =>
            {
                using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
                source.Open();
                using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _snapshotPath }.ToString());
                destination.Open();
                source.BackupDatabase(destination);
            });
        }
        catch
        {
            // A failed snapshot must never take the app down; the next tick retries.
        }
        finally
        {
            lock (_gate) { _busy = false; }
        }
    }

    public void Dispose() => _timer?.Dispose();
}
