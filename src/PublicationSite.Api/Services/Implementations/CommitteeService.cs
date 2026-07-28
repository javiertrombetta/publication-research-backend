using Microsoft.EntityFrameworkCore;
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
    INotificationService notificationService) : ICommitteeService
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

        var latestVersionApproved = await db.Reviews
            .Where(r => r.PublicationVersion.PublicationId == publicationId && r.ReviewerType == ReviewerType.Supervisor)
            .OrderByDescending(r => r.ReviewedAt)
            .Select(r => r.Decision == ReviewDecision.Approve)
            .FirstOrDefaultAsync(cancellationToken);

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
            throw new BusinessRuleException("All committee members must have a Committee Member profile.");
        }

        var committee = new Committee
        {
            PublicationId = publicationId,
            MinApprovalsRequired = request.MinApprovalsRequired,
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
}
