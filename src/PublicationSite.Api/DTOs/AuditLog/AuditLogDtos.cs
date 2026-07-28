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

public class AuditLogQuery
{
    public Guid? UserId { get; set; }
    public string? EntityType { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
