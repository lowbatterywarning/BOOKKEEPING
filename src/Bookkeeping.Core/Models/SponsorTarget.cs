namespace Bookkeeping.Core.Models;

/// <summary>
/// A per-program donation target for a sponsor.
/// E.g., Sponsor A aims to give $500 to Zakat and $200 to Qurbani this year.
/// </summary>
public class SponsorTarget
{
    public int Id { get; set; }
    public int SponsorId { get; set; }
    public int ProgramId { get; set; }
    public decimal TargetAmount { get; set; }
    public int? Year { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Sponsor Sponsor { get; set; } = null!;
    public OrgProgram Program { get; set; } = null!;
}
