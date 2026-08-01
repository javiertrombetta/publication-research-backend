using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Containers;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class ContainerService(
    ApplicationDbContext db,
    IDepartmentService departmentService,
    IContainerAccessService accessService,
    IAuditService auditService,
    ISystemSettingService settingService) : IContainerService
{
    public async Task<PublicationContainerDto> CreateAsync(Guid studentUserId, CancellationToken cancellationToken = default)
    {
        // A student may run several publication processes at the same time, each with its own
        // proposals, ethics workflow and paper, so there is deliberately no one-per-student cap.
        var studentProfile = await db.StudentProfiles.FirstOrDefaultAsync(s => s.UserId == studentUserId, cancellationToken)
            ?? throw new BusinessRuleException("Only students with a completed profile can start the publication process.");

        var coordinatorId = await departmentService.SelectCoordinatorForDepartmentAsync(studentProfile.DepartmentId, cancellationToken);

        // Taken now and kept, so that a later change to the settings governs the publications
        // opened after it and not this one.
        var committeeRules = await settingService.GetCommitteeSettingsAsync(cancellationToken);

        var container = new PublicationContainer
        {
            StudentId = studentUserId,
            CoordinatorId = coordinatorId,
            CurrentPipeline = PipelineStage.ResearchProposals,
            Status = ContainerStatus.InProgress,
            RequiredInternalCommitteeMembers = committeeRules.InternalMembers,
            RequiredExternalCommitteeMembers = committeeRules.ExternalMembers,
            RequiredCommitteeApprovals = committeeRules.MinimumApprovals
        };

        db.PublicationContainers.Add(container);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, studentUserId, "ContainerCreated",
            "Publication Container created; Coordinator auto-assigned by department workload. " +
            $"Its evaluation committee will need {committeeRules.InternalMembers} internal and " +
            $"{committeeRules.ExternalMembers} external members.",
            newStatus: container.Status.ToString());

        return await GetByIdInternalAsync(container.Id, cancellationToken);
    }

    public async Task<PublicationContainerDto> GetByIdAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(id, requestingUserId);
        return await GetByIdInternalAsync(id, cancellationToken);
    }

    public async Task<PagedResult<PublicationContainerDto>> GetMineAsync(
        Guid studentUserId, PageRequest page, CancellationToken cancellationToken = default) =>
        // Order before projecting: the DTO carries a correlated sub-query for Title, and EF Core
        // cannot translate an OrderBy applied on top of that projection.
        await ProjectToDto(
                db.PublicationContainers
                    .Where(c => c.StudentId == studentUserId)
                    .OrderByDescending(c => c.CreatedAt))
            .ToPageAsync(page, cancellationToken);

    public async Task<PagedResult<PublicationContainerDto>> GetSupervisingAsync(
        Guid supervisorUserId, ContainerQuery query, CancellationToken cancellationToken = default) =>
        // Ordered before projecting, as in GetMineAsync: the DTO carries correlated sub-queries
        // that EF Core cannot order on top of.
        await ProjectToDto(
                WhereEthicsStep(
                    db.PublicationContainers.Where(c => c.AssignedSupervisorId == supervisorUserId),
                    query.EthicsStep)
                .OrderByDescending(c => c.CreatedAt))
            .ToPageAsync(query, cancellationToken);

    public async Task<PagedResult<PublicationContainerDto>> GetInMyDepartmentAsync(
        Guid headOfDepartmentUserId, ContainerQuery query, CancellationToken cancellationToken = default)
    {
        // Exactly one Head of Department per Department, so a single id is all there is to match.
        var departmentId = await db.HeadOfDepartmentProfiles
            .Where(h => h.UserId == headOfDepartmentUserId)
            .Select(h => (Guid?)h.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);

        if (departmentId is null)
        {
            return new PagedResult<PublicationContainerDto>([], query.SafePage, query.SafePageSize, 0);
        }

        return await ProjectToDto(
                WhereEthicsStep(
                    db.PublicationContainers.Where(c => c.Student.StudentProfile != null
                                && c.Student.StudentProfile.DepartmentId == departmentId),
                    query.EthicsStep)
                .OrderByDescending(c => c.CreatedAt))
            .ToPageAsync(query, cancellationToken);
    }

    public async Task DeleteOwnAsync(Guid containerId, Guid studentUserId, CancellationToken cancellationToken = default)
    {
        var container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

        if (container.StudentId != studentUserId)
        {
            throw new ForbiddenException("You can only delete your own Publication Container.");
        }

        var hasProposals = await db.ResearchProposals.AnyAsync(p => p.PublicationContainerId == containerId, cancellationToken);
        if (hasProposals || container.CurrentPipeline != PipelineStage.ResearchProposals)
        {
            throw new BusinessRuleException(
                "This publication can no longer be deleted because its process has already started. " +
                "A Publication Container can only be discarded while it still has no research proposals.");
        }

        // Written before the delete so the trail survives it: AuditLogEntry deliberately has no
        // foreign key to the Container, so it is never cascaded away. The Container's own
        // ActivityHistory is cascade-deleted along with it.
        await auditService.LogAuditAsync(
            studentUserId,
            "ContainerDeleted",
            nameof(PublicationContainer),
            containerId,
            previousValue: container.Status.ToString(),
            comments: "Student discarded a Publication Container that had no research proposals.");

        db.PublicationContainers.Remove(container);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityHistoryEntryDto>> GetActivityHistoryAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(id, requestingUserId);

        return await db.ActivityHistoryEntries
            .Where(a => a.PublicationContainerId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ActivityHistoryEntryDto(
                a.Id,
                a.ActorUser.FirstName + " " + a.ActorUser.LastName,
                // Staff is the placeholder role every @ais.ac.nz account starts with, so it is
                // ordered last: whatever operational role the actor also holds is the one they
                // were acting in.
                db.UserRoles
                    .Where(ur => ur.UserId == a.ActorUserId)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .OrderBy(name => name == RoleNames.Staff ? 1 : 0)
                    .FirstOrDefault(),
                a.OnBehalfOfUser == null ? null : a.OnBehalfOfUser.FirstName + " " + a.OnBehalfOfUser.LastName,
                a.Action,
                a.Comments,
                a.PreviousStatus,
                a.NewStatus,
                a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<PublicationContainerDto>> GetAllAsync(
        ContainerQuery query, CancellationToken cancellationToken = default)
    {
        var containers = db.PublicationContainers.AsQueryable();

        if (query.StudentId is not null) containers = containers.Where(c => c.StudentId == query.StudentId);
        if (query.CoordinatorId is not null) containers = containers.Where(c => c.CoordinatorId == query.CoordinatorId);
        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<ContainerStatus>(query.Status, true, out var statusFilter))
        {
            containers = containers.Where(c => c.Status == statusFilter);
        }

        return await ProjectToDto(
                WhereEthicsStep(containers, query.EthicsStep).OrderByDescending(c => c.CreatedAt))
            .ToPageAsync(query, cancellationToken);
    }

    /// <summary>
    /// Narrows to the containers waiting at particular ethics steps.
    ///
    /// Expressed against the entity rather than against the projected step name. The name is a CASE
    /// built during projection, and filtering on it afterwards is not something EF Core can turn
    /// into SQL. It tried to compare the whole row. Written this way it also filters before
    /// projecting, so the rows that are not wanted are never shaped at all.
    ///
    /// Which flags are wanted is decided in C# first, so what reaches the query are plain
    /// parameters rather than a list the database is asked to search.
    /// </summary>
    private static IQueryable<PublicationContainer> WhereEthicsStep(
        IQueryable<PublicationContainer> query, IReadOnlyList<string>? steps)
    {
        if (steps is null or { Count: 0 }) return query;

        var supervisorDecision = steps.Contains(EthicsSteps.SupervisorDecision);
        var coordinatorConfirmation = steps.Contains(EthicsSteps.CoordinatorConfirmation);
        var studentUpload = steps.Contains(EthicsSteps.StudentUpload);
        var supervisorDocuments = steps.Contains(EthicsSteps.SupervisorDocumentReview);
        var coordinatorDocuments = steps.Contains(EthicsSteps.CoordinatorDocumentReview);
        var headOfDepartment = steps.Contains(EthicsSteps.HeadOfDepartmentReview);
        var coordinatorFinal = steps.Contains(EthicsSteps.CoordinatorFinalDecision);

        return query.Where(c => c.EthicsApproval != null && (
            (supervisorDecision && c.EthicsApproval.Status == EthicsStatus.PendingSupervisorDecision)
            || (coordinatorConfirmation
                && c.EthicsApproval.Status == EthicsStatus.NotRequired
                && c.EthicsApproval.FinalDecisionAt == null)
            || (studentUpload && c.EthicsApproval.Status == EthicsStatus.PendingUpload)
            || (supervisorDocuments
                && c.EthicsApproval.Status == EthicsStatus.PendingVerification
                && c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview))
            || (coordinatorDocuments
                && c.EthicsApproval.Status == EthicsStatus.PendingVerification
                && !c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview)
                && c.EthicsApproval.CoordinatorDecisionAt == null)
            || (headOfDepartment
                && c.EthicsApproval.Status == EthicsStatus.PendingVerification
                && !c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview)
                && c.EthicsApproval.CoordinatorDecisionAt != null
                && c.EthicsApproval.HeadOfDepartmentReviewedAt == null)
            || (coordinatorFinal
                && c.EthicsApproval.Status == EthicsStatus.PendingVerification
                && !c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview)
                && c.EthicsApproval.HeadOfDepartmentReviewedAt != null)));
    }

    public async Task<PublicationContainerDto> AssignCoordinatorManuallyAsync(AssignCoordinatorRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        // A student can have several containers, so "which one" has to be explicit: with an id
        // we reassign that container, without one we create an additional container for them.
        PublicationContainer? container = null;
        if (request.PublicationContainerId is { } containerId)
        {
            container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
                ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

            if (container.StudentId != request.StudentUserId)
            {
                throw new BusinessRuleException("That Publication Container does not belong to the specified student.");
            }
        }

        if (container is null)
        {
            container = new PublicationContainer
            {
                StudentId = request.StudentUserId,
                CoordinatorId = request.CoordinatorUserId,
                CurrentPipeline = PipelineStage.ResearchProposals,
                Status = ContainerStatus.InProgress
            };
            db.PublicationContainers.Add(container);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(container.Id, actingUserId, "ContainerCreated",
                request.Comments, newStatus: container.Status.ToString());
        }
        else
        {
            var previousCoordinatorId = container.CoordinatorId;
            container.CoordinatorId = request.CoordinatorUserId;
            container.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(container.Id, actingUserId, "CoordinatorReassigned",
                request.Comments, previousStatus: previousCoordinatorId.ToString(), newStatus: request.CoordinatorUserId.ToString());
        }

        return await GetByIdInternalAsync(container.Id, cancellationToken);
    }

    private async Task<PublicationContainerDto> GetByIdInternalAsync(Guid id, CancellationToken cancellationToken)
    {
        return await ProjectToDto(db.PublicationContainers.Where(c => c.Id == id)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), id);
    }

    private static IQueryable<PublicationContainerDto> ProjectToDto(IQueryable<PublicationContainer> query) =>
        query.Select(c => new PublicationContainerDto(
            c.Id,
            c.StudentId,
            c.Student.FirstName + " " + c.Student.LastName,
            c.CoordinatorId,
            c.Coordinator.FirstName + " " + c.Coordinator.LastName,
            c.AssignedSupervisorId,
            c.AssignedSupervisor == null ? null : c.AssignedSupervisor.FirstName + " " + c.AssignedSupervisor.LastName,
            (int)c.CurrentPipeline,
            c.Status.ToString(),
            c.CreatedAt,
            c.Publication != null && c.Publication.Title != ""
                ? c.Publication.Title
                : c.Proposals.Where(p => p.Status == ProposalStatus.Assigned).Select(p => p.Title).FirstOrDefault(),
            c.Proposals.Count,
            c.Publication == null ? null : c.Publication.Status.ToString(),
            c.EthicsApproval == null ? null : c.EthicsApproval.Status.ToString(),
            c.EthicsApproval == null
                ? null
                // Nobody has ruled on the declaration yet.
                : c.EthicsApproval.Status == EthicsStatus.PendingSupervisorDecision
                    ? RoleNames.Supervisor
                // A Supervisor said no documentation is needed; the Coordinator confirms it.
                : c.EthicsApproval.Status == EthicsStatus.NotRequired && c.EthicsApproval.FinalDecisionAt == null
                    ? RoleNames.Coordinator
                // Documentation was asked for and the student has yet to upload it.
                : c.EthicsApproval.Status == EthicsStatus.PendingUpload
                    ? RoleNames.Student
                : c.EthicsApproval.Status == EthicsStatus.PendingVerification
                    // Uploaded and not yet looked at: the Supervisor sees them first.
                    ? (c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview)
                        ? RoleNames.Supervisor
                        : c.EthicsApproval.CoordinatorDecisionAt == null
                            ? RoleNames.Coordinator
                            : c.EthicsApproval.HeadOfDepartmentReviewedAt == null
                                ? RoleNames.HeadOfDepartment
                                // Everyone has had their say; the Coordinator closes it.
                                : RoleNames.Coordinator)
                    : null,
            // Whose turn it is on the research paper. Like the ethics answer above, this cannot be
            // read off the status: UnderReview covers four separate waits: the Supervisor reading
            // it, an Admin appointing a committee, the committee voting, and the Coordinator's
            // decision, told apart only by what has been recorded against the paper. Every screen
            // that tried to work it out from the status alone got it wrong in the same way, by
            // offering people work that was not theirs yet.
            c.Publication == null
                ? null
                : c.Publication.Status == PublicationStatus.Draft
                        || c.Publication.Status == PublicationStatus.RevisionsRequested
                    // Nothing to submit yet, or sent back for another version. Either way the
                    // paper is in the author's hands.
                    ? RoleNames.Student
                : c.Publication.Status == PublicationStatus.Accepted
                    // Only the author decides whether an accepted paper is published.
                    ? RoleNames.Student
                : c.Publication.Status == PublicationStatus.Published
                    ? null
                : c.Publication.Status == PublicationStatus.Resubmitted
                    // A new version needs a fresh reading; the approval on record is of a draft
                    // the student has already replaced.
                    ? RoleNames.Supervisor
                : c.Publication.Status == PublicationStatus.UnderReview
                    ? (!c.Publication.Versions
                            .OrderByDescending(v => v.VersionNumber)
                            .Take(1)
                            .SelectMany(v => v.Reviews)
                            .Any(r => r.ReviewerType == ReviewerType.Supervisor && r.Decision == ReviewDecision.Approve)
                        ? RoleNames.Supervisor
                        : c.Publication.Committee == null
                            ? RoleNames.Admin
                            : c.Publication.Committee.Status != CommitteeStatus.Completed
                                ? RoleNames.EvaluationCommittee
                                : RoleNames.Coordinator)
                    : null,
            // The same waits as EthicsAwaitingRole, named individually. Two of them belong to the
            // Coordinator and are different screens, so a role alone cannot select either.
            c.EthicsApproval == null
                ? null
                : c.EthicsApproval.Status == EthicsStatus.PendingSupervisorDecision
                    ? EthicsSteps.SupervisorDecision
                : c.EthicsApproval.Status == EthicsStatus.NotRequired && c.EthicsApproval.FinalDecisionAt == null
                    ? EthicsSteps.CoordinatorConfirmation
                : c.EthicsApproval.Status == EthicsStatus.PendingUpload
                    ? EthicsSteps.StudentUpload
                : c.EthicsApproval.Status == EthicsStatus.PendingVerification
                    ? (c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview)
                        ? EthicsSteps.SupervisorDocumentReview
                        : c.EthicsApproval.CoordinatorDecisionAt == null
                            ? EthicsSteps.CoordinatorDocumentReview
                            : c.EthicsApproval.HeadOfDepartmentReviewedAt == null
                                ? EthicsSteps.HeadOfDepartmentReview
                                : EthicsSteps.CoordinatorFinalDecision)
                    : null,
            c.RequiredInternalCommitteeMembers,
            c.RequiredExternalCommitteeMembers));
}
