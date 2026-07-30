namespace Bookkeeping.Core.Models;

/// <summary>
/// An expense category (e.g., Rent, Salaries, Utilities).
/// Each category may optionally be linked to a Fund for fund accounting.
/// </summary>
public class ExpenseCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int? FundId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Fund? Fund { get; set; }
    public Account? ExpenseAccount { get; set; }
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
