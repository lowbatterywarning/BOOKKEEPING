using System.IO;
using System.Windows.Threading;
using Bookkeeping.Core.Enums;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Bookkeeping.Services;
using Bookkeeping.Wpf.Services;
using Bookkeeping.Wpf.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bookkeeping.Tests;

public sealed class ApplicationRegressionTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private AppDbContext Db => _fixture.Db;
    private JournalEngine Engine => new(Db);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task CashBank_CarriesPriorYearClosingBalancesIntoRunningLedger()
    {
        var priorYear = new DateTime(DateTime.Today.Year - 1, 12, 31);
        await Engine.RecordBeginningBalanceAsync("1000", 1000, priorYear, 1);
        await Engine.RecordBeginningBalanceAsync("1010", 2000, priorYear, 1);
        await Db.SaveChangesAsync();
        await _fixture.AddDonationAsync(125);
        await _fixture.AddExpenseAsync(25, PaymentMethod.Bank);

        var vm = new CashBankViewModel(Db, Engine);
        await vm.LoadAsync();

        Assert.Equal(1000, vm.CashBeginningBalance);
        Assert.Equal(1125, vm.CashEndingBalance);
        Assert.Equal(1125, Assert.Single(vm.CashTransactions).Balance);
        Assert.Equal(2000, vm.BankBeginningBalance);
        Assert.Equal(1975, vm.BankEndingBalance);
        Assert.Equal(1975, Assert.Single(vm.BankTransactions).Balance);
    }

    [Fact]
    public async Task CashBank_NoCurrentActivityStillShowsCarriedBalance()
    {
        await Engine.RecordBeginningBalanceAsync("1000", 123, new DateTime(DateTime.Today.Year - 1, 1, 1), 1);
        await Db.SaveChangesAsync();
        var vm = new CashBankViewModel(Db, Engine);
        await vm.LoadAsync();
        Assert.Equal(123, vm.CashBeginningBalance);
        Assert.Equal(123, vm.CashEndingBalance);
        Assert.Empty(vm.CashTransactions);
    }

    [Fact]
    public async Task ReceiptCalledOpen_IsDonationAndDoesNotBlockRealOpeningBalance()
    {
        await _fixture.AddDonationAsync(50, receipt: "OPEN");
        var vm = new CashBankViewModel(Db, Engine);
        await vm.LoadAsync();
        Assert.Equal("Donation", Assert.Single(vm.CashTransactions).Type);
        Assert.Equal(50, vm.CashIncome);
        Assert.Equal(0, vm.CashBeginningBalance);

        vm.BeginningBalanceAmount = 100;
        await vm.SetBeginningBalanceCommand.ExecuteAsync(null);
        Assert.Null(vm.BeginningBalanceError);
        Assert.Equal(150, vm.CashEndingBalance);
        Assert.Equal(100, vm.CashBeginningBalance);
        Assert.Equal(1, await Db.JournalEntries.CountAsync(j => j.Reference == "OPEN" && j.Donation == null));

        vm.BeginningBalanceAmount = 20;
        await vm.SetBeginningBalanceCommand.ExecuteAsync(null);
        Assert.NotNull(vm.BeginningBalanceError);
        Assert.Equal(150, vm.CashEndingBalance);
    }

    [Fact]
    public async Task DetailTotals_IncludeMoreThan500RecordsWhileListsStayBounded()
    {
        for (var i = 0; i < 501; i++)
            await _fixture.AddDonationAsync(1, save: false);
        for (var i = 0; i < 501; i++)
            await _fixture.AddExpenseAsync(2, save: false);
        await Db.SaveChangesAsync();

        var program = new ProgramDetailViewModel(Db, _fixture.ProgramId, "Program");
        await program.LoadAsync();
        Assert.Equal(501, program.TotalRevenue);
        Assert.Equal(1002, program.TotalExpenses);
        Assert.Equal(-501, program.NetAmount);
        Assert.Equal(500, program.Transactions.Count);
        Assert.True(program.HasMoreTransactions);
        program.ShowCurrentMonth = false;
        Assert.Equal(501, program.TotalRevenue);

        var sponsor = new SponsorDetailViewModel(Db, _fixture.SponsorId, "Sponsor");
        await sponsor.LoadAsync();
        Assert.Equal(501, sponsor.TotalDonated);
        Assert.Equal(500, sponsor.Donations.Count);
        Assert.True(sponsor.HasMoreDonations);
        sponsor.ShowCurrentMonth = false;
        Assert.Equal(501, sponsor.TotalDonated);
    }

    [Fact]
    public async Task DetailPeriods_ExcludeFutureAndPreviousYearRecords()
    {
        await _fixture.AddDonationAsync(10);
        await _fixture.AddDonationAsync(100, date: DateTime.Today.AddDays(2));
        await _fixture.AddDonationAsync(1000, date: new DateTime(DateTime.Today.Year - 1, 12, 31));
        var vm = new SponsorDetailViewModel(Db, _fixture.SponsorId, "Sponsor");
        await vm.LoadAsync();
        vm.ShowCurrentMonth = false;
        Assert.Equal(10, vm.TotalDonated);
        Assert.Single(vm.Donations);
        Assert.False(vm.HasMoreDonations);
    }

    [Fact]
    public async Task DonationCreation_PersistsAuditAfterRefresh()
    {
        var vm = new IncomeViewModel(Db, Engine, new AuditService(Db));
        await vm.LoadAsync();
        vm.NewSponsor = vm.Sponsors.Single();
        vm.NewProgram = vm.Programs.Single();
        vm.NewAmount = 35;
        await vm.SaveDonationCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        var donation = await Db.Donations.SingleAsync();
        var audit = await Db.AuditLogs.SingleAsync();
        Assert.Equal("Create", audit.Action);
        Assert.Equal("Donation", audit.EntityType);
        Assert.Equal(donation.Id, audit.EntityId);
        Assert.Equal(2, await Db.JournalEntryLines.CountAsync());
    }

    [Fact]
    public async Task ExpenseCreation_PersistsAuditAfterRefresh()
    {
        var vm = new ExpensesViewModel(Db, Engine, new AuditService(Db), new ExportService(), _fixture.Attachments);
        await vm.LoadAsync();
        vm.NewProgram = vm.Programs.Single();
        vm.NewVendorName = "Vendor";
        vm.NewAmount = 15;
        await vm.SaveExpenseCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        var expense = await Db.Expenses.SingleAsync();
        var audit = await Db.AuditLogs.SingleAsync();
        Assert.Equal("Expense", audit.EntityType);
        Assert.Equal(expense.Id, audit.EntityId);
        Assert.Equal(2, await Db.JournalEntryLines.CountAsync());
    }

    [Fact]
    public async Task SponsorEdit_PreservesHiddenTargetsAndUpdatesExistingTargetInPlace()
    {
        var inactive = new OrgProgram { Name = "Inactive", FundId = 1, IsActive = false };
        Db.Programs.Add(inactive);
        await Db.SaveChangesAsync();
        var visibleTarget = new SponsorTarget { SponsorId = _fixture.SponsorId, ProgramId = _fixture.ProgramId, TargetAmount = 100 };
        var hiddenTarget = new SponsorTarget { SponsorId = _fixture.SponsorId, ProgramId = inactive.Id, TargetAmount = 200 };
        var historicalTarget = new SponsorTarget { SponsorId = _fixture.SponsorId, ProgramId = _fixture.ProgramId, TargetAmount = 300, Year = DateTime.Today.Year - 1 };
        Db.SponsorTargets.AddRange(visibleTarget, hiddenTarget, historicalTarget);
        await Db.SaveChangesAsync();
        var visibleId = visibleTarget.Id;
        var hiddenId = hiddenTarget.Id;
        var historicalId = historicalTarget.Id;

        var vm = new SponsorsViewModel(Db);
        await vm.LoadAsync();
        vm.SelectedSponsor = vm.Sponsors.Single();
        await vm.EditCommand.ExecuteAsync(null);
        vm.EditName = "Updated contact";
        Assert.Single(vm.EditTargets).TargetAmount = 150;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.EditErrorMessage);
        Assert.Equal(3, await Db.SponsorTargets.CountAsync());
        Assert.Equal(150, (await Db.SponsorTargets.FindAsync(visibleId))!.TargetAmount);
        Assert.Equal(200, (await Db.SponsorTargets.FindAsync(hiddenId))!.TargetAmount);
        Assert.Equal(300, (await Db.SponsorTargets.FindAsync(historicalId))!.TargetAmount);
    }

    [Fact]
    public async Task SponsorCreation_SavesContactAndTargetsTogether()
    {
        var vm = new SponsorsViewModel(Db);
        await vm.AddNewCommand.ExecuteAsync(null);
        vm.EditName = "New sponsor";
        Assert.Single(vm.EditTargets).TargetAmount = 250;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.EditErrorMessage);
        var sponsor = await Db.Sponsors.Include(s => s.Targets).SingleAsync(s => s.Name == "New sponsor");
        Assert.Equal(250, Assert.Single(sponsor.Targets).TargetAmount);
    }

    [Fact]
    public async Task SponsorEdit_FailedSaveLeavesContactAndTargetsUnchanged()
    {
        Db.SponsorTargets.Add(new SponsorTarget { SponsorId = _fixture.SponsorId, ProgramId = _fixture.ProgramId, TargetAmount = 100 });
        await Db.SaveChangesAsync();
        var vm = new SponsorsViewModel(Db);
        await vm.LoadAsync();
        vm.SelectedSponsor = vm.Sponsors.Single();
        await vm.EditCommand.ExecuteAsync(null);
        vm.EditName = "Should roll back";
        Assert.Single(vm.EditTargets).TargetAmount = 200;
        await Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_target_update BEFORE UPDATE ON SponsorTargets BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(vm.EditErrorMessage);
        Assert.Equal("Sponsor", (await Db.Sponsors.SingleAsync()).Name);
        Assert.Equal(100, (await Db.SponsorTargets.SingleAsync()).TargetAmount);
    }

    [Fact]
    public async Task Dashboard_AllProgramsIncludesInactiveActivityEvenWhenNoneAreActive()
    {
        await _fixture.AddDonationAsync(100);
        await _fixture.AddExpenseAsync(30);
        (await Db.Programs.SingleAsync()).IsActive = false;
        await Db.SaveChangesAsync();
        var vm = new DashboardViewModel(Db);
        await vm.LoadDataAsync();
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsEmpty);
        var tile = Assert.Single(vm.Tiles);
        Assert.True(tile.IsAllTile);
        Assert.Equal(100, tile.RevenueMonthToDate);
        Assert.Equal(30, tile.ExpensesMonthToDate);
    }

    [Fact]
    public async Task SettingsInitialization_LoadsSavedScheduleBeforeStartingTimer()
    {
        (await Db.AppSettings.SingleAsync(s => s.Key == "AutoBackupEnabled")).Value = "true";
        (await Db.AppSettings.SingleAsync(s => s.Key == "AutoBackupIntervalHours")).Value = "12";
        await Db.SaveChangesAsync();
        await RunOnDispatcherAsync(async () =>
        {
            using var vm = new SettingsViewModel(Db, _fixture.Backup);
            await vm.InitializeAsync();
            Assert.True(vm.AutoBackupEnabled);
            Assert.Equal(12, vm.AutoBackupIntervalHours);
            Assert.True(vm.IsAutoBackupScheduled);
            vm.Dispose();
            Assert.False(vm.IsAutoBackupScheduled);
        });
    }

    private static Task RunOnDispatcherAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

