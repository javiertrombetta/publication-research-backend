namespace PublicationSite.Api.Enums;

/// <summary>
/// Numbered explicitly, and 4 is deliberately missing. It was DeferredToNextCycle, which is gone:
/// a round is meant to finish inside the answer-by date it was given, and the date now enforces
/// that, so there is nothing to defer it to. Renumbering to close the gap would silently turn
/// every stored Rejected into something else.
/// </summary>
public enum ProposalStatus
{
    Draft = 0,
    Submitted = 1,
    SelectedBySupervisor = 2,
    Assigned = 3,
    Rejected = 5
}
