using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Common;
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

    public async Task<PublicationDto> GetByIdAsync(Guid publicationId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications
            .Include(p => p.Keywords).Include(p => p.ResearchAreas)
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        // Access is a property of the Container, so it is checked there rather than here.
        await accessService.EnsureAccessAsync(publication.PublicationContainerId, requestingUserId);

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
                    // Explicit Add is required: Keyword.Id already has a non-default value from its
                    // property initializer, so if this entity were only reached via the Keywords
                    // navigation fixup, EF Core's change tracker would infer EntityState.Modified
                    // (an UPDATE by Id) instead of Added, and since no row with that Id exists yet,
                    // that UPDATE affects 0 rows and SaveChangesAsync throws
                    // DbUpdateConcurrencyException.
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

    /// <summary>What a supervisor may order their paper queue by.</summary>
    private static readonly Dictionary<string, Expression<Func<Publication, object?>>> PendingPaperSorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = p => p.Title,
            ["student"] = p => p.PublicationContainer.Student.LastName,
            ["submitted"] = p => p.UpdatedAt
        };

    public async Task<PagedResult<PublicationDto>> GetPendingForSupervisorAsync(
        Guid supervisorId, PageRequest page, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = db.Publications
            .Where(p => p.PublicationContainer.AssignedSupervisorId == supervisorId
                        && (p.Status == PublicationStatus.UnderReview || p.Status == PublicationStatus.Resubmitted))
            // Approving a paper leaves it UnderReview, so without this a Supervisor kept seeing
            // every paper they had already dealt with alongside the ones still waiting.
            .WhereLatestVersionApprovedBySupervisor(false);

        // One term against the title and the student's name, applied before the page is cut so it
        // searches the queue rather than the rows already in hand.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                p.Title.Contains(term)
                || p.PublicationContainer.Student.FirstName.Contains(term)
                || p.PublicationContainer.Student.LastName.Contains(term));
        }

        // Oldest first where nothing was asked for, as on every other queue: the paper that has
        // been waiting longest is the one somebody is waiting longest on.
        query = page.SortBy is not null && PendingPaperSorts.TryGetValue(page.SortBy, out var key)
            ? page.SortDescending ? query.OrderByDescending(key) : query.OrderBy(key)
            : query.OrderBy(p => p.UpdatedAt);

        var total = await query.CountAsync(cancellationToken);

        // Materialised one page at a time. Keywords and research areas are collections, so they
        // are included after the page is chosen rather than for every paper in the department.
        var publications = await query
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .Include(p => p.Keywords).Include(p => p.ResearchAreas)
            .ToListAsync(cancellationToken);

        return new PagedResult<PublicationDto>(
            publications.Select(ToDto).ToList(), page.SafePage, page.SafePageSize, total);
    }

    public async Task<IReadOnlyList<AwaitingCommitteeDto>> GetAwaitingCommitteeAsync(CancellationToken cancellationToken = default)
    {
        return await db.Publications
            .Where(p => p.Status == PublicationStatus.UnderReview && p.Committee == null)
            .WhereLatestVersionApprovedBySupervisor()
            .OrderBy(p => p.UpdatedAt)
            .Select(p => new AwaitingCommitteeDto(
                p.Id,
                p.PublicationContainerId,
                p.Title,
                p.Abstract,
                p.PublicationContainer.Student.FirstName + " " + p.PublicationContainer.Student.LastName,
                p.PublicationContainer.RequiredInternalCommitteeMembers,
                p.PublicationContainer.RequiredExternalCommitteeMembers))
            .ToListAsync(cancellationToken);
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

    public async Task<(Stream Content, string FileName)> DownloadVersionAsync(
        Guid publicationId, Guid versionId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        var version = await db.PublicationVersions
            .Include(v => v.Publication)
            .FirstOrDefaultAsync(v => v.Id == versionId && v.PublicationId == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationVersion), versionId);

        // Access belongs to the container, so it is asked there, which is what lets a supervisor, a
        // coordinator, the head of that department and the appointed committee all read it, and
        // nobody else.
        await accessService.EnsureAccessAsync(version.Publication.PublicationContainerId, requestingUserId);

        var content = await fileStorageService.OpenReadAsync(version.FilePath, cancellationToken);

        // Named for the paper and the version rather than by the stored file name, which is a
        // GUID: a reviewer downloading three papers should be able to tell them apart afterwards.
        var title = string.IsNullOrWhiteSpace(version.Publication.Title) ? "Research paper" : version.Publication.Title;
        return (content, $"{Sanitise(title)} v{version.VersionNumber}{Path.GetExtension(version.FilePath)}");
    }

    /// <summary>Strips what a file name cannot carry, so a title with a colon still downloads.</summary>
    private static string Sanitise(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray());
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
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

    public async Task PublishDecisionAsync(
        Guid publicationId, Guid actingUserId, PublishDecisionRequest request, bool actingAsAdmin = false,
        CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.Include(p => p.PublicationContainer)
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        var container = publication.PublicationContainer;
        var isOwner = container.StudentId == actingUserId;

        // Whose decision this is. The author's, and after them only the people who oversee their
        // work: this publication's own coordinator, or an administrator. Without this the endpoint
        // let any student publish any accepted paper in the institution, because holding the
        // Student role was the whole of the check and the rest of the method only asked whether
        // the caller was the author in order to decide whether to insist on a reason.
        var onBehalfOfTheAuthor = actingAsAdmin || container.CoordinatorId == actingUserId;

        if (!isOwner && !onBehalfOfTheAuthor)
        {
            throw new ForbiddenException(
                "Only the author, their Coordinator or an Administrator can make this decision.");
        }

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
