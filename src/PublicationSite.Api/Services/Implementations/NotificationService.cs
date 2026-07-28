using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class NotificationService(ApplicationDbContext db, IEmailSender emailSender) : INotificationService
{
    public async Task NotifyAsync(
        Guid recipientUserId,
        NotificationType type,
        string title,
        string message,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            UserId = recipientUserId,
            Type = type,
            Title = title,
            Message = message,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        var recipientEmail = await db.Users
            .Where(u => u.Id == recipientUserId)
            .Select(u => u.Email)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return;
        }

        var body = $"""
            <p>{message}</p>
            <p>Please log in to the AIS Research Publication Site to complete the required action.</p>
            """;

        await emailSender.SendAsync(recipientEmail, title, body, cancellationToken);

        notification.EmailSentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
