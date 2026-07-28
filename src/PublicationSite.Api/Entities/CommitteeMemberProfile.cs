using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class CommitteeMemberProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public CommitteeMemberRoleType Type { get; set; }
    public string? Affiliation { get; set; }
}
