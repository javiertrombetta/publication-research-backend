namespace PublicationSite.Api.DTOs.Ethics;

public record EthicsDeclarationRequest(string Response);

public record EthicsDeclarationDto(Guid Id, Guid PublicationContainerId, string StudentResponse, DateTime DecidedAt);

/// <param name="StudentDeclaration">What the student answered when asked whether their research involves people: Yes, No or Unsure, or null before they have said. It is the whole of the evidence a supervisor rules on, and it was not on this record at all: the screen asking them to decide was showing the stage the workflow had reached and calling it the declaration.</param>
/// <param name="StudentDeclaredAt">When they answered.</param>
public record EthicsApprovalDto(
    Guid Id,
    Guid PublicationContainerId,
    string Status,
    string? ReferenceNumber,
    DateTime? ApprovalDate,
    DateTime? ExpiryDate,
    bool? IsRequiredPerSupervisor,
    string? SupervisorDecisionComments,
    bool? IsRequiredPerCoordinator,
    string? CoordinatorDecisionComments,
    string? HeadOfDepartmentComments,
    DateTime? HeadOfDepartmentReviewedAt,
    DateTime? FinalDecisionAt,
    string? StudentDeclaration = null,
    DateTime? StudentDeclaredAt = null);

public record SupervisorRequirementDecisionRequest(bool IsRequired, string Comments);

/// <summary>
/// One document this publication has been asked for, and whether it has arrived. Carries the
/// requirement's id because that is what an upload is addressed to, and names can be edited.
/// </summary>
public record RequiredEthicsDocumentDto(
    Guid RequirementId,
    string Name,
    string? Description,
    int SortOrder,
    bool IsSatisfied);

public record EthicsDocumentDto(
    Guid Id,
    string DocumentType,
    string FileName,
    int Version,
    string Status,
    DateTime UploadedAt,
    string? ReviewComments);

public record DocumentReviewDecisionRequest(bool Accept, string Comments);

public record CoordinatorNotRequiredReviewRequest(bool RequireDocumentation, string Comments);

public record CoordinatorDocumentReviewRequest(bool Approve, string Comments);

public record HeadOfDepartmentReviewRequest(string Comments);

public record CoordinatorFinalDecisionRequest(bool Approve, string Comments);

public record EthicsGuidanceDto(string Title, string Content);
