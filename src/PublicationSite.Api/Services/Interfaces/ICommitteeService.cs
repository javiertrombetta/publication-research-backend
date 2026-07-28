using PublicationSite.Api.DTOs.Committees;

namespace PublicationSite.Api.Services.Interfaces;

public interface ICommitteeService
{
    Task<CommitteeDto> AssignAsync(Guid publicationId, AssignCommitteeRequest request, Guid adminId, CancellationToken cancellationToken = default);
    Task<CommitteeDto> GetByPublicationAsync(Guid publicationId, Guid requestingUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommitteeDto>> GetAssignmentsForMemberAsync(Guid memberUserId, CancellationToken cancellationToken = default);
    Task MemberReviewAsync(Guid committeeId, Guid memberUserId, CommitteeMemberReviewRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommitteeRoleConfigDto>> GetDefaultConfigAsync(CancellationToken cancellationToken = default);
    Task SetDefaultConfigAsync(SetCommitteeRoleConfigRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommitteeRoleConfigDto>> GetCommitteeConfigAsync(Guid committeeId, CancellationToken cancellationToken = default);
    Task SetCommitteeConfigAsync(Guid committeeId, SetCommitteeRoleConfigRequest request, Guid actingUserId, CancellationToken cancellationToken = default);
}
