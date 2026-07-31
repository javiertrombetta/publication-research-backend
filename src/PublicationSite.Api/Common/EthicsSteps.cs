namespace PublicationSite.Api.Common;

/// <summary>
/// Exactly which decision an ethics approval is waiting for.
///
/// EthicsAwaitingRole says whose turn it is, which is enough to show a badge and not enough to
/// build a screen: the Coordinator has two separate steps here — reviewing the documents, and
/// closing the stage once the Head of Department has commented — and both answer "Coordinator".
/// The screens told them apart by fetching every approval and reading its timestamps, which meant
/// no page of containers could be a stable page of either screen. Naming the step lets the
/// filtering happen in the database, where the paging is.
/// </summary>
public static class EthicsSteps
{
    /// <summary>The student has declared; a Supervisor decides whether documentation is needed.</summary>
    public const string SupervisorDecision = "SupervisorDecision";

    /// <summary>A Supervisor said none is needed; the Coordinator confirms or overrules.</summary>
    public const string CoordinatorConfirmation = "CoordinatorConfirmation";

    /// <summary>Documentation was asked for and the student has yet to supply it.</summary>
    public const string StudentUpload = "StudentUpload";

    /// <summary>Uploaded and unread; the Supervisor looks first.</summary>
    public const string SupervisorDocumentReview = "SupervisorDocumentReview";

    /// <summary>The Supervisor accepted them; the Coordinator reviews.</summary>
    public const string CoordinatorDocumentReview = "CoordinatorDocumentReview";

    /// <summary>The Coordinator approved them; the Head of Department comments.</summary>
    public const string HeadOfDepartmentReview = "HeadOfDepartmentReview";

    /// <summary>Everyone has had their say; the Coordinator closes the stage.</summary>
    public const string CoordinatorFinalDecision = "CoordinatorFinalDecision";
}
