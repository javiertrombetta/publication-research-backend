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
        // Every figure here is a round trip, and on a hosted database round trips are what the
        // page costs — so each table is read once and the figures are separated afterwards. This
        // was ten queries asking six questions: three separate COUNTs over the containers' status
        // column, two more asking opposite halves of the same question about committee decisions,
        // and a count of published papers beside a grouping that had already visited every row.
        var containers = await db.PublicationContainers
            .GroupBy(c => new { c.Status, c.CurrentPipeline })
            .Select(g => new { g.Key.Status, g.Key.CurrentPipeline, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalContainers = containers.Sum(c => c.Count);
        var inProgress = containers.Where(c => c.Status == ContainerStatus.InProgress).Sum(c => c.Count);
        var completed = containers.Where(c => c.Status == ContainerStatus.Completed).Sum(c => c.Count);

        var byPipeline = containers
            .GroupBy(c => c.CurrentPipeline.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Count));

        var publications = await db.Publications
            .GroupBy(p => new { p.Status, p.IsPublished })
            .Select(g => new { g.Key.Status, g.Key.IsPublished, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byPublicationStatus = publications
            .GroupBy(p => p.Status.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Count));

        // Read from IsPublished rather than from the Published status: an administrator can
        // withdraw a paper from the catalogue, which clears the flag and leaves the status alone.
        var publishedCount = publications.Where(p => p.IsPublished).Sum(p => p.Count);

        var byEthicsStatus = await db.EthicsApprovals
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status.ToString(), x => x.Count, cancellationToken);

        var byCommitteeDecision = await db.CommitteeMembers
            .GroupBy(m => m.Decision)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var pendingCommitteeReviews = byCommitteeDecision.GetValueOrDefault(CommitteeMemberDecision.Pending);
        var completedCommitteeReviews = byCommitteeDecision
            .Where(entry => entry.Key != CommitteeMemberDecision.Pending)
            .Sum(entry => entry.Value);

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
