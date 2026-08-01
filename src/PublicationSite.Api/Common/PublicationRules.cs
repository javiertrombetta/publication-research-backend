using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Common;

/// <summary>
/// Conditions about a research paper that more than one part of the system has to agree on.
/// </summary>
public static class PublicationRules
{
    /// <summary>
    /// Whether the Supervisor has approved the paper as it currently stands.
    ///
    /// This cannot be read from the status. <c>UnderReview</c> is the status both before the
    /// Supervisor has looked at a paper and after they have approved it. Approval does not move it
    /// on, because the next step belongs to an administrator appointing a committee. Three places
    /// need to tell those two apart: the Supervisor's own queue, the administrator's list of papers
    /// ready for a committee, and the check that refuses to appoint one prematurely. Reading it
    /// from the status left the first two listing papers the third then rejected.
    ///
    /// It asks about the newest version specifically. A paper sent back and resubmitted carries the
    /// Supervisor's approval of the draft that was superseded, and that approval says nothing about
    /// the one now in front of them.
    /// </summary>
    /// <param name="approved">
    /// False to select the papers still waiting on the Supervisor. Both polarities come from this
    /// one expression so they cannot drift into disagreeing about the same paper.
    /// </param>
    public static IQueryable<Publication> WhereLatestVersionApprovedBySupervisor(
        this IQueryable<Publication> source, bool approved = true) =>
        source.Where(p => p.Versions
            .OrderByDescending(v => v.VersionNumber)
            .Take(1)
            .SelectMany(v => v.Reviews)
            .Any(r => r.ReviewerType == ReviewerType.Supervisor && r.Decision == ReviewDecision.Approve) == approved);
}
