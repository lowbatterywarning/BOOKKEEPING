namespace Bookkeeping.Core.Models;

/// <summary>
/// A donation category (e.g., Himmet, Sadaka, Fitre, Kurban).
/// Each category is linked to a Fund for fund accounting.
/// </summary>
public class DonationCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int FundId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Fund Fund { get; set; } = null!;
    public Account? IncomeAccount { get; set; }
    public ICollection<Donation> Donations { get; set; } = new List<Donation>();
}
