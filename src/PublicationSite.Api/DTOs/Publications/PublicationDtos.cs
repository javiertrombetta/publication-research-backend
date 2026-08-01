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

/// <summary>
/// A research paper the Supervisor has approved and that has no evaluation committee yet. This is
/// the administrator's queue, with everything that screen needs to show and to build the committee.
///
/// Assembled here rather than left to the caller: working it out from the containers list meant two
/// further requests per publication, and the answer still came out wrong, because nothing in those
/// responses says whether the Supervisor has approved.
/// </summary>
/// <param name="RequiredInternalCommitteeMembers"><summary>The composition agreed when this publication was opened; null on ones that predate it.</summary></param>
public record AwaitingCommitteeDto(
    Guid Id,
    Guid PublicationContainerId,
    string Title,
    string Abstract,
    string StudentName,
    int? RequiredInternalCommitteeMembers,
    int? RequiredExternalCommitteeMembers);
