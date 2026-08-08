namespace Bookkeeping.Core.Models;

/// <summary>
/// A program that the organization runs (e.g., General Operations, Kids Club, Ramadan Program).
/// Income and expenses can be tagged to a program for tracking.
/// </summary>
public class OrgProgram
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int FundId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Fund Fund { get; set; } = null!;
    public Account? IncomeAccount { get; set; }
    public Account? ExpenseAccount { get; set; }
    public ICollection<Donation> Donations { get; set; } = new List<Donation>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<SponsorTarget> SponsorTargets { get; set; } = new List<SponsorTarget>();
}
