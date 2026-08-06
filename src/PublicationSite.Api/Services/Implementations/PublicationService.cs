using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Publications;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class PublicationService(
    ApplicationDbContext db,
    IContainerAccessService accessService,
    IAuditService auditService,
    INotificationService notificationService,
    IFileStorageService fileStorageService,
    IDecisionCommentPolicy commentPolicy,
    ISystemSettingService settingService,
    ILogger<PublicationService> logger) : IPublicationService
{
    public async Task<PublicationVersionDto> AdminUploadVersionAsync(
        Guid publicationId, Guid adminId, Stream content, string fileName, string comments,
        CancellationToken cancellationToken = default)
    {
        var publication = await LoadForAdminAsync(publicationId, comments, cancellationToken);

        var stored = await fileStorageService.SaveAsync(
            content, fileName, $"papers/{publication.Id}", cancellationToken: cancellationToken);

        var nextVersion = (await db.PublicationVersions
            .Where(v => v.PublicationId == publication.Id)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var version = new PublicationVersion
        {
            PublicationId = publication.Id,
            VersionNumber = nextVersion,
            FilePath = stored.RelativePath,
            UploadedByUserId = adminId
        };

        db.PublicationVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);

        // No change of status. Where the paper should now stand is the administrator's next
        // decision rather than a side effect of attaching a file.
        await auditService.LogActivityAsync(publication.PublicationContainerId, adminId, "PaperVersionAddedByAdmin",
            $"Added version {nextVersion}. {comments}");

        return new PublicationVersionDto(version.Id, version.VersionNumber, stored.FileName,
            version.SupplementaryFilesPath, version.ReviewerNotes, "An administrator", version.UploadedAt);
    }

    public async Task AdminRemoveVersionAsync(
        Guid publicationId, Guid adminId, Guid versionId, string comments,
        CancellationToken cancellationToken = default)
    {
        var publication = await LoadForAdminAsync(publicationId, comments, cancellationToken);

        var versions = await db.PublicationVersions
            .Where(v => v.PublicationId == publication.Id)
            .ToListAsync(cancellationToken);

        var version = versions.FirstOrDefault(v => v.Id == versionId)
            ?? throw new NotFoundException(nameof(PublicationVersion), versionId);

        // A paper with no version at all is not a state the pipeline has: every reviewer's screen
        // offers the newest one, and there would be nothing to offer.
        if (versions.Count == 1)
        {
            throw new BusinessRuleException(
                "This is the only version of the paper. Upload a replacement first, then remove this one.");
        }

        try
        {
            await fileStorageService.DeleteAsync(version.FilePath, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Removed paper version {VersionId} but could not delete its file at {Path}.",
                version.Id, version.FilePath);
        }

        db.PublicationVersions.Remove(version);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(publication.PublicationContainerId, adminId, "PaperVersionRemovedByAdmin",
            $"Removed version {version.VersionNumber}. {comments}");
    }

    /// <summary>
    /// Moves the publication on now that the paper has been accepted, where the paper is the
    /// first of the two. Where ethics came first it is already done, so the publication stays
    /// where it is and the student publishes from there.
    /// </summary>
    private async Task AdvanceAfterPaperAcceptedAsync(
        PublicationContainer container, PaperWorkflowSettingsDto workflow, CancellationToken cancellationToken)
    {
        if (workflow.EthicsBeforePaper) return;

        container.CurrentPipeline = PipelineStage.EthicsApproval;
        container.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The paper, for an administrator correcting it. Refused once the publication has finished:
    /// its versions are the record of what was judged.
    /// </summary>
    private async Task<Publication> LoadForAdminAsync(
        Guid publicationId, string comments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(comments))
        {
            throw new BusinessRuleException("Say why. It stays on the publication's history.");
        }

        var publication = await db.Publications
            .Include(p => p.PublicationContainer)
            .FirstOrDefaultAsync(p => p.Id == publicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        if (publication.PublicationContainer.Status == ContainerStatus.Completed)
        {
            throw new BusinessRuleException(
                "This publication has finished. Its versions are the record of what was judged.");
        }

        // A container is not marked Completed the moment its paper is accepted, so the check above
        // let an administrator add and remove versions of a paper somebody had already passed.
        if (SettledPaper.Is(publication.Status))
        {
            throw new BusinessRuleException(SettledPaper.Message);
        }

        return publication;
    }

    public async Task<PublicationDto> GetOrCreateDraftAsync(Guid publicationContainerId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var container = await GetOwnedContainerAsync(publicationContainerId, studentId, cancellationToken);

        if (container.CurrentPipeline != PipelineStage.ResearchPaper)
        {
            throw new BusinessRuleException(
                "The research paper stage is not open on this publication, in the order this institution runs its stages.");
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

        // A paper sent back for revisions is with the student again, and the title, the abstract
        // and the keywords are as much a part of what a reviewer asked to be changed as the file
        // is. Only a draft could be edited, so a student acting on their supervisor's comments was
        // refused on the first save.
        if (publication.Status is not (PublicationStatus.Draft or PublicationStatus.RevisionsRequested))
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

        // Uploading is not submitting, in either cycle. A first draft stays a draft until the
        // student says it is ready, and a revision stays with the student in the same way.
        //
        // This used to send a revision straight back to the supervisor, which left the student's
        // own Submit with nothing to do and an error to show for it, and gave anyone attaching a
        // file no way to look at it again before it went.
        publication.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(publication.PublicationContainerId, studentId, "PublicationVersionUploaded",
            $"Uploaded version {nextVersion} of the research paper.");

        return new PublicationVersionDto(version.Id, version.VersionNumber, fileName, version.SupplementaryFilesPath,
            version.ReviewerNotes, "You", version.UploadedAt);
    }

    /// <summary>What a paper's versions can be ordered by, one per column of the screen.</summary>
    private static readonly Dictionary<string, Expression<Func<PublicationVersion, object?>>> VersionSorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["version"] = v => v.VersionNumber,
            ["file"] = v => v.FilePath,
            ["uploaded"] = v => v.UploadedAt
        };

    public async Task<IReadOnlyList<PublicationVersionDto>> GetVersionsAsync(
        Guid publicationId, Guid requestingUserId, SortRequest? sort = null,
        CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.FindAsync([publicationId], cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        await accessService.EnsureAccessAsync(publication.PublicationContainerId, requestingUserId);

        return await db.PublicationVersions
            .Where(v => v.PublicationId == publicationId)
            .SortBy(sort ?? new SortRequest(), v => v.VersionNumber, VersionSorts, fallbackDescending: true)
            .Select(v => new PublicationVersionDto(v.Id, v.VersionNumber, v.FilePath, v.SupplementaryFilesPath,
                v.ReviewerNotes, v.UploadedByUser.FirstName + " " + v.UploadedByUser.LastName, v.UploadedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task SubmitAsync(Guid publicationId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var publication = await GetOwnedPublicationAsync(publicationId, studentId, cancellationToken);

        // Two ways in: a first draft, and one that came back for revisions. They part company only
        // at the end, where the status says which of the two a reviewer is being handed.
        var isRevision = publication.Status == PublicationStatus.RevisionsRequested;

        if (publication.Status is not (PublicationStatus.Draft or PublicationStatus.RevisionsRequested))
        {
            throw new BusinessRuleException("The research paper has already been submitted.");
        }

        var hasVersion = await db.PublicationVersions.AnyAsync(v => v.PublicationId == publication.Id, cancellationToken);
        if (!hasVersion)
        {
            throw new BusinessRuleException("Upload the research paper before submitting.");
        }

        var container = publication.PublicationContainer;

        // The status and the closing date, not the status alone. A supervisor ruling that ethics is
        // not required puts the record straight into NotRequired, but the decision is not finished
        // until the coordinator has confirmed it: it is the confirmation that closes the stage and
        // opens the paper. Nothing can reach here before that today, since the paper stage will not
        // hand out a draft either, but a rule that says what it means does not depend on that.
        var ethics = await db.EthicsApprovals
            .Where(a => a.PublicationContainerId == container.Id)
            .Select(a => new { a.Status, a.FinalDecisionAt })
            .FirstOrDefaultAsync(cancellationToken);

        var ethicsSettled = ethics is not null
            && ethics.FinalDecisionAt is not null
            && ethics.Status is EthicsStatus.Verified or EthicsStatus.NotRequired;

        if (!ethicsSettled)
        {
            throw new BusinessRuleException("The research paper cannot be submitted until the ethics approval process is complete.");
        }

        // Resubmitted, not UnderReview, so the queues can tell a revision from a first reading and
        // say so to whoever picks it up.
        publication.Status = isRevision ? PublicationStatus.Resubmitted : PublicationStatus.UnderReview;
        publication.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, studentId,
            isRevision ? "PublicationResubmitted" : "PublicationSubmitted",
            isRevision
                ? "Revised research paper submitted for Supervisor review."
                : "Research paper submitted for Supervisor review.",
            newStatus: publication.Status.ToString());

        if (container.AssignedSupervisorId is Guid supervisorId)
        {
            await notificationService.NotifyAsync(supervisorId, NotificationType.CommitteeReviewRequested,
                isRevision ? "Revised research paper awaiting review" : "Research paper awaiting review",
                isRevision
                    ? "A student has acted on your comments and submitted a revised research paper. Please log in to review it."
                    : "A student has submitted their research paper. Please log in to review it.",
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
        // Nothing where this institution does not ask supervisors to read papers: the review
        // itself is refused, so offering it would only waste the reading.
        if (!(await settingService.GetPaperWorkflowSettingsAsync(cancellationToken)).SupervisorReviews)
        {
            return new PagedResult<PublicationDto>([], page.SafePage, page.SafePageSize, 0);
        }

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
            ? (page.SortDescending ? query.OrderByDescending(key) : query.OrderBy(key)).ThenBy(p => p.Id)
            : query.OrderBy(p => p.UpdatedAt).ThenBy(p => p.Id);

        var total = await query.CountAsync(cancellationToken);

        // Materialised one page at a time. Keywords and research areas are collections, so they
        // are included after the page is chosen rather than for every paper in the department.
        var publications = await query
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .Include(p => p.Keywords).Include(p => p.ResearchAreas)
            // The author, for the page in hand only. This screen searches and orders by the
            // student, and named neither: a supervisor was being asked to review a paper without
            // being told whose it was.
            .Include(p => p.PublicationContainer).ThenInclude(c => c.Student)
            .ToListAsync(cancellationToken);

        return new PagedResult<PublicationDto>(
            publications
                .Select(p => ToDto(p, p.PublicationContainer.Student.FirstName + " " + p.PublicationContainer.Student.LastName))
                .ToList(),
            page.SafePage, page.SafePageSize, total);
    }

    /// <summary>What the administrator's committee queue may be ordered by.</summary>
    private static readonly Dictionary<string, Expression<Func<Publication, object?>>> AwaitingCommitteeSorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = p => p.Title,
            ["student"] = p => p.PublicationContainer.Student.LastName,
            ["waiting"] = p => p.UpdatedAt
        };

    public async Task<PagedResult<AwaitingCommitteeDto>> GetAwaitingCommitteeAsync(
        PageRequest paging, string? search = null, CancellationToken cancellationToken = default)
    {
        var workflow = await settingService.GetPaperWorkflowSettingsAsync(cancellationToken);

        // Nothing at all where this institution appoints no committees: offering a paper here
        // would be offering work the assignment itself refuses.
        if (!workflow.CommitteeEvaluates)
        {
            return new PagedResult<AwaitingCommitteeDto>([], paging.SafePage, paging.SafePageSize, 0);
        }

        var papers = db.Publications
            .Where(p => p.Status == PublicationStatus.UnderReview && p.Committee == null);

        // The supervisor's approval is only a precondition where the supervisor reads papers.
        // Asked for regardless, a paper on a stage that skips them never carried one, so it was
        // invisible to the only screen that could appoint its committee.
        if (workflow.SupervisorReviews)
        {
            papers = papers.WhereLatestVersionApprovedBySupervisor();
        }

        // Applied before the page is cut, so it searches the whole queue rather than the rows in
        // hand. One term across the paper and its author, as the other queues take it.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            papers = papers.Where(p =>
                p.Title.Contains(term)
                || p.PublicationContainer.Student.FirstName.Contains(term)
                || p.PublicationContainer.Student.LastName.Contains(term));
        }

        // Longest waiting first by default: this is a queue, and the paper nobody has dealt with
        // for a fortnight is the one holding a coordinator up.
        var ordered = paging.SortBy is not null && AwaitingCommitteeSorts.TryGetValue(paging.SortBy, out var key)
            ? (paging.SortDescending ? papers.OrderByDescending(key) : papers.OrderBy(key)).ThenBy(p => p.Id)
            : papers.OrderBy(p => p.UpdatedAt).ThenBy(p => p.Id);

        var total = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((paging.SafePage - 1) * paging.SafePageSize)
            .Take(paging.SafePageSize)
            .Select(p => new AwaitingCommitteeDto(
                p.Id,
                p.PublicationContainerId,
                p.Title,
                p.Abstract,
                p.PublicationContainer.Student.FirstName + " " + p.PublicationContainer.Student.LastName,
                p.PublicationContainer.RequiredReviewerMembers,
                p.PublicationContainer.RequiredExternalCommitteeMembers))
            .ToListAsync(cancellationToken);

        return new PagedResult<AwaitingCommitteeDto>(items, paging.SafePage, paging.SafePageSize, total);
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

        await commentPolicy.EnsureAsync(request.Accept
            ? DecisionPoints.PaperSupervisorAccept
            : DecisionPoints.PaperSupervisorReturn, request.Comments, cancellationToken);

        if (publication.Status is not (PublicationStatus.UnderReview or PublicationStatus.Resubmitted))
        {
            throw new BusinessRuleException("This research paper is not awaiting Supervisor review.");
        }

        var workflow = await settingService.GetPaperWorkflowSettingsAsync(cancellationToken);

        if (!workflow.SupervisorReviews)
        {
            throw new BusinessRuleException("This institution does not ask the supervisor to read research papers.");
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

        // Where nothing follows the supervisor, their acceptance is the acceptance of the paper:
        // otherwise it would sit UnderReview waiting on a committee and a coordinator that this
        // institution does not use.
        var nobodyFollows = !workflow.CommitteeEvaluates && !workflow.CoordinatorDecides;

        publication.Status = request.Accept
            ? nobodyFollows ? PublicationStatus.Accepted : PublicationStatus.UnderReview
            : PublicationStatus.RevisionsRequested;
        publication.UpdatedAt = DateTime.UtcNow;

        if (publication.Status == PublicationStatus.Accepted)
        {
            await AdvanceAfterPaperAcceptedAsync(publication.PublicationContainer, workflow, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(publication.PublicationContainerId, supervisorId, "SupervisorPaperReview",
            request.Comments, newStatus: publication.Status.ToString());

        if (request.Accept && nobodyFollows)
        {
            await notificationService.NotifyAsync(publication.PublicationContainer.StudentId, NotificationType.PublicationApproved,
                "Research paper accepted",
                "Your Supervisor has accepted your research paper. You can now decide whether to publish it.",
                nameof(PublicationContainer), publication.PublicationContainerId, cancellationToken);
        }

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

    /// <summary>What a committee's verdicts can be ordered by, one per column of the screen.</summary>
    private static readonly Dictionary<string, Expression<Func<Review, object?>>> ReviewSorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["reviewer"] = r => r.ReviewerUser.LastName,
            ["seat"] = r => r.ReviewerType,
            ["decision"] = r => r.Decision,
            ["comments"] = r => r.Comments,
            ["when"] = r => r.ReviewedAt
        };

    public async Task<IReadOnlyList<ReviewDto>> GetReviewsAsync(
        Guid publicationId, Guid requestingUserId, SortRequest? sort = null,
        CancellationToken cancellationToken = default)
    {
        var publication = await db.Publications.FindAsync([publicationId], cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);

        await accessService.EnsureAccessAsync(publication.PublicationContainerId, requestingUserId);

        return await db.Reviews
            .Where(r => r.PublicationVersion.PublicationId == publicationId)
            .SortBy(sort ?? new SortRequest(), r => r.ReviewedAt, ReviewSorts, fallbackDescending: true)
            .Select(r => new ReviewDto(r.Id, r.ReviewerUser.FirstName + " " + r.ReviewerUser.LastName,
                r.ReviewerType.ToString(), r.Decision.ToString(), r.Comments, r.ReviewedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The papers of a set of publications, with what the committee said, in a fixed number of
    /// queries. The coordinator's decision queue asked for the paper and then for its reviews once
    /// per row, at the same cost and for the same reason as the ethics queues.
    /// </summary>
    public async Task<IReadOnlyList<ContainerPaperDto>> GetPapersForAsync(
        IReadOnlyCollection<Guid> publicationContainerIds, Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        if (publicationContainerIds.Count == 0) return [];

        var readable = await accessService
            .WhereReadableBy(db.PublicationContainers.Where(c => publicationContainerIds.Contains(c.Id)), requestingUserId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (readable.Count == 0) return [];

        var papers = await db.Publications
            .Where(p => readable.Contains(p.PublicationContainerId))
            .Include(p => p.Keywords)
            .Include(p => p.ResearchAreas)
            .ToListAsync(cancellationToken);

        var paperIds = papers.Select(p => p.Id).ToList();

        var reviews = await db.Reviews
            .Where(r => paperIds.Contains(r.PublicationVersion.PublicationId))
            .OrderByDescending(r => r.ReviewedAt)
            .Select(r => new
            {
                r.PublicationVersion.PublicationId,
                Dto = new ReviewDto(r.Id, r.ReviewerUser.FirstName + " " + r.ReviewerUser.LastName,
                    r.ReviewerType.ToString(), r.Decision.ToString(), r.Comments, r.ReviewedAt)
            })
            .ToListAsync(cancellationToken);

        var byPaper = reviews
            .GroupBy(r => r.PublicationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ReviewDto>)[.. g.Select(r => r.Dto)]);

        return [.. papers.Select(p => new ContainerPaperDto(
            p.PublicationContainerId,
            ToDto(p),
            byPaper.TryGetValue(p.Id, out var theirs) ? theirs : []))];
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

        var workflow = await settingService.GetPaperWorkflowSettingsAsync(cancellationToken);

        if (!workflow.CoordinatorDecides)
        {
            throw new BusinessRuleException("This institution does not ask the coordinator to decide on research papers.");
        }

        if (workflow.CommitteeEvaluates
            && (publication.Committee is null || publication.Committee.Status != CommitteeStatus.Completed))
        {
            throw new BusinessRuleException("The evaluation committee has not yet completed its review.");
        }

        await commentPolicy.EnsureAsync(request.Accept
            ? DecisionPoints.PaperCoordinatorAccept
            : DecisionPoints.PaperCoordinatorReturn, request.Comments, cancellationToken);

        if (request.Accept)
        {
            publication.Status = PublicationStatus.Accepted;
            await AdvanceAfterPaperAcceptedAsync(publication.PublicationContainer, workflow, cancellationToken);
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

        if (!isOwner)
        {
            await commentPolicy.EnsureAsync(DecisionPoints.PaperPublishOnBehalf, request.Comments, cancellationToken);
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

        // Back to accepted, and the publication stamps cleared with it. Only the flag used to be
        // turned off, which left a record saying it was published, on a date, by somebody, and not
        // published: the catalogue dropped it while every screen showing a status still read
        // "Published". The outcome the paper earned is Accepted, and that is what it now says.
        await commentPolicy.EnsureAsync(DecisionPoints.PaperWithdrawFromCatalogue, comments, cancellationToken);

        publication.IsPublished = false;
        publication.Status = PublicationStatus.Accepted;
        publication.PublishedAt = null;
        publication.PublishedByUserId = null;
        publication.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(publication.PublicationContainerId, adminId, "PublishedPaperRemoved",
            comments, newStatus: publication.Status.ToString());
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

    /// <param name="studentName">
    /// Passed only by the queues that ask somebody else to judge the paper. Everywhere else the
    /// caller is the author or already knows whose it is, and a name loaded per row would be a
    /// join nobody reads.
    /// </param>
    private static PublicationDto ToDto(Publication publication, string? studentName = null) => new(
        publication.Id, publication.PublicationContainerId, publication.Title, publication.Abstract,
        publication.PublicationType, publication.PublicationYear, publication.Status.ToString(),
        publication.IsPublished, publication.PublishedAt,
        publication.Keywords.Select(k => k.Name).ToList(),
        publication.ResearchAreas.Select(r => r.Name).ToList(),
        studentName, publication.UpdatedAt);
}
