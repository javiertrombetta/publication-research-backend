namespace PublicationSite.Api.Entities;

/// <summary>
/// What a supervisor is, beyond the role itself.
///
/// No department here: a supervisor may be attached to several, so which ones live in
/// DepartmentMembership. A single field would have made the second department unsayable.
/// </summary>
public class SupervisorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string? AreasOfExpertise { get; set; }
    public string? ResearchInterests { get; set; }
}
