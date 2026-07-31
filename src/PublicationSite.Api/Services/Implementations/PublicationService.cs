using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Publications;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class PublicationService(
    ApplicationDbContext db,
    IContainerAccessService accessService,
    IAuditService auditService,
    INotificationService notificationService,
    IFileStorageService fileStorageService) : IPublicationService
{
    public async Task<PublicationDto> GetOrCreateDraftAsync(Guid publicationContainerId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var container = await GetOwnedContainerAsync(publicationContainerId, studentId, cancellationToken);

        if (container.CurrentPipeline < PipelineStage.ResearchPaper)
        {
            throw new BusinessRuleException("The research paper stage is not yet available for this Publication Container.");
        }

        var publication = await db.Publications
            .Include(p => p.Keywords).Include(p => p.ResearchAreas)
            .FirstOrDefaultAsync(p => p.PublicationContainerId == container.Id, cancellationToken);

        if (publication is null)
        {
            publication = new Publication
            {
                PublicationContainerId = container.Id,
                Title = string.Empty,
                Abstract = string.Empty,
                Status = PublicationStatus.Draft
            };
            db.Publications.Add(publication);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToDto(publication);
    }

    public async Task<PublicationDto> GetByContainerAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, requestingUserId);

        var publication = await db.Publications
            .Include(p => p.Keywords).Include(p => p.ResearchAreas)
            .FirstOrDefaultAsync(p => p.PublicationContainerId == publicationContainerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationContainerId);

        return ToDto(publication);
    }

    public async Task<PublicationDto> UpdateMetadataAsync(Guid publicationId, Guid studentId, UpdatePublicationMetadataRequest request, CancellationToken cancellationToken = default)
    {
        var publication = await GetOwnedPublicationAsync(publicationId, studentId, cancellationToken, includeMetadata: true);

        if (publication.Status != PublicationStatus.Draft)
        {
            throw new BusinessRuleException("This research paper's metadata can no longer be edited.");
        }

        publication.Title = request.Title;
        publication.Abstract = request.Abstract;
        publication.PublicationType = request.PublicationType;
        publication.PublicationYear = request.PublicationYear;
        publication.UpdatedAt = DateTime.UtcNow;

        if (request.Keywords is not null)
        {
            var keywords = new List<Keyword>();
            foreach (var name in request.Keywords.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var keyword = await db.Keywords.FirstOrDefaultAsync(k => k.Name == name, cancellationToken);
                if (keyword is null)
                {
                    // Explicit Add is required: Keyword.Id already has a non-default value
                    // from its property initializer, so if this entity were only reached
                    // via the Keywords navigation fixup, EF Core's change tracker would
                    // infer EntityState.Modified (an UPDATE by Id) instead of Added — and
                    // since no row with that Id exists yet, that UPDATE affects 0 rows and
                    // SaveChangesAsync throws DbUpdateConcurrencyException.
                    keyword = new Keyword { Name = name };
                    db.Keywords.Add(keyword);
                }
                keywords.Add(keyword);
            }
            publication.Keywords = keywords;
        }

        if (request.ResearchAreaIds is not null)
        {
            publication.ResearchAreas = await db.ResearchAreas
                .Where(r => request.ResearchAreaIds.Contains(r.Id)).ToListAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(publication);
    }

    public async Task<PublicationVersionDto> UploadVersionAsync(
        Guid publicationId, Guid studentId, Stream content, string fileName,
        Stream? supplementary, string? supplementaryFileName, string? reviewerNotes, CancellationToken cancellationToken = default)
    {
        var publication = await GetOwnedPublicationAsync(publicationId, studentId, cancellationToken);

        if (publication.Status is not (PublicationStatus.Draft or PublicationStatus.RevisionsRequested))
        {
            throw new BusinessRuleException("A new version cannot be uploaded at this stage.");
        }

        var stored = await fileStorageService.SaveAsync(content, fileName, $"papers/{publication.Id}", cancellationToken: cancellationToken);
        string? supplementaryPath = null;
        if (supplementary is not null && supplementaryFileName is not null)
        {
            var storedSupplementary = await fileStorageService.SaveAsync(supplementary, supplementaryFileName, $"papers/{publication.Id}/supplementary", cancellationToken: cancellationToken);
            supplementaryPath = storedSupplementary.RelativePath;
        }

        var nextVersion = (await db.PublicationVersions
            .Where(v => v.PublicationId == publication.Id)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var version = new PublicationVersion
        {
            PublicationId = publication.Id,
            VersionNumber = nextVersion,
            FilePath = stored.RelativePath,
            SupplementaryFilesPath = supplementaryPath,
            ReviewerNotes = reviewerNotes,
            UploadedByUserId = studentId
        };
        db.PublicationVersions.Add(version);

        var wasRevision = publication.Status == PublicationStatus.RevisionsRequested;
        publication.Status = wasRevision ? PublicationStatus.Resubmitted : PublicationStatus.Draft;
        publication.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(publication.PublicationContainerId, studentId, "PublicationVersionUploaded",
            $"Uploaded version {nextVersion} of the research paper.");

        return new PublicationVersionDto(version.Id, version.VersionNumber, fileName, version.SupplementaryFilesPath,
            version.ReviewerNotes, "You", version.UploadedAt);
    }

    public async Task<IReadOnlyList<PublicationVersionDto>> GetVersionsAsync(Guid publicationId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.FindAsync([publicationId], cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        await accessService.EnsureAccessAsync(publication.PublicationContainerId, requestingUserId);

        return await db.PublicationVersions
            .Where(v => v.PublicationId == publicationId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new PublicationVersionDto(v.Id, v.VersionNumber, v.FilePath, v.SupplementaryFilesPath,
                v.ReviewerNotes, v.UploadedByUser.FirstName + " " + v.UploadedByUser.LastName, v.UploadedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task SubmitAsync(Guid publicationId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var publication = await GetOwnedPublicationAsync(publicationId, studentId, cancellationToken);

        if (publication.Status != PublicationStatus.Draft)
        {
            throw new BusinessRuleException("The research paper has already been submitted.");
        }

        var hasVersion = await db.PublicationVersions.AnyAsync(v => v.PublicationId == publication.Id, cancellationToken);
        if (!hasVersion)
        {
            throw new BusinessRuleException("Upload the research paper before submitting.");
        }

        var container = publication.PublicationContainer;
        var ethicsStatus = await db.EthicsApprovals
            .Where(a => a.PublicationContainerId == container.Id)
            .Select(a => (EthicsStatus?)a.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (ethicsStatus is not (EthicsStatus.Verified or EthicsStatus.NotRequired))
        {
            throw new BusinessRuleException("The research paper cannot be submitted until the ethics approval process is complete.");
        }

        publication.Status = PublicationStatus.UnderReview;
        publication.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, studentId, "PublicationSubmitted",
            "Research paper submitted for Supervisor review.", newStatus: PublicationStatus.UnderReview.ToString());

        if (container.AssignedSupervisorId is Guid supervisorId)
        {
            await notificationService.NotifyAsync(supervisorId, NotificationType.CommitteeReviewRequested,
                "Research paper awaiting review",
                "A student has submitted their research paper. Please log in to review it.",
                nameof(PublicationContainer), container.Id, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<PublicationDto>> GetPendingForSupervisorAsync(Guid supervisorId, CancellationToken cancellationToken = default)
    {
        var publications = await db.Publications
            .Include(p => p.Keywords).Include(p => p.ResearchAreas)
            .Where(p => p.PublicationContainer.AssignedSupervisorId == supervisorId
                        && (p.Status == PublicationStatus.UnderReview || p.Status == PublicationStatus.Resubmitted))
            .ToListAsync(cancellationToken);

        return publications.Select(ToDto).ToList();
    }

    public async Task SupervisorReviewAsync(Guid publicationId, Guid supervisorId, PaperReviewDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.Include(p => p.PublicationContainer)
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        if (publication.PublicationContainer.AssignedSupervisorId != supervisorId)
        {
            throw new ForbiddenException();
        }

        if (publication.Status is not (PublicationStatus.UnderReview or PublicationStatus.Resubmitted))
        {
            throw new BusinessRuleException("This research paper is not awaiting Supervisor review.");
        }

        var latestVersion = await GetLatestVersionAsync(publication.Id, cancellationToken);

        db.Reviews.Add(new Review
        {
            PublicationVersionId = latestVersion.Id,
            ReviewerUserId = supervisorId,
            ReviewerType = ReviewerType.Supervisor,
            Decision = request.Accept ? ReviewDecision.Approve : ReviewDecision.RequestRevision,
            Comments = request.Comments
        });

        publication.Status = request.Accept ? PublicationStatus.UnderReview : PublicationStatus.RevisionsRequested;
        publication.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(publication.PublicationContainerId, supervisorId, "SupervisorPaperReview",
            request.Comments, newStatus: publication.Status.ToString());

        if (!request.Accept)
        {
            await notificationService.NotifyAsync(publication.PublicationContainer.StudentId, NotificationType.ResearchPaperRevisionRequested,
                "Research paper revision requested",
                "Your Supervisor has requested revisions to your research paper. Please log in to review the comments.",
                nameof(PublicationContainer), publication.PublicationContainerId, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ReviewDto>> GetReviewsAsync(Guid publicationId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.FindAsync([publicationId], cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        await accessService.EnsureAccessAsync(publication.PublicationContainerId, requestingUserId);

        return await db.Reviews
            .Where(r => r.PublicationVersion.PublicationId == publicationId)
            .OrderByDescending(r => r.ReviewedAt)
            .Select(r => new ReviewDto(r.Id, r.ReviewerUser.FirstName + " " + r.ReviewerUser.LastName,
                r.ReviewerType.ToString(), r.Decision.ToString(), r.Comments, r.ReviewedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task CoordinatorFinalDecisionAsync(Guid publicationId, Guid coordinatorId, PaperReviewDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.Include(p => p.PublicationContainer).Include(p => p.Committee)
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        if (publication.PublicationContainer.CoordinatorId != coordinatorId)
        {
            throw new ForbiddenException();
        }

        if (publication.Committee is null || publication.Committee.Status != CommitteeStatus.Completed)
        {
            throw new BusinessRuleException("The evaluation committee has not yet completed its review.");
        }

        if (request.Accept)
        {
            publication.Status = PublicationStatus.Accepted;
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(publication.PublicationContainerId, coordinatorId, "PublicationFinallyAccepted",
                request.Comments, newStatus: PublicationStatus.Accepted.ToString());

            await notificationService.NotifyAsync(publication.PublicationContainer.StudentId, NotificationType.PublicationDecisionRequested,
                "Publication process complete",
                "Your research paper has been fully approved. Please log in to decide whether to publish it.",
                nameof(PublicationContainer), publication.PublicationContainerId, cancellationToken);
        }
        else
        {
            publication.Status = PublicationStatus.RevisionsRequested;
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(publication.PublicationContainerId, coordinatorId, "PublicationRevisionRequestedByCoordinator",
                request.Comments, newStatus: PublicationStatus.RevisionsRequested.ToString());

            await notificationService.NotifyAsync(publication.PublicationContainer.StudentId, NotificationType.ResearchPaperRevisionRequested,
                "Research paper revision requested",
                "The Coordinator has requested revisions to your research paper. Please log in to review the comments.",
                nameof(PublicationContainer), publication.PublicationContainerId, cancellationToken);
        }
    }

    public async Task PublishDecisionAsync(Guid publicationId, Guid actingUserId, PublishDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.Include(p => p.PublicationContainer)
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        var container = publication.PublicationContainer;
        var isOwner = container.StudentId == actingUserId;

        if (!isOwner && string.IsNullOrWhiteSpace(request.Comments))
        {
            throw new BusinessRuleException("Comments are required when publishing on behalf of the student.");
        }

        if (publication.Status != PublicationStatus.Accepted)
        {
            throw new BusinessRuleException("The research paper must be fully accepted before a publication decision can be made.");
        }

        publication.IsPublished = request.Publish;
        publication.PublishedAt = request.Publish ? DateTime.UtcNow : null;
        publication.PublishedByUserId = request.Publish ? actingUserId : null;
        if (request.Publish)
        {
            publication.Status = PublicationStatus.Published;
            publication.PublicationYear ??= DateTime.UtcNow.Year;
        }

        container.Status = ContainerStatus.Completed;
        container.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var comments = request.Comments ?? (isOwner
            ? (request.Publish ? "Student chose to publish the research paper." : "Student chose not to publish the research paper.")
            : "Publication decision made on behalf of the student.");

        await auditService.LogActivityAsync(container.Id, actingUserId, request.Publish ? "PublicationPublished" : "PublicationNotPublished",
            comments, newStatus: publication.Status.ToString(), onBehalfOfUserId: isOwner ? null : container.StudentId);
    }

    public async Task RemovePublishedAsync(Guid publicationId, string comments, Guid adminId, CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        publication.IsPublished = false;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(publication.PublicationContainerId, adminId, "PublishedPaperRemoved", comments);
    }

    private async Task<PublicationVersion> GetLatestVersionAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        return await db.PublicationVersions
            .Where(v => v.PublicationId == publicationId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("No version has been uploaded for this research paper yet.");
    }

    private async Task<PublicationContainer> GetOwnedContainerAsync(Guid containerId, Guid studentId, CancellationToken cancellationToken)
    {
        var container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

        if (container.StudentId != studentId)
        {
            throw new ForbiddenException();
        }

        return container;
    }

    private async Task<Publication> GetOwnedPublicationAsync(Guid publicationId, Guid studentId, CancellationToken cancellationToken, bool includeMetadata = false)
    {
        var query = db.Publications.Include(p => p.PublicationContainer).AsQueryable();
        if (includeMetadata) query = query.Include(p => p.Keywords).Include(p => p.ResearchAreas);

        var publication = await query.FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        if (publication.PublicationContainer.StudentId != studentId)
        {
            throw new ForbiddenException();
        }

        return publication;
    }

    private static PublicationDto ToDto(Publication publication) => new(
        publication.Id, publication.PublicationContainerId, publication.Title, publication.Abstract,
        publication.PublicationType, publication.PublicationYear, publication.Status.ToString(),
        publication.IsPublished, publication.PublishedAt,
        publication.Keywords.Select(k => k.Name).ToList(),
        publication.ResearchAreas.Select(r => r.Name).ToList());
}
