using Bookkeeping.Core.Models;
using Bookkeeping.Data;

namespace Bookkeeping.Wpf.Services;

/// <summary>
/// Simple audit logging service. Records create/update/delete actions for key entities.
/// Designed to be fire-and-forget — logging failures should never break the main operation.
/// </summary>
public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Log an audit entry synchronously within an existing DbContext transaction.
    /// </summary>
    public void Log(int userId, string action, string entityType, int entityId, string? oldValues = null, string? newValues = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues
        });
    }

    /// <summary>
    /// Log entity creation. Call AFTER SaveChangesAsync so entityId is populated.
    /// </summary>
    public void LogCreate(int userId, string entityType, int entityId, string? summary = null)
    {
        Log(userId, "Create", entityType, entityId, null, summary);
    }

    /// <summary>
    /// Log entity deletion.
    /// </summary>
    public void LogDelete(int userId, string entityType, int entityId, string? summary = null)
    {
        Log(userId, "Delete", entityType, entityId, summary, null);
    }
}
