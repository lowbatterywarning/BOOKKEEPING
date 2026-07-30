namespace Bookkeeping.Core.Models;

/// <summary>
/// A donor/sponsor who makes donations to the organization.
/// </summary>
public class Sponsor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Donation> Donations { get; set; } = new List<Donation>();
    public ICollection<SponsorTarget> Targets { get; set; } = new List<SponsorTarget>();
}
