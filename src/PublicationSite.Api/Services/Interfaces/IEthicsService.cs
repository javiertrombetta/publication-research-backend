using PublicationSite.Api.DTOs.Ethics;

namespace PublicationSite.Api.Services.Interfaces;

public interface IEthicsService
{
    EthicsGuidanceDto GetGuidance();

    Task<EthicsDeclarationDto> SubmitDeclarationAsync(Guid publicationContainerId, Guid studentId, EthicsDeclarationRequest request, CancellationToken cancellationToken = default);
    Task<EthicsApprovalDto> GetApprovalAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task SubmitSupervisorRequirementDecisionAsync(Guid publicationContainerId, Guid supervisorId, SupervisorRequirementDecisionRequest request, CancellationToken cancellationToken = default);

    Task<EthicsDocumentDto> UploadDocumentAsync(Guid publicationContainerId, Guid studentId, string documentType, Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EthicsDocumentDto>> GetDocumentsAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task SupervisorReviewDocumentsAsync(Guid publicationContainerId, Guid supervisorId, DocumentReviewDecisionRequest request, CancellationToken cancellationToken = default);
    Task CoordinatorReviewNotRequiredAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorNotRequiredReviewRequest request, CancellationToken cancellationToken = default);
    Task CoordinatorReviewDocumentsAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorDocumentReviewRequest request, CancellationToken cancellationToken = default);
    Task HeadOfDepartmentReviewAsync(Guid publicationContainerId, Guid headOfDepartmentId, HeadOfDepartmentReviewRequest request, CancellationToken cancellationToken = default);
    Task CoordinatorFinalDecisionAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorFinalDecisionRequest request, CancellationToken cancellationToken = default);
}
