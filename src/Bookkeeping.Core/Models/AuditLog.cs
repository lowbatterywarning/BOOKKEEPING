namespace Bookkeeping.Core.Models;

/// <summary>
/// Append-only audit log recording all data changes.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int UserId { get; set; }
    public string Action { get; set; } = string.Empty; // "Create", "Update", "Delete"
    public string EntityType { get; set; } = string.Empty; // e.g., "Donation", "Expense"
    public int EntityId { get; set; }
    public string? OldValues { get; set; } // JSON snapshot
    public string? NewValues { get; set; } // JSON snapshot

    // Navigation
    public User User { get; set; } = null!;
}
