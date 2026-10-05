using System.Data.Common;
using System.IO;
using System.IO.Compression;
using Bookkeeping.Data;
using Bookkeeping.Wpf.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bookkeeping.Tests;

public sealed class BackupRegressionTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task ZipBackup_IncludesCommittedWalTransactions()
    {
        await _fixture.Db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await _fixture.Db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
        await _fixture.AddDonationAsync(125);
        Assert.True(new FileInfo(_fixture.DatabasePath + "-wal").Length > 0);

        var path = await _fixture.Backup.CreateBackupAsync();
        using var archive = ZipFile.OpenRead(path);
        var snapshotPath = Path.Combine(_fixture.Root, "extracted.db");
        archive.GetEntry("bookkeeping.db")!.ExtractToFile(snapshotPath);
        using var snapshot = new SqliteConnection($"Data Source={snapshotPath};Pooling=False");
        await snapshot.OpenAsync();
        using var query = snapshot.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM Donations";
        Assert.Equal(1L, await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Restore_UpdatesLiveDatabaseAndAttachmentsAndPreservesRecoveryCopy()
    {
        await _fixture.AddDonationAsync(125);
        File.WriteAllText(Path.Combine(_fixture.Attachments, "receipt.txt"), "Original receipt");
        var service = _fixture.Backup;
        var archive = await service.CreateBackupAsync();
        await _fixture.AddDonationAsync(999);
        File.WriteAllText(Path.Combine(_fixture.Attachments, "receipt.txt"), "Later receipt");
        File.WriteAllText(Path.Combine(_fixture.Attachments, "later.txt"), "Later file");

        await service.RestoreFromBackupAsync(archive);
        _fixture.Db.ChangeTracker.Clear();
        Assert.Equal(1, await _fixture.Db.Donations.CountAsync());
        Assert.Equal(125, (await _fixture.Db.Donations.SingleAsync()).Amount);
        Assert.Equal("Original receipt", File.ReadAllText(Path.Combine(_fixture.Attachments, "receipt.txt")));
        Assert.False(File.Exists(Path.Combine(_fixture.Attachments, "later.txt")));
        var oldAttachments = Assert.Single(Directory.GetDirectories(_fixture.Root, "attachments.pre_restore_*"));
        Assert.Equal("Later receipt", File.ReadAllText(Path.Combine(oldAttachments, "receipt.txt")));
        var oldDatabase = Assert.Single(Directory.GetFiles(_fixture.Root, "bookkeeping.db.pre_restore_*.bak"));
        using var recovery = new SqliteConnection($"Data Source={oldDatabase};Pooling=False");
        recovery.Open();
        using var query = recovery.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM Donations";
        Assert.Equal(2L, query.ExecuteScalar());
        Assert.Empty(Directory.GetDirectories(_fixture.Root, ".restore_*"));
    }

    [Theory]
    [InlineData("attachments/../../outside.txt")]
    [InlineData("attachments/..\\..\\outside.txt")]
    [InlineData("attachments/C:/outside.txt")]
    [InlineData("/attachments/outside.txt")]
    [InlineData("attachments/receipt.txt:stream")]
    [InlineData("attachments/.. /.. /outside.txt")]
    public async Task Restore_RejectsUnsafeArchiveBeforeChangingData(string unsafeName)
    {
        await _fixture.AddDonationAsync(50);
        File.WriteAllText(Path.Combine(_fixture.Attachments, "keep.txt"), "Keep");
        var archivePath = await _fixture.Backup.CreateBackupAsync();
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(zip.CreateEntry(unsafeName).Open());
            writer.Write("Overwrite");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => _fixture.Backup.RestoreFromBackupAsync(archivePath));
        Assert.Equal(1, await _fixture.Db.Donations.CountAsync());
        Assert.Equal("Keep", File.ReadAllText(Path.Combine(_fixture.Attachments, "keep.txt")));
        Assert.Empty(Directory.GetFiles(_fixture.Root, "*.bak"));
        Assert.Empty(Directory.GetDirectories(_fixture.Root, ".restore_*"));
    }

    [Fact]
    public async Task Restore_RejectsMissingDatabaseAndDuplicateEntries()
    {
        var path = Path.Combine(_fixture.Root, "invalid.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) zip.CreateEntry("attachments/receipt.txt");
        await Assert.ThrowsAsync<InvalidDataException>(() => _fixture.Backup.RestoreFromBackupAsync(path));
        File.Delete(path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("bookkeeping.db");
            zip.CreateEntry("attachments/receipt.txt");
            zip.CreateEntry("attachments/RECEIPT.txt");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => _fixture.Backup.RestoreFromBackupAsync(path));
        Assert.Equal(1, await _fixture.Db.Sponsors.CountAsync());
    }

    [Fact]
    public async Task Restore_RejectsUnrelatedSqliteDatabase()
    {
        var unrelatedPath = Path.Combine(_fixture.Root, "unrelated.db");
        using (var unrelated = new SqliteConnection($"Data Source={unrelatedPath};Pooling=False"))
        {
            unrelated.Open();
            using var command = unrelated.CreateCommand();
            command.CommandText = "CREATE TABLE unrelated (value TEXT);";
            command.ExecuteNonQuery();
        }
        var archivePath = Path.Combine(_fixture.Root, "unrelated.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create)) zip.CreateEntryFromFile(unrelatedPath, "bookkeeping.db");
        await Assert.ThrowsAsync<InvalidDataException>(() => _fixture.Backup.RestoreFromBackupAsync(archivePath));
        Assert.Equal(1, await _fixture.Db.Sponsors.CountAsync());
        Assert.Empty(Directory.GetFiles(_fixture.Root, "*.bak"));
    }

    [Fact]
    public async Task FreshDatabase_MigratesAndInitializesDefaultProgramAccounts()
    {
        var path = Path.Combine(_fixture.Root, "fresh.db");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        await using var db = new AppDbContext(options);
        await new DatabaseInitializer(db).InitializeAsync();
        var program = await db.Programs.Include(p => p.IncomeAccount).Include(p => p.ExpenseAccount).SingleAsync();
        Assert.NotNull(program.IncomeAccount);
        Assert.NotNull(program.ExpenseAccount);
        Assert.Equal("General Operations", program.Name);
        Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task MigrationFailure_IsReportedOnceWithoutResetOrRetry()
    {
        await _fixture.AddDonationAsync(75);
        var interceptor = new FailMigrationInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_fixture.DatabasePath};Pooling=False")
            .AddInterceptors(interceptor).Options;
        await using var db = new AppDbContext(options);
        var initializer = new DatabaseInitializer(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync());
        Assert.Equal(1, interceptor.Attempts);
        Assert.Equal(1, await _fixture.Db.Donations.CountAsync());
        Assert.Empty(Directory.GetFiles(_fixture.Root, "*.broken_*"));
    }

    private sealed class FailMigrationInterceptor : DbCommandInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("CREATE TABLE", StringComparison.Ordinal))
            {
                Attempts++;
                throw new InvalidOperationException("Simulated migration failure");
            }
            return new(result);
        }
    }
}
