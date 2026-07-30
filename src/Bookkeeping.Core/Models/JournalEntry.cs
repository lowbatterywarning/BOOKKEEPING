namespace Bookkeeping.Core.Models;

/// <summary>
/// A balanced journal entry representing one complete transaction.
/// Sum of all DebitAmounts must equal sum of all CreditAmounts.
/// </summary>
public class JournalEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }

    // Navigation
    public User CreatedByUser { get; set; } = null!;
    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();

    // Related entities (one-to-one optional)
    public Donation? Donation { get; set; }
    public Expense? Expense { get; set; }
    public CashBankTransfer? CashBankTransfer { get; set; }
}
