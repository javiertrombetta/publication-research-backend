using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

/// <summary>
/// Closes rounds whose answer-by date has passed.
///
/// A coordinator can say when they want supervisors to have decided. Nothing enforced that date:
/// it sat in the database while the proposals waited on people who were never going to reply, and
/// the coordinator had to notice for themselves. This is what makes the date mean something.
///
/// The rule is the same one the coordinator applies by hand when they turn every offer down: a
/// student with nothing anybody offered to take on goes back to the dispatch queue, where the
/// coordinator either sends them to different supervisors or asks for new proposals. A student
/// with even one offer is left alone. That decision is theirs, not a timer's.
/// </summary>
public class ExpiredProposalRoundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpiredProposalRoundService> logger) : BackgroundService
{
    /// <summary>
    /// Often enough that a date set to the hour is acted on within the hour, rarely enough that an
    /// idle installation is not running a query every few seconds. The work is one query that
    /// usually matches nothing.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A first pass on the way up, so a deadline that passed while the service was down is
        // acted on when it comes back rather than at the end of the next interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var closed = await CloseExpiredRoundsAsync(stoppingToken);
                if (closed > 0)
                {
                    logger.LogInformation(
                        "Closed {Count} research proposal round(s) whose answer-by date had passed.", closed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Swallowed deliberately: a failed sweep is a round closed late, and throwing here
                // would take the background service down for the lifetime of the process.
                logger.LogError(ex, "Could not close expired research proposal rounds. Trying again shortly.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <returns>How many students were sent back to the dispatch queue.</returns>
    private async Task<int> CloseExpiredRoundsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var now = DateTime.UtcNow;

        // Publications still choosing a supervisor, where somebody was asked by a date that has
        // gone, and where nobody offered to take anything on.
        var expired = await db.PublicationContainers
            .Where(c => c.CurrentPipeline == PipelineStage.ResearchProposals
                        && c.Status != ContainerStatus.Completed
                        && c.AssignedSupervisorId == null
                        && c.Proposals.Any(p => p.SupervisorSelections.Any(s => s.RespondBy != null
                                                                                && s.RespondBy <= now))
                        && !c.Proposals.Any(p => p.Status != ProposalStatus.Rejected
                                                 && p.SupervisorSelections.Any(s => s.IsSelected)))
            .Select(c => new { c.Id, c.CoordinatorId, c.StudentId })
            .ToListAsync(cancellationToken);

        if (expired.Count == 0) return 0;

        foreach (var container in expired)
        {
            var goingBack = await db.ResearchProposals
                .Where(p => p.PublicationContainerId == container.Id
                            && p.Status != ProposalStatus.Assigned
                            && p.Status != ProposalStatus.Rejected)
                .ToListAsync(cancellationToken);

            if (goingBack.Count == 0) continue;

            var ids = goingBack.Select(p => p.Id).ToList();

            // The invitations go with them. The question they carried has expired, and a proposal
            // is in the dispatch queue when nobody has been asked about it.
            db.ProposalSupervisorSelections.RemoveRange(await db.ProposalSupervisorSelections
                .Where(s => ids.Contains(s.ProposalId))
                .ToListAsync(cancellationToken));

            foreach (var proposal in goingBack)
            {
                proposal.Status = ProposalStatus.Submitted;
                proposal.ReturnedToDispatchAt = now;
                proposal.UpdatedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);

            await audit.LogActivityAsync(container.Id, container.CoordinatorId, "ProposalRoundExpired",
                $"The date supervisors had to answer by has passed and nobody offered to supervise, "
                + $"so {goingBack.Count} proposal(s) went back to the dispatch queue.",
                newStatus: ProposalStatus.Submitted.ToString());

            // The coordinator is told, because this happened without them doing anything and the
            // next move is theirs: send to different supervisors, or ask for new proposals.
            await notifications.NotifyAsync(container.CoordinatorId, NotificationType.ProposalNotAccepted,
                "A research proposal round has run out of time",
                "Nobody offered to supervise before the date you set, so these proposals are back "
                + "in Send proposals. Please log in to the system.",
                nameof(PublicationContainer), container.Id, cancellationToken);
        }

        return expired.Count;
    }
}
