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

    Task<CommitteeDto> AssignAsync(Guid publicationId, AssignCommitteeRequest request, Guid adminId, CancellationToken cancellationToken = default);
    Task<CommitteeDto> GetByPublicationAsync(Guid publicationId, Guid requestingUserId, CancellationToken cancellationToken = default);
    Task<PagedResult<CommitteeDto>> GetAssignmentsForMemberAsync(Guid memberUserId, PageRequest page, CancellationToken cancellationToken = default);
    Task MemberReviewAsync(Guid committeeId, Guid memberUserId, CommitteeMemberReviewRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommitteeRoleConfigDto>> GetDefaultConfigAsync(CancellationToken cancellationToken = default);
    Task SetDefaultConfigAsync(SetCommitteeRoleConfigRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommitteeRoleConfigDto>> GetCommitteeConfigAsync(Guid committeeId, CancellationToken cancellationToken = default);
    /// <param name="actingAsAdmin">True when an administrator is doing this. Otherwise the caller has to be the coordinator of the publication this committee sits on.</param>
    Task SetCommitteeConfigAsync(Guid committeeId, SetCommitteeRoleConfigRequest request, Guid actingUserId, bool actingAsAdmin = false, CancellationToken cancellationToken = default);
}
