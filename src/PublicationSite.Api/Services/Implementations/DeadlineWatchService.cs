using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

/// <summary>
/// Reminds people before a deadline runs out, and says so afterwards where nothing else does.
///
/// Three deadlines are configurable, and each gets a reminder a set number of days beforehand, to
/// whoever can still meet it. What happens when one passes differs. The answer-by date on a round
/// of proposals has its own service, because missing it changes the work: the students nobody
/// offered to take on go back to the coordinator. The other two change nothing. An ethics review
/// that has run out of time is still the supervisor's to do, and a committee that has not finished
/// voting is still the committee's, so what was missing was anybody being told, and a coordinator
/// only found out by going looking.
///
/// Everything here is said once. The marks on the approval, the committee and the invitation
/// record what has already been reported, and the first two are cleared whenever the work moves
/// on, so a second round of the same step warns again rather than staying silent because the first
/// one did.
/// </summary>
public class DeadlineWatchService(
    IServiceScopeFactory scopeFactory,
    ILogger<DeadlineWatchService> logger) : BackgroundService
{
    /// <summary>
    /// The same cadence as the round-closing sweep, and for the same reason: a deadline set to the
    /// hour is acted on within the hour, and an idle installation is not querying every few
    /// seconds. Each pass is a handful of queries that usually match nothing.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Swallowed deliberately, as in the round-closing sweep: a failed pass is a late
                // reminder, and throwing here would take the service down for the life of the
                // process and stop every later pass too.
                logger.LogError(ex, "Could not sweep for overdue work. Trying again shortly.");
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

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var settingService = scope.ServiceProvider.GetRequiredService<ISystemSettingService>();

        var deadlines = await settingService.GetDeadlineSettingsAsync(cancellationToken);
        var workflow = await settingService.GetEthicsWorkflowSettingsAsync(cancellationToken);

        await WatchProposalRoundsAsync(db, notifications, deadlines, cancellationToken);
        await WatchEthicsAsync(db, notifications, deadlines, workflow, cancellationToken);
        await WatchCommitteesAsync(db, notifications, deadlines, cancellationToken);
    }

    /// <summary>
    /// Supervisors whose answer-by date is close.
    ///
    /// Only a warning. What happens when the date passes belongs to ExpiredProposalRoundService,
    /// which sends the students nobody wanted back to their coordinator. A supervisor is written to
    /// once however many proposals they were sent, because being told the same thing four times is
    /// how a person learns to ignore it.
    /// </summary>
    internal static async Task WatchProposalRoundsAsync(
        ApplicationDbContext db, INotificationService notifications,
        DTOs.Settings.DeadlineSettingsDto deadlines, CancellationToken cancellationToken)
    {
        if (deadlines.SupervisorResponseWarningDays <= 0) return;

        var now = DateTime.UtcNow;
        var warnBefore = now.AddDays(deadlines.SupervisorResponseWarningDays);

        var dueSoon = await db.ProposalSupervisorSelections
            .Where(s => !s.IsSelected
                        && s.SelectedAt == null
                        && s.DueSoonWarnedAt == null
                        && s.RespondBy != null
                        && s.RespondBy > now
                        && s.RespondBy <= warnBefore)
            .ToListAsync(cancellationToken);

        if (dueSoon.Count == 0) return;

        foreach (var group in dueSoon.GroupBy(s => s.SupervisorId))
        {
            var waiting = group.Count();

            foreach (var selection in group) selection.DueSoonWarnedAt = now;

            await notifications.NotifyAsync(group.Key, NotificationType.ProposalResponseDueSoon,
                "Research proposals are waiting on you",
                $"{waiting} research proposal(s) are waiting on you and the date you were asked to answer by is "
                + $"within {deadlines.SupervisorResponseWarningDays} day(s). Proposals nobody offers to supervise "
                + "go back to the coordinator when it passes. Please log in to say which you would take on.",
                cancellationToken: cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Ethics reviews that have run out of time, and those about to.
    ///
    /// The coordinator is told when one expires, because they are the person who can move it: they
    /// can chase the supervisor, or an administrator can put the review to somebody else. Whoever
    /// owes the review is the one reminded beforehand, since they are the only person who can
    /// still meet it.
    /// </summary>
    internal static async Task WatchEthicsAsync(
        ApplicationDbContext db, INotificationService notifications,
        DTOs.Settings.DeadlineSettingsDto deadlines, DTOs.Settings.EthicsWorkflowSettingsDto workflow,
        CancellationToken cancellationToken)
    {
        if (deadlines.EthicsReviewDays <= 0) return;

        var now = DateTime.UtcNow;
        var overdueBefore = now.AddDays(-deadlines.EthicsReviewDays);
        var warnBefore = deadlines.EthicsReviewWarningDays > 0
            ? now.AddDays(deadlines.EthicsReviewWarningDays - deadlines.EthicsReviewDays)
            : (DateTime?)null;

        // Only what is genuinely waiting on somebody: an approval that has closed owes nothing.
        var open = await db.EthicsApprovals
            .Include(a => a.PublicationContainer)
            .Where(a => a.FinalDecisionAt == null
                        && a.StepEnteredAt != null
                        && a.PublicationContainer.Status != ContainerStatus.Completed
                        && (a.Status == EthicsStatus.PendingSupervisorDecision
                            || a.Status == EthicsStatus.PendingVerification))
            .ToListAsync(cancellationToken);

        foreach (var approval in open)
        {
            var container = approval.PublicationContainer;
            var owedBySupervisor = OwedBySupervisor(approval, workflow);

            if (approval.StepEnteredAt <= overdueBefore && approval.OverdueReportedAt is null)
            {
                approval.OverdueReportedAt = now;

                await notifications.NotifyAsync(container.CoordinatorId, NotificationType.EthicsReviewOverdue,
                    "An ethics review has run out of time",
                    owedBySupervisor
                        ? $"The {deadlines.EthicsReviewDays}-day ethics review period has passed and the supervisor "
                          + "has not acted on this publication. Please log in to see where it stands."
                        : $"The {deadlines.EthicsReviewDays}-day ethics review period has passed on this publication "
                          + "and it is still waiting on a decision. Please log in to see where it stands.",
                    nameof(PublicationContainer), container.Id, cancellationToken);

                continue;
            }

            if (warnBefore is { } dueSoon
                && approval.StepEnteredAt <= dueSoon
                && approval.StepEnteredAt > overdueBefore
                && approval.DueSoonWarnedAt is null)
            {
                approval.DueSoonWarnedAt = now;

                // Whoever owes it. The supervisor where it is theirs; otherwise the coordinator,
                // who owns every other step of this stage bar the Head of Department's.
                var owedBy = owedBySupervisor && container.AssignedSupervisorId is { } supervisor
                    ? supervisor
                    : container.CoordinatorId;

                await notifications.NotifyAsync(owedBy, NotificationType.EthicsReviewDueSoon,
                    "An ethics review is due soon",
                    $"An ethics review is waiting on you and its {deadlines.EthicsReviewDays}-day period runs out in "
                    + $"{deadlines.EthicsReviewWarningDays} day(s). Please log in to deal with it.",
                    nameof(PublicationContainer), container.Id, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Whether the ethics stage is waiting on the supervisor rather than on somebody else.</summary>
    internal static bool OwedBySupervisor(EthicsApproval approval, DTOs.Settings.EthicsWorkflowSettingsDto workflow)
    {
        if (approval.Status == EthicsStatus.PendingSupervisorDecision) return true;

        return approval.Status == EthicsStatus.PendingVerification
               && workflow.SupervisorReviewsDocuments
               && approval.SupervisorDocumentsReviewedAt is null
               && (!workflow.CoordinatorReadsFirst
                   || !workflow.CoordinatorReviewsDocuments
                   || approval.CoordinatorDecisionAt is not null);
    }

    /// <summary>
    /// Committees that have run out of time, and those about to.
    ///
    /// The coordinator is told who has not decided, by name. "The committee is late" is not
    /// something anybody can act on; "these two have not voted" is. The members who owe a vote are
    /// the ones reminded beforehand, and only those: the ones who have already voted are done.
    /// </summary>
    internal static async Task WatchCommitteesAsync(
        ApplicationDbContext db, INotificationService notifications,
        DTOs.Settings.DeadlineSettingsDto deadlines, CancellationToken cancellationToken)
    {
        if (deadlines.CommitteeReviewDays <= 0) return;

        var now = DateTime.UtcNow;
        var overdueBefore = now.AddDays(-deadlines.CommitteeReviewDays);
        var warnBefore = deadlines.CommitteeReviewWarningDays > 0
            ? now.AddDays(deadlines.CommitteeReviewWarningDays - deadlines.CommitteeReviewDays)
            : (DateTime?)null;

        var open = await db.Committees
            .Include(c => c.Members).ThenInclude(m => m.User)
            .Include(c => c.Publication).ThenInclude(p => p.PublicationContainer)
            .Where(c => c.Status != CommitteeStatus.Completed
                        && c.Publication.PublicationContainer.Status != ContainerStatus.Completed)
            .ToListAsync(cancellationToken);

        foreach (var committee in open)
        {
            var outstanding = committee.Members
                .Where(m => m.Decision == CommitteeMemberDecision.Pending)
                .ToList();

            if (outstanding.Count == 0) continue;

            var container = committee.Publication.PublicationContainer;

            if (committee.CreatedAt <= overdueBefore && committee.OverdueReportedAt is null)
            {
                committee.OverdueReportedAt = now;

                var names = string.Join(", ", outstanding
                    .Select(m => $"{m.User.FirstName} {m.User.LastName}".Trim())
                    .OrderBy(name => name));

                await notifications.NotifyAsync(container.CoordinatorId, NotificationType.CommitteeReviewOverdue,
                    "A committee has run out of time",
                    $"The {deadlines.CommitteeReviewDays}-day committee review period has passed on this research "
                    + $"paper and {outstanding.Count} member(s) have not decided: {names}.",
                    nameof(PublicationContainer), container.Id, cancellationToken);

                continue;
            }

            if (warnBefore is { } dueSoon
                && committee.CreatedAt <= dueSoon
                && committee.CreatedAt > overdueBefore
                && committee.DueSoonWarnedAt is null)
            {
                committee.DueSoonWarnedAt = now;

                foreach (var member in outstanding)
                {
                    await notifications.NotifyAsync(member.UserId, NotificationType.CommitteeReviewDueSoon,
                        "Your committee decision is due soon",
                        $"A research paper is waiting on your decision and the {deadlines.CommitteeReviewDays}-day "
                        + $"review period runs out in {deadlines.CommitteeReviewWarningDays} day(s). "
                        + "Please log in to record it.",
                        nameof(PublicationContainer), container.Id, cancellationToken);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
