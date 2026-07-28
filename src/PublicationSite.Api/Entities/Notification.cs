using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }

    public bool IsRead { get; set; }
    public DateTime? EmailSentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
