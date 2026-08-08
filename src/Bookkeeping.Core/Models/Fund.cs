namespace Bookkeeping.Core.Models;

/// <summary>
/// Represents a fund for fund accounting.
/// Each fund tracks restricted or unrestricted net assets separately.
/// </summary>
public class Fund
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRestricted { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}
