using Microsoft.EntityFrameworkCore.Migrations;

namespace Bookkeeping.Data.Migrations;

public partial class AnnualBudgetUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Retain the newest row as the active budget, and preserve every displaced
        // record verbatim for recovery. Summing duplicates would invent a budget.
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS BudgetDuplicateArchive (
                Id INTEGER NOT NULL PRIMARY KEY, Year INTEGER NOT NULL, Month INTEGER NULL,
                Amount decimal(18,2) NOT NULL, ProgramId INTEGER NULL, Notes TEXT NULL,
                CreatedAt TEXT NOT NULL, RetainedBudgetId INTEGER NOT NULL);
            INSERT OR IGNORE INTO BudgetDuplicateArchive (Id, Year, Month, Amount, ProgramId, Notes, CreatedAt, RetainedBudgetId)
            SELECT b.Id, b.Year, b.Month, b.Amount, b.ProgramId, b.Notes, b.CreatedAt,
                (SELECT k.Id FROM Budgets k WHERE k.Year = b.Year AND k.ProgramId = b.ProgramId AND k.Month IS NULL
                 ORDER BY k.CreatedAt DESC, k.Id DESC LIMIT 1)
            FROM Budgets b WHERE b.Month IS NULL AND b.ProgramId IS NOT NULL
            AND b.Id <> (SELECT k.Id FROM Budgets k WHERE k.Year = b.Year AND k.ProgramId = b.ProgramId AND k.Month IS NULL
                         ORDER BY k.CreatedAt DESC, k.Id DESC LIMIT 1);
            DELETE FROM Budgets WHERE Month IS NULL AND ProgramId IS NOT NULL
            AND Id <> (SELECT k.Id FROM Budgets k WHERE k.Year = Budgets.Year AND k.ProgramId = Budgets.ProgramId AND k.Month IS NULL
                       ORDER BY k.CreatedAt DESC, k.Id DESC LIMIT 1);
            """);
        migrationBuilder.CreateIndex("IX_Budgets_AnnualProgram", "Budgets", new[] { "Year", "ProgramId" }, unique: true,
            filter: "Month IS NULL AND ProgramId IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Budgets_AnnualProgram", "Budgets");
        // Keep the recovery archive across a downgrade: later edits to the active
        // budget must not be overwritten by automatically restoring old values.
    }
}
