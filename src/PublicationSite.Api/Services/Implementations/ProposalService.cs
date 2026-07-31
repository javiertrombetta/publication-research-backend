using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Proposals;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class ProposalService(
    ApplicationDbContext db,
    IContainerAccessService accessService,
    IAuditService auditService,
    INotificationService notificationService) : IProposalService
{
    public async Task<ProposalDto> CreateAsync(Guid publicationContainerId, Guid studentId, SaveProposalRequest request, CancellationToken cancellationToken = default)
    {
        var container = await GetOwnedContainerAsync(publicationContainerId, studentId, cancellationToken);
        await EnsureProposalsEditableAsync(container.Id, cancellationToken);

        var proposal = new ResearchProposal
        {
            PublicationContainerId = container.Id,
            Title = request.Title,
            Abstract = request.Abstract,
            Status = ProposalStatus.Draft
        };

        db.ResearchProposals.Add(proposal);
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(proposal);
    }

    public async Task<ProposalDto> UpdateAsync(Guid proposalId, Guid studentId, SaveProposalRequest request, CancellationToken cancellationToken = default)
    {
        var proposal = await db.ResearchProposals.Include(p => p.PublicationContainer)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken)
            ?? throw new NotFoundException(nameof(ResearchProposal), proposalId);

        if (proposal.PublicationContainer.StudentId != studentId)
        {
            throw new ForbiddenException();
        }

        if (proposal.Status != ProposalStatus.Draft)
        {
            throw new BusinessRuleException("This proposal is locked and can no longer be edited.");
        }

        proposal.Title = request.Title;
        proposal.Abstract = request.Abstract;
        proposal.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(proposal);
    }

    public async Task<IReadOnlyList<ProposalDto>> GetByContainerAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, requestingUserId);

        return await db.ResearchProposals
            .Where(p => p.PublicationContainerId == publicationContainerId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken);
    }

    public async Task FinishSubmissionAsync(Guid publicationContainerId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var container = await GetOwnedContainerAsync(publicationContainerId, studentId, cancellationToken);

        var drafts = await db.ResearchProposals
            .Where(p => p.PublicationContainerId == container.Id && p.Status == ProposalStatus.Draft)
            .ToListAsync(cancellationToken);

        if (drafts.Count == 0)
        {
            throw new BusinessRuleException("At least one research proposal is required before finishing submission.");
        }

        foreach (var proposal in drafts)
        {
            proposal.Status = ProposalStatus.Submitted;
            proposal.SubmittedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, studentId, "ProposalsSubmitted",
            $"{drafts.Count} research proposal(s) submitted for evaluation.",
            previousStatus: ProposalStatus.Draft.ToString(), newStatus: ProposalStatus.Submitted.ToString());
    }

    public async Task RequestNewSubmissionAsync(Guid publicationContainerId, string comments, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var container = await db.PublicationContainers.FindAsync([publicationContainerId], cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), publicationContainerId);

        var proposals = await db.ResearchProposals
            .Where(p => p.PublicationContainerId == container.Id && p.Status != ProposalStatus.Rejected)
            .ToListAsync(cancellationToken);

        foreach (var proposal in proposals)
        {
            proposal.Status = ProposalStatus.Rejected;
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, actingUserId, "NewProposalSubmissionRequested", comments,
            newStatus: ProposalStatus.Rejected.ToString());

        await notificationService.NotifyAsync(container.StudentId, NotificationType.NewProposalSubmissionRequested,
            "New research proposal submission required",
            "You have been asked to submit a new set of research proposals. Please log in to the system.",
            nameof(PublicationContainer), container.Id, cancellationToken);
    }

    public async Task<PagedResult<ProposalDto>> GetPendingForCoordinatorAsync(
        Guid coordinatorId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var query = db.ResearchProposals
            .Where(p => p.PublicationContainer.CoordinatorId == coordinatorId
                        && p.Status == ProposalStatus.Submitted
                        && !p.SupervisorSelections.Any())
            .OrderBy(p => p.PublicationContainerId).ThenBy(p => p.CreatedAt);

        return await query.ToPageAsync(p => ToDto(p), page, cancellationToken);
    }

    public async Task<PagedResult<ProposalWithInvitationsDto>> GetForCoordinatorAsync(
        Guid coordinatorId, PageRequest page, bool awaitingAllocation = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.ResearchProposals.Where(p => p.PublicationContainer.CoordinatorId == coordinatorId);

        if (awaitingAllocation)
        {
            // A Supervisor has offered to take it on and nobody has been allocated yet: exactly
            // the rows the Coordinator's selection screen can do something about.
            query = query.Where(p => p.PublicationContainer.CurrentPipeline == PipelineStage.ResearchProposals
                                     && p.PublicationContainer.Status != ContainerStatus.Completed
                                     && p.SupervisorSelections.Any(s => s.IsSelected));
        }

        return await ProjectWithInvitations(query).ToPageAsync(page, cancellationToken);
    }

    public async Task<PagedResult<ProposalWithInvitationsDto>> GetInDepartmentAsync(
        Guid headOfDepartmentUserId, PageRequest page, CancellationToken cancellationToken = default)
    {
        // Exactly one Head of Department per department, so a single id is all there is to match.
        var departmentId = await db.HeadOfDepartmentProfiles
            .Where(h => h.UserId == headOfDepartmentUserId)
            .Select(h => (Guid?)h.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);

        if (departmentId is null)
        {
            return new PagedResult<ProposalWithInvitationsDto>([], page.SafePage, page.SafePageSize, 0);
        }

        return await ProjectWithInvitations(db.ResearchProposals
                .Where(p => p.PublicationContainer.Student.StudentProfile != null
                            && p.PublicationContainer.Student.StudentProfile.DepartmentId == departmentId))
            .ToPageAsync(page, cancellationToken);
    }

    /// <summary>
    /// One query for the proposals and their invitations together. The invitations are a
    /// correlated collection rather than a request each — which is what the screens built from
    /// the per-proposal endpoint were doing, once per row.
    /// </summary>
    private static IQueryable<ProposalWithInvitationsDto> ProjectWithInvitations(IQueryable<ResearchProposal> query) =>
        query
            .OrderBy(p => p.PublicationContainerId).ThenBy(p => p.CreatedAt)
            .Select(p => new ProposalWithInvitationsDto(
                p.Id,
                p.PublicationContainerId,
                p.PublicationContainer.Student.FirstName + " " + p.PublicationContainer.Student.LastName,
                p.Title,
                p.Abstract,
                p.Status.ToString(),
                p.SubmittedAt,
                p.SupervisorSelections
                    .OrderBy(s => s.InvitedAt)
                    .Select(s => new SupervisorInvitationDto(
                        s.ProposalId,
                        s.SupervisorId,
                        s.Supervisor.FirstName + " " + s.Supervisor.LastName,
                        s.IsSelected,
                        s.Comments,
                        s.InvitedAt,
                        s.SelectedAt))
                    .ToList()));

    public async Task SendToSupervisorsAsync(SendToSupervisorsRequest request, Guid coordinatorId, CancellationToken cancellationToken = default)
    {
        var proposals = await db.ResearchProposals
            .Include(p => p.PublicationContainer)
            .Where(p => request.ProposalIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        foreach (var proposal in proposals)
        {
            if (proposal.PublicationContainer.CoordinatorId != coordinatorId)
            {
                throw new ForbiddenException("You are not the Coordinator for one or more of these proposals.");
            }

            if (proposal.Status != ProposalStatus.Submitted)
            {
                throw new BusinessRuleException($"Proposal '{proposal.Title}' is not awaiting evaluation.");
            }

            // Deduplicated: the same Supervisor named twice in one request would otherwise be
            // invited twice, because the "already invited" check below reads the database and
            // cannot see a row this loop has added but not yet saved. The unique index then
            // rejects the save and the caller gets a server error for what is only a redundant
            // selection.
            foreach (var supervisorId in request.SupervisorIds.Distinct())
            {
                var alreadyInvited = await db.ProposalSupervisorSelections
                    .AnyAsync(s => s.ProposalId == proposal.Id && s.SupervisorId == supervisorId, cancellationToken);

                if (!alreadyInvited)
                {
                    db.ProposalSupervisorSelections.Add(new ProposalSupervisorSelection
                    {
                        ProposalId = proposal.Id,
                        SupervisorId = supervisorId
                    });
                }
            }

            await auditService.LogActivityAsync(proposal.PublicationContainerId, coordinatorId, "ProposalsSentToSupervisors",
                request.Comments);
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var supervisorId in request.SupervisorIds.Distinct())
        {
            await notificationService.NotifyAsync(supervisorId, NotificationType.ProposalsAwaitingEvaluation,
                "Research proposals awaiting your evaluation",
                "New research proposals have been sent to you for evaluation. Please log in to the system.",
                cancellationToken: cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProposalDto>> GetInvitedProposalsForSupervisorAsync(Guid supervisorId, CancellationToken cancellationToken = default)
    {
        return await db.ResearchProposals
            .Where(p => p.Status == ProposalStatus.Submitted
                        && p.SupervisorSelections.Any(s => s.SupervisorId == supervisorId))
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken);
    }

    public async Task SelectAsFeasibleAsync(Guid proposalId, Guid supervisorId, SupervisorSelectionRequest request, CancellationToken cancellationToken = default)
    {
        var selection = await db.ProposalSupervisorSelections
            .Include(s => s.Proposal)
            .FirstOrDefaultAsync(s => s.ProposalId == proposalId && s.SupervisorId == supervisorId, cancellationToken)
            ?? throw new ForbiddenException("This proposal was not sent to you for evaluation.");

        selection.IsSelected = true;
        selection.Comments = request.Comments;
        selection.SelectedAt = DateTime.UtcNow;

        if (selection.Proposal.Status == ProposalStatus.Submitted)
        {
            selection.Proposal.Status = ProposalStatus.SelectedBySupervisor;
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(selection.Proposal.PublicationContainerId, supervisorId,
            "ProposalSelectedBySupervisor", request.Comments ?? "Marked as feasible to supervise.");
    }

    public async Task<IReadOnlyList<SupervisorInvitationDto>> GetSelectionsAsync(Guid proposalId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        var proposal = await db.ResearchProposals.FindAsync([proposalId], cancellationToken)
            ?? throw new NotFoundException(nameof(ResearchProposal), proposalId);

        await accessService.EnsureAccessAsync(proposal.PublicationContainerId, requestingUserId);

        return await db.ProposalSupervisorSelections
            .Where(s => s.ProposalId == proposalId)
            .Select(s => new SupervisorInvitationDto(
                s.ProposalId, s.SupervisorId, s.Supervisor.FirstName + " " + s.Supervisor.LastName,
                s.IsSelected, s.Comments, s.InvitedAt, s.SelectedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task AssignSupervisorAsync(Guid proposalId, AssignSupervisorRequest request, Guid coordinatorId, CancellationToken cancellationToken = default)
    {
        var proposal = await db.ResearchProposals
            .Include(p => p.PublicationContainer)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken)
            ?? throw new NotFoundException(nameof(ResearchProposal), proposalId);

        if (proposal.PublicationContainer.CoordinatorId != coordinatorId)
        {
            throw new ForbiddenException();
        }

        var wasSelected = await db.ProposalSupervisorSelections.AnyAsync(
            s => s.ProposalId == proposalId && s.SupervisorId == request.SupervisorId && s.IsSelected, cancellationToken);

        if (!wasSelected)
        {
            throw new BusinessRuleException("You may only assign a Supervisor who selected this proposal as feasible.");
        }

        db.ProposalAssignments.Add(new ProposalAssignment
        {
            ProposalId = proposalId,
            SupervisorId = request.SupervisorId,
            CoordinatorId = coordinatorId,
            Comments = request.Comments
        });

        proposal.Status = ProposalStatus.Assigned;

        var container = proposal.PublicationContainer;
        container.AssignedSupervisorId = request.SupervisorId;
        container.CurrentPipeline = PipelineStage.EthicsApproval;
        container.UpdatedAt = DateTime.UtcNow;

        var siblingProposals = await db.ResearchProposals
            .Where(p => p.PublicationContainerId == container.Id && p.Id != proposalId
                        && p.Status != ProposalStatus.Assigned)
            .ToListAsync(cancellationToken);
        foreach (var sibling in siblingProposals)
        {
            sibling.Status = ProposalStatus.Rejected;
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, coordinatorId, "SupervisorAssigned", request.Comments,
            previousStatus: PipelineStage.ResearchProposals.ToString(), newStatus: PipelineStage.EthicsApproval.ToString());

        await notificationService.NotifyAsync(request.SupervisorId, NotificationType.ProposalAccepted,
            "You have been assigned as Supervisor",
            "A research proposal has been assigned to you as Supervisor. Please log in to the system.",
            nameof(PublicationContainer), container.Id, cancellationToken);

        await notificationService.NotifyAsync(container.StudentId, NotificationType.ProposalAccepted,
            "Your research proposal has been accepted",
            "Your research proposal has been accepted and a Supervisor has been assigned. Please log in to the system.",
            nameof(PublicationContainer), container.Id, cancellationToken);
    }

    public async Task DeferToNextCycleAsync(Guid publicationContainerId, string comments, Guid coordinatorId, CancellationToken cancellationToken = default)
    {
        var container = await db.PublicationContainers.FirstOrDefaultAsync(
            c => c.Id == publicationContainerId && c.CoordinatorId == coordinatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), publicationContainerId);

        var proposals = await db.ResearchProposals
            .Where(p => p.PublicationContainerId == container.Id &&
                        (p.Status == ProposalStatus.Submitted || p.Status == ProposalStatus.SelectedBySupervisor))
            .ToListAsync(cancellationToken);

        foreach (var proposal in proposals)
        {
            proposal.Status = ProposalStatus.DeferredToNextCycle;
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, coordinatorId, "ProposalsDeferredToNextCycle", comments,
            newStatus: ProposalStatus.DeferredToNextCycle.ToString());
    }

    private async Task<PublicationContainer> GetOwnedContainerAsync(Guid containerId, Guid studentId, CancellationToken cancellationToken)
    {
        var container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

        if (container.StudentId != studentId)
        {
            throw new ForbiddenException();
        }

        return container;
    }

    private async Task EnsureProposalsEditableAsync(Guid containerId, CancellationToken cancellationToken)
    {
        var isLocked = await db.ResearchProposals.AnyAsync(
            p => p.PublicationContainerId == containerId && p.Status != ProposalStatus.Draft && p.Status != ProposalStatus.Rejected,
            cancellationToken);

        if (isLocked)
        {
            throw new BusinessRuleException("Proposals are locked and cannot be added or edited at this stage.");
        }
    }

    private static ProposalDto ToDto(ResearchProposal proposal) => new(
        proposal.Id, proposal.PublicationContainerId, proposal.Title, proposal.Abstract,
        proposal.Status.ToString(), proposal.SubmittedAt);
}
