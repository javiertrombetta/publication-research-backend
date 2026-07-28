using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class AuditService(ApplicationDbContext db) : IAuditService
{
    public async Task LogAuditAsync(
        Guid actorUserId,
        string actionType,
        string entityType,
        Guid? entityId,
        string? previousValue = null,
        string? newValue = null,
        string? comments = null,
        Guid? onBehalfOfUserId = null)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            ActorUserId = actorUserId,
            OnBehalfOfUserId = onBehalfOfUserId,
            ActionType = actionType,
            EntityType = entityType,
            EntityId = entityId,
            PreviousValue = previousValue,
            NewValue = newValue,
            Comments = comments
        });

        await db.SaveChangesAsync();
    }

    public async Task LogActivityAsync(
        Guid publicationContainerId,
        Guid actorUserId,
        string action,
        string comments,
        string? previousStatus = null,
        string? newStatus = null,
        Guid? onBehalfOfUserId = null)
    {
        db.ActivityHistoryEntries.Add(new ActivityHistoryEntry
        {
            PublicationContainerId = publicationContainerId,
            ActorUserId = actorUserId,
            OnBehalfOfUserId = onBehalfOfUserId,
            Action = action,
            Comments = comments,
            PreviousStatus = previousStatus,
            NewStatus = newStatus
        });

        await LogAuditAsync(
            actorUserId,
            action,
            nameof(PublicationContainer),
            publicationContainerId,
            previousStatus,
            newStatus,
            comments,
            onBehalfOfUserId);
    }
}
