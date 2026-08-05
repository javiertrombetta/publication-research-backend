using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Containers;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class ContainerService(
    ApplicationDbContext db,
    IDepartmentService departmentService,
    IContainerAccessService accessService,
    IAuditService auditService,
    INotificationService notificationService,
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
            RequiredReviewerMembers = committeeRules.ReviewerMembers,
            RequiredExternalCommitteeMembers = committeeRules.ExternalMembers,
            RequiredCommitteeApprovals = committeeRules.MinimumApprovals
        };

        db.PublicationContainers.Add(container);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, studentUserId, "ContainerCreated",
            "Publication Container created; Coordinator auto-assigned by department workload. " +
            $"Its evaluation committee will need {committeeRules.ReviewerMembers} reviewers and " +
            $"{committeeRules.ExternalMembers} external members.",
            newStatus: container.Status.ToString());

        return await GetByIdInternalAsync(container.Id, cancellationToken);
    }

    public async Task<PublicationContainerDto> GetByIdAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(id, requestingUserId);
        return await GetByIdInternalAsync(id, cancellationToken);
    }


    /// <summary>
    /// The columns a publication listing can be ordered by. Applied to the entity, before the DTO
    /// projection: the DTO carries correlated sub-queries for its title and its waiting-on fields,
    /// and EF Core cannot order on top of those. Applied before the page is cut, too, so "oldest
    /// first" means the oldest of the whole list rather than of the ten rows already in hand.
    /// </summary>
    private static readonly Dictionary<string, Expression<Func<PublicationContainer, object?>>> SortColumns = new()
    {
        ["student"] = c => c.Student.LastName,
        // The title the listing prints, which is the paper's once there is a paper and the
        // assigned proposal's until then. Ordering by the paper's title alone left every
        // publication still choosing a topic tied on an empty string, in the order they happened
        // to come back.
        ["title"] = c => c.Publication != null && c.Publication.Title != ""
            ? c.Publication.Title
            : c.Proposals.Where(p => p.Status == ProposalStatus.Assigned).Select(p => p.Title).FirstOrDefault(),
        ["coordinator"] = c => c.Coordinator!.LastName,
        ["supervisor"] = c => c.AssignedSupervisor!.LastName,
        ["stage"] = c => c.CurrentPipeline,
        // Ordered by the status the listing shows, which is the paper's once there is a paper and
        // the container's only until then. Ordering by the container's own status sorted by a value
        // that is not on screen: almost every row on a working queue is InProgress, so every row
        // tied and the column appeared to do nothing at all.
        //
        // Ranked in workflow order rather than alphabetically. These are stages, and a reader
        // clicking this column is asking how far along things are, not for Accepted before Draft.
        ["status"] = c =>
            c.Status == ContainerStatus.Completed ? 7
            : c.Publication == null ? 0
            : c.Publication.Status == PublicationStatus.Draft ? 1
            : c.Publication.Status == PublicationStatus.RevisionsRequested ? 2
            : c.Publication.Status == PublicationStatus.Resubmitted ? 3
            : c.Publication.Status == PublicationStatus.UnderReview ? 4
            : c.Publication.Status == PublicationStatus.Accepted ? 5
            : 6,
        ["started"] = c => c.CreatedAt,
        // What the Coordinator has to do next, ordered so that ascending puts the work first and
        // the publications waiting on somebody else last. It restates the two conditions the
        // dashboard reads off EthicsAwaitingRole and PaperStatus, because ordering has to happen
        // on the entity, before the projection those two fields are computed in. Grouping the two
        // kinds of work apart is deliberate: a coordinator clearing ethics decisions and one
        // clearing paper decisions are on different screens.
        ["waiting"] = c =>
            c.Status == ContainerStatus.Completed
                ? 2
            : c.EthicsApproval != null
              && ((c.EthicsApproval.Status == EthicsStatus.NotRequired && c.EthicsApproval.FinalDecisionAt == null)
                  || (c.EthicsApproval.Status == EthicsStatus.PendingVerification
                      && !c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview)
                      && (c.EthicsApproval.CoordinatorDecisionAt == null
                          || c.EthicsApproval.HeadOfDepartmentReviewedAt != null)))
                ? 0
            : c.Publication != null
              && c.Publication.Status == PublicationStatus.UnderReview
              && c.Publication.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Take(1)
                    .SelectMany(v => v.Reviews)
                    .Any(r => r.ReviewerType == ReviewerType.Supervisor && r.Decision == ReviewDecision.Approve)
              && c.Publication.Committee != null
              && c.Publication.Committee.Status == CommitteeStatus.Completed
                ? 1
            : 2
    };

    /// <summary>
    /// The same columns, with "waiting on you" answered for a supervisor instead of a coordinator.
    ///
    /// It has to be a different expression, not a shared one. Whose turn it is depends on who is
    /// asking: the coordinator's version ranks the two decisions that are theirs, so a supervisor
    /// ordering by it was ordering their own screen by somebody else's workload and the column
    /// looked broken. Ascending puts their work first, ethics before papers, matching how the
    /// screen groups it.
    /// </summary>
    private static readonly Dictionary<string, Expression<Func<PublicationContainer, object?>>> SupervisorSortColumns =
        new(SortColumns)
        {
            // The two conditions restate what the projection reads out as EthicsAwaitingRole and
            // PaperAwaitingRole for a supervisor. Restated rather than reused because ordering
            // happens on the entity, before those fields are computed.
            ["waiting"] = c =>
                c.EthicsApproval != null
                && (c.EthicsApproval.Status == EthicsStatus.PendingSupervisorDecision
                    || (c.EthicsApproval.Status == EthicsStatus.PendingVerification
                        && c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview)))
                    ? 0
                : c.Publication != null
                  && (c.Publication.Status == PublicationStatus.Resubmitted
                      || (c.Publication.Status == PublicationStatus.UnderReview
                          && !c.Publication.Versions
                                .OrderByDescending(v => v.VersionNumber)
                                .Take(1)
                                .SelectMany(v => v.Reviews)
                                .Any(r => r.ReviewerType == ReviewerType.Supervisor
                                          && r.Decision == ReviewDecision.Approve)))
                    ? 1
                : 2
        };

    /// <summary>
    /// The same columns again, with "waiting on" answered for a whole department rather than for
    /// one person.
    ///
    /// The other two dashboards ask "is this mine", and rank the decisions that belong to whoever
    /// is reading. A head of department is not in most of these queues; their screen names whoever
    /// the next move belongs to, so ordering by that column has to rank the same answer the column
    /// prints. Sharing the coordinator's version would order this screen by somebody else's
    /// workload, which is exactly how that column looked broken on the supervisor's.
    ///
    /// Ranked in the order the filter beside it lists the roles, so the sorted listing and the
    /// dropdown agree, and with nobody's turn last: a row nobody owes anything on is the one a
    /// reader scanning for hold-ups wants furthest away.
    /// </summary>
    private static readonly Dictionary<string, Expression<Func<PublicationContainer, object?>>> DepartmentSortColumns =
        new(SortColumns)
        {
            // The same chain the projection uses to name the role, as a rank instead. Restated
            // rather than shared because ordering happens on the entity, before the projection
            // those two fields are computed in.
            ["waiting"] = c =>
                c.Status == ContainerStatus.Completed
                    ? 6
                : c.EthicsApproval != null && c.EthicsApproval.Status == EthicsStatus.PendingSupervisorDecision
                    ? 1
                : c.EthicsApproval != null && c.EthicsApproval.Status == EthicsStatus.NotRequired
                  && c.EthicsApproval.FinalDecisionAt == null
                    ? 2
                : c.EthicsApproval != null && c.EthicsApproval.Status == EthicsStatus.PendingUpload
                    ? 0
                : c.EthicsApproval != null && c.EthicsApproval.Status == EthicsStatus.PendingVerification
                    ? (c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview)
                        ? 1
                        : c.EthicsApproval.CoordinatorDecisionAt == null
                            ? 2
                            : c.EthicsApproval.HeadOfDepartmentReviewedAt == null
                                ? 3
                                : 2)
                : c.Publication == null
                    ? 6
                : c.Publication.Status == PublicationStatus.Draft
                  || c.Publication.Status == PublicationStatus.RevisionsRequested
                  || c.Publication.Status == PublicationStatus.Accepted
                    ? 0
                : c.Publication.Status == PublicationStatus.Resubmitted
                    ? 1
                : c.Publication.Status == PublicationStatus.UnderReview
                    ? (!c.Publication.Versions
                            .OrderByDescending(v => v.VersionNumber)
                            .Take(1)
                            .SelectMany(v => v.Reviews)
                            .Any(r => r.ReviewerType == ReviewerType.Supervisor && r.Decision == ReviewDecision.Approve)
                        ? 1
                        : c.Publication.Committee == null
                            ? 4
                            : c.Publication.Committee.Status != CommitteeStatus.Completed
                                ? 5
                                : 2)
                : 6
        };

    public async Task<PagedResult<PublicationContainerDto>> GetMineAsync(
        Guid studentUserId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var workflow = await EthicsWorkflowAsync(cancellationToken);
        var paperWorkflow = await PaperWorkflowAsync(cancellationToken);

        // Order before projecting: the DTO carries a correlated sub-query for Title, and EF Core
        // cannot translate an OrderBy applied on top of that projection.
        return await ProjectToDto(
                db.PublicationContainers
                    .Where(c => c.StudentId == studentUserId)
                    .SortBy(page, c => c.CreatedAt, SortColumns),
                workflow, paperWorkflow)
            .ToPageAsync(page, cancellationToken);
    }

    public async Task<PagedResult<PublicationContainerDto>> GetSupervisingAsync(
        Guid supervisorUserId, ContainerQuery query, CancellationToken cancellationToken = default)
    {
        var workflow = await EthicsWorkflowAsync(cancellationToken);
        var paperWorkflow = await PaperWorkflowAsync(cancellationToken);

        // Ordered before projecting, as in GetMineAsync: the DTO carries correlated sub-queries
        // that EF Core cannot order on top of.
        return await ProjectToDto(
                WhereMatches(
                    WhereEthicsStep(
                        db.PublicationContainers.Where(c => c.AssignedSupervisorId == supervisorUserId),
                        query.EthicsStep, workflow),
                    query.Search)
                .SortBy(query, c => c.CreatedAt, SupervisorSortColumns),
                workflow, paperWorkflow)
            .ToPageAsync(query, cancellationToken);
    }

    public async Task<PagedResult<PublicationContainerDto>> GetInMyDepartmentAsync(
        Guid headOfDepartmentUserId, ContainerQuery query, CancellationToken cancellationToken = default)
    {
        // Which department this person heads. One each, so a single id is all there is to match;
        // a department may have several heads and they all oversee the same publications.
        var departmentId = await db.HeadOfDepartmentProfiles
            .Where(h => h.UserId == headOfDepartmentUserId)
            .Select(h => (Guid?)h.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);

        if (departmentId is null)
        {
            return new PagedResult<PublicationContainerDto>([], query.SafePage, query.SafePageSize, 0);
        }

        // Searchable and filterable by the paper's turn, like the coordinator's listing. A head of
        // department oversees every stage in their department, so the screens that show it need the
        // same handles: narrow to a student, or to the papers waiting on somebody in particular.
        // Ordered by DepartmentSortColumns, which answers "waiting on" for the department rather
        // than for whoever is reading.
        var workflow = await EthicsWorkflowAsync(cancellationToken);
        var paperWorkflow = await PaperWorkflowAsync(cancellationToken);

        var department = WherePaperAwaiting(
            WhereMatches(
                WhereEthicsStep(
                    db.PublicationContainers.Where(c => c.Student.StudentProfile != null
                                && c.Student.StudentProfile.DepartmentId == departmentId),
                    query.EthicsStep, workflow),
                query.Search),
            query.PaperAwaiting);

        // Asking for the ethics reviews narrows to the ones put to this head. Every other listing
        // stays department-wide, because oversight of the department is the whole point of these
        // screens; it is the queue of work to do that belongs to one person.
        if (query.EthicsStep is { Count: > 0 } steps && steps.Contains(EthicsSteps.HeadOfDepartmentReview))
        {
            department = department.Where(c => c.EthicsApproval!.HeadOfDepartmentUserId == null
                || c.EthicsApproval.HeadOfDepartmentUserId == headOfDepartmentUserId);
        }

        return await ProjectToDto(department.SortBy(query, c => c.CreatedAt, DepartmentSortColumns), workflow, paperWorkflow)
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

    public async Task<PagedResult<ActivityHistoryEntryDto>> GetActivityHistoryAsync(
        Guid id, Guid requestingUserId, PageRequest paging, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(id, requestingUserId);

        // Paged like every other listing. A publication that has been through three stages, several
        // revisions and a committee accumulates a long trail, and the whole of it arrived in one
        // response and was drawn as one unbroken list.
        var query = db.ActivityHistoryEntries.Where(a => a.PublicationContainerId == id);

        if (paging is ActivityHistoryQuery filter)
        {
            // Whole days, in the reader's terms: somebody asking for the 4th means all of it, not
            // up to midnight at its start. The stored instants are UTC, which is close enough for
            // a trail read by people in one country and far simpler than carrying a timezone
            // through the query string.
            if (filter.From is { } from)
            {
                var start = from.ToDateTime(TimeOnly.MinValue);
                query = query.Where(a => a.CreatedAt >= start);
            }

            if (filter.To is { } to)
            {
                var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
                query = query.Where(a => a.CreatedAt < end);
            }

            if (!string.IsNullOrWhiteSpace(filter.Action))
            {
                query = query.Where(a => a.Action == filter.Action);
            }

            // Either side of "on behalf of": somebody looking for what a student's record shows
            // wants the decisions taken for them as well as by them.
            if (filter.ActorUserId is { } actor)
            {
                query = query.Where(a => a.ActorUserId == actor || a.OnBehalfOfUserId == actor);
            }
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((paging.SafePage - 1) * paging.SafePageSize)
            .Take(paging.SafePageSize)
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

        return new PagedResult<ActivityHistoryEntryDto>(items, paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<ActivityHistoryFiltersDto> GetActivityHistoryFiltersAsync(
        Guid id, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(id, requestingUserId);

        // Only what this publication's own trail actually holds. A fixed list of every action the
        // system can record would offer a student a dozen filters that match nothing on the page
        // in front of them.
        var entries = db.ActivityHistoryEntries.Where(a => a.PublicationContainerId == id);

        var actions = await entries
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(action => action)
            .ToListAsync(cancellationToken);

        var actors = await entries
            .Select(a => new { a.ActorUserId, a.ActorUser.FirstName, a.ActorUser.LastName })
            .Distinct()
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Select(a => new ActivityHistoryActorDto(a.ActorUserId, a.FirstName + " " + a.LastName))
            .ToListAsync(cancellationToken);

        return new ActivityHistoryFiltersDto(actions, actors);
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

        containers = WherePaperAwaiting(containers, query.PaperAwaiting);
        containers = WhereMatches(containers, query.Search);

        var workflow = await EthicsWorkflowAsync(cancellationToken);
        var paperWorkflow = await PaperWorkflowAsync(cancellationToken);

        return await ProjectToDto(
                WhereEthicsStep(containers, query.EthicsStep, workflow)
                    .SortBy(query, c => c.CreatedAt, SortColumns),
                workflow, paperWorkflow)
            .ToPageAsync(query, cancellationToken);
    }

    /// <summary>
    /// Narrows to the publications whose research paper is waiting on a particular role, or on
    /// anybody but that role when the name is prefixed with <c>!</c>.
    ///
    /// Expressed against the entity for the same reason as the ethics filter: the role name in the
    /// DTO is a CASE built during projection, and EF Core cannot filter on it afterwards. The two
    /// stay in step by construction, because both are written from the same conditions.
    /// </summary>
    private static IQueryable<PublicationContainer> WherePaperAwaiting(
        IQueryable<PublicationContainer> query, string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return query;

        var negate = role.StartsWith('!');
        var wanted = negate ? role[1..] : role;

        // Only the coordinator's turn is asked for today, and it is the last of the four waits
        // UnderReview covers: a supervisor has approved the latest version, a committee exists, and
        // it has finished voting. Any other name is not a filter this endpoint knows, and returning
        // everything is the safer answer than returning nothing.
        if (wanted != RoleNames.Coordinator) return query;

        // The negation is "a paper under review that is somebody else's turn", not "everything that
        // is not the coordinator's turn". Those differ by every publication with no paper at all,
        // every draft and everything already published, and the screen that asks for it is showing
        // papers in flight. Written into the filter rather than left to the caller, because the
        // total the pager reports comes from here: a list filtered again after the page was cut
        // says nineteen and shows three.
        return negate
            ? query.Where(c => c.Publication != null
                && (c.Publication.Status == PublicationStatus.UnderReview
                    || c.Publication.Status == PublicationStatus.Resubmitted
                    || c.Publication.Status == PublicationStatus.RevisionsRequested)
                && !(c.Publication.Status == PublicationStatus.UnderReview
                    && c.Publication.Versions
                        .OrderByDescending(v => v.VersionNumber)
                        .Take(1)
                        .SelectMany(v => v.Reviews)
                        .Any(r => r.ReviewerType == ReviewerType.Supervisor && r.Decision == ReviewDecision.Approve)
                    && c.Publication.Committee != null
                    && c.Publication.Committee.Status == CommitteeStatus.Completed))
            : query.Where(c => c.Publication != null
                && c.Publication.Status == PublicationStatus.UnderReview
                && c.Publication.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Take(1)
                    .SelectMany(v => v.Reviews)
                    .Any(r => r.ReviewerType == ReviewerType.Supervisor && r.Decision == ReviewDecision.Approve)
                && c.Publication.Committee != null
                && c.Publication.Committee.Status == CommitteeStatus.Completed);
    }

    /// <summary>
    /// One term across everything a reader might be holding in mind: the student's name, the
    /// paper's title and abstract, the proposals under it, and the people who have reviewed it.
    ///
    /// The reviewers are in there because the coordinator's paper queue is often searched by who
    /// looked at something rather than by what it was called: "the one Okoro sent back". Separate
    /// boxes would make somebody decide which of those they were remembering before they could
    /// start typing.
    /// </summary>
    private static IQueryable<PublicationContainer> WhereMatches(
        IQueryable<PublicationContainer> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;

        var term = search.Trim();

        return query.Where(c =>
            c.Student.FirstName.Contains(term)
            || c.Student.LastName.Contains(term)
            || (c.Publication != null && (c.Publication.Title.Contains(term)
                                          || c.Publication.Abstract.Contains(term)))
            || c.Proposals.Any(p => p.Title.Contains(term) || p.Abstract.Contains(term))
            || (c.Publication != null && c.Publication.Versions.Any(v => v.Reviews.Any(r =>
                r.ReviewerUser.FirstName.Contains(term) || r.ReviewerUser.LastName.Contains(term)))));
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
        IQueryable<PublicationContainer> query, IReadOnlyList<string>? steps, EthicsWorkflowSettingsDto workflow)
    {
        if (steps is null or { Count: 0 }) return query;

        var headOfDepartmentReviews = workflow.HeadOfDepartmentReviews;
        var headOfDepartmentReviewsNotRequired = workflow.HeadOfDepartmentReviewsWhenNotRequired;
        var coordinatorReadsDocuments = workflow.CoordinatorReviewsDocuments;
        var supervisorReadsDocuments = workflow.SupervisorReviewsDocuments;
        var coordinatorReadsFirst = workflow.CoordinatorReadsFirst;

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
                && c.EthicsApproval.FinalDecisionAt == null
                && c.EthicsApproval.CoordinatorDecisionAt == null)
            || (studentUpload && c.EthicsApproval.Status == EthicsStatus.PendingUpload)
            || (supervisorDocuments
                && supervisorReadsDocuments
                && c.EthicsApproval.Status == EthicsStatus.PendingVerification
                && c.EthicsApproval.SupervisorDocumentsReviewedAt == null
                && (!coordinatorReadsFirst || !coordinatorReadsDocuments || c.EthicsApproval.CoordinatorDecisionAt != null))
            || (coordinatorDocuments
                && coordinatorReadsDocuments
                && c.EthicsApproval.Status == EthicsStatus.PendingVerification
                && c.EthicsApproval.CoordinatorDecisionAt == null
                && (coordinatorReadsFirst || !supervisorReadsDocuments
                    || c.EthicsApproval.SupervisorDocumentsReviewedAt != null))
            || (headOfDepartment
                && headOfDepartmentReviews
                && c.EthicsApproval.Status == EthicsStatus.PendingVerification
                && (c.EthicsApproval.SupervisorDocumentsReviewedAt != null || !supervisorReadsDocuments)
                && (c.EthicsApproval.CoordinatorDecisionAt != null || !coordinatorReadsDocuments)
                && c.EthicsApproval.HeadOfDepartmentReviewedAt == null)
            // The same step, reached by the other route: nothing to read, a ruling to weigh.
            || (headOfDepartment
                && headOfDepartmentReviewsNotRequired
                && c.EthicsApproval.Status == EthicsStatus.NotRequired
                && c.EthicsApproval.FinalDecisionAt == null
                && c.EthicsApproval.CoordinatorDecisionAt != null
                && c.EthicsApproval.HeadOfDepartmentReviewedAt == null)
            || (coordinatorFinal
                && c.EthicsApproval.Status == EthicsStatus.PendingVerification
                && (c.EthicsApproval.SupervisorDocumentsReviewedAt != null || !supervisorReadsDocuments)
                && (c.EthicsApproval.CoordinatorDecisionAt != null || !coordinatorReadsDocuments)
                && (c.EthicsApproval.HeadOfDepartmentReviewedAt != null || !headOfDepartmentReviews))
            // A stage with no documentation only waits on the coordinator a second time where the
            // Head of Department has been through it; otherwise their agreement already closed it.
            || (coordinatorFinal
                && headOfDepartmentReviewsNotRequired
                && c.EthicsApproval.Status == EthicsStatus.NotRequired
                && c.EthicsApproval.FinalDecisionAt == null
                && c.EthicsApproval.CoordinatorDecisionAt != null
                && c.EthicsApproval.HeadOfDepartmentReviewedAt != null)));
    }

    public async Task<PublicationContainerDto> MoveToAsync(
        Guid containerId, MoveContainerRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var container = await db.PublicationContainers
            .Include(c => c.EthicsApproval).ThenInclude(a => a!.Documents)
            .Include(c => c.Publication)
            .FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

        if (container.Status == ContainerStatus.Completed)
        {
            throw new BusinessRuleException(
                "This publication has finished. Moving it back would reopen decisions already made.");
        }

        if (string.IsNullOrWhiteSpace(request.Comments))
        {
            throw new BusinessRuleException("Say why this publication is being moved. It stays on its history.");
        }

        if (request.Stage is < (int)PipelineStage.ResearchProposals or > (int)PipelineStage.ResearchPaper)
        {
            throw new BusinessRuleException("That is not one of the three stages.");
        }

        var stage = (PipelineStage)request.Stage;
        var changes = new List<string>();

        if (container.CurrentPipeline != stage)
        {
            changes.Add($"stage moved from {container.CurrentPipeline} to {stage}");
            container.CurrentPipeline = stage;
        }

        if (stage == PipelineStage.EthicsApproval && request.EthicsStep is { Length: > 0 } step)
        {
            var approval = container.EthicsApproval
                ?? throw new BusinessRuleException(
                    "This publication has no ethics approval yet, so there is no step to put it at. "
                    + "The student makes their declaration first.");

            MoveEthicsTo(approval, step);
            changes.Add($"ethics set to {step}");
        }

        if (stage == PipelineStage.ResearchPaper && request.PaperStatus is { Length: > 0 } wanted)
        {
            var publication = container.Publication
                ?? throw new BusinessRuleException(
                    "This publication has no research paper yet, so there is no status to set.");

            if (!Enum.TryParse<PublicationStatus>(wanted, ignoreCase: true, out var status))
            {
                throw new BusinessRuleException($"'{wanted}' is not a status a research paper can have.");
            }

            // Publishing is the student's own decision and carries its own trail, so it is not one
            // of the positions this can drop a paper into.
            if (status == PublicationStatus.Published)
            {
                throw new BusinessRuleException(
                    "A paper is published by its author, or on their behalf from the paper's own screen, not by moving it here.");
            }

            if (publication.Status != status)
            {
                changes.Add($"paper set from {publication.Status} to {status}");
                publication.Status = status;
                publication.IsPublished = false;
                publication.PublishedAt = null;
            }
        }

        if (changes.Count == 0)
        {
            throw new BusinessRuleException("That is where this publication already stands.");
        }

        container.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, actingAdminId, "PublicationMovedByAdmin",
            string.Join("; ", changes) + ". " + request.Comments,
            newStatus: container.CurrentPipeline.ToString());

        // Whoever now has it. Told rather than left to notice, which is the whole point of moving
        // a publication: somebody is meant to pick it up.
        foreach (var userId in await WhoIsWaitingAsync(container, cancellationToken))
        {
            await notificationService.NotifyAsync(userId, NotificationType.ContainerAssigned,
                "A publication is waiting on you",
                "An administrator has moved a publication to a step that is yours. Please log in to see where it stands.",
                nameof(PublicationContainer), container.Id, cancellationToken);
        }

        return await ProjectToDto(db.PublicationContainers.Where(c => c.Id == container.Id),
                await EthicsWorkflowAsync(cancellationToken), await PaperWorkflowAsync(cancellationToken))
            .FirstAsync(cancellationToken);
    }

    /// <summary>
    /// Rewinds or advances an ethics approval to the step named, by setting exactly the marks that
    /// step is defined by and clearing every later one. The steps are told apart by which
    /// timestamps are set, so putting one back means unsetting what came after it; leaving those
    /// behind would land the approval on a later step than the one asked for.
    /// </summary>
    private static void MoveEthicsTo(EthicsApproval approval, string step)
    {
        var now = DateTime.UtcNow;

        // Everything after the step being set is cleared, then the step's own marks are written.
        approval.FinalDecisionAt = null;
        approval.HeadOfDepartmentReviewedAt = null;
        approval.HeadOfDepartmentUserId = null;
        approval.SupervisorDocumentsReviewedAt = null;

        switch (step)
        {
            case EthicsSteps.SupervisorDecision:
                approval.Status = EthicsStatus.PendingSupervisorDecision;
                approval.SupervisorDecisionAt = null;
                approval.IsRequiredPerSupervisor = null;
                approval.CoordinatorDecisionAt = null;
                approval.IsRequiredPerCoordinator = null;
                break;

            case EthicsSteps.CoordinatorConfirmation:
                approval.Status = EthicsStatus.NotRequired;
                approval.IsRequiredPerSupervisor = false;
                approval.SupervisorDecisionAt ??= now;
                approval.CoordinatorDecisionAt = null;
                approval.IsRequiredPerCoordinator = null;
                break;

            case EthicsSteps.StudentUpload:
                approval.Status = EthicsStatus.PendingUpload;
                approval.IsRequiredPerSupervisor = true;
                approval.SupervisorDecisionAt ??= now;
                approval.CoordinatorDecisionAt = null;
                break;

            case EthicsSteps.SupervisorDocumentReview:
                RequireDocuments(approval);
                approval.Status = EthicsStatus.PendingVerification;
                approval.SupervisorDecisionAt ??= now;
                approval.CoordinatorDecisionAt = null;

                // The supervisor's step is defined by there being something they have not read.
                foreach (var document in NewestPerRequirement(approval))
                {
                    document.Status = EthicsDocumentStatus.PendingReview;
                }
                break;

            case EthicsSteps.CoordinatorDocumentReview:
                RequireDocuments(approval);
                approval.Status = EthicsStatus.PendingVerification;
                approval.SupervisorDecisionAt ??= now;
                approval.SupervisorDocumentsReviewedAt = now;
                approval.CoordinatorDecisionAt = null;

                foreach (var document in NewestPerRequirement(approval))
                {
                    document.Status = EthicsDocumentStatus.Accepted;
                }
                break;

            case EthicsSteps.HeadOfDepartmentReview:
                RequireDocuments(approval);
                approval.Status = EthicsStatus.PendingVerification;
                approval.SupervisorDecisionAt ??= now;
                approval.SupervisorDocumentsReviewedAt = now;
                approval.CoordinatorDecisionAt ??= now;
                foreach (var document in NewestPerRequirement(approval))
                {
                    document.Status = EthicsDocumentStatus.Accepted;
                }
                break;

            case EthicsSteps.CoordinatorFinalDecision:
                RequireDocuments(approval);
                approval.Status = EthicsStatus.PendingVerification;
                approval.SupervisorDecisionAt ??= now;
                approval.SupervisorDocumentsReviewedAt = now;
                approval.CoordinatorDecisionAt ??= now;
                approval.HeadOfDepartmentReviewedAt = now;
                foreach (var document in NewestPerRequirement(approval))
                {
                    document.Status = EthicsDocumentStatus.Accepted;
                }
                break;

            default:
                throw new BusinessRuleException($"'{step}' is not a step of the ethics stage.");
        }

        static void RequireDocuments(EthicsApproval approval)
        {
            if (approval.Documents.Count == 0)
            {
                throw new BusinessRuleException(
                    "There is no ethics documentation on this publication, so it cannot wait at a step that reads it. "
                    + "Put it at the student's upload step, or add the documents first.");
            }
        }

        static IEnumerable<EthicsDocument> NewestPerRequirement(EthicsApproval approval) =>
            approval.Documents
                .GroupBy(d => d.EthicsDocumentRequirementId)
                .Select(versions => versions.OrderByDescending(d => d.Version).First());
    }

    /// <summary>Who the publication now waits on, so they can be told rather than left to find it.</summary>
    private async Task<IReadOnlyList<Guid>> WhoIsWaitingAsync(
        PublicationContainer container, CancellationToken cancellationToken)
    {
        var moved = await ProjectToDto(db.PublicationContainers.Where(c => c.Id == container.Id),
                await EthicsWorkflowAsync(cancellationToken), await PaperWorkflowAsync(cancellationToken))
            .FirstAsync(cancellationToken);

        var role = moved.EthicsAwaitingRole ?? moved.PaperAwaitingRole;

        return role switch
        {
            RoleNames.Student => [container.StudentId],
            RoleNames.Coordinator => [container.CoordinatorId],
            RoleNames.Supervisor when container.AssignedSupervisorId is { } supervisor => [supervisor],
            RoleNames.HeadOfDepartment => await db.StudentProfiles
                .Where(s => s.UserId == container.StudentId)
                .SelectMany(s => s.Department.HeadsOfDepartment)
                .Select(h => h.UserId)
                .ToListAsync(cancellationToken),
            _ => []
        };
    }

    /// <summary>
    /// Changes who is responsible for a publication that is still under way.
    ///
    /// The one lever there was for this appointed a coordinator; there was none at all for a
    /// supervisor, which is the appointment the whole ethics and paper pipeline waits on. A
    /// supervisor who leaves therefore stopped the publication with no way to restart it short of
    /// the database.
    /// </summary>
    public async Task<PublicationContainerDto> ReassignAsync(
        Guid containerId, ReassignContainerRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

        if (container.Status == ContainerStatus.Completed)
        {
            throw new BusinessRuleException(
                "This publication has finished. Changing who was responsible for it would rewrite the record of who decided what.");
        }

        // Always. This overrides a decision somebody else made, and it is the only account of why.
        if (string.IsNullOrWhiteSpace(request.Comments))
        {
            throw new BusinessRuleException("Say why these assignments are being changed. It stays on the publication's history.");
        }

        if (request.CoordinatorUserId is null && request.SupervisorUserId is null && request.HeadOfDepartmentUserId is null)
        {
            throw new BusinessRuleException("Choose somebody to change.");
        }

        // Every appointment here is scoped to the student's own department, so it is worth knowing
        // once. Null only where the student has no profile, which the checks below refuse on.
        var studentDepartmentId = await db.StudentProfiles
            .Where(s => s.UserId == container.StudentId)
            .Select(s => (Guid?)s.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);

        var changes = new List<string>();
        Guid? notifyCoordinator = null;
        Guid? notifySupervisor = null;
        Guid? notifyHeadOfDepartment = null;

        if (request.CoordinatorUserId is { } coordinatorId && coordinatorId != container.CoordinatorId)
        {
            await EnsureHoldsRoleAsync(coordinatorId, RoleNames.Coordinator, "coordinator", cancellationToken);
            await EnsurePostedToDepartmentAsync(
                db.CoordinatorProfiles.Where(p => p.UserId == coordinatorId).Select(p => p.DepartmentId),
                studentDepartmentId, "coordinator", cancellationToken);

            var previous = await NameOfAsync(container.CoordinatorId, cancellationToken);
            container.CoordinatorId = coordinatorId;
            notifyCoordinator = coordinatorId;
            changes.Add($"coordinator changed from {previous} to {await NameOfAsync(coordinatorId, cancellationToken)}");
        }

        if (request.SupervisorUserId is { } supervisorId && supervisorId != container.AssignedSupervisorId)
        {
            // Only where one has already been appointed. Choosing the first supervisor is the
            // coordinator's decision on a proposal, and doing it from here would settle which
            // proposal goes ahead as a side effect of an administrative fix.
            if (container.AssignedSupervisorId is null)
            {
                throw new BusinessRuleException(
                    "This publication has no supervisor yet. The coordinator appoints the first one by assigning a proposal.");
            }

            await EnsureHoldsRoleAsync(supervisorId, RoleNames.Supervisor, "supervisor", cancellationToken);

            var previous = await NameOfAsync(container.AssignedSupervisorId.Value, cancellationToken);
            container.AssignedSupervisorId = supervisorId;
            notifySupervisor = supervisorId;
            changes.Add($"supervisor changed from {previous} to {await NameOfAsync(supervisorId, cancellationToken)}");
        }

        if (request.HeadOfDepartmentUserId is { } headId)
        {
            var approval = await db.EthicsApprovals
                .FirstOrDefaultAsync(a => a.PublicationContainerId == container.Id, cancellationToken)
                ?? throw new BusinessRuleException(
                    "This publication has not reached its ethics stage, so there is no ethics decision to put to anybody.");

            if (approval.HeadOfDepartmentReviewedAt is not null || approval.FinalDecisionAt is not null)
            {
                throw new BusinessRuleException(
                    "This ethics decision has already been commented on. Moving it now would rewrite who reviewed it.");
            }

            if (headId != approval.HeadOfDepartmentUserId)
            {
                await EnsureHoldsRoleAsync(headId, RoleNames.HeadOfDepartment, "head of department", cancellationToken);
                await EnsurePostedToDepartmentAsync(
                    db.HeadOfDepartmentProfiles.Where(p => p.UserId == headId).Select(p => p.DepartmentId),
                    studentDepartmentId, "head of department", cancellationToken);

                var previous = approval.HeadOfDepartmentUserId is { } was
                    ? await NameOfAsync(was, cancellationToken)
                    : "nobody";

                approval.HeadOfDepartmentUserId = headId;
                notifyHeadOfDepartment = headId;
                changes.Add($"ethics review moved from {previous} to {await NameOfAsync(headId, cancellationToken)}");
            }
        }

        if (changes.Count == 0)
        {
            throw new BusinessRuleException("Those are the people already responsible for this publication.");
        }

        container.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, actingAdminId, "AssignmentsChanged",
            string.Join("; ", changes) + ". " + request.Comments);

        // Whoever now has the work, so it does not sit unnoticed on a queue they have never looked
        // at. The person replaced is not told: they have nothing left to do here, and the
        // publication's history says what happened.
        if (notifyCoordinator is { } newCoordinator)
        {
            await notificationService.NotifyAsync(newCoordinator, NotificationType.ContainerAssigned,
                "A publication has been assigned to you",
                "An administrator has made you the coordinator for a publication. Please log in to see where it has got to.",
                nameof(PublicationContainer), container.Id, cancellationToken);
        }

        if (notifyHeadOfDepartment is { } newHead)
        {
            await notificationService.NotifyAsync(newHead, NotificationType.EthicsHeadOfDepartmentReviewRequested,
                "An ethics decision has been put to you",
                "An administrator has moved a student's ethics decision to you. Please log in to review it and record your comments.",
                nameof(PublicationContainer), container.Id, cancellationToken);
        }

        if (notifySupervisor is { } newSupervisor)
        {
            await notificationService.NotifyAsync(newSupervisor, NotificationType.ContainerAssigned,
                "A publication has been assigned to you",
                "An administrator has made you the supervisor for a publication. Please log in to see where it has got to.",
                nameof(PublicationContainer), container.Id, cancellationToken);
        }

        return await GetByIdInternalAsync(container.Id, cancellationToken);
    }

    /// <summary>Refuses somebody who does not hold the role the job needs.</summary>
    /// <summary>
    /// Refuses somebody who holds the role but not in this student's department.
    ///
    /// A coordinator or head of department belongs to a department, and their authority over a
    /// publication comes from the student being in it. Somebody posted elsewhere would appear on
    /// no listing that could reach this publication, so naming them here would strand it.
    /// </summary>
    private static async Task EnsurePostedToDepartmentAsync(
        IQueryable<Guid> theirDepartments, Guid? studentDepartmentId, string what, CancellationToken cancellationToken)
    {
        if (studentDepartmentId is null)
        {
            throw new BusinessRuleException(
                $"This student has no department on record, so there is no way to tell which {what} may take this on.");
        }

        if (!await theirDepartments.AnyAsync(id => id == studentDepartmentId, cancellationToken))
        {
            throw new BusinessRuleException(
                $"The person chosen is not a {what} in this student's department. Only somebody in it can take this on.");
        }
    }

    private async Task EnsureHoldsRoleAsync(Guid userId, string role, string what, CancellationToken cancellationToken)
    {
        var holds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .AnyAsync(name => name == role, cancellationToken);

        if (!holds)
        {
            throw new BusinessRuleException($"The person chosen is not a {what}. Give them the role first, in Users.");
        }
    }

    private async Task<string> NameOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FirstName + " " + u.LastName)
            .FirstOrDefaultAsync(cancellationToken);

        return user ?? "somebody no longer on record";
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
        var workflow = await EthicsWorkflowAsync(cancellationToken);
        var paperWorkflow = await PaperWorkflowAsync(cancellationToken);

        return await ProjectToDto(db.PublicationContainers.Where(c => c.Id == id), workflow, paperWorkflow)
                   .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), id);
    }

    /// <summary>
    /// Whether this institution puts the Head of Department between the coordinator's approval and
    /// their final decision. Read once per request and passed into the projections: they are
    /// expressions the database runs, so they cannot ask a setting for themselves.
    /// </summary>
    private Task<EthicsWorkflowSettingsDto> EthicsWorkflowAsync(CancellationToken cancellationToken) =>
        settingService.GetEthicsWorkflowSettingsAsync(cancellationToken);

    private Task<PaperWorkflowSettingsDto> PaperWorkflowAsync(CancellationToken cancellationToken) =>
        settingService.GetPaperWorkflowSettingsAsync(cancellationToken);

    private static IQueryable<PublicationContainerDto> ProjectToDto(
        IQueryable<PublicationContainer> query, EthicsWorkflowSettingsDto workflow, PaperWorkflowSettingsDto paper)
    {
        var supervisorReadsPapers = paper.SupervisorReviews;
        var committeeEvaluates = paper.CommitteeEvaluates;
        var coordinatorDecidesOnPapers = paper.CoordinatorDecides;

        // Read out here rather than off the record inside the expression: what the database runs
        // takes two values, not an object it would have to know how to open.
        var headOfDepartmentReviews = workflow.HeadOfDepartmentReviews;
        var headOfDepartmentReviewsNotRequired = workflow.HeadOfDepartmentReviewsWhenNotRequired;
        var coordinatorReadsDocuments = workflow.CoordinatorReviewsDocuments;
        var supervisorReadsDocuments = workflow.SupervisorReviewsDocuments;
        var coordinatorReadsFirst = workflow.CoordinatorReadsFirst;

        return query.Select(c => new PublicationContainerDto(
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
                // A Supervisor said no documentation is needed. The Coordinator agrees, then the
                // Head of Department comments where this institution asks for it, then the
                // Coordinator closes the stage.
                : c.EthicsApproval.Status == EthicsStatus.NotRequired && c.EthicsApproval.FinalDecisionAt == null
                    ? (c.EthicsApproval.CoordinatorDecisionAt == null
                        ? RoleNames.Coordinator
                        : headOfDepartmentReviewsNotRequired && c.EthicsApproval.HeadOfDepartmentReviewedAt == null
                            ? RoleNames.HeadOfDepartment
                            : RoleNames.Coordinator)
                // Documentation was asked for and the student has yet to upload it.
                : c.EthicsApproval.Status == EthicsStatus.PendingUpload
                    ? RoleNames.Student
                : c.EthicsApproval.Status == EthicsStatus.PendingVerification
                    // The two readings, in the order this institution runs them, each skipped
                    // where it is switched off so nothing parks on a queue nobody works.
                    ? (coordinatorReadsFirst
                        ? (coordinatorReadsDocuments && c.EthicsApproval.CoordinatorDecisionAt == null
                            ? RoleNames.Coordinator
                            : supervisorReadsDocuments && c.EthicsApproval.SupervisorDocumentsReviewedAt == null
                                ? RoleNames.Supervisor
                                : headOfDepartmentReviews && c.EthicsApproval.HeadOfDepartmentReviewedAt == null
                                    ? RoleNames.HeadOfDepartment
                                    // Everyone has had their say; the Coordinator closes it.
                                    : RoleNames.Coordinator)
                        : (supervisorReadsDocuments && c.EthicsApproval.SupervisorDocumentsReviewedAt == null
                            ? RoleNames.Supervisor
                            : coordinatorReadsDocuments && c.EthicsApproval.CoordinatorDecisionAt == null
                                ? RoleNames.Coordinator
                                : headOfDepartmentReviews && c.EthicsApproval.HeadOfDepartmentReviewedAt == null
                                    ? RoleNames.HeadOfDepartment
                                    : RoleNames.Coordinator))
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
                : c.Publication.Status == PublicationStatus.Resubmitted && supervisorReadsPapers
                    // A new version needs a fresh reading; the approval on record is of a draft
                    // the student has already replaced.
                    ? RoleNames.Supervisor
                : c.Publication.Status == PublicationStatus.UnderReview
                        || c.Publication.Status == PublicationStatus.Resubmitted
                    // Each reading is skipped where this institution does not run it, so a paper
                    // cannot sit waiting on a step nobody works.
                    ? (supervisorReadsPapers
                        && !c.Publication.Versions
                            .OrderByDescending(v => v.VersionNumber)
                            .Take(1)
                            .SelectMany(v => v.Reviews)
                            .Any(r => r.ReviewerType == ReviewerType.Supervisor && r.Decision == ReviewDecision.Approve)
                        ? RoleNames.Supervisor
                        : committeeEvaluates && c.Publication.Committee == null
                            ? RoleNames.Admin
                            : committeeEvaluates && c.Publication.Committee!.Status != CommitteeStatus.Completed
                                ? RoleNames.EvaluationCommittee
                                : coordinatorDecidesOnPapers
                                    ? RoleNames.Coordinator
                                    : null)
                    : null,
            // The same waits as EthicsAwaitingRole, named individually. Two of them belong to the
            // Coordinator and are different screens, so a role alone cannot select either.
            c.EthicsApproval == null
                ? null
                : c.EthicsApproval.Status == EthicsStatus.PendingSupervisorDecision
                    ? EthicsSteps.SupervisorDecision
                : c.EthicsApproval.Status == EthicsStatus.NotRequired && c.EthicsApproval.FinalDecisionAt == null
                    ? (c.EthicsApproval.CoordinatorDecisionAt == null
                        ? EthicsSteps.CoordinatorConfirmation
                        : headOfDepartmentReviewsNotRequired && c.EthicsApproval.HeadOfDepartmentReviewedAt == null
                            ? EthicsSteps.HeadOfDepartmentReview
                            : EthicsSteps.CoordinatorFinalDecision)
                : c.EthicsApproval.Status == EthicsStatus.PendingUpload
                    ? EthicsSteps.StudentUpload
                : c.EthicsApproval.Status == EthicsStatus.PendingVerification
                    ? (coordinatorReadsFirst
                        ? (coordinatorReadsDocuments && c.EthicsApproval.CoordinatorDecisionAt == null
                            ? EthicsSteps.CoordinatorDocumentReview
                            : supervisorReadsDocuments && c.EthicsApproval.SupervisorDocumentsReviewedAt == null
                                ? EthicsSteps.SupervisorDocumentReview
                                : headOfDepartmentReviews && c.EthicsApproval.HeadOfDepartmentReviewedAt == null
                                    ? EthicsSteps.HeadOfDepartmentReview
                                    : EthicsSteps.CoordinatorFinalDecision)
                        : (supervisorReadsDocuments && c.EthicsApproval.SupervisorDocumentsReviewedAt == null
                            ? EthicsSteps.SupervisorDocumentReview
                            : coordinatorReadsDocuments && c.EthicsApproval.CoordinatorDecisionAt == null
                                ? EthicsSteps.CoordinatorDocumentReview
                                : headOfDepartmentReviews && c.EthicsApproval.HeadOfDepartmentReviewedAt == null
                                    ? EthicsSteps.HeadOfDepartmentReview
                                    : EthicsSteps.CoordinatorFinalDecision))
                    : null,
            c.RequiredReviewerMembers,
            c.RequiredExternalCommitteeMembers,
            // Sent back, rather than never sent. Both read as "waiting on the student", and a
            // student asked to correct one document could not tell which publication it was.
            c.EthicsApproval != null
                && c.EthicsApproval.Status == EthicsStatus.PendingUpload
                && c.EthicsApproval.Documents.Any(d => d.Status == EthicsDocumentStatus.RevisionRequested),
            // The student's department, because every appointment on this publication is scoped to
            // it: a screen that offers a choice has to know which people are eligible.
            c.Student.StudentProfile == null ? null : c.Student.StudentProfile.DepartmentId,
            c.Student.StudentProfile == null ? null : c.Student.StudentProfile.Department.Name,
            c.EthicsApproval == null ? null : c.EthicsApproval.HeadOfDepartmentUserId,
            c.EthicsApproval == null || c.EthicsApproval.HeadOfDepartmentUser == null
                ? null
                : c.EthicsApproval.HeadOfDepartmentUser.FirstName + " " + c.EthicsApproval.HeadOfDepartmentUser.LastName));
    }
}
