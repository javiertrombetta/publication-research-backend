namespace PublicationSite.Api.DTOs.Ethics;

/// <param name="Screening">The screening questions and what was answered to each, in the order they were asked. Optional: a declaration is still a declaration without them, and the ones made before they were kept have none.</param>
public record EthicsDeclarationRequest(string Response, IReadOnlyList<EthicsScreeningAnswerDto>? Screening = null);

/// <param name="Question">The question as it was put to the student, kept with the answer so that a decision still reads as it read when it was made.</param>
/// <param name="Answer">Yes, No or Unsure.</param>
public record EthicsScreeningAnswerDto(int Number, string Question, string Answer);

public record EthicsDeclarationDto(Guid Id, Guid PublicationContainerId, string StudentResponse, DateTime DecidedAt);

/// <param name="StudentDeclaration">What the student answered when asked whether their research involves people: Yes, No or Unsure, or null before they have said. It is the whole of the evidence a supervisor rules on, and it was not on this record at all: the screen asking them to decide was showing the stage the workflow had reached and calling it the declaration.</param>
/// <param name="StudentDeclaredAt">When they answered.</param>
/// <param name="StudentScreening">The twenty screening questions the student worked through on the way to that answer, and what they said to each. Null for declarations made before these were kept.</param>
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
    DateTime? StudentDeclaredAt = null,
    IReadOnlyList<EthicsScreeningAnswerDto>? StudentScreening = null);

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

/// <param name="DocumentIds">Which of the uploaded documents are being asked for again. Empty, or left out, means all of them. Ignored when accepting.</param>
public record DocumentReviewDecisionRequest(bool Accept, string Comments, IReadOnlyList<Guid>? DocumentIds = null);

public record CoordinatorNotRequiredReviewRequest(bool RequireDocumentation, string Comments);

/// <param name="DocumentIds">Which of the uploaded documents are being asked for again. Empty, or left out, means all of them. Ignored when approving.</param>
public record CoordinatorDocumentReviewRequest(bool Approve, string Comments, IReadOnlyList<Guid>? DocumentIds = null);

public record HeadOfDepartmentReviewRequest(string Comments);

/// <param name="DocumentIds">Which of the documents are being asked for again. Empty, or left out, means all of them. Ignored when approving.</param>
public record CoordinatorFinalDecisionRequest(
    bool Approve, string Comments, IReadOnlyList<Guid>? DocumentIds = null);

public record EthicsGuidanceDto(string Title, string Content);
