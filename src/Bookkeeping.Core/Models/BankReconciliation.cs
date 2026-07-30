namespace Bookkeeping.Core.Models;

/// <summary>
/// Represents a bank reconciliation session.
/// Tracks which journal entries have been cleared against a bank statement.
/// </summary>
public class BankReconciliation
{
    public int Id { get; set; }
    public DateTime StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public decimal LedgerBalance { get; set; }
    public decimal Difference { get; set; }
    public bool IsCompleted { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }

    // Navigation
    public User CreatedByUser { get; set; } = null!;
    public ICollection<ReconciledItem> ReconciledItems { get; set; } = new List<ReconciledItem>();
}

/// <summary>
/// A single item marked as cleared/reconciled during a bank reconciliation.
/// Links a journal entry line to a reconciliation session.
/// </summary>
public class ReconciledItem
{
    public int Id { get; set; }
    public int BankReconciliationId { get; set; }
    public int JournalEntryLineId { get; set; }
    public bool IsCleared { get; set; } = true;

    // Navigation
    public BankReconciliation BankReconciliation { get; set; } = null!;
    public JournalEntryLine JournalEntryLine { get; set; } = null!;
}