internal sealed class TestDatabase : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "BookkeepingTests", Guid.NewGuid().ToString("N"));
    public string DatabasePath => Path.Combine(Root, "bookkeeping.db");
    public string Attachments => Path.Combine(Root, "attachments");
    public AppDbContext Db { get; }
    public BackupService Backup => new(DatabasePath, Attachments, Path.Combine(Root, "backups"));
    public int ProgramId { get; }
    public int SponsorId { get; }

    public TestDatabase()
    {
        Directory.CreateDirectory(Attachments);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, Pooling = false }.ToString()).Options;
        Db = new AppDbContext(options);
        Db.Database.OpenConnection();
        Db.Database.EnsureCreated();
        var program = new OrgProgram { Name = "Program", FundId = 1,
            IncomeAccount = new Account { Code = "4001", Name = "Income", AccountType = AccountType.Income, FundId = 1 },
            ExpenseAccount = new Account { Code = "5001", Name = "Expense", AccountType = AccountType.Expense, FundId = 1 } };
        var sponsor = new Sponsor { Name = "Sponsor" };
        Db.AddRange(program, sponsor);
        Db.SaveChanges();
        ProgramId = program.Id;
        SponsorId = sponsor.Id;
    }

    public async Task AddDonationAsync(decimal amount, string? receipt = null, DateTime? date = null, bool save = true)
    {
        var donation = new Donation { ProgramId = ProgramId, SponsorId = SponsorId, Amount = amount,
            Date = date ?? DateTime.Today, ReceiptNumber = receipt, PaymentMethod = PaymentMethod.Cash, CreatedByUserId = 1 };
        donation.JournalEntry = await new JournalEngine(Db).RecordDonationAsync(donation);
        Db.Donations.Add(donation);
        if (save) await Db.SaveChangesAsync();
    }

    public async Task AddExpenseAsync(decimal amount, PaymentMethod method = PaymentMethod.Cash, bool save = true)
    {
        var expense = new Expense { ProgramId = ProgramId, Amount = amount, VendorName = "Vendor",
            Date = DateTime.Today, PaymentMethod = method, CreatedByUserId = 1 };
        expense.JournalEntry = await new JournalEngine(Db).RecordExpenseAsync(expense);
        Db.Expenses.Add(expense);
        if (save) await Db.SaveChangesAsync();
    }

    public void Dispose()
    {
        Db.Dispose();
        var root = Path.GetFullPath(Root);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BookkeepingTests")) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
        Directory.Delete(root, recursive: true);
    }
}
