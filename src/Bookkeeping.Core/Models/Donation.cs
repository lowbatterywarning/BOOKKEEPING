using Bookkeeping.Core.Enums;

namespace Bookkeeping.Core.Models;

/// <summary>
/// A donation/income record. Each donation generates a balanced journal entry behind the scenes.
/// </summary>
public class Donation
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int SponsorId { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public int ProgramId { get; set; }
    public decimal Amount { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
    public int JournalEntryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }

    // Navigation
    public Sponsor Sponsor { get; set; } = null!;
    public OrgProgram Program { get; set; } = null!;
    public JournalEntry JournalEntry { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
