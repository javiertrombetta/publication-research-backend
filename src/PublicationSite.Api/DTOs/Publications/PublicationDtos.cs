namespace PublicationSite.Api.DTOs.Publications;

/// <param name="StudentName">Whose paper it is. Null wherever the caller is the author or already knows. The queues that ask somebody else to judge a paper let them search and order by the student, so a screen that cannot name one is offering controls over something it never shows.</param>
/// <param name="UpdatedAt">When the paper last changed. The reviewer queues order by it and call it the submission date, which for a paper under review is what it is: submitting, resubmitting and every revision move it. Carried so those queues can show the date they order by.</param>
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
    IReadOnlyList<string> ResearchAreas,
    string? StudentName = null,
    DateTime? UpdatedAt = null);

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
/// <param name="RequiredReviewerMembers"><summary>The composition agreed when this publication was opened; null on ones that predate it.</summary></param>
public record AwaitingCommitteeDto(
    Guid Id,
    Guid PublicationContainerId,
    string Title,
    string Abstract,
    string StudentName,
    int? RequiredReviewerMembers,
    int? RequiredExternalCommitteeMembers);

/// <summary>
/// One publication's paper and what the committee said about it, for a screen filling in a page.
///
/// The coordinator's decision queue asked for the paper and then for its reviews, once per row,
/// for the same reason and at the same cost as the ethics queues.
/// </summary>
public record ContainerPaperDto(
    Guid PublicationContainerId,
    PublicationDto Paper,
    IReadOnlyList<ReviewDto> Reviews);

