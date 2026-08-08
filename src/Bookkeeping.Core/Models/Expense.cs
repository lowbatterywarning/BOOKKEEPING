using Bookkeeping.Core.Enums;

namespace Bookkeeping.Core.Models;

/// <summary>
/// An expense record. Each expense generates a balanced journal entry behind the scenes.
/// Receipt attachments are stored as files, with only the relative path stored here.
/// </summary>
public class Expense
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public int ProgramId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public string? ReceiptAttachmentPath { get; set; }
    public int JournalEntryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }

    // Navigation
    public OrgProgram Program { get; set; } = null!;
    public JournalEntry JournalEntry { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
