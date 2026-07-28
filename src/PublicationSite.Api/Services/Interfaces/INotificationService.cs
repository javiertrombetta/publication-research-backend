using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Services.Interfaces;

public interface INotificationService
{
    /// <summary>
    /// Persists an in-app notification and emails the recipient, per the client's
    /// requirement that every workflow notification is delivered both ways.
    /// </summary>
    Task NotifyAsync(
        Guid recipientUserId,
        NotificationType type,
        string title,
        string message,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        CancellationToken cancellationToken = default);
}
