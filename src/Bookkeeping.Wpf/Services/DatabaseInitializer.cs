using Bookkeeping.Data;
using Bookkeeping.Core.Models;
using Bookkeeping.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.IO;

namespace Bookkeeping.Wpf.Services;

/// <summary>
/// Ensures the database is created and seeded with initial data on first run.
/// </summary>
public class DatabaseInitializer
{
    private readonly AppDbContext _db;

    public DatabaseInitializer(AppDbContext db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await InitializeInternalAsync();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 11 || ex.SqliteErrorCode == 26)
        {
            // SQLITE_CORRUPT (11) or SQLITE_NOTADB (26) — database file is corrupted.
            // Recreate from scratch to recover.
            System.Diagnostics.Debug.WriteLine($"DB corruption detected (error {ex.SqliteErrorCode}): {ex.Message}. Recreating database...");
            try { await _db.Database.EnsureDeletedAsync(); } catch { }
            await InitializeInternalAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DB init failed: {ex.Message}");
            throw;
        }
    }

    private async Task InitializeInternalAsync()
    {
        // Check if database was previously created by EnsureCreatedAsync (no migration history table)
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        bool hasMigrationHistory;
        try
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
                hasMigrationHistory = (await cmd.ExecuteScalarAsync()) != null;
            }
        }
        finally
        {
            await conn.CloseAsync();
        }

        if (!hasMigrationHistory)
        {
            // Database exists but was created by EnsureCreatedAsync (no migration support).
            // Use EnsureDeleted to properly clean up all connections and files.
            await _db.Database.EnsureDeletedAsync();
        }

        // Apply all pending migrations (creates DB if needed, upgrades existing)
        await _db.Database.MigrateAsync();

        // Fix legacy spellings from older versions
        await FixLegacySpellingsAsync();

        // Ensure restricted funds exist
        await SeedFundsAsync();

        // Ensure default donation categories exist
        await SeedDonationCategoriesAsync();

        // Ensure default expense categories exist
        await SeedExpenseCategoriesAsync();

        // Ensure default program exists
        await SeedProgramsAsync();
        // Ensure all categories have linked income/expense accounts
        await EnsureCategoryAccountsAsync();
    }

    private async Task EnsureCategoryAccountsAsync()
    {
        var changed = false;

        // Ensure every DonationCategory has an IncomeAccount
        var donationCats = await _db.DonationCategories
            .Include(c => c.IncomeAccount)
            .Where(c => c.IncomeAccount == null)
            .ToListAsync();

        if (donationCats.Count > 0)
        {
            var nextSuffix = await GetNextAccountSuffixAsync("4");
            foreach (var cat in donationCats)
            {
                var account = new Account
                {
                    Code = $"{4}{nextSuffix++:D3}",
                    Name = $"Donation Income - {cat.Name}",
                    AccountType = Core.Enums.AccountType.Income,
                    FundId = cat.FundId,
                    IsSystem = false,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Accounts.Add(account);
                cat.IncomeAccount = account;
                changed = true;
            }
        }

        // Ensure every ExpenseCategory has an ExpenseAccount
        var expenseCats = await _db.ExpenseCategories
            .Include(c => c.ExpenseAccount)
            .Where(c => c.ExpenseAccount == null)
            .ToListAsync();

        if (expenseCats.Count > 0)
        {
            var nextSuffix = await GetNextAccountSuffixAsync("5");
            foreach (var cat in expenseCats)
            {
                var account = new Account
                {
                    Code = $"{5}{nextSuffix++:D3}",
                    Name = $"Expense - {cat.Name}",
                    AccountType = Core.Enums.AccountType.Expense,
                    FundId = cat.FundId,
                    IsSystem = false,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Accounts.Add(account);
                cat.ExpenseAccount = account;
                changed = true;
            }
        }

        if (changed)
            await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Returns the next available 3-digit suffix for a given account code prefix (e.g., "4" or "5").
    /// Reads all existing codes once, then increments locally to avoid duplicates within the batch.
    /// </summary>
    private async Task<int> GetNextAccountSuffixAsync(string prefix)
    {
        var codes = await _db.Accounts
            .Where(a => a.Code.StartsWith(prefix))
            .Select(a => a.Code)
            .ToListAsync();

        int maxSuffix = 0;
        foreach (var code in codes)
        {
            var suffixStr = code[prefix.Length..];
            if (int.TryParse(suffixStr, out int suffix) && suffix > maxSuffix)
                maxSuffix = suffix;
        }

        return maxSuffix + 1;
    }

    private async Task<string> GetNextAccountCodeAsync(string prefix)
    {
        var maxCode = await _db.Accounts
            .Where(a => a.Code.StartsWith(prefix))
            .MaxAsync(a => (string?)a.Code);

        if (string.IsNullOrEmpty(maxCode))
            return $"{prefix}001";

        var suffixStr = maxCode[prefix.Length..];
        if (int.TryParse(suffixStr, out int suffix) && suffix < 999)
            return $"{prefix}{(suffix + 1):D3}";

        return $"{prefix}{DateTime.UtcNow:MMddHHmm}";    }

    private async Task FixLegacySpellingsAsync()
    {
        var zekatExists = await _db.Funds.AnyAsync(f => f.Name == "Zekat Fund");
        var legacyFund = await _db.Funds.FirstOrDefaultAsync(f => f.Name == "Zakat Fund");

        if (legacyFund != null)
        {
            if (zekatExists)
            {
                // Both exist — reassign all dependents to Zekat Fund, then delete legacy
                var zekatFund = await _db.Funds.FirstAsync(f => f.Name == "Zekat Fund");
                await ReassignFundDependentsAsync(legacyFund.Id, zekatFund.Id);
                _db.Funds.Remove(legacyFund);
            }
            else
            {
                // Only legacy exists — simple rename
                legacyFund.Name = "Zekat Fund";
                legacyFund.Description = "Restricted fund for Zekat collections and distributions";
            }
        }

        // Fix category names regardless of fund situation
        var legacyCat = await _db.DonationCategories.FirstOrDefaultAsync(c => c.Name == "Zakat");
        if (legacyCat != null) legacyCat.Name = "Zekat";

        var legacyExp = await _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Name == "Zakat Distribution");
        if (legacyExp != null) legacyExp.Name = "Zekat Distribution";

        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync();
    }

    private async Task ReassignFundDependentsAsync(int fromFundId, int toFundId)
    {
        // Reassign DonationCategories
        var donationCats = await _db.DonationCategories.Where(c => c.FundId == fromFundId).ToListAsync();
        foreach (var c in donationCats) c.FundId = toFundId;

        // Reassign ExpenseCategories
        var expenseCats = await _db.ExpenseCategories.Where(c => c.FundId == fromFundId).ToListAsync();
        foreach (var c in expenseCats) c.FundId = toFundId;

        // Reassign Accounts
        var accounts = await _db.Accounts.Where(a => a.FundId == fromFundId).ToListAsync();
        foreach (var a in accounts) a.FundId = toFundId;

        // Reassign JournalEntryLines
        var lines = await _db.JournalEntryLines.Where(l => l.FundId == fromFundId).ToListAsync();
        foreach (var l in lines) l.FundId = toFundId;
    }

    private async Task SeedFundsAsync()
    {
        // General Fund is seeded by EF Core model seed data
        // Add Zekat (restricted) fund if it doesn't exist
        if (!await _db.Funds.AnyAsync(f => f.Name == "Zekat Fund"))
        {
            _db.Funds.Add(new Fund
            {
                Name = "Zekat Fund",
                Description = "Restricted fund for Zekat collections and distributions",
                IsRestricted = true,
                IsActive = true
            });
            await _db.SaveChangesAsync();
        }
    }

    private async Task SeedDonationCategoriesAsync()
    {
        if (await _db.DonationCategories.AnyAsync())
            return;

        var generalFund = await _db.Funds.FirstOrDefaultAsync(f => f.Name == "General Fund");
        var zekatFund = await _db.Funds.FirstOrDefaultAsync(f => f.Name == "Zekat Fund");

        if (generalFund == null) return; // Should never happen — seeded by EF

        var categories = new List<DonationCategory>
        {
            new() { Name = "Himmet", FundId = generalFund.Id, Description = "General charitable giving" },
            new() { Name = "Muavenet", FundId = generalFund.Id, Description = "Muavenet donations" },
            new() { Name = "Sadaka", FundId = generalFund.Id, Description = "Voluntary charity" },
            new() { Name = "Fitre", FundId = generalFund.Id, Description = "Fitre donations" },
            new() { Name = "Kurban", FundId = generalFund.Id, Description = "Kurban donations" },
        };

        if (zekatFund != null)
            categories.Add(new DonationCategory { Name = "Zekat", FundId = zekatFund.Id, Description = "Zekat (obligatory alms)" });

        _db.DonationCategories.AddRange(categories);
        await _db.SaveChangesAsync();
    }

    private async Task SeedExpenseCategoriesAsync()
    {
        if (await _db.ExpenseCategories.AnyAsync())
            return;

        var generalFund = await _db.Funds.FirstOrDefaultAsync(f => f.Name == "General Fund");
        var zekatFund = await _db.Funds.FirstOrDefaultAsync(f => f.Name == "Zekat Fund");

        var categories = new List<ExpenseCategory>
        {
            new() { Name = "Rent", FundId = generalFund?.Id },
            new() { Name = "Salaries", FundId = generalFund?.Id },
            new() { Name = "Payroll Taxes", FundId = generalFund?.Id },
            new() { Name = "Electricity", FundId = generalFund?.Id },
            new() { Name = "Water", FundId = generalFund?.Id },
            new() { Name = "Gas", FundId = generalFund?.Id },
            new() { Name = "Internet", FundId = generalFund?.Id },
            new() { Name = "Telephone", FundId = generalFund?.Id },
            new() { Name = "Lawn Care", FundId = generalFund?.Id },
            new() { Name = "Repairs", FundId = generalFund?.Id },
            new() { Name = "Airfare", FundId = generalFund?.Id },
            new() { Name = "Travel", FundId = generalFund?.Id },
            new() { Name = "Guidance Expenses", FundId = generalFund?.Id },
            new() { Name = "Office Supplies", FundId = generalFund?.Id },
            new() { Name = "Miscellaneous", FundId = generalFund?.Id },
        };

        if (zekatFund != null)
            categories.Add(new ExpenseCategory { Name = "Zekat Distribution", FundId = zekatFund.Id });

        _db.ExpenseCategories.AddRange(categories);
        await _db.SaveChangesAsync();
    }

    private async Task SeedProgramsAsync()
    {
        if (await _db.Programs.AnyAsync())
            return;

        _db.Programs.Add(new OrgProgram { Name = "General Operations", Description = "Default program for general activities" });
        await _db.SaveChangesAsync();
    }
}
