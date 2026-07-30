namespace Bookkeeping.Core.Models;

/// <summary>
/// A per-category donation target for a sponsor.
/// E.g., Sponsor A aims to give $500 to Zekat and $200 to Kurban this year.
/// </summary>
public class SponsorTarget
{
    public int Id { get; set; }
    public int SponsorId { get; set; }
    public int DonationCategoryId { get; set; }
    public decimal TargetAmount { get; set; }
    public int? Year { get; set; } // null = ongoing/indefinite annual target
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Sponsor Sponsor { get; set; } = null!;
    public DonationCategory DonationCategory { get; set; } = null!;
}
