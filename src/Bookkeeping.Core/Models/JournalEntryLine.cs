namespace Bookkeeping.Core.Models;

/// <summary>
/// A single line within a journal entry.
/// Each line has either a debit or credit amount (not both).
/// The journal entry as a whole must balance (total debits = total credits).
/// </summary>
public class JournalEntryLine
{
    public int Id { get; set; }
    public int JournalEntryId { get; set; }
    public int AccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public int? FundId { get; set; }
    public string? Description { get; set; }

    // Navigation
    public JournalEntry JournalEntry { get; set; } = null!;
    public Account Account { get; set; } = null!;
    public Fund? Fund { get; set; }
}
