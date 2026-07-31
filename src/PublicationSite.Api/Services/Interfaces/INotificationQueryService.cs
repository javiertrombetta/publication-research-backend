using PublicationSite.Api.DTOs.Notifications;

namespace PublicationSite.Api.Services.Interfaces;

public interface INotificationQueryService
{
    Task<IReadOnlyList<NotificationDto>> GetForUserAsync(Guid userId, bool? unreadOnly, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks everything this person has not yet read. Separate from marking them one at a time
    /// because a queue that can only be cleared item by item stops being read at all.
    /// </summary>
    /// <returns>How many were marked.</returns>
    Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many are unread. Its own query rather than counting a full listing: the top bar asks
    /// for this on every page load and does not need the notifications themselves.
    /// </summary>
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);
}
