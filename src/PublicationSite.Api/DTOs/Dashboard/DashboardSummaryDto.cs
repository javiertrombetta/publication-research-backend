namespace PublicationSite.Api.DTOs.Dashboard;

public record DashboardSummaryDto(
    int TotalContainers,
    int ContainersInProgress,
    int ContainersCompleted,
    IReadOnlyDictionary<string, int> ContainersByPipelineStage,
    IReadOnlyDictionary<string, int> PublicationsByStatus,
    int PublishedPublicationsCount,
    IReadOnlyDictionary<string, int> EthicsApprovalsByStatus,
    int PendingCommitteeReviews,
    int CompletedCommitteeReviews,
    IReadOnlyDictionary<string, int> ReviewDecisionCounts);
