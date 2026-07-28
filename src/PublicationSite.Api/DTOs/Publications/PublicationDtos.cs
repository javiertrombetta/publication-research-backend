namespace PublicationSite.Api.DTOs.Publications;

public record PublicationDto(
    Guid Id,
    Guid PublicationContainerId,
    string Title,
    string Abstract,
    string? PublicationType,
    int? PublicationYear,
    string Status,
    bool IsPublished,
    DateTime? PublishedAt,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> ResearchAreas);

public record UpdatePublicationMetadataRequest(
    string Title,
    string Abstract,
    string? PublicationType,
    int? PublicationYear,
    IReadOnlyList<string>? Keywords,
    IReadOnlyList<Guid>? ResearchAreaIds);

public record PublicationVersionDto(
    Guid Id,
    int VersionNumber,
    string FileName,
    string? SupplementaryFilesPath,
    string? ReviewerNotes,
    string UploadedByName,
    DateTime UploadedAt);

public record ReviewDto(
    Guid Id,
    string ReviewerName,
    string ReviewerType,
    string Decision,
    string Comments,
    DateTime ReviewedAt);

public record PaperReviewDecisionRequest(bool Accept, string Comments);

public record PublishDecisionRequest(bool Publish, string? Comments);
