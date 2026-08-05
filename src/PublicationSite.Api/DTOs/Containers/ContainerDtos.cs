using Microsoft.AspNetCore.Mvc.ModelBinding;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.DTOs.Containers;

/// <param name="Title">Best available label for this container: the research paper's title once one exists, otherwise the approved proposal's title. Null while the student is still drafting proposals. Needed because a student can have several containers at once, and the container itself carries no name of its own.</param>
/// <param name="ProposalCount">How many research proposals this Container holds. Zero means it is still empty, which is the only point at which its owning student may discard it.</param>
/// <param name="PaperStatus">The research paper's own status, or null while no paper exists yet. The Container's Status only distinguishes InProgress from Completed, so without this a listing cannot tell an accepted paper awaiting its publication decision from one still being reviewed.</param>
/// <param name="EthicsStatus">The ethics approval's status, or null before the student has declared. Lets a listing show what a Container is waiting on without a request per row.</param>
/// <param name="EthicsAwaitingRole">Whose turn it is in the ethics workflow, as a role name, or null when nothing is pending. EthicsStatus alone cannot answer this. PendingVerification covers four different waits, told apart only by which timestamps have been set: the Supervisor checking the documents, the Coordinator reviewing them, the Head of Department commenting, and the Coordinator's final decision. Deriving that belongs here rather than in every client.</param>
/// <param name="EthicsAwaitingStep">Which ethics decision this is waiting for, by name. See Common/EthicsSteps. Finer than EthicsAwaitingRole, because two of the steps belong to the Coordinator and are separate screens.</param>
/// <param name="PaperAwaitingRole">Whose turn it is on the research paper, as a role name, or null when nothing is pending, which covers the time before a paper exists and the time after it is published. UnderReview alone cannot answer this: it covers the Supervisor still reading the paper, an Admin appointing a committee, the committee voting and the Coordinator's decision, told apart only by what has been recorded against it. RoleNames.EvaluationCommittee is returned where the wait belongs to the committee as a body rather than to one role.</param>
/// <param name="EthicsDocumentsReturned">True while the student is being asked to upload an ethics document again, as opposed to for the first time. The stage reads PendingUpload either way, so without this a listing cannot tell a publication that has been sent back from one that has not started, and both look like ordinary work in progress.</param>
/// <param name="RequiredReviewerMembers">The evaluation committee this publication needs, as agreed when it was opened rather than as configured today, so an administrator assigning a committee months later is told the figures this piece of research was actually started under. Null on containers created before the figures were recorded; the current settings apply to those.</param>
/// <param name="PaperId">The research paper's own id, where one exists. The endpoints that act on a paper take it, and a screen listing publications otherwise has to fetch each one to find it.</param>
/// <param name="LastActivityAt">When anything last happened to this publication, taken from its own trail rather than from a timestamp on the row. The trail is written at all forty-odd points where somebody acts; the row's UpdatedAt is set at nine of them, so it says a publication has been untouched for a month when a supervisor commented on it yesterday. Null only where nothing has been recorded yet, which is a publication created and not yet worked on.</param>
public record PublicationContainerDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    Guid CoordinatorId,
    string CoordinatorName,
    Guid? AssignedSupervisorId,
    string? AssignedSupervisorName,
    int CurrentPipeline,
    string Status,
    DateTime CreatedAt,
    string? Title,
    int ProposalCount,
    string? PaperStatus = null,
    string? EthicsStatus = null,
    string? EthicsAwaitingRole = null,
    string? PaperAwaitingRole = null,
    string? EthicsAwaitingStep = null,
    int? RequiredReviewerMembers = null,
    int? RequiredExternalCommitteeMembers = null,
    bool EthicsDocumentsReturned = false,
    Guid? StudentDepartmentId = null,
    string? StudentDepartmentName = null,
    Guid? EthicsHeadOfDepartmentId = null,
    string? EthicsHeadOfDepartmentName = null,
    Guid? PaperId = null,
    DateTime? LastActivityAt = null);

