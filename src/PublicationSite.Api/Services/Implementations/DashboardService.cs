using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Dashboard;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class DashboardService(ApplicationDbContext db) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var totalContainers = await db.PublicationContainers.CountAsync(cancellationToken);
        var inProgress = await db.PublicationContainers.CountAsync(c => c.Status == ContainerStatus.InProgress, cancellationToken);
        var completed = await db.PublicationContainers.CountAsync(c => c.Status == ContainerStatus.Completed, cancellationToken);

        var byPipeline = await db.PublicationContainers
            .GroupBy(c => c.CurrentPipeline)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Stage.ToString(), x => x.Count, cancellationToken);

        var byPublicationStatus = await db.Publications
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status.ToString(), x => x.Count, cancellationToken);

        var publishedCount = await db.Publications.CountAsync(p => p.IsPublished, cancellationToken);

        var byEthicsStatus = await db.EthicsApprovals
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status.ToString(), x => x.Count, cancellationToken);

        var pendingCommitteeReviews = await db.CommitteeMembers.CountAsync(m => m.Decision == CommitteeMemberDecision.Pending, cancellationToken);
        var completedCommitteeReviews = await db.CommitteeMembers.CountAsync(m => m.Decision != CommitteeMemberDecision.Pending, cancellationToken);

        var byReviewDecision = await db.Reviews
            .GroupBy(r => r.Decision)
            .Select(g => new { Decision = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Decision.ToString(), x => x.Count, cancellationToken);

        return new DashboardSummaryDto(
            totalContainers, inProgress, completed, byPipeline,
            byPublicationStatus, publishedCount, byEthicsStatus,
            pendingCommitteeReviews, completedCommitteeReviews, byReviewDecision);
    }
}
