namespace PublicationSite.Api.Services.Interfaces;

public interface IAuditService
{
    /// <summary>System-wide technical audit trail entry. Never deleted.</summary>
    Task LogAuditAsync(
        Guid actorUserId,
        string actionType,
        string entityType,
        Guid? entityId,
        string? previousValue = null,
        string? newValue = null,
        string? comments = null,
        Guid? onBehalfOfUserId = null);

    /// <summary>
    /// Narrative entry attached to a PublicationContainer's Activity History. Required
    /// for every action that modifies a Container, per the client's traceability rule.
    /// </summary>
    Task LogActivityAsync(
        Guid publicationContainerId,
        Guid actorUserId,
        string action,
        string comments,
        string? previousStatus = null,
        string? newStatus = null,
        Guid? onBehalfOfUserId = null);
}
