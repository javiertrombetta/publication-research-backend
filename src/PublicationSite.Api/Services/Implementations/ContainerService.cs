using System.Linq.Expressions;
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
        Guid studentUserId, PageRequest page, CancellationToken cancellationToken = default) =>
        // Order before projecting: the DTO carries a correlated sub-query for Title, and EF Core
        // cannot translate an OrderBy applied on top of that projection.
        await ProjectToDto(
                db.PublicationContainers
                    .Where(c => c.StudentId == studentUserId)
                    .SortBy(page, c => c.CreatedAt, SortColumns))
            .ToPageAsync(page, cancellationToken);

    public async Task<PagedResult<PublicationContainerDto>> GetSupervisingAsync(
        Guid supervisorUserId, ContainerQuery query, CancellationToken cancellationToken = default) =>
        // Ordered before projecting, as in GetMineAsync: the DTO carries correlated sub-queries
        // that EF Core cannot order on top of.
        await ProjectToDto(
                WhereMatches(
                    WhereEthicsStep(
                        db.PublicationContainers.Where(c => c.AssignedSupervisorId == supervisorUserId),
                        query.EthicsStep),
                    query.Search)
                .SortBy(query, c => c.CreatedAt, SupervisorSortColumns))
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

        // Searchable and filterable by the paper's turn, like the coordinator's listing. A head of
        // department oversees every stage in their department, so the screens that show it need the
        // same handles: narrow to a student, or to the papers waiting on somebody in particular.
        // Ordered by DepartmentSortColumns, which answers "waiting on" for the department rather
        // than for whoever is reading.
        var department = WherePaperAwaiting(
            WhereMatches(
                WhereEthicsStep(
                    db.PublicationContainers.Where(c => c.Student.StudentProfile != null
                                && c.Student.StudentProfile.DepartmentId == departmentId),
                    query.EthicsStep),
                query.Search),
            query.PaperAwaiting);

        return await ProjectToDto(department.SortBy(query, c => c.CreatedAt, DepartmentSortColumns))
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

        return await ProjectToDto(
                WhereEthicsStep(containers, query.EthicsStep).SortBy(query, c => c.CreatedAt, SortColumns))
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
            c.RequiredReviewerMembers,
            c.RequiredExternalCommitteeMembers));
}
