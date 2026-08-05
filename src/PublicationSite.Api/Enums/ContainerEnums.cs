namespace PublicationSite.Api.Enums;

public enum ContainerStatus
{
    InProgress,
    Completed,
    Archived
}

/// <summary>
/// Which of the three stages a publication is on. Sent as its number, so the meaning of each is
/// written here: 1 research proposals, 2 ethics approval, 3 research paper.
/// </summary>
public enum PipelineStage
{
    ResearchProposals = 1,
    EthicsApproval = 2,
    ResearchPaper = 3
}
