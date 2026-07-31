using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class CommitteeService(
    ApplicationDbContext db,
    IContainerAccessService accessService,
    IAuditService auditService,
    INotificationService notificationService,
    ISystemSettingService settingService) : ICommitteeService
{
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

        var members = await db.Users
            .Where(u => request.MemberUserIds.Contains(u.Id))
            .Include(u => u.CommitteeMemberProfile)
            .ToListAsync(cancellationToken);

        if (members.Any(m => m.CommitteeMemberProfile is null))
        {
            throw new BusinessRuleException(
                "Everyone on a committee must hold a committee member role. Grant it from the user directory first.");
        }

        // Checked as well as the profile, which outlives the role that created it: someone moved
        // off a committee role keeps their profile, and without this would still be assignable.
        var committeeRoleIds = await db.Roles
            .Where(r => RoleNames.CommitteeRoles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var stillCommitteeMembers = await db.UserRoles
            .Where(ur => committeeRoleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken);

        if (members.Any(m => !stillCommitteeMembers.Contains(m.Id)))
        {
            throw new BusinessRuleException(
                "Everyone on a committee must currently hold a committee member role.");
        }

        // A member id the request named but the database does not have would otherwise vanish
        // silently, producing a committee smaller than the administrator believes they built.
        if (members.Count != request.MemberUserIds.Distinct().Count())
        {
            throw new BusinessRuleException("One or more of the people selected could not be found.");
        }

        var minApprovals = await ResolveMinimumApprovalsAsync(
            publication.PublicationContainer, members, request.MinApprovalsRequired, cancellationToken);

        var committee = new Committee
        {
            PublicationId = publicationId,
            MinApprovalsRequired = minApprovals,
            CreatedByUserId = adminId,
            Members = members.Select(m => new CommitteeMember
            {
                UserId = m.Id,
                RoleType = m.CommitteeMemberProfile!.Type
            }).ToList()
        };

        db.Committees.Add(committee);
        publication.Status = PublicationStatus.UnderReview;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(publication.PublicationContainerId, adminId, "CommitteeAssigned", request.Comments);

        foreach (var member in members)
        {
            await notificationService.NotifyAsync(member.Id, NotificationType.CommitteeReviewRequested,
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

        var committee = await db.Committees.Include(c => c.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.PublicationId == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Committee), publicationId);

        return ToDto(committee);
    }

    public async Task<IReadOnlyList<CommitteeDto>> GetAssignmentsForMemberAsync(Guid memberUserId, CancellationToken cancellationToken = default)
    {
        var committees = await db.Committees
            .Include(c => c.Members).ThenInclude(m => m.User)
            .Where(c => c.Members.Any(m => m.UserId == memberUserId))
            .ToListAsync(cancellationToken);

        return committees.Select(ToDto).ToList();
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

    public async Task SetCommitteeConfigAsync(Guid committeeId, SetCommitteeRoleConfigRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        if (!await db.Committees.AnyAsync(c => c.Id == committeeId, cancellationToken))
        {
            throw new NotFoundException(nameof(Committee), committeeId);
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
        committee.Id, committee.PublicationId, committee.Status.ToString(), committee.MinApprovalsRequired,
        committee.Members.Select(m => new CommitteeMemberDto(
            m.UserId, m.User.FirstName + " " + m.User.LastName, m.RoleType.ToString(),
            m.Decision.ToString(), m.DecisionComments, m.DecidedAt)).ToList());

    /// <summary>
    /// Checks the proposed committee against the composition this publication was opened under.
    ///
    /// Read from the container rather than from the settings so that an administrator changing
    /// the rules does not invalidate work already in flight — see PublicationContainer. A
    /// container from before the snapshot existed has nothing recorded, and falls back to what
    /// is configured now.
    /// </summary>
    private async Task<int> ResolveMinimumApprovalsAsync(
        PublicationContainer container,
        IReadOnlyList<ApplicationUser> members,
        int requested,
        CancellationToken cancellationToken)
    {
        await EnsureCompositionMatchesRulesAsync(container, members, cancellationToken);

        // Zero means the administrator did not override it, so the figure this publication was
        // opened under applies.
        if (requested <= 0)
        {
            return container.RequiredCommitteeApprovals
                   ?? (await settingService.GetCommitteeSettingsAsync(cancellationToken)).MinimumApprovals;
        }

        if (requested > members.Count)
        {
            throw new BusinessRuleException(
                $"A committee of {members.Count} cannot be asked for {requested} approvals.");
        }

        return requested;
    }

    private async Task EnsureCompositionMatchesRulesAsync(
        PublicationContainer container, IReadOnlyList<ApplicationUser> members, CancellationToken cancellationToken)
    {
        int requiredInternal, requiredExternal;

        if (container.RequiredInternalCommitteeMembers is { } snapshotInternal &&
            container.RequiredExternalCommitteeMembers is { } snapshotExternal)
        {
            requiredInternal = snapshotInternal;
            requiredExternal = snapshotExternal;
        }
        else
        {
            var current = await settingService.GetCommitteeSettingsAsync(cancellationToken);
            requiredInternal = current.InternalMembers;
            requiredExternal = current.ExternalMembers;
        }

        var actualInternal = members.Count(m => m.CommitteeMemberProfile!.Type == CommitteeMemberRoleType.Internal);
        var actualExternal = members.Count(m => m.CommitteeMemberProfile!.Type == CommitteeMemberRoleType.External);

        if (actualInternal != requiredInternal || actualExternal != requiredExternal)
        {
            throw new BusinessRuleException(
                $"This publication needs a committee of {requiredInternal} internal and {requiredExternal} external " +
                $"members. You have selected {actualInternal} internal and {actualExternal} external.");
        }
    }
}
