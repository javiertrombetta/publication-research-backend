namespace PublicationSite.Api.Entities;

/// <summary>
/// A named set of supervisors a Coordinator sends proposals to together, saved so they do not have
/// to reassemble it every time. Personal to the Coordinator who made it: two coordinators can both
/// keep a group called "Data ethics" and mean different people by it.
///
/// It is a shortcut for filling in the form, not a rule about who may be asked. Membership is
/// resolved when the proposals go out, so a supervisor who has since been disabled or has marked
/// themselves unavailable is left out of that send without being removed from the group.
/// </summary>
public class SupervisorGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The Coordinator this group belongs to. Nobody else can see or change it.</summary>
    public Guid OwnerId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<SupervisorGroupMember> Members { get; set; } = [];
}

/// <summary>One supervisor's membership of one group.</summary>
public class SupervisorGroupMember
{
    public Guid SupervisorGroupId { get; set; }
    public SupervisorGroup SupervisorGroup { get; set; } = null!;

    public Guid SupervisorId { get; set; }
    public ApplicationUser Supervisor { get; set; } = null!;
}
