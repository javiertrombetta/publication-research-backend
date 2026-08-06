using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Notifications;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class NotificationQueryService(ApplicationDbContext db) : INotificationQueryService
{
    public async Task<PagedResult<NotificationDto>> GetForUserAsync(
        Guid userId,
        bool? unreadOnly,
        string? search,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var query = db.Notifications.Where(n => n.UserId == userId);
        if (unreadOnly == true)
        {
            query = query.Where(n => !n.IsRead);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // The title and the message are the whole of what a notification says, so they are the
            // whole of what there is to search.
            //
            // Case is left to the database's collation rather than lowercased on both sides here.
            // The schema is utf8mb4_0900_ai_ci, so "ethics" finds "Ethics approval completed";
            // lowercasing the column in the query would forbid an index and change nothing anyone
            // would notice. Worth knowing when reading the tests: they run on SQLite, which
            // compares case-sensitively, so they search with the case as written.
            var term = search.Trim();
            query = query.Where(n => n.Title.Contains(term) || n.Message.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .Select(n => new NotificationDto(n.Id, n.Type.ToString(), n.Title, n.Message,
                n.RelatedEntityType, n.RelatedEntityId, n.IsRead, n.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public Task<NotificationDto?> GetOneAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default) =>
        db.Notifications
            .Where(n => n.Id == notificationId && n.UserId == userId)
            .Select(n => new NotificationDto(n.Id, n.Type.ToString(), n.Title, n.Message,
                n.RelatedEntityType, n.RelatedEntityId, n.IsRead, n.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Notification), notificationId);

        if (notification.UserId != userId)
        {
            throw new ForbiddenException();
        }

        notification.IsRead = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(n => n.SetProperty(x => x.IsRead, true), cancellationToken);

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
}
