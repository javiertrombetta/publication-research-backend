namespace PublicationSite.Api.DTOs.Notifications;

public record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Message,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    bool IsRead,
    DateTime CreatedAt);
