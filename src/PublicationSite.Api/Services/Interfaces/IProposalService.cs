using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Proposals;

namespace PublicationSite.Api.Services.Interfaces;

public interface IProposalService
{
    Task<ProposalDto> CreateAsync(Guid publicationContainerId, Guid studentId, SaveProposalRequest request, CancellationToken cancellationToken = default);
    Task<ProposalDto> UpdateAsync(Guid proposalId, Guid studentId, SaveProposalRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProposalDto>> GetByContainerAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default);
    Task FinishSubmissionAsync(Guid publicationContainerId, Guid studentId, CancellationToken cancellationToken = default);
    /// <param name="actingAsAdmin">True when an administrator is doing this. Otherwise the caller has to be the coordinator this publication belongs to.</param>
    Task RequestNewSubmissionAsync(Guid publicationContainerId, string comments, Guid actingUserId, bool actingAsAdmin = false, CancellationToken cancellationToken = default);

    /// <param name="returnedOnly">Narrows the queue to proposals that have already been out once and came back with nobody willing.</param>
    Task<PagedResult<ProposalWithInvitationsDto>> GetPendingForCoordinatorAsync(Guid coordinatorId, PageRequest page, string? search = null, bool returnedOnly = false, CancellationToken cancellationToken = default);

    /// <summary>How many students, and how many proposals of theirs, are in the dispatch queue for a second time.</summary>
    Task<ReturnedToDispatchSummaryDto> GetReturnedToDispatchSummaryAsync(Guid coordinatorId, CancellationToken cancellationToken = default);

    /// <summary>Every proposal in this Coordinator's publications, with what each Supervisor said.</summary>
    Task<PagedResult<ProposalWithInvitationsDto>> GetForCoordinatorAsync(Guid coordinatorId, PageRequest page, bool awaitingAllocation = false, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>Every proposal from the students of the department this person heads.</summary>
    Task<PagedResult<ProposalWithInvitationsDto>> GetInDepartmentAsync(Guid headOfDepartmentUserId, PageRequest page, string? search = null, CancellationToken cancellationToken = default);
    Task SendToSupervisorsAsync(SendToSupervisorsRequest request, Guid coordinatorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The proposals a supervisor has been asked about, one page at a time. Paged and ordered like
    /// every other queue: a supervisor asked about forty proposals was handed all forty at once.
    /// </summary>
    Task<PagedResult<ProposalDto>> GetInvitedProposalsForSupervisorAsync(
        Guid supervisorId, PageRequest paging, string? search = null, CancellationToken cancellationToken = default);
    Task SelectAsFeasibleAsync(Guid proposalId, Guid supervisorId, SupervisorSelectionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupervisorInvitationDto>> GetSelectionsAsync(Guid proposalId, Guid requestingUserId, CancellationToken cancellationToken = default);
    Task AssignSupervisorAsync(Guid proposalId, AssignSupervisorRequest request, Guid coordinatorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refuses the offers a supervisor made on one proposal, so it stops being one the coordinator
    /// is choosing between. One proposal of three being turned down changes nothing else: the
    /// others are still live. Only when nothing of the student's still has somebody willing does
    /// the round come to nothing, and then the whole set goes back to the dispatch queue.
    /// </summary>
    Task<DiscardSelectionsResultDto> DiscardSelectionsAsync(Guid proposalId, string comments, Guid coordinatorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Administrators only: settles the publication on a different one of its proposals, leaving
    /// who supervises it alone. Refused once the research paper has been accepted.
    /// </summary>
    Task ChangeAssignedProposalAsync(
        Guid proposalId, string comments, Guid actingAdminId, CancellationToken cancellationToken = default);
}
