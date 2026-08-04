using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class CommitteeService(
    ApplicationDbContext db,
    IContainerAccessService accessService,
    IAuditService auditService,
    INotificationService notificationService,
    ISystemSettingService settingService,
    IDecisionCommentPolicy commentPolicy) : ICommitteeService
{
    public async Task<bool> IsCandidateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // The same three questions AssignAsync asks, about one person. Someone already sitting on
        // a committee is a candidate whatever the rules say now: they were appointed under the
        // rules of the day and still owe a decision, and taking the screen away would strand it.
        if (await db.CommitteeMembers.AnyAsync(m => m.UserId == userId, cancellationToken)) return true;

        var excluded = await settingService.GetExcludedCommitteeUsersAsync(cancellationToken);
        if (excluded.Contains(userId)) return false;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.Status != UserStatus.Enabled) return false;

        var roles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .ToListAsync(cancellationToken);

        var candidateRoles = await settingService.GetCandidateRolesAsync(cancellationToken);
        return roles.Any(candidateRoles.Contains);
    }

    public async Task<IReadOnlyList<CommitteeCandidateDto>> GetCandidatesAsync(CancellationToken cancellationToken = default)
    {
        // Built from the same two settings AssignAsync enforces, so the people offered here are
        // exactly the people it will accept. Availability and account status are checked too, for
        // the same reason: an administrator should not be able to pick somebody the save refuses.
        var candidateRoles = await settingService.GetCandidateRolesAsync(cancellationToken);
        var excluded = await settingService.GetExcludedCommitteeUsersAsync(cancellationToken);

        var people = await db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Enabled && u.IsAvailable && !excluded.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                Roles = db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return people
            .Where(p => p.Roles.Any(candidateRoles.Contains))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Select(p => new CommitteeCandidateDto(
                p.Id, p.FirstName, p.LastName, p.Email ?? string.Empty, p.Roles,
                p.Roles.Contains(RoleNames.ExternalCommitteeMember)))
            .ToList();
    }

    public async Task<CommitteeDto> AssignAsync(Guid publicationId, AssignCommitteeRequest request, Guid adminId, CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.Include(p => p.PublicationContainer)
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        if (publication.Status != PublicationStatus.UnderReview)
        {
            throw new BusinessRuleException("A committee can only be assigned once the Supervisor has approved the paper.");
        }

        // The same condition the administrator's queue is built from, so the list can no longer
        // offer a paper that this then refuses. It used to take the Supervisor's most recent
        // review across every version, which is not the same question: after a resubmission that
        // is an approval of the draft the student has already replaced.
        var latestVersionApproved = await db.Publications
            .Where(p => p.Id == publicationId)
            .WhereLatestVersionApprovedBySupervisor()
            .AnyAsync(cancellationToken);

        if (!latestVersionApproved)
        {
            throw new BusinessRuleException("The Supervisor has not yet approved the current version of this research paper.");
        }

        if (await db.Committees.AnyAsync(c => c.PublicationId == publicationId, cancellationToken))
        {
            throw new ConflictException("A committee has already been assigned to this research paper.");
        }

        // Anyone with a job here may be asked to evaluate a paper. Holding a committee-member role
        // is not the entry ticket: supervisors, coordinators and heads of department are exactly
        // who an institution draws its evaluators from, and requiring an extra role first meant an
        // administrator had to grant one before they could ask anybody.
        var members = await db.Users
            .Where(u => request.MemberUserIds.Contains(u.Id))
            .Select(u => new
            {
                User = u,
                Roles = db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        // Two exclusions are the rule. A committee judges a student's work, so its members cannot be
        // drawn from the people whose work is being judged. And Staff is the placeholder an
        // institutional account holds until an administrator says what it is: it is not a job, so
        // there is nobody there to ask yet.
        var ineligible = members
            .Where(m => !m.Roles.Any(RoleNames.CommitteeEligible.Contains))
            .ToList();

        if (ineligible.Count > 0)
        {
            var reason = ineligible.Any(m => m.Roles.Contains(RoleNames.Student))
                ? "A committee cannot include students: it judges their work."
                : "Somebody chosen has no role here yet. Give them one first, and they can be appointed.";

            throw new BusinessRuleException(reason);
        }

        // Within that, an administrator says which roles this institution draws on and who to leave
        // out. Checked here rather than only on the screen that lists candidates: the screen is a
        // convenience, and a rule that lives only in a list is not a rule.
        var candidateRoles = await settingService.GetCandidateRolesAsync(cancellationToken);
        var notCandidates = members.Where(m => !m.Roles.Any(candidateRoles.Contains)).ToList();

        if (notCandidates.Count > 0)
        {
            throw new BusinessRuleException(
                "Somebody chosen holds a role that this institution does not draw committees from. "
                + "An administrator sets which roles those are in System settings.");
        }

        var excluded = await settingService.GetExcludedCommitteeUsersAsync(cancellationToken);
        if (members.Any(m => excluded.Contains(m.User.Id)))
        {
            throw new BusinessRuleException(
                "Somebody chosen has been left out of committee work by an administrator.");
        }

        if (members.Any(m => m.User.Status != UserStatus.Enabled))
        {
            throw new BusinessRuleException("Everyone on a committee must have an enabled account.");
        }

        // Refused rather than filtered out silently: the administrator chose these people, and a
        // committee quietly built one member short is worse than being told why. Not the same as
        // a disabled account either, so the message says which it is.
        var unavailable = members.Where(m => !m.User.IsAvailable).ToList();
        if (unavailable.Count > 0)
        {
            var names = string.Join(", ", unavailable.Select(m => $"{m.User.FirstName} {m.User.LastName}"));
            throw new BusinessRuleException(
                $"Not taking new work on at the moment: {names}. Choose somebody else, or ask them to mark "
                + "themselves available.");
        }

        // A member id the request named but the database does not have would otherwise vanish
        // silently, producing a committee smaller than the administrator believes they built.
        if (members.Count != request.MemberUserIds.Distinct().Count())
        {
            throw new BusinessRuleException("One or more of the people selected could not be found.");
        }

        var isExternal = members
            .Select(m => m.Roles.Contains(RoleNames.ExternalCommitteeMember))
            .ToList();

        var container = publication.PublicationContainer;

        await commentPolicy.EnsureAsync(request.OverrideComposition
            ? DecisionPoints.PaperCommitteeAssignOverride
            : DecisionPoints.PaperCommitteeAssign, request.Comments, cancellationToken);

        // Read before the override rewrites them, so the history can say what was set aside.
        var (wasReviewers, wasExternal) = await ResolveRequiredCompositionAsync(container, cancellationToken);

        var minApprovals = await ResolveMinimumApprovalsAsync(
            container, isExternal, request.MinApprovalsRequired, request.OverrideComposition, cancellationToken);

        var appointedExternal = isExternal.Count(external => external);
        var appointedReviewers = isExternal.Count - appointedExternal;

        // What was actually appointed becomes what this publication is judged by. Everything
        // downstream reads these figures, so leaving them at the old ones would describe a
        // committee that does not exist.
        if (request.OverrideComposition)
        {
            container.RequiredReviewerMembers = appointedReviewers;
            container.RequiredExternalCommitteeMembers = appointedExternal;
            container.RequiredCommitteeApprovals = minApprovals;
            container.UpdatedAt = DateTime.UtcNow;
        }

        var committee = new Committee
        {
            PublicationId = publicationId,
            MinApprovalsRequired = minApprovals,
            CreatedByUserId = adminId,
            // External means from outside the institution, which is a fact about the person rather
            // than a choice made per committee, and the only people outside it are the ones invited
            // as external members. Everybody else is a reviewer by definition.
            Members = members.Select(m => new CommitteeMember
            {
                UserId = m.User.Id,
                RoleType = m.Roles.Contains(RoleNames.ExternalCommitteeMember)
                    ? CommitteeMemberRoleType.External
                    : CommitteeMemberRoleType.Reviewer
            }).ToList()
        };

        db.Committees.Add(committee);
        publication.Status = PublicationStatus.UnderReview;
        await db.SaveChangesAsync(cancellationToken);

        // Named as its own event when the shape changed, and carrying both compositions, because
        // "why does this one have two reviewers when the rule says three" is a question asked
        // months later by somebody who was not in the room.
        await auditService.LogActivityAsync(publication.PublicationContainerId, adminId,
            request.OverrideComposition ? "CommitteeAssignedWithDifferentComposition" : "CommitteeAssigned",
            request.OverrideComposition
                ? $"Appointed {appointedReviewers} reviewers and {appointedExternal} external, in place of "
                  + $"the {wasReviewers} and {wasExternal} this publication was opened under. {request.Comments}"
                : request.Comments);

        foreach (var member in members)
        {
            await notificationService.NotifyAsync(member.User.Id, NotificationType.CommitteeReviewRequested,
                "Research paper awaiting your evaluation",
                "You have been assigned to an evaluation committee. Please log in to review the research paper.",
                nameof(Committee), committee.Id, cancellationToken);
        }

        return await GetByPublicationAsync(publicationId, adminId, cancellationToken);
    }

    public async Task<CommitteeDto> GetByPublicationAsync(Guid publicationId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.FindAsync([publicationId], cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        await accessService.EnsureAccessAsync(publication.PublicationContainerId, requestingUserId);

        var committee = await db.Committees
            .Include(c => c.Publication).ThenInclude(p => p.Keywords)
            // The author, so the queue can name whose paper each assignment is. It already lets a
            // member search and order by the student.
            .Include(c => c.Publication).ThenInclude(p => p.PublicationContainer).ThenInclude(pc => pc.Student)
            .Include(c => c.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.PublicationId == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Committee), publicationId);

        return ToDto(committee);
    }

    /// <summary>What a committee member may order their queue by.</summary>
    private static readonly Dictionary<string, Expression<Func<Committee, object?>>> AssignmentSorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = c => c.Publication.Title,
            ["student"] = c => c.Publication.PublicationContainer.Student.LastName,
            ["submitted"] = c => c.CreatedAt
        };

    public async Task<PagedResult<CommitteeDto>> GetAssignmentsForMemberAsync(
        Guid memberUserId, PageRequest page, string? search = null, CancellationToken cancellationToken = default)
    {
        var filtered = db.Committees.Where(c => c.Members.Any(m => m.UserId == memberUserId));

        // One term across the paper and its author, applied before the page is cut so it searches
        // everything this person has been asked to evaluate rather than the rows in hand.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(c =>
                c.Publication.Title.Contains(term)
                || c.Publication.PublicationContainer.Student.FirstName.Contains(term)
                || c.Publication.PublicationContainer.Student.LastName.Contains(term));
        }

        // The ones still needing this person's vote first: that is what they came for. An explicit
        // ordering replaces that, since somebody who asks for a column has said what they want.
        var query = page.SortBy is not null && AssignmentSorts.TryGetValue(page.SortBy, out var key)
            ? page.SortDescending ? filtered.OrderByDescending(key) : filtered.OrderBy(key)
            : filtered
                .OrderBy(c => c.Members.Any(m => m.UserId == memberUserId && m.Decision == CommitteeMemberDecision.Pending) ? 0 : 1)
                .ThenByDescending(c => c.CreatedAt);

        var total = await query.CountAsync(cancellationToken);

        var committees = await query
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .Include(c => c.Publication).ThenInclude(p => p.Keywords)
            // The author, so the queue can name whose paper each assignment is. It already lets a
            // member search and order by the student, and named nobody.
            .Include(c => c.Publication).ThenInclude(p => p.PublicationContainer).ThenInclude(pc => pc.Student)
            .Include(c => c.Members).ThenInclude(m => m.User)
            .ToListAsync(cancellationToken);

        return new PagedResult<CommitteeDto>(
            committees.Select(ToDto).ToList(), page.SafePage, page.SafePageSize, total);
    }

    public async Task MemberReviewAsync(Guid committeeId, Guid memberUserId, CommitteeMemberReviewRequest request, CancellationToken cancellationToken = default)
    {
        var committee = await db.Committees
            .Include(c => c.Members)
            .Include(c => c.Publication).ThenInclude(p => p.PublicationContainer)
            .FirstOrDefaultAsync(c => c.Id == committeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Committee), committeeId);

        var member = committee.Members.FirstOrDefault(m => m.UserId == memberUserId)
            ?? throw new ForbiddenException("You are not a member of this committee.");

        if (member.Decision != CommitteeMemberDecision.Pending)
        {
            throw new ConflictException("You have already submitted your decision for this committee.");
        }

        await commentPolicy.EnsureAsync(request.Approve
            ? DecisionPoints.PaperCommitteeApprove
            : DecisionPoints.PaperCommitteeReject, request.Comments, cancellationToken);

        member.Decision = request.Approve ? CommitteeMemberDecision.Approve : CommitteeMemberDecision.Reject;
        member.DecisionComments = request.Comments;
        member.DecidedAt = DateTime.UtcNow;

        var latestVersion = await db.PublicationVersions
            .Where(v => v.PublicationId == committee.PublicationId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync(cancellationToken);

        db.Reviews.Add(new Review
        {
            PublicationVersionId = latestVersion.Id,
            ReviewerUserId = memberUserId,
            ReviewerType = ReviewerType.CommitteeMember,
            Decision = request.Approve ? ReviewDecision.Approve : ReviewDecision.Reject,
            Comments = request.Comments
        });

        await auditService.LogActivityAsync(committee.Publication.PublicationContainerId, memberUserId,
            "CommitteeMemberReview", request.Comments);

        var allDecided = committee.Members.All(m => m.Decision != CommitteeMemberDecision.Pending);
        if (allDecided)
        {
            committee.Status = CommitteeStatus.Completed;
            await db.SaveChangesAsync(cancellationToken);

            await notificationService.NotifyAsync(committee.Publication.PublicationContainer.CoordinatorId,
                NotificationType.CommitteeFinalReviewRequested,
                "Committee review complete",
                "All committee members have submitted their decisions. Please log in to make the final decision on this research paper.",
                nameof(PublicationContainer), committee.Publication.PublicationContainerId, cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CommitteeRoleConfigDto>> GetDefaultConfigAsync(CancellationToken cancellationToken = default)
    {
        return await db.CommitteeRoleConfigs
            .Where(c => c.CommitteeId == null)
            .Select(c => new CommitteeRoleConfigDto(c.CommitteeId, c.RoleType.ToString(), c.RequiredCount))
            .ToListAsync(cancellationToken);
    }

    public async Task SetDefaultConfigAsync(SetCommitteeRoleConfigRequest request, CancellationToken cancellationToken = default)
    {
        await UpsertRoleConfigAsync(null, request, cancellationToken);
    }

    public async Task<IReadOnlyList<CommitteeRoleConfigDto>> GetCommitteeConfigAsync(Guid committeeId, CancellationToken cancellationToken = default)
    {
        return await db.CommitteeRoleConfigs
            .Where(c => c.CommitteeId == committeeId)
            .Select(c => new CommitteeRoleConfigDto(c.CommitteeId, c.RoleType.ToString(), c.RequiredCount))
            .ToListAsync(cancellationToken);
    }

    public async Task SetCommitteeConfigAsync(
        Guid committeeId, SetCommitteeRoleConfigRequest request, Guid actingUserId, bool actingAsAdmin = false,
        CancellationToken cancellationToken = default)
    {
        var committee = await db.Committees
            .Include(c => c.Publication).ThenInclude(p => p.PublicationContainer)
            .FirstOrDefaultAsync(c => c.Id == committeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Committee), committeeId);

        // Whose committee this is. The acting user was already being passed in and then ignored,
        // so a coordinator could rewrite the make-up of a committee sitting on a publication in
        // another department. Administrators set the rules for the institution; a coordinator sets
        // them for their own students.
        if (!actingAsAdmin && committee.Publication.PublicationContainer.CoordinatorId != actingUserId)
        {
            throw new ForbiddenException("You are not the Coordinator for this publication.");
        }

        await UpsertRoleConfigAsync(committeeId, request, cancellationToken);
    }

    private async Task UpsertRoleConfigAsync(Guid? committeeId, SetCommitteeRoleConfigRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CommitteeMemberRoleType>(request.RoleType, true, out var roleType))
        {
            throw new BusinessRuleException($"'{request.RoleType}' is not a recognised committee role type.");
        }

        var config = await db.CommitteeRoleConfigs
            .FirstOrDefaultAsync(c => c.CommitteeId == committeeId && c.RoleType == roleType, cancellationToken);

        if (config is null)
        {
            db.CommitteeRoleConfigs.Add(new CommitteeRoleConfig
            {
                CommitteeId = committeeId,
                RoleType = roleType,
                RequiredCount = request.RequiredCount
            });
        }
        else
        {
            config.RequiredCount = request.RequiredCount;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static CommitteeDto ToDto(Committee committee) => new(
        committee.Id, committee.PublicationId,
        committee.Publication is null
            ? null
            : new CommitteePaperDto(
                committee.Publication.Id,
                committee.Publication.Title,
                committee.Publication.Abstract,
                committee.Publication.PublicationYear,
                committee.Publication.Keywords.Select(k => k.Name).ToList(),
                // Null where the author was not loaded, which is every caller that does not need
                // it. The assignment listing does, and includes them.
                committee.Publication.PublicationContainer?.Student is null
                    ? null
                    : committee.Publication.PublicationContainer.Student.FirstName
                      + " " + committee.Publication.PublicationContainer.Student.LastName),
        committee.Status.ToString(), committee.MinApprovalsRequired,
        committee.Members.Select(m => new CommitteeMemberDto(
            m.UserId, m.User.FirstName + " " + m.User.LastName, m.RoleType.ToString(),
            m.Decision.ToString(), m.DecisionComments, m.DecidedAt)).ToList());

    /// <summary>
    /// Checks the proposed committee against the composition this publication was opened under.
    ///
    /// Read from the container rather than from the settings so that an administrator changing the
    /// rules does not invalidate work already in flight. See PublicationContainer. A container from
    /// before the snapshot existed has nothing recorded, and falls back to what is configured now.
    /// </summary>
    private async Task<int> ResolveMinimumApprovalsAsync(
        PublicationContainer container,
        IReadOnlyList<bool> membersAreExternal,
        int requested,
        bool overrideComposition,
        CancellationToken cancellationToken)
    {
        if (!overrideComposition)
        {
            await EnsureCompositionMatchesRulesAsync(container, membersAreExternal, cancellationToken);
        }

        // Zero means the administrator did not override it, so the figure this publication was
        // opened under applies.
        if (requested <= 0)
        {
            return container.RequiredCommitteeApprovals
                   ?? (await settingService.GetCommitteeSettingsAsync(cancellationToken)).MinimumApprovals;
        }

        if (requested > membersAreExternal.Count)
        {
            throw new BusinessRuleException(
                $"A committee of {membersAreExternal.Count} cannot be asked for {requested} approvals.");
        }

        return requested;
    }

    /// <param name="membersAreExternal">
    /// One entry per proposed member: true where they come from outside the institution. Passed as
    /// the answer rather than as the people, because who counts as external is decided once, where
    /// the committee is built, and this only has to count them.
    /// </param>
    private async Task EnsureCompositionMatchesRulesAsync(
        PublicationContainer container, IReadOnlyList<bool> membersAreExternal, CancellationToken cancellationToken)
    {
        var (requiredReviewers, requiredExternal) = await ResolveRequiredCompositionAsync(container, cancellationToken);

        var actualExternal = membersAreExternal.Count(isExternal => isExternal);
        var actualReviewers = membersAreExternal.Count - actualExternal;

        if (actualReviewers != requiredReviewers || actualExternal != requiredExternal)
        {
            throw new BusinessRuleException(
                $"This publication needs a committee of {requiredReviewers} reviewers and {requiredExternal} external " +
                $"members. You have selected {actualReviewers} reviewers and {actualExternal} external. To appoint a "
                + "different composition for this publication, say why.");
        }
    }

    /// <summary>
    /// The composition this publication is judged by: the figures recorded when it was opened, or
    /// what is configured now for a container from before those were kept.
    /// </summary>
    private async Task<(int Reviewers, int External)> ResolveRequiredCompositionAsync(
        PublicationContainer container, CancellationToken cancellationToken)
    {
        if (container.RequiredReviewerMembers is { } reviewers &&
            container.RequiredExternalCommitteeMembers is { } external)
        {
            return (reviewers, external);
        }

        var current = await settingService.GetCommitteeSettingsAsync(cancellationToken);
        return (current.ReviewerMembers, current.ExternalMembers);
    }
}
