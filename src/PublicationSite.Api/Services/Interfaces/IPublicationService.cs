using PublicationSite.Api.DTOs.Publications;

namespace PublicationSite.Api.Services.Interfaces;

public interface IPublicationService
{
    Task<PublicationDto> GetOrCreateDraftAsync(Guid publicationContainerId, Guid studentId, CancellationToken cancellationToken = default);
    Task<PublicationDto> GetByContainerAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default);
    Task<PublicationDto> UpdateMetadataAsync(Guid publicationId, Guid studentId, UpdatePublicationMetadataRequest request, CancellationToken cancellationToken = default);

    Task<PublicationVersionDto> UploadVersionAsync(Guid publicationId, Guid studentId, Stream content, string fileName, Stream? supplementary, string? supplementaryFileName, string? reviewerNotes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicationVersionDto>> GetVersionsAsync(Guid publicationId, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task SubmitAsync(Guid publicationId, Guid studentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicationDto>> GetPendingForSupervisorAsync(Guid supervisorId, CancellationToken cancellationToken = default);
    Task SupervisorReviewAsync(Guid publicationId, Guid supervisorId, PaperReviewDecisionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewDto>> GetReviewsAsync(Guid publicationId, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task CoordinatorFinalDecisionAsync(Guid publicationId, Guid coordinatorId, PaperReviewDecisionRequest request, CancellationToken cancellationToken = default);
    Task PublishDecisionAsync(Guid publicationId, Guid actingUserId, PublishDecisionRequest request, CancellationToken cancellationToken = default);
    Task RemovePublishedAsync(Guid publicationId, string comments, Guid adminId, CancellationToken cancellationToken = default);
}
