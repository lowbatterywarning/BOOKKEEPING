using Bookkeeping.Data;
using Bookkeeping.Core.Models;
using Bookkeeping.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Bookkeeping.Wpf.Services;

public class DatabaseInitializer
{
    private readonly AppDbContext _db;

    public DatabaseInitializer(AppDbContext db) { _db = db; }

    public async Task InitializeAsync()
    {
        // A migration failure does not imply corruption. Preserve the database
        // and let startup report the error instead of silently replacing data.
        await _db.Database.MigrateAsync();
        await SeedProgramsAsync();
        await EnsureProgramAccountsAsync();
    }

    private async Task SeedProgramsAsync()
    {
        if (await _db.Programs.AnyAsync()) return;
        var generalFund = await _db.Funds.FirstOrDefaultAsync(f => f.Name == "General Fund");
        if (generalFund == null) return;

        _db.Programs.Add(new OrgProgram
        {
            Name = "General Operations",
            Description = "Default program for general activities",
            FundId = generalFund.Id,
            IsActive = true
        });
        await _db.SaveChangesAsync();
    }

    private async Task EnsureProgramAccountsAsync()
    {
        var programs = await _db.Programs.Include(p => p.IncomeAccount).Include(p => p.ExpenseAccount)
            .Where(p => p.IncomeAccount == null || p.ExpenseAccount == null).ToListAsync();

        foreach (var program in programs)
        {
            if (program.IncomeAccount == null)
            {
                var maxCode = await _db.Accounts.Where(a => a.Code.StartsWith("4")).MaxAsync(a => (string?)a.Code);
                var nextCode = string.IsNullOrEmpty(maxCode) ? "4001"
                    : int.TryParse(maxCode[1..], out int s) && s < 9999 ? $"4{(s + 1):D3}" : $"4{DateTime.UtcNow:MMddHHmm}";
                var account = new Account { Code = nextCode, Name = $"Donation Income - {program.Name}", AccountType = AccountType.Income, FundId = program.FundId, IsSystem = false, CreatedAt = DateTime.UtcNow };
                _db.Accounts.Add(account);
                await _db.SaveChangesAsync();
                _db.Entry(program).Property("IncomeAccountId").CurrentValue = account.Id;
            }
            if (program.ExpenseAccount == null)
            {
                var maxCode = await _db.Accounts.Where(a => a.Code.StartsWith("5")).MaxAsync(a => (string?)a.Code);
                var nextCode = string.IsNullOrEmpty(maxCode) ? "5001"
                    : int.TryParse(maxCode[1..], out int s) && s < 9999 ? $"5{(s + 1):D3}" : $"5{DateTime.UtcNow:MMddHHmm}";
                var account = new Account { Code = nextCode, Name = $"Expense - {program.Name}", AccountType = AccountType.Expense, FundId = program.FundId, IsSystem = false, CreatedAt = DateTime.UtcNow };
                _db.Accounts.Add(account);
                await _db.SaveChangesAsync();
                _db.Entry(program).Property("ExpenseAccountId").CurrentValue = account.Id;
            }
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
    }
}
