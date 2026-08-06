using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Notifications;

namespace PublicationSite.Api.Services.Interfaces;

public interface INotificationQueryService
{
    /// <summary>
    /// One page of this person's notifications, newest first.
    ///
    /// Paged because this list only grows: every decision, every request and every reminder in
    /// every publication a person touches lands here and nothing removes it. A supervisor with a
    /// year behind them was being sent the lot on one screen.
    /// </summary>
    /// <param name="search">Matched against the title and the message. Ignored when blank.</param>
    Task<PagedResult<NotificationDto>> GetForUserAsync(
        Guid userId,
        bool? unreadOnly,
        string? search,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One notification, by id, if it belongs to this person.
    ///
    /// Its own query because the caller that needs it is opening a notification, and asking for a
    /// page and searching it only works while everything fits on one. Null when there is no such
    /// notification or it is somebody else's, which are the same answer to whoever asked: nothing
    /// here for you.
    /// </summary>
    Task<NotificationDto?> GetOneAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default);

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
