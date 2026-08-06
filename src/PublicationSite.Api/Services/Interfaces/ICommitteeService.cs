using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Services.Interfaces;

public interface ICommitteeService
{
    /// <summary>
    /// Everybody who could be put on a committee right now, by surname. The same rule
    /// <see cref="AssignAsync"/> applies, so the list offered and the list accepted agree.
    /// </summary>
    Task<IReadOnlyList<CommitteeCandidateDto>> GetCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this one person could be put on a committee as the rules stand, so a client can
    /// decide whether to offer committee work at all. Somebody already on one always can, whatever
    /// the rules say now.
    /// </summary>
    Task<bool> IsCandidateAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<CommitteeDto> AssignAsync(Guid publicationId, AssignCommitteeRequest request, Guid adminId, CancellationToken cancellationToken = default);
    Task<CommitteeDto> GetByPublicationAsync(Guid publicationId, Guid requestingUserId, CancellationToken cancellationToken = default);

    /// <summary>Every committee still sitting, so an administrator can find one to change.</summary>
    Task<PagedResult<CommitteeDto>> GetInProgressAsync(PageRequest paging, CancellationToken cancellationToken = default);

    /// <summary>Changes who is on a committee, and how many approvals it needs. Refused once it has finished.</summary>
    Task<CommitteeDto> UpdateAsync(
        Guid committeeId, UpdateCommitteeRequest request, Guid adminId, CancellationToken cancellationToken = default);
    /// <param name="awaitingMeOnly">Narrows it to the papers this member has still to vote on.</param>
    Task<PagedResult<CommitteeDto>> GetAssignmentsForMemberAsync(
        Guid memberUserId, PageRequest page, string? search = null, bool awaitingMeOnly = false,
        CancellationToken cancellationToken = default);
    Task MemberReviewAsync(Guid committeeId, Guid memberUserId, CommitteeMemberReviewRequest request, CancellationToken cancellationToken = default);
}
