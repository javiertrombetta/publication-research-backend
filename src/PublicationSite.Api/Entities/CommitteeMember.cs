using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class CommitteeMember : IHaveAConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommitteeId { get; set; }
    public Committee Committee { get; set; } = null!;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public CommitteeMemberRoleType RoleType { get; set; }
    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    public CommitteeMemberDecision Decision { get; set; } = CommitteeMemberDecision.Pending;
    public string? DecisionComments { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>
    /// Changed on every save, and part of the WHERE clause of every UPDATE. See
    /// <see cref="IHaveAConcurrencyStamp"/> for why a decision needs one.
    /// </summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
