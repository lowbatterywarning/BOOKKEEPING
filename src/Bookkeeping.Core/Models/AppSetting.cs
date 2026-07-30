namespace Bookkeeping.Core.Models;

/// <summary>
/// Key-value application settings (e.g., auto-logout timeout, backup interval).
/// </summary>
public class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
