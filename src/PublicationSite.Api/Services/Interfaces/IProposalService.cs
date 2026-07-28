using PublicationSite.Api.DTOs.Proposals;

namespace PublicationSite.Api.Services.Interfaces;

public interface IProposalService
{
    Task<ProposalDto> CreateAsync(Guid publicationContainerId, Guid studentId, SaveProposalRequest request, CancellationToken cancellationToken = default);
    Task<ProposalDto> UpdateAsync(Guid proposalId, Guid studentId, SaveProposalRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProposalDto>> GetByContainerAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default);
    Task FinishSubmissionAsync(Guid publicationContainerId, Guid studentId, CancellationToken cancellationToken = default);
    Task RequestNewSubmissionAsync(Guid publicationContainerId, string comments, Guid actingUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProposalDto>> GetPendingForCoordinatorAsync(Guid coordinatorId, CancellationToken cancellationToken = default);
    Task SendToSupervisorsAsync(SendToSupervisorsRequest request, Guid coordinatorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProposalDto>> GetInvitedProposalsForSupervisorAsync(Guid supervisorId, CancellationToken cancellationToken = default);
    Task SelectAsFeasibleAsync(Guid proposalId, Guid supervisorId, SupervisorSelectionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupervisorInvitationDto>> GetSelectionsAsync(Guid proposalId, Guid requestingUserId, CancellationToken cancellationToken = default);
    Task AssignSupervisorAsync(Guid proposalId, AssignSupervisorRequest request, Guid coordinatorId, CancellationToken cancellationToken = default);
    Task DeferToNextCycleAsync(Guid publicationContainerId, string comments, Guid coordinatorId, CancellationToken cancellationToken = default);
}
