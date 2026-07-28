using PublicationSite.Api.DTOs.Notifications;

namespace PublicationSite.Api.Services.Interfaces;

public interface INotificationQueryService
{
    Task<IReadOnlyList<NotificationDto>> GetForUserAsync(Guid userId, bool? unreadOnly, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default);
}
