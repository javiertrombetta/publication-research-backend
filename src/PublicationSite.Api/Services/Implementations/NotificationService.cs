using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class NotificationService(
    ApplicationDbContext db,
    IEmailSender emailSender,
    ISystemSettingsProvider settings) : INotificationService
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

        // Written first and unconditionally: the in-app notification is the delivery that always
        // happens, and email is a copy of it. With email off, this is the whole mechanism. The
        // person sees it when they next sign in.
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        var emailEnabled = await settings.GetBoolAsync(
            SettingKeys.EmailNotificationsEnabled, SettingKeys.DefaultEmailNotificationsEnabled, cancellationToken);

        if (!emailEnabled)
        {
            return;
        }

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

        // Only stamped when the message actually left: a delivery time against an email that was
        // never sent is worse than no delivery time at all.
        if (await emailSender.SendAsync(recipientEmail, title, body, cancellationToken))
        {
            notification.EmailSentAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
