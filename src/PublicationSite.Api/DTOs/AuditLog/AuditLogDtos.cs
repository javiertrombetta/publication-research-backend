namespace PublicationSite.Api.DTOs.AuditLog;

public record AuditLogEntryDto(
    Guid Id,
    string ActorName,
    string? OnBehalfOfName,
    string ActionType,
    string EntityType,
    Guid? EntityId,
    string? PreviousValue,
    string? NewValue,
    string? Comments,
    DateTime Timestamp);

public class AuditLogQuery : Common.PageRequest
{
    /// <summary>Only what this person did. The account acted on behalf of counts as them too.</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Only entries about one kind of thing, by entity name: PublicationContainer, Publication,
    /// EthicsApproval, User, and so on.
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>Inclusive. The trail is kept in UTC, which is what these are read as.</summary>
    public DateTime? From { get; set; }

    /// <summary>Inclusive, and read as UTC like <see cref="From"/>.</summary>
    public DateTime? To { get; set; }
}
