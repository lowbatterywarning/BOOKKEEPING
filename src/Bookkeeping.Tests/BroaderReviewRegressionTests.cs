using System.IO;
using System.Data.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Bookkeeping.Services;
using Bookkeeping.Wpf.Services;
using Bookkeeping.Wpf.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit.Abstractions;

namespace Bookkeeping.Tests;

// Regression coverage for the second review, using real SQLite and WPF bindings.
public sealed class BroaderReviewRegressionTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task FailedTransferRetry_PersistsOnlyOneTransfer()
    {
        var db = _fixture.Db;
        var vm = new CashBankViewModel(db, new JournalEngine(db)) { TransferAmount = 25 };
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_transfer BEFORE INSERT ON CashBankTransfers BEGIN SELECT RAISE(ABORT, 'Temporary failure'); END;");
        await vm.ExecuteTransferCommand.ExecuteAsync(null);
        Assert.NotNull(vm.TransferError);
        Assert.Equal(0, await db.CashBankTransfers.CountAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_transfer;");
        await vm.ExecuteTransferCommand.ExecuteAsync(null);
        Assert.Null(vm.TransferError);
        var count = await db.CashBankTransfers.CountAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DuplicateProgramName_CanRecoverByCorrectingName()
    {
        var db = _fixture.Db;
        var vm = new ProgramsViewModel(db);
        await vm.LoadAsync();
        vm.AddNewCommand.Execute(null);
        vm.EditName = "Program";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(vm.EditErrorMessage);
        vm.EditName = "Corrected unique name";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.EditErrorMessage);
        Assert.True(await db.Programs.AnyAsync(p => p.Name == "Corrected unique name"));

    }

    [Fact]
    public async Task ReportPaymentFilter_CheckTranslation()
    {
        await _fixture.AddDonationAsync(15);
        var service = new ReportService(_fixture.Db);
        var rows = await service.GetDonationReportAsync(null, null, null, null, "Cash");
        Assert.Equal(15, Assert.Single(rows).Amount);

    }

    [Fact]
    public async Task ReportTypeChange_InvalidatesOldTotalUntilGenerate()
    {
        await _fixture.AddDonationAsync(100);
        await _fixture.AddExpenseAsync(20);
        var vm = new ReportsViewModel(_fixture.Db, new ReportService(_fixture.Db), new ExportService());
        await vm.LoadAsync();
        vm.SelectedReportType = "Expense";
        Assert.Equal(0, vm.TotalAmount);
        Assert.Empty(vm.ExpenseReport);
        await vm.GenerateReportAsync();
        Assert.Equal(20, vm.TotalAmount);

    }

    [Fact]
    public async Task DuplicateAnnualBudgets_AreRejectedByDatabase()
    {
        var db = _fixture.Db;
        db.Budgets.AddRange(new Budget { Year = DateTime.Today.Year, ProgramId = _fixture.ProgramId, Amount = 100 },
            new Budget { Year = DateTime.Today.Year, ProgramId = _fixture.ProgramId, Amount = 200 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Budgets.ToListAsync());
    }

    [Fact]
    public async Task InvalidAmountText_RejectsSave()
    {
        await OnDispatcher(async () =>
        {
            var db = _fixture.Db;
            var vm = new IncomeViewModel(db, new JournalEngine(db), new AuditService(db));
            await vm.LoadAsync();
            vm.NewProgram = vm.Programs.Single();
            vm.NewSponsor = vm.Sponsors.Single();
            var input = new TextBox();
            input.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.NewAmountText))
            { Source = vm, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            input.Text = "100";
            input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.Equal(100, vm.NewAmount);
            input.Text = "100x";
            input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.Equal("100x", vm.NewAmountText);
            await vm.SaveDonationCommand.ExecuteAsync(null);
            Assert.NotNull(vm.ErrorMessage);
            Assert.Empty(await db.Donations.ToListAsync());
            input.Text = "125";
            input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            await vm.SaveDonationCommand.ExecuteAsync(null);
            Assert.Equal(125, (await db.Donations.SingleAsync()).Amount);

        });
    }

    [Fact]
    public async Task FilterReload_RetainsSelectedProgramInBoundComboBox()
    {
        await OnDispatcher(async () =>
        {
            var vm = new IncomeViewModel(_fixture.Db, new JournalEngine(_fixture.Db), new AuditService(_fixture.Db));
            await vm.LoadAsync();
            var selector = new ComboBox();
            selector.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Programs)) { Source = vm });
            selector.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(vm.FilterProgram)) { Source = vm, Mode = BindingMode.TwoWay });
            selector.SelectedItem = vm.Programs.Single();
            Assert.NotNull(vm.FilterProgram);
            await vm.LoadAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            Assert.NotNull(vm.FilterProgram);
        });
    }

    [Fact]
    public async Task NegativeBudget_SavePreservesExistingBudget()
    {
        var db = _fixture.Db;
        db.Budgets.Add(new Budget { Year = DateTime.Today.Year, ProgramId = _fixture.ProgramId, Amount = 100 });
        await db.SaveChangesAsync();
        var vm = new BudgetViewModel(db);
        await vm.LoadAsync();
        Assert.Single(vm.BudgetRows).BudgetAmount = -5;
        await vm.SaveAllBudgetsCommand.ExecuteAsync(null);
        Assert.Equal(100, (await db.Budgets.SingleAsync()).Amount);
        Assert.Contains("non-negative", vm.StatusMessage);

    }

    [Fact]
    public async Task Search_ReportsTotalMatchesAndTruncation()
    {
        for (var i = 0; i < 51; i++) await _fixture.AddDonationAsync(1, save: false);
        await _fixture.Db.SaveChangesAsync();
        var vm = new SearchViewModel(_fixture.Db) { SearchQuery = "Sponsor" };
        await vm.SearchAsync();
        Assert.Equal(50, vm.DonationResults.Count);
        Assert.Contains("donations: showing 50 of 51", vm.StatusMessage);

    }

    [Fact]
    public async Task ReceiptPathFromDatabase_CannotDeleteOutsideAttachments()
    {
        var outside = Path.Combine(_fixture.Root, "outside.txt");
        File.WriteAllText(outside, "Keep this unrelated file");
        await _fixture.AddExpenseAsync(10);
        var expense = await _fixture.Db.Expenses.SingleAsync();
        expense.ReceiptAttachmentPath = "../outside.txt";
        await _fixture.Db.SaveChangesAsync();
        var vm = new ExpensesViewModel(_fixture.Db, new JournalEngine(_fixture.Db), new AuditService(_fixture.Db), new ExportService(), _fixture.Attachments);
        await vm.DeleteExpenseCommand.ExecuteAsync(expense);
        Assert.True(File.Exists(outside));

    }

    [Fact]
    public async Task RefreshFailureAfterDonationCommit_ReportsSavedRecord()
    {
        var interceptor = new FailDonationRead();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_fixture.DatabasePath};Pooling=False").AddInterceptors(interceptor).Options;
        await using var db = new AppDbContext(options);
        var vm = new IncomeViewModel(db, new JournalEngine(db), new AuditService(db))
        {
            NewSponsor = await db.Sponsors.SingleAsync(), NewProgram = await db.Programs.SingleAsync(), NewAmount = 40
        };
        var error = await Record.ExceptionAsync(() => vm.SaveDonationCommand.ExecuteAsync(null));
        Assert.Null(error);
        Assert.Contains("Donation saved. Refresh failed", vm.ErrorMessage);
        Assert.False(vm.IsAdding);
        Assert.Equal(1, await _fixture.Db.Donations.CountAsync());

    }

    [Fact]
    public async Task SearchAndBudgetControls_HaveWorkingProperties()
    {
        await Task.CompletedTask;
        Assert.NotNull(typeof(SearchViewModel).GetProperty("SearchTypes"));
        Assert.NotNull(typeof(SearchViewModel).GetProperty("SearchType"));
        Assert.NotNull(typeof(SearchViewModel).GetProperty("SponsorResults"));
        Assert.NotNull(typeof(BudgetViewModel).GetProperty("BudgetTypes"));
        Assert.NotNull(typeof(BudgetViewModel).GetProperty("BudgetType"));

    }

    [Fact]
    public async Task BudgetActualYtd_ExcludesFutureDatedExpenses()
    {
        await _fixture.AddExpenseAsync(50);
        var expense = await _fixture.Db.Expenses.SingleAsync();
        expense.Date = new DateTime(DateTime.Today.Year, 12, 31, 23, 59, 59);
        await _fixture.Db.SaveChangesAsync();
        var vm = new BudgetViewModel(_fixture.Db);
        await vm.LoadAsync();
        Assert.Equal(0, vm.TotalActual);

    }

    private sealed class FailDonationRead : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Donations\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Temporary refresh failure");
            return new(result);
        }
    }

    [Fact]
    public async Task ExistingYearTarget_IsExcludedFromCurrentProgress()
    {
        _fixture.Db.SponsorTargets.Add(new SponsorTarget { SponsorId = _fixture.SponsorId, ProgramId = _fixture.ProgramId,
            TargetAmount = 100, Year = DateTime.Today.Year - 1 });
        await _fixture.Db.SaveChangesAsync();
        var sponsors = new SponsorsViewModel(_fixture.Db);
        await sponsors.LoadAsync();
        var dashboard = new SponsorDashboardViewModel(_fixture.Db);
        await dashboard.LoadDataAsync();
        Assert.Equal(0, Assert.Single(sponsors.Sponsors).TotalTargetAmount);
        Assert.Equal(0, Assert.Single(dashboard.Tiles).TotalTargetAmount);

    }

    [Fact]
    public async Task MonthlyBudget_UsesSelectedPeriodAndPreservesAnnualBudget()
    {
        var db = _fixture.Db;
        db.Budgets.Add(new Budget { Year = DateTime.Today.Year, ProgramId = _fixture.ProgramId, Amount = 100 });
        await db.SaveChangesAsync();
        var vm = new BudgetViewModel(db);
        await vm.LoadAsync();
        vm.BudgetType = "Monthly";
        await vm.PendingRefresh;
        Assert.Single(vm.BudgetRows).BudgetAmount = 25;
        await vm.SaveAllBudgetsCommand.ExecuteAsync(null);
        Assert.Equal(100, (await db.Budgets.SingleAsync(b => b.Month == null)).Amount);
        Assert.Equal(25, (await db.Budgets.SingleAsync(b => b.Month == DateTime.Today.Month)).Amount);
        vm.SelectedMonth = DateTime.Today.Month == 12 ? 1 : 12;
        await vm.PendingRefresh;
        Assert.Equal(0, Assert.Single(vm.BudgetRows).BudgetAmount);
    }

    [Fact]
    public async Task SponsorSearch_FindsContactFieldsAndHonorsType()
    {
        var sponsor = await _fixture.Db.Sponsors.SingleAsync();
        sponsor.Address = "Unique contact address";
        await _fixture.Db.SaveChangesAsync();
        var vm = new SearchViewModel(_fixture.Db) { SearchType = "Sponsors", SearchQuery = "contact" };
        await vm.SearchAsync();
        Assert.Single(vm.SponsorResults);
        Assert.Empty(vm.DonationResults); Assert.Empty(vm.ExpenseResults);
        vm.ClearSearch(); Assert.Empty(vm.SponsorResults);
    }

    [Fact]
    public async Task AllNumericEditors_RejectInvalidTextWithoutPersistingStaleAmounts()
    {
        var db = _fixture.Db;
        var expenses = new ExpensesViewModel(db, new JournalEngine(db), new AuditService(db), new ExportService(), _fixture.Attachments)
        { NewProgram = await db.Programs.SingleAsync(), NewVendorName = "Vendor", NewAmount = 100, NewAmountText = "100x" };
        await expenses.SaveExpenseCommand.ExecuteAsync(null);
        Assert.NotNull(expenses.ErrorMessage); Assert.Empty(await db.Expenses.ToListAsync());
        var cash = new CashBankViewModel(db, new JournalEngine(db))
        { TransferAmount = 100, TransferAmountText = "100x", BeginningBalanceAmount = 100, BeginningBalanceAmountText = "100x" };
        await cash.ExecuteTransferCommand.ExecuteAsync(null); await cash.SetBeginningBalanceCommand.ExecuteAsync(null);
        Assert.NotNull(cash.TransferError); Assert.NotNull(cash.BeginningBalanceError);
        Assert.Empty(await db.JournalEntries.ToListAsync());
        var budget = new BudgetViewModel(db); await budget.LoadAsync();
        budget.BudgetRows.Single().BudgetAmount = 100;
        budget.BudgetRows.Single().BudgetAmountText = "100x";
        await budget.SaveAllBudgetsCommand.ExecuteAsync(null); Assert.Empty(await db.Budgets.ToListAsync());
        var sponsors = new SponsorsViewModel(db); await sponsors.AddNewCommand.ExecuteAsync(null);
        sponsors.EditName = "New Sponsor"; sponsors.EditTargets.Single().TargetAmount = 100;
        sponsors.EditTargets.Single().TargetAmountText = "100x";
        await sponsors.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(sponsors.EditErrorMessage); Assert.Equal(1, await db.Sponsors.CountAsync());
    }

    [Fact]
    public async Task FailedBeginningBalanceRetry_PersistsOnlyOneEntry()
    {
        var db = _fixture.Db;
        var vm = new CashBankViewModel(db, new JournalEngine(db)) { BeginningBalanceAmount = 25 };
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_entry BEFORE INSERT ON JournalEntries BEGIN SELECT RAISE(ABORT, 'Temporary failure'); END;");
        await vm.SetBeginningBalanceCommand.ExecuteAsync(null);
        Assert.NotNull(vm.BeginningBalanceError);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_entry;");
        await vm.SetBeginningBalanceCommand.ExecuteAsync(null);
        Assert.Null(vm.BeginningBalanceError); Assert.Equal(1, await db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Restore_RejectsUnsafeReceiptPathStoredInsideDatabase()
    {
        await _fixture.AddExpenseAsync(10);
        (await _fixture.Db.Expenses.SingleAsync()).ReceiptAttachmentPath = "../outside.txt";
        await _fixture.Db.SaveChangesAsync();
        var backup = await _fixture.Backup.CreateBackupAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => _fixture.Backup.RestoreFromBackupAsync(backup));
        Assert.Empty(Directory.GetFiles(_fixture.Root, "*.bak"));
        Assert.Equal(1, await _fixture.Db.Expenses.CountAsync());
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("/outside.txt")]
    [InlineData("receipt.txt:stream")]
    [InlineData("folder./receipt.txt")]
    [InlineData("folder /receipt.txt")]
    public void ReceiptPaths_RejectUnsafeNames(string path) =>
        Assert.Throws<InvalidDataException>(() => ReceiptPaths.Resolve(_fixture.Attachments, path));

    [Fact]
    public async Task AnnualBudgetMigration_ArchivesDuplicatesAndEnforcesUniqueIndex()
    {
        var path = Path.Combine(_fixture.Root, "legacy.db");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        await using var db = new AppDbContext(options);
        await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20260806112621_InitialCreate");
        var program = new OrgProgram { Name = "Legacy", FundId = 1 }; db.Programs.Add(program); await db.SaveChangesAsync();
        db.Budgets.AddRange(new Budget { Year = 2026, ProgramId = program.Id, Amount = 100, Notes = "Old", CreatedAt = new DateTime(2026,1,1) },
            new Budget { Year = 2026, ProgramId = program.Id, Amount = 200, Notes = "New", CreatedAt = new DateTime(2026,2,1) });
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.Equal(200, (await db.Budgets.SingleAsync()).Amount);
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT Amount FROM BudgetDuplicateArchive WHERE Notes = 'Old'";
        Assert.Equal(100m, Convert.ToDecimal(await command.ExecuteScalarAsync()));
        db.Budgets.Add(new Budget { Year = 2026, ProgramId = program.Id, Amount = 300 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("Income", "FilterProgram")]
    [InlineData("Income", "NewProgram")]
    [InlineData("Income", "FilterSponsor")]
    [InlineData("Income", "NewSponsor")]
    [InlineData("Expenses", "FilterProgram")]
    [InlineData("Expenses", "NewProgram")]
    [InlineData("Reports", "FilterProgram")]
    [InlineData("Reports", "FilterSponsor")]
    public async Task AllPickerBindings_RetainSelectionsDuringReload(string screen, string property)
    {
        await OnDispatcher(async () =>
        {
            var db = _fixture.Db;
            object vm = screen switch
            {
                "Income" => new IncomeViewModel(db, new JournalEngine(db), new AuditService(db)),
                "Expenses" => new ExpensesViewModel(db, new JournalEngine(db), new AuditService(db), new ExportService(), _fixture.Attachments),
                _ => new ReportsViewModel(db, new ReportService(db), new ExportService())
            };
            async Task Reload()
            {
                switch (vm)
                {
                    case IncomeViewModel income: await income.LoadAsync(); break;
                    case ExpensesViewModel expenses: await expenses.LoadAsync(); break;
                    case ReportsViewModel reports: await reports.LoadAsync(); break;
                }
            }
            await Reload();
            var source = property.Contains("Sponsor") ? "Sponsors" : "Programs";
            var selector = new ComboBox();
            selector.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(source) { Source = vm });
            selector.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(property) { Source = vm, Mode = BindingMode.TwoWay });
            selector.SelectedIndex = 0;
            var selected = selector.SelectedItem;
            Assert.NotNull(selected);
            await Reload(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(selected, vm.GetType().GetProperty(property)!.GetValue(vm));
            Assert.Same(selected, selector.SelectedItem);
        });
    }

    [Theory]
    [InlineData("Expense")]
    [InlineData("Transfer")]
    [InlineData("Beginning balance")]
    public async Task AllFinancialSaves_ReportRefreshFailuresAfterCommit(string operation)
    {
        var state = new CommitState();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_fixture.DatabasePath};Pooling=False")
            .AddInterceptors(new MarkCommit(state), new FailReadAfterCommit(state)).Options;
        await using var db = new AppDbContext(options);
        string? message;
        if (operation == "Expense")
        {
            var vm = new ExpensesViewModel(db, new JournalEngine(db), new AuditService(db), new ExportService(), _fixture.Attachments)
            { NewProgram = await db.Programs.SingleAsync(), NewVendorName = "Vendor", NewAmount = 40, IsAdding = true };
            await vm.SaveExpenseCommand.ExecuteAsync(null); message = vm.ErrorMessage;
            Assert.False(vm.IsAdding); Assert.Equal(1, await _fixture.Db.Expenses.CountAsync());
        }
        else
        {
            var vm = new CashBankViewModel(db, new JournalEngine(db))
            { TransferAmount = 40, BeginningBalanceAmount = 40, IsTransferring = true, IsSettingBeginningBalance = true };
            if (operation == "Transfer")
            { await vm.ExecuteTransferCommand.ExecuteAsync(null); message = vm.TransferError; Assert.False(vm.IsTransferring); }
            else
            { await vm.SetBeginningBalanceCommand.ExecuteAsync(null); message = vm.BeginningBalanceError; Assert.False(vm.IsSettingBeginningBalance); }
            Assert.Equal(1, await _fixture.Db.JournalEntries.CountAsync());
        }
        Assert.Contains($"{operation} saved. Refresh failed", message);
    }

    private sealed class CommitState { public bool Committed { get; set; } }
    private sealed class MarkCommit(CommitState state) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { state.Committed = true; return Task.CompletedTask; }
    }
    private sealed class FailReadAfterCommit(CommitState state) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (state.Committed) throw new InvalidOperationException("Temporary refresh failure");
            return new(result);
        }
    }

    [Fact]
    public async Task MissingRequiredDates_RejectFinancialSaves()
    {
        var db = _fixture.Db;
        var income = new IncomeViewModel(db, new JournalEngine(db), new AuditService(db))
        { NewDate = null, NewAmount = 25, NewProgram = await db.Programs.SingleAsync(), NewSponsor = await db.Sponsors.SingleAsync() };
        await income.SaveDonationCommand.ExecuteAsync(null); Assert.Contains("date", income.ErrorMessage);
        var expenses = new ExpensesViewModel(db, new JournalEngine(db), new AuditService(db), new ExportService(), _fixture.Attachments)
        { NewDate = null, NewAmount = 25, NewVendorName = "Vendor", NewProgram = await db.Programs.SingleAsync() };
        await expenses.SaveExpenseCommand.ExecuteAsync(null); Assert.Contains("date", expenses.ErrorMessage);
        var cash = new CashBankViewModel(db, new JournalEngine(db))
        { TransferDate = null, TransferAmount = 25, BeginningBalanceDate = null, BeginningBalanceAmount = 25 };
        await cash.ExecuteTransferCommand.ExecuteAsync(null); Assert.Contains("date", cash.TransferError);
        await cash.SetBeginningBalanceCommand.ExecuteAsync(null); Assert.Contains("date", cash.BeginningBalanceError);
        Assert.Empty(await db.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task BudgetGridFailure_IsRecoverableAndClearsStaleTotals()
    {
        var db = _fixture.Db;
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Budgets_AnnualProgram;");
        db.Budgets.AddRange(new Budget { Year = DateTime.Today.Year, ProgramId = _fixture.ProgramId, Amount = 100 },
            new Budget { Year = DateTime.Today.Year, ProgramId = _fixture.ProgramId, Amount = 200 });
        await db.SaveChangesAsync();
        var vm = new BudgetViewModel(db);
        await vm.LoadAsync();
        Assert.Contains("Could not load budgets", vm.StatusMessage);
        Assert.Empty(vm.BudgetRows); Assert.Equal(0, vm.TotalBudget);
    }

    private static Task OnDispatcher(Func<Task> action)
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
