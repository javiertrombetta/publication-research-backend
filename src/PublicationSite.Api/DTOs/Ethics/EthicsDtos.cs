namespace PublicationSite.Api.DTOs.Ethics;

public record EthicsDeclarationRequest(string Response);

public record EthicsDeclarationDto(Guid Id, Guid PublicationContainerId, string StudentResponse, DateTime DecidedAt);

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
    DateTime? FinalDecisionAt);

public record SupervisorRequirementDecisionRequest(bool IsRequired, string Comments);

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
