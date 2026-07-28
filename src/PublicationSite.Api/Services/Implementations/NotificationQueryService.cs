using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Notifications;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class NotificationQueryService(ApplicationDbContext db) : INotificationQueryService
{
    public async Task<IReadOnlyList<NotificationDto>> GetForUserAsync(Guid userId, bool? unreadOnly, CancellationToken cancellationToken = default)
    {
        var query = db.Notifications.Where(n => n.UserId == userId);
        if (unreadOnly == true)
        {
            query = query.Where(n => !n.IsRead);
        }

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto(n.Id, n.Type.ToString(), n.Title, n.Message,
                n.RelatedEntityType, n.RelatedEntityId, n.IsRead, n.CreatedAt))
            .ToListAsync(cancellationToken);
    }

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
}
