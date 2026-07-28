namespace PublicationSite.Api.Entities;

/// <summary>
/// Narrative, per-Container record of every change and the mandatory comments that
/// justified it. Visible to every user/role with access to the Container.
/// </summary>
public class ActivityHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationContainerId { get; set; }
    public PublicationContainer PublicationContainer { get; set; } = null!;

    public Guid ActorUserId { get; set; }
    public ApplicationUser ActorUser { get; set; } = null!;

    /// <summary>Set when the actor performed the action on behalf of another role (e.g. Admin acting as Coordinator).</summary>
    public Guid? OnBehalfOfUserId { get; set; }
    public ApplicationUser? OnBehalfOfUser { get; set; }

    public string Action { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