/// <summary>
/// Which part of a publication's history a reader wants.
///
/// A publication that has been through three stages, revisions and a committee accumulates a
/// trail dozens of entries long, and "when was it sent back, and by whom" is what people open it
/// to answer. Filtered before the page is cut, or the answer would only ever be found in whatever
/// ten entries happened to be on screen.
/// </summary>
public class ActivityHistoryQuery : PageRequest
{
    /// <summary>Inclusive, read as a date in the reader's own day rather than an instant.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Inclusive, and read the same way as <see cref="From"/>.</summary>
    public DateOnly? To { get; set; }

    /// <summary>One of the action names the trail records. See ActivityHistoryEntryDto.Action.</summary>
    public string? Action { get; set; }

    /// <summary>Whoever did it. The person acted on behalf of counts too, since that is who it was for.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// Free text over the action, the person and the comment they left. The filters above each
    /// answer one question exactly; this answers the question somebody actually arrives with,
    /// which is usually half a remembered phrase from a comment.
    /// </summary>
    public string? Search { get; set; }
}

/// <summary>What this publication's own trail can be filtered by, so a screen offers only what is there.</summary>
public record ActivityHistoryFiltersDto(
    IReadOnlyList<string> Actions,
    IReadOnlyList<ActivityHistoryActorDto> Actors);

public record ActivityHistoryActorDto(Guid UserId, string Name);

/// <param name="ActorRole">The capacity the actor was acting in (Coordinator, Supervisor, ...). Without it the history is a list of names, and a student reading it can't tell who decided what. Null only if the account somehow carries no role.</param>
public record ActivityHistoryEntryDto(
    Guid Id,
    string ActorName,
    string? ActorRole,
    string? OnBehalfOfName,
    string Action,
    string Comments,
    string? PreviousStatus,
    string? NewStatus,
    DateTime CreatedAt);

/// <summary>
/// Changing who is responsible for a publication that is already under way.
///
/// A process stalls on a person: a coordinator who has left, a supervisor on sick leave. Without
/// this the publication simply stops, because every step waits on somebody named on it and nothing
/// could rename them.
/// </summary>
/// <param name="CoordinatorUserId">The coordinator it should now have. Null leaves it as it is.</param>
/// <param name="SupervisorUserId">The supervisor it should now have. Null leaves it as it is; a publication that has not reached one yet cannot be given one here, since choosing the supervisor is the coordinator's decision on a proposal.</param>
/// <param name="Comments">Why. Required, and recorded on the publication's history.</param>
/// <param name="HeadOfDepartmentUserId">Who the ethics decision is put to, where the stage has reached that step. Must head the student's own department: the review is that department's oversight of its own students.</param>
public record ReassignContainerRequest(
    Guid? CoordinatorUserId,
    Guid? SupervisorUserId,
    string Comments,
    Guid? HeadOfDepartmentUserId = null);

/// <summary>
/// Where a publication should stand, set by an administrator.
///
/// Correcting what a publication holds and correcting where it stands are separate acts, and the
/// second is the one that lets people carry on: a document put right is no use while the stage
/// still says the person who needed it has already had their turn. So attaching or removing a
/// file never moves the stage, and this does nothing else.
/// </summary>
/// <param name="Stage">1 research proposals, 2 ethics approval, 3 research paper.</param>
/// <param name="EthicsStep">Which ethics step it should be waiting at, by the names in EthicsSteps. Ignored outside the ethics stage.</param>
/// <param name="PaperStatus">What the paper's status should be. Ignored outside the paper stage, and Published is refused: publishing is the student's decision and has its own trail.</param>
/// <param name="Comments">Why. Required, and recorded on the publication's history.</param>
public record MoveContainerRequest(
    int Stage,
    string Comments,
    string? EthicsStep = null,
    string? PaperStatus = null);

/// <summary>
/// Admin manual assignment: opens a publication for a student and names its coordinator.
///
/// <paramref name="PublicationContainerId"/> is refused. It used to select one of the student's
/// publications and move it to another coordinator, which is what the assignments endpoint does,
/// with the role and department checks and the reason on the record that this one never had. It
/// stays on the request so that a caller still sending it is told where that change belongs
/// rather than having it silently ignored.
/// </summary>
public record AssignCoordinatorRequest(
    Guid StudentUserId,
    Guid CoordinatorUserId,
    string Comments,
    Guid? PublicationContainerId = null);

