using System.Linq.Expressions;
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
    INotificationService notificationService,
    ISystemSettingsProvider settings,
    ISystemSettingService settingService,
    IDecisionCommentPolicy commentPolicy) : IProposalService
{
    public async Task<ProposalDto> CreateAsync(Guid publicationContainerId, Guid studentId, SaveProposalRequest request, CancellationToken cancellationToken = default)
    {
        var container = await GetOwnedContainerAsync(publicationContainerId, studentId, cancellationToken);
        await EnsureProposalsEditableAsync(container.Id, cancellationToken);

        // The ceiling for a round, checked as each one is written rather than only when the round
        // is sent: a student who has typed a fourth proposal has done the work before being told
        // the institution asks for three.
        var (_, most) = await ProposalsPerRoundAsync(cancellationToken);

        var drafted = await db.ResearchProposals.CountAsync(
            p => p.PublicationContainerId == container.Id && p.Status == ProposalStatus.Draft,
            cancellationToken);

        if (drafted >= most)
        {
            throw new BusinessRuleException(
                $"This round takes at most {most} research {(most == 1 ? "proposal" : "proposals")}. "
                + "Edit one of the ones you have, or remove it, to write another.");
        }

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

        // The size of a round, as the institution has it. A round asked for again is a round like
        // the first, so this is the one check both go through: letting a second one past with
        // fewer would give the supervisor less to choose between than the first was refused for.
        var (fewest, most) = await ProposalsPerRoundAsync(cancellationToken);

        // Having nothing in draft means one of two things, and the count check below can only tell
        // the student the one that is often untrue. Anybody who submitted and then went back, or
        // pressed the button twice, was told they had written no proposals at all while three of
        // theirs sat with a supervisor.
        if (drafts.Count == 0)
        {
            var alreadySent = await db.ResearchProposals.AnyAsync(
                p => p.PublicationContainerId == container.Id
                     && p.Status != ProposalStatus.Draft
                     && p.Status != ProposalStatus.Rejected,
                cancellationToken);

            if (alreadySent)
            {
                throw new BusinessRuleException("These research proposals have already been sent.");
            }
        }

        if (drafts.Count < fewest)
        {
            throw new BusinessRuleException(fewest == 1
                ? "At least one research proposal is required before finishing submission."
                : $"This round asks for at least {fewest} research proposals, so that there is a choice to make. You have {drafts.Count}.");
        }

        if (drafts.Count > most)
        {
            throw new BusinessRuleException(
                $"This round takes at most {most} research {(most == 1 ? "proposal" : "proposals")}. You have {drafts.Count}.");
        }

        // One instant for the whole round. Read inside the loop, DateTime.UtcNow gives each
        // proposal a slightly different value, and a coordinator's queue groups these by student
        // and orders them by date: proposals a student sent together would spread by fractions of
        // a second and, between two students submitting at once, interleave and split a group.
        var submittedAt = DateTime.UtcNow;

        foreach (var proposal in drafts)
        {
            proposal.Status = ProposalStatus.Submitted;
            proposal.SubmittedAt = submittedAt;
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, studentId, "ProposalsSubmitted",
            $"{drafts.Count} research proposal(s) submitted for evaluation.",
            previousStatus: ProposalStatus.Draft.ToString(), newStatus: ProposalStatus.Submitted.ToString());
    }

    public async Task RequestNewSubmissionAsync(
        Guid publicationContainerId, string comments, Guid actingUserId, bool actingAsAdmin = false,
        CancellationToken cancellationToken = default)
    {
        await commentPolicy.EnsureAsync(DecisionPoints.ProposalRequestNewRound, comments, cancellationToken);

        var container = await db.PublicationContainers
            .Include(c => c.Publication)
            .FirstOrDefaultAsync(c => c.Id == publicationContainerId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), publicationContainerId);

        // Asking for a new round throws away every proposal on the publication, including the one
        // the accepted paper was written from. Nobody would then be able to say what the paper had
        // been approved to be about.
        if (SettledPaper.Is(container.Publication?.Status))
        {
            throw new BusinessRuleException(SettledPaper.Message);
        }

        // Whose student this is. Without the check any coordinator could throw away the proposals
        // of a student in another department, which is not a thing the screen offers but was a
        // thing the endpoint allowed. Administrators are exempt, as they are everywhere else.
        if (!actingAsAdmin && container.CoordinatorId != actingUserId)
        {
            throw new ForbiddenException("You are not the Coordinator for this publication.");
        }

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

    /// <summary>
    /// The columns a proposal listing can be ordered by, named as a client sends them. Sorting is
    /// done here rather than on the page the client received, because the oldest proposal in a
    /// department is on the last page and ordering ten rows would never bring it into view.
    /// </summary>
    private static readonly Dictionary<string, Expression<Func<ResearchProposal, object?>>> SortColumns = new()
    {
        ["student"] = p => p.PublicationContainer.Student.LastName,
        ["title"] = p => p.Title,
        ["status"] = p => p.Status,
        ["submitted"] = p => p.SubmittedAt ?? p.CreatedAt,

        // The date supervisors were asked to answer by, which is the one the coordinator's
        // selection screen shows and the one that decides what to chase. A proposal sent more than
        // once has more than one, so the earliest stands for the round: it is the deadline that has
        // passed, or is about to.
        ["due"] = p => p.SupervisorSelections.Min(sel => sel.RespondBy)
    };

    public async Task<PagedResult<ProposalWithInvitationsDto>> GetPendingForCoordinatorAsync(
        Guid coordinatorId, PageRequest page, string? search = null, bool returnedOnly = false,
        CancellationToken cancellationToken = default)
    {
        // Nothing where this institution appoints supervisors directly: the send itself is
        // refused, so a queue of things to send would only be a queue of dead ends.
        if (!(await settingService.GetProposalSettingsAsync(cancellationToken)).SupervisorsExpressInterest)
        {
            return new PagedResult<ProposalWithInvitationsDto>([], page.SafePage, page.SafePageSize, 0);
        }

        var query = ApplySearch(AwaitingDispatch(coordinatorId), search);

        if (returnedOnly) query = query.Where(p => p.ReturnedToDispatchAt != null);

        return await ProjectWithInvitations(query, page).ToPageAsync(page, cancellationToken);
    }

    /// <summary>
    /// Proposals with nobody asked about them yet: submitted, and carrying no invitation at all.
    /// A proposal whose round was discarded loses its invitations, which is what brings it back
    /// into this queue rather than leaving it stranded between two screens.
    ///
    /// Carries the student's name, like the other listings. The dispatch screen groups proposals
    /// by student, and was naming them from a separate page of containers: any student whose
    /// publication fell past the first page of that lookup was headed "Unknown student".
    /// </summary>
    private IQueryable<ResearchProposal> AwaitingDispatch(Guid coordinatorId) =>
        db.ResearchProposals.Where(p => p.PublicationContainer.CoordinatorId == coordinatorId
                                        && p.Status == ProposalStatus.Submitted
                                        && !p.SupervisorSelections.Any());

    public async Task<ReturnedToDispatchSummaryDto> GetReturnedToDispatchSummaryAsync(
        Guid coordinatorId, CancellationToken cancellationToken = default)
    {
        var returned = AwaitingDispatch(coordinatorId).Where(p => p.ReturnedToDispatchAt != null);

        // Students rather than publications: a coordinator reading this wants to know how many
        // people are waiting on them, and one student can have more than one publication open.
        var students = await returned
            .Select(p => p.PublicationContainer.StudentId)
            .Distinct()
            .CountAsync(cancellationToken);

        // The date to offer for the next send, from the institution's own expectation of how long a
        // supervisor takes to answer. That figure existed as an administrator setting and nothing
        // read it; a coordinator typing a date by hand every time was choosing a policy the
        // institution had already chosen.
        var days = await settings.GetIntAsync(
            SettingKeys.SupervisorResponseDays, SettingKeys.DefaultSupervisorResponseDays, cancellationToken);

        return new ReturnedToDispatchSummaryDto(
            students,
            await returned.CountAsync(cancellationToken),
            DateTime.UtcNow.AddDays(days <= 0 ? SettingKeys.DefaultSupervisorResponseDays : days));
    }

    public async Task<PagedResult<ProposalWithInvitationsDto>> GetForCoordinatorAsync(
        Guid coordinatorId, PageRequest page, bool awaitingAllocation = false, string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.ResearchProposals.Where(p => p.PublicationContainer.CoordinatorId == coordinatorId);

        if (awaitingAllocation)
        {
            // Exactly the rows the coordinator's selection screen can do something about. Where
            // supervisors express interest that means somebody has offered; where they do not, no
            // offer is ever made, so it is every submitted proposal instead. Asking for an offer
            // regardless left that screen permanently empty and the stage with no way forward.
            var byInterest = (await settingService.GetProposalSettingsAsync(cancellationToken)).SupervisorsExpressInterest;

            query = query.Where(p => p.PublicationContainer.CurrentPipeline == PipelineStage.ResearchProposals
                                     && p.PublicationContainer.Status != ContainerStatus.Completed
                                     && (byInterest
                                         ? p.SupervisorSelections.Any(s => s.IsSelected)
                                         : p.Status == ProposalStatus.Submitted));
        }

        query = ApplySearch(query, search);

        return await ProjectWithInvitations(query, page).ToPageAsync(page, cancellationToken);
    }

    public async Task<PagedResult<ProposalWithInvitationsDto>> GetInDepartmentAsync(
        Guid headOfDepartmentUserId, PageRequest page, string? search = null,
        CancellationToken cancellationToken = default)
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

        var query = ApplySearch(db.ResearchProposals
            .Where(p => p.PublicationContainer.Student.StudentProfile != null
                        && p.PublicationContainer.Student.StudentProfile.DepartmentId == departmentId), search);

        return await ProjectWithInvitations(query, page).ToPageAsync(page, cancellationToken);
    }

    /// <summary>
    /// One term across everything a reader might have in mind when looking for a row: the student
    /// whose proposal it is, its title, its abstract, and any supervisor who was asked about it.
    /// The abstract is in there because a coordinator often remembers what a proposal was about
    /// rather than what it was called. Separate boxes would make somebody choose which of those
    /// they were remembering before they could start typing.
    /// </summary>
    private static IQueryable<ResearchProposal> ApplySearch(IQueryable<ResearchProposal> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;

        var term = search.Trim();

        return query.Where(p =>
            p.Title.Contains(term)
            || p.Abstract.Contains(term)
            || p.PublicationContainer.Student.FirstName.Contains(term)
            || p.PublicationContainer.Student.LastName.Contains(term)
            || p.SupervisorSelections.Any(s => s.Supervisor.FirstName.Contains(term)
                                               || s.Supervisor.LastName.Contains(term)));
    }

    /// <summary>
    /// One query for the proposals and their invitations together. The invitations are a correlated
    /// collection rather than a request each, which is what the screens built from the per-proposal
    /// endpoint were doing, once per row.
    /// </summary>
    private static IQueryable<ProposalWithInvitationsDto> ProjectWithInvitations(
        IQueryable<ResearchProposal> query, PageRequest page) =>
        query
            .SortBy(page, p => p.CreatedAt, SortColumns, p => p.Id, fallbackDescending: false)
            .ThenBy(p => p.PublicationContainerId)
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
                        s.SelectedAt,
                        s.RespondBy))
                    .ToList(),
                p.ReturnedToDispatchAt,
                // Who the student is, rather than only what they are called. A queue grouped by
                // student shows one heading per publication, so the same name appears twice for
                // anybody with two open, and two people can share a name outright.
                p.PublicationContainer.Student.StudentProfile == null
                    ? null
                    : p.PublicationContainer.Student.StudentProfile.StudentIdNumber,
                p.PublicationContainer.Student.Email));

    public async Task SendToSupervisorsAsync(SendToSupervisorsRequest request, Guid coordinatorId, CancellationToken cancellationToken = default)
    {
        if (!(await settingService.GetProposalSettingsAsync(cancellationToken)).SupervisorsExpressInterest)
        {
            throw new BusinessRuleException(
                "This institution does not send proposals out for supervisors to choose between. "
                + "The coordinator appoints a supervisor directly.");
        }

        await commentPolicy.EnsureAsync(DecisionPoints.ProposalSendToSupervisors, request.Comments, cancellationToken);

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
                        SupervisorId = supervisorId,
                        RespondBy = request.RespondBy
                    });
                }
            }

            // It is out again, so it is no longer waiting for a second try. Left set, the dispatch
            // screen would keep counting it among the ones that came back long after it had gone.
            proposal.ReturnedToDispatchAt = null;

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

    /// <summary>
    /// What a supervisor may order their queue by. The date they have to answer by comes first,
    /// because it is the only one of these that decides what to read next.
    /// </summary>
    private static readonly Dictionary<string, Expression<Func<ResearchProposal, object?>>> InvitedSorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = p => p.Title,
            ["student"] = p => p.PublicationContainer.Student.LastName,
            ["submitted"] = p => p.SubmittedAt
        };

    public async Task<PagedResult<ProposalDto>> GetInvitedProposalsForSupervisorAsync(
        Guid supervisorId, PageRequest paging, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = db.ResearchProposals
            .Where(p => p.Status == ProposalStatus.Submitted
                        && p.SupervisorSelections.Any(s => s.SupervisorId == supervisorId));

        // One term against the two things a supervisor remembers a proposal by. Applied before the
        // page is cut, so it searches the queue rather than the ten rows in hand.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                p.Title.Contains(term)
                || p.PublicationContainer.Student.FirstName.Contains(term)
                || p.PublicationContainer.Student.LastName.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);

        // Answer-by first where nothing was asked for: the one they are closest to running out of
        // time on is the one to read next. Proposals with no date sit after those that have one,
        // which is what the ordering by "has a date" ahead of the date itself does.
        var ordered = paging.SortBy is not null && InvitedSorts.TryGetValue(paging.SortBy, out var key)
            ? (paging.SortDescending ? query.OrderByDescending(key) : query.OrderBy(key)).ThenBy(p => p.Id)
            : query
                .OrderBy(p => p.SupervisorSelections
                    .Where(s => s.SupervisorId == supervisorId)
                    .Select(s => s.RespondBy).FirstOrDefault() == null)
                .ThenBy(p => p.SupervisorSelections
                    .Where(s => s.SupervisorId == supervisorId)
                    .Select(s => s.RespondBy).FirstOrDefault())
                .ThenBy(p => p.SubmittedAt).ThenBy(p => p.Id);

        // Carries the date this supervisor has to answer by. A deadline the person being held to
        // it cannot see is not a deadline, and it is on their own invitation, so it costs nothing.
        var items = await ordered
            .Skip((paging.SafePage - 1) * paging.SafePageSize)
            .Take(paging.SafePageSize)
            .Select(p => new ProposalDto(
                p.Id, p.PublicationContainerId, p.Title, p.Abstract, p.Status.ToString(), p.SubmittedAt,
                p.SupervisorSelections
                    .Where(s => s.SupervisorId == supervisorId)
                    .Select(s => s.RespondBy)
                    .FirstOrDefault(),
                // Whose it is. The queue lets a supervisor search and order by the student, and
                // taking a piece of work on is agreeing to supervise a person.
                p.PublicationContainer.Student.FirstName + " " + p.PublicationContainer.Student.LastName))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProposalDto>(items, paging.SafePage, paging.SafePageSize, total);
    }

    public async Task SelectAsFeasibleAsync(Guid proposalId, Guid supervisorId, SupervisorSelectionRequest request, CancellationToken cancellationToken = default)
    {
        var selection = await db.ProposalSupervisorSelections
            .Include(s => s.Proposal)
            .FirstOrDefaultAsync(s => s.ProposalId == proposalId && s.SupervisorId == supervisorId, cancellationToken)
            ?? throw new ForbiddenException("This proposal was not sent to you for evaluation.");

        await commentPolicy.EnsureAsync(DecisionPoints.ProposalSupervisorSelection, request.Comments, cancellationToken);

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
                s.IsSelected, s.Comments, s.InvitedAt, s.SelectedAt, s.RespondBy))
            .ToListAsync(cancellationToken);
    }

    public async Task AssignSupervisorAsync(Guid proposalId, AssignSupervisorRequest request, Guid coordinatorId, CancellationToken cancellationToken = default)
    {
        await commentPolicy.EnsureAsync(DecisionPoints.ProposalCoordinatorAssign, request.Comments, cancellationToken);

        var proposal = await db.ResearchProposals
            .Include(p => p.PublicationContainer)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken)
            ?? throw new NotFoundException(nameof(ResearchProposal), proposalId);

        if (proposal.PublicationContainer.CoordinatorId != coordinatorId)
        {
            throw new ForbiddenException();
        }

        // Only where the institution asks supervisors first. Where it does not, no offer is ever
        // made and requiring one would leave the coordinator unable to appoint anybody at all.
        var byInterest = (await settingService.GetProposalSettingsAsync(cancellationToken)).SupervisorsExpressInterest;

        var wasSelected = !byInterest || await db.ProposalSupervisorSelections.AnyAsync(
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
        // Whichever of the two this institution runs first. Proposals are always first, because
        // this assignment is what names the supervisor both later stages wait on.
        var order = await settingService.GetPaperWorkflowSettingsAsync(cancellationToken);
        container.CurrentPipeline = order.FirstOfTheTwo;
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
            previousStatus: PipelineStage.ResearchProposals.ToString(), newStatus: container.CurrentPipeline.ToString());

        await notificationService.NotifyAsync(request.SupervisorId, NotificationType.ProposalAccepted,
            "You have been assigned as Supervisor",
            "A research proposal has been assigned to you as Supervisor. Please log in to the system.",
            nameof(PublicationContainer), container.Id, cancellationToken);

        await notificationService.NotifyAsync(container.StudentId, NotificationType.ProposalAccepted,
            "Your research proposal has been accepted",
            "Your research proposal has been accepted and a Supervisor has been assigned. Please log in to the system.",
            nameof(PublicationContainer), container.Id, cancellationToken);
    }

    /// <summary>
    /// An administrator settles the publication on a different one of its proposals.
    ///
    /// The coordinator's own act picks a proposal and a supervisor together, and cannot be redone:
    /// the moment it lands the publication leaves the proposals stage and that screen no longer
    /// lists it. So when the wrong one was chosen, or the student and supervisor agreed between
    /// them to work on another, there was nothing anybody could do and the publication ran to
    /// completion under a title nobody had meant.
    ///
    /// Only which proposal it is. Who supervises stays as it is, and is changed from Assignments,
    /// where changing it is the whole point of the screen.
    /// </summary>
    public async Task ChangeAssignedProposalAsync(
        Guid proposalId, string comments, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        await commentPolicy.EnsureAsync(DecisionPoints.ProposalCoordinatorAssign, comments, cancellationToken);

        var proposal = await db.ResearchProposals
            .Include(p => p.PublicationContainer).ThenInclude(c => c.Publication)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken)
            ?? throw new NotFoundException(nameof(ResearchProposal), proposalId);

        var container = proposal.PublicationContainer;

        if (SettledPaper.Is(container.Publication?.Status))
        {
            throw new BusinessRuleException(SettledPaper.Message);
        }

        if (proposal.Status == ProposalStatus.Assigned)
        {
            throw new BusinessRuleException("This is already the proposal this publication is running on.");
        }

        // Nothing to change while the coordinator has not chosen yet. Doing it here would settle
        // the publication on a proposal without naming a supervisor, which is half of the act and
        // leaves the stage waiting on somebody who does not exist.
        var current = await db.ResearchProposals
            .Where(p => p.PublicationContainerId == container.Id && p.Status == ProposalStatus.Assigned)
            .ToListAsync(cancellationToken);

        if (current.Count == 0)
        {
            throw new BusinessRuleException(
                "No proposal has been assigned on this publication yet. That is the coordinator's to do.");
        }

        var previous = current[0];

        // Exactly one assigned and the rest turned down, which is the shape the coordinator's own
        // assignment leaves behind. The one being stepped down is turned down rather than put back
        // in the set: the set was decided, and this is a correction to which of them won.
        foreach (var stepped in current) stepped.Status = ProposalStatus.Rejected;
        proposal.Status = ProposalStatus.Assigned;
        proposal.UpdatedAt = DateTime.UtcNow;
        container.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, actingAdminId, "AssignedProposalChangedByAdmin",
            $"Changed from '{previous.Title}' to '{proposal.Title}'. {comments}",
            previousStatus: previous.Title, newStatus: proposal.Title);

        // Everyone who was working to the old one. The title on every screen changes underneath
        // them, and being told why is the difference between a correction and a fault.
        var told = new List<Guid> { container.StudentId, container.CoordinatorId };
        if (container.AssignedSupervisorId is { } supervisor) told.Add(supervisor);

        foreach (var person in told.Distinct())
        {
            await notificationService.NotifyAsync(person, NotificationType.ProposalAccepted,
                "This publication is now running on a different proposal",
                $"An administrator has changed it from '{previous.Title}' to '{proposal.Title}'. {comments}",
                nameof(PublicationContainer), container.Id, cancellationToken);
        }
    }

    public async Task<DiscardSelectionsResultDto> DiscardSelectionsAsync(
        Guid proposalId, string comments, Guid coordinatorId, CancellationToken cancellationToken = default)
    {
        await commentPolicy.EnsureAsync(DecisionPoints.ProposalCoordinatorDiscard, comments, cancellationToken);

        var proposal = await db.ResearchProposals
            .Include(p => p.PublicationContainer).ThenInclude(c => c.Student)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken)
            ?? throw new NotFoundException(nameof(ResearchProposal), proposalId);

        var container = proposal.PublicationContainer;

        if (container.CoordinatorId != coordinatorId)
        {
            throw new ForbiddenException("You are not the Coordinator for this proposal.");
        }

        if (proposal.Status == ProposalStatus.Assigned || container.AssignedSupervisorId is not null)
        {
            throw new BusinessRuleException(
                "This publication already has a supervisor, so there is no offer left to refuse.");
        }

        if (container.CurrentPipeline != PipelineStage.ResearchProposals)
        {
            throw new BusinessRuleException("This publication has moved past its research proposals.");
        }

        var hasOffer = await db.ProposalSupervisorSelections
            .AnyAsync(s => s.ProposalId == proposalId && s.IsSelected, cancellationToken);

        if (!hasOffer)
        {
            throw new BusinessRuleException("No supervisor has offered to take this proposal on.");
        }

        var now = DateTime.UtcNow;

        // The offers on this one are refused. The invitations stay: the record of who was asked is
        // worth keeping, and a proposal that was sent out and has nobody willing is exactly what a
        // sent invitation with no offer against it says.
        var refused = await db.ProposalSupervisorSelections
            .Where(s => s.ProposalId == proposalId)
            .ToListAsync(cancellationToken);

        foreach (var invitation in refused)
        {
            invitation.IsSelected = false;
            invitation.SelectedAt = null;
        }

        proposal.Status = ProposalStatus.Submitted;
        proposal.UpdatedAt = now;

        // Whether anything of this student's is still wanted. One proposal of three being turned
        // down is not a student who has to start again: the other two are still live, and the
        // coordinator is choosing between them. Only when nothing at all has a supervisor willing
        // to take it on has the round come to nothing.
        var stillWanted = await db.ProposalSupervisorSelections.AnyAsync(
            s => s.ProposalId != proposalId
                 && s.Proposal.PublicationContainerId == container.Id
                 && s.Proposal.Status != ProposalStatus.Rejected
                 && s.Proposal.Status != ProposalStatus.Assigned
                 && s.IsSelected,
            cancellationToken);

        var returned = 0;

        if (!stillWanted)
        {
            // Nobody wants any of it, so the round is void and all of it goes back together:
            // the ones just refused, and the ones whose supervisors never replied.
            var goingBack = await db.ResearchProposals
                .Where(p => p.PublicationContainerId == container.Id
                            && p.Status != ProposalStatus.Assigned
                            && p.Status != ProposalStatus.Rejected)
                .ToListAsync(cancellationToken);

            var goingBackIds = goingBack.Select(p => p.Id).ToList();

            // Here the invitations do go. A proposal is in the dispatch queue when nobody has been
            // asked about it, so leaving the rows behind would take it off the selection screen
            // without putting it on any other.
            db.ProposalSupervisorSelections.RemoveRange(await db.ProposalSupervisorSelections
                .Where(s => goingBackIds.Contains(s.ProposalId))
                .ToListAsync(cancellationToken));

            foreach (var back in goingBack)
            {
                back.Status = ProposalStatus.Submitted;
                back.ReturnedToDispatchAt = now;
                back.UpdatedAt = now;
            }

            returned = goingBack.Count;
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, coordinatorId, "SupervisorOffersDiscarded",
            comments, newStatus: ProposalStatus.Submitted.ToString());

        return new DiscardSelectionsResultDto(
            $"{container.Student.FirstName} {container.Student.LastName}",
            returned,
            !stillWanted);
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

    /// <summary>
    /// How many proposals make one round, as configured. One place, so the ceiling enforced while
    /// a student writes and the pair enforced when they send cannot come apart.
    /// </summary>
    private async Task<(int Fewest, int Most)> ProposalsPerRoundAsync(CancellationToken cancellationToken)
    {
        var fewest = await settings.GetIntAsync(
            SettingKeys.ProposalsMinimumPerRound, SettingKeys.DefaultProposalsMinimumPerRound, cancellationToken);
        var most = await settings.GetIntAsync(
            SettingKeys.ProposalsMaximumPerRound, SettingKeys.DefaultProposalsMaximumPerRound, cancellationToken);

        // A maximum below the minimum can only come from a setting written outside the screen that
        // validates them. Taken the generous way round rather than refusing every submission.
        return (fewest, Math.Max(fewest, most));
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
