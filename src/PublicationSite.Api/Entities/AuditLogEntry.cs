namespace PublicationSite.Api.Entities;

/// <summary>
/// System-wide, technical audit trail of every action by every user. Never deleted.
/// Distinct from ActivityHistoryEntry, which is the narrative log scoped to a single
/// PublicationContainer and visible to its participants.
/// </summary>
public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ActorUserId { get; set; }
    public ApplicationUser ActorUser { get; set; } = null!;

    public Guid? OnBehalfOfUserId { get; set; }
    public ApplicationUser? OnBehalfOfUser { get; set; }

    public string ActionType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }

    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? Comments { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
