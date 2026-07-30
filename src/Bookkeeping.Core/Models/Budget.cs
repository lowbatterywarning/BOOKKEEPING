namespace Bookkeeping.Core.Models;

/// <summary>
/// A budget entry for a specific expense category or program, for a given month or year.
/// Supports both monthly and annual budgets.
/// </summary>
public class Budget
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int? Month { get; set; } // null = annual budget
    public decimal Amount { get; set; }
    public int? ExpenseCategoryId { get; set; }
    public int? ProgramId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ExpenseCategory? ExpenseCategory { get; set; }
    public OrgProgram? Program { get; set; }
}
