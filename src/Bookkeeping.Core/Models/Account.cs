using Bookkeeping.Core.Enums;

namespace Bookkeeping.Core.Models;

/// <summary>
/// Chart of Accounts entry.
/// Accounts are organized hierarchically:
/// 1xxx = Assets (Cash, Bank)
/// 2xxx = Liabilities
/// 3xxx = Equity/Net Assets (split by fund: unrestricted + restricted funds)
/// 4xxx = Income (donation categories)
/// 5xxx = Expenses (expense categories)
/// </summary>
public class Account
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType AccountType { get; set; }
    public int? FundId { get; set; }
    public int? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; } = false; // System accounts cannot be deleted
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Fund? Fund { get; set; }
    public Account? Parent { get; set; }
    public ICollection<Account> Children { get; set; } = new List<Account>();
    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
}