/// <summary>
/// How much of the container list a caller wants, and which of it.
///
/// EthicsStep is what lets a screen ask for its own queue instead of everything: the screens used
/// to fetch every container and filter in the browser, which meant no page could be a page of one
/// screen. Comma-separated, because the Coordinator's first ethics screen covers two steps that
/// arrive at the same moment.
/// </summary>
public class ContainerQuery : PageRequest
{
    /// <summary>
    /// Only this student's publications.
    /// </summary>
    public Guid? StudentId { get; set; }
    /// <summary>
    /// Only the publications this coordinator carries.
    /// </summary>
    public Guid? CoordinatorId { get; set; }
    /// <summary>
    /// The container's own status by name: InProgress, Completed or Archived.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>One or more names from Common/EthicsSteps, comma-separated.</summary>
    public string? EthicsSteps { get; set; }

    /// <summary>
    /// The list above, split. Hidden from binding, and so from the API reference: it is how
    /// EthicsSteps is read rather than a second way of sending the same thing, and Swagger was
    /// offering callers an array parameter with no setter behind it to bind to.
    /// </summary>
    [BindNever]
    public IReadOnlyList<string>? EthicsStep => string.IsNullOrWhiteSpace(EthicsSteps)
        ? null
        : EthicsSteps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Whose turn it is on the research paper, as a role name, so a screen can ask for its own
    /// queue instead of everything. <c>!</c> in front of a name asks for the opposite: the paper
    /// listings are two lists side by side, one the coordinator can act on and one they are only
    /// watching, and each has to be a page of its own or neither can be paged at all.
    /// </summary>
    public string? PaperAwaiting { get; set; }

    /// <summary>
    /// The research paper's own status, by name. What the catalogue screen filters on: an
    /// administrator deciding what to withdraw is looking at published papers, and one deciding
    /// what to publish on a student's behalf at accepted ones.
    /// </summary>
    public string? PaperStatus { get; set; }

    /// <summary>
    /// Which of the three stages a publication is on, by name or by number.
    ///
    /// Here so the administrator's dashboard figures can be opened. Every count on it is a number
    /// with no way through to what it counts, which makes it something to look at rather than
    /// something to work from, and each of these narrows the listing to exactly one of them.
    /// </summary>
    public string? Pipeline { get; set; }

    /// <summary>
    /// The ethics approval's status, by name, or <c>Any</c> for every publication that has one at
    /// all. The dashboard counts approvals by status and counts them all together, and both
    /// figures have to lead somewhere.
    /// </summary>
    public string? EthicsStatus { get; set; }

    /// <summary>
    /// Publications whose evaluation committee holds at least one seat in this state:
    /// <c>Pending</c> for a member who has not voted, <c>Decided</c> for one who has, or
    /// <c>Any</c> for every publication that has a committee at all.
    ///
    /// Seats rather than committees, because that is what the dashboard's committee figures count.
    /// A committee of three with one vote outstanding is one publication and appears under both.
    /// </summary>
    public string? CommitteeDecision { get; set; }

    /// <summary>
    /// Publications carrying at least one review recorded with this verdict, by name. The
    /// dashboard's decision tally counts reviews rather than publications, so a figure of six can
    /// perfectly well open five rows; the listing says what it is showing.
    /// </summary>
    public string? ReviewDecision { get; set; }

    /// <summary>
    /// A word to look for in the student's name, the publication's title or its abstract. One term
    /// across all three, because somebody hunting for a row remembers whichever of them stuck.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>What <see cref="EthicsStatus"/> and <see cref="CommitteeDecision"/> take to mean "any of them".</summary>
    public const string AnyStatus = "Any";

    /// <summary>The two seat states <see cref="CommitteeDecision"/> takes.</summary>
    public const string PendingDecision = "Pending";

    /// <inheritdoc cref="PendingDecision"/>
    public const string SettledDecision = "Decided";
}
