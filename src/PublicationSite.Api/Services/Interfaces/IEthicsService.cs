using PublicationSite.Api.DTOs.Ethics;

namespace PublicationSite.Api.Services.Interfaces;

public interface IEthicsService
{
    EthicsGuidanceDto GetGuidance();

    Task<EthicsDeclarationDto> SubmitDeclarationAsync(Guid publicationContainerId, Guid studentId, EthicsDeclarationRequest request, CancellationToken cancellationToken = default);
    Task<EthicsApprovalDto> GetApprovalAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task SubmitSupervisorRequirementDecisionAsync(Guid publicationContainerId, Guid supervisorId, SupervisorRequirementDecisionRequest request, CancellationToken cancellationToken = default);

    Task<EthicsDocumentDto> UploadDocumentAsync(Guid publicationContainerId, Guid studentId, string documentType, Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin-initiated: puts a document on a publication that is still running, whatever step it
    /// has reached. For a file that arrived by another route, or one the student cannot upload
    /// because the stage has moved past them. Always with a reason, and it never advances the
    /// stage on its own: where the publication should stand afterwards is a separate decision.
    /// </summary>
    Task<EthicsDocumentDto> AdminUploadDocumentAsync(
        Guid publicationContainerId, Guid adminId, string documentType, Stream content, string fileName,
        string comments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin-initiated: takes a document off a publication that is still running, file and all.
    /// For something uploaded in error or against the wrong requirement. Always with a reason.
    /// </summary>
    Task AdminRemoveDocumentAsync(
        Guid publicationContainerId, Guid adminId, Guid documentId, string comments,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// One uploaded ethics document, for anyone with access to its publication. Reviewers are asked
    /// to approve these; until now they could see that a file existed but not read it.
    /// </summary>
    Task<(Stream Content, string FileName)> DownloadDocumentAsync(
        Guid publicationContainerId, Guid documentId, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EthicsDocumentDto>> GetDocumentsAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task SupervisorReviewDocumentsAsync(Guid publicationContainerId, Guid supervisorId, DocumentReviewDecisionRequest request, CancellationToken cancellationToken = default);
    Task CoordinatorReviewNotRequiredAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorNotRequiredReviewRequest request, CancellationToken cancellationToken = default);
    Task CoordinatorReviewDocumentsAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorDocumentReviewRequest request, CancellationToken cancellationToken = default);
    Task HeadOfDepartmentReviewAsync(Guid publicationContainerId, Guid headOfDepartmentId, HeadOfDepartmentReviewRequest request, CancellationToken cancellationToken = default);
    Task CoordinatorFinalDecisionAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorFinalDecisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The documents this particular publication must supply, with the ones already accepted
    /// marked off. Read from the approval's own snapshot, so it reflects what was asked of this
    /// student rather than what would be asked of one starting today.
    /// </summary>
    Task<IReadOnlyList<RequiredEthicsDocumentDto>> GetRequiredDocumentsAsync(
        Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default);
}
