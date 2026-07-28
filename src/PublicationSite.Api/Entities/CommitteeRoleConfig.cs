using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

/// <summary>
/// Required member counts per role type. When CommitteeId is null this row is part of
/// the system-wide default template (Admin-configurable); otherwise it overrides that
/// default for one specific Committee.
/// </summary>
public class CommitteeRoleConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? CommitteeId { get; set; }
    public Committee? Committee { get; set; }

    public CommitteeMemberRoleType RoleType { get; set; }
    public int RequiredCount { get; set; }
}
