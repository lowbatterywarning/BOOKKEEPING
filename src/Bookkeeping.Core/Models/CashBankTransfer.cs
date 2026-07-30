using Bookkeeping.Core.Enums;

namespace Bookkeeping.Core.Models;

/// <summary>
/// Records a transfer of funds between Cash and Bank.
/// Must generate a balanced journal entry (debit one asset, credit the other).
/// </summary>
public class CashBankTransfer
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public TransferDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public int JournalEntryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }

    // Navigation
    public JournalEntry JournalEntry { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
