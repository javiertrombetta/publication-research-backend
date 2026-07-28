using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class Committee
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationId { get; set; }
    public Publication Publication { get; set; } = null!;

    public CommitteeStatus Status { get; set; } = CommitteeStatus.Assigned;
    public int MinApprovalsRequired { get; set; }

    public Guid CreatedByUserId { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CommitteeRoleConfig> RoleConfigs { get; set; } = [];
    public ICollection<CommitteeMember> Members { get; set; } = [];
}
