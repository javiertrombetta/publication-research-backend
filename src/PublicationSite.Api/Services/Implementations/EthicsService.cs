using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Ethics;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class EthicsService(
    ApplicationDbContext db,
    IContainerAccessService accessService,
    IAuditService auditService,
    INotificationService notificationService,
    IFileStorageService fileStorageService,
    IDecisionCommentPolicy commentPolicy,
    ISystemSettingService settingService) : IEthicsService
{

    public EthicsGuidanceDto GetGuidance() => new(
        "Understanding Research Ethics Approval",
        "Research involving human participants, animals, sensitive data, or potential conflicts of interest " +
        "generally requires ethics approval under AIS's institutional research ethics policy. If you are unsure " +
        "whether your project needs approval, discuss it with your Supervisor. Selecting 'Unsure' keeps this " +
        "guidance available until you are ready to answer Yes or No.");

    public async Task<EthicsDeclarationDto> SubmitDeclarationAsync(Guid publicationContainerId, Guid studentId, EthicsDeclarationRequest request, CancellationToken cancellationToken = default)
    {
        var container = await GetOwnedContainerAsync(publicationContainerId, studentId, cancellationToken);

        // The proposal stage has to be finished first. A coordinator chooses which proposal goes
        // ahead and appoints the supervisor, and this declaration is addressed to that supervisor:
        // made earlier it produced an approval waiting on a person who did not exist yet, on
        // nobody's queue, and it opened the ethics stage while the publication was still deciding
        // what the research would be. The site's own screens already refuse this; the rule belongs
        // here, where it is a rule rather than a screen.
        if (container.CurrentPipeline < PipelineStage.EthicsApproval)
        {
            throw new BusinessRuleException(
                "The ethics stage has not opened yet. It follows the coordinator choosing a proposal and appointing a supervisor.");
        }

        if (!Enum.TryParse<EthicsStudentResponse>(request.Response, true, out var response))
        {
            throw new BusinessRuleException("Response must be Yes, No or Unsure.");
        }

        var screening = SerialiseScreening(request.Screening);

        var declaration = await db.EthicsDeclarations.FirstOrDefaultAsync(d => d.PublicationContainerId == container.Id, cancellationToken);
        if (declaration is null)
        {
            declaration = new EthicsDeclaration
            {
                PublicationContainerId = container.Id,
                StudentResponse = response,
                ScreeningAnswers = screening
            };
            db.EthicsDeclarations.Add(declaration);
        }
        else
        {
            declaration.StudentResponse = response;

            // Only overwritten when this submission brought answers with it. A declaration revised
            // from a screen that does not ask them should not wipe the working behind the last one.
            if (screening is not null) declaration.ScreeningAnswers = screening;
            declaration.DecidedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (response != EthicsStudentResponse.Unsure)
        {
            var approval = await GetOrCreateApprovalAsync(container.Id, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(container.Id, studentId, "EthicsDeclarationSubmitted",
                $"Student answered '{response}' to the ethics declaration.", newStatus: response.ToString());

            if (container.AssignedSupervisorId is Guid supervisorId)
            {
                await notificationService.NotifyAsync(supervisorId, NotificationType.EthicsEvaluationRequested,
                    "Ethics requirement evaluation requested",
                    "A student has completed their ethics declaration. Please log in to determine whether ethics approval documentation is required.",
                    nameof(PublicationContainer), container.Id, cancellationToken);
            }
        }

        return new EthicsDeclarationDto(declaration.Id, declaration.PublicationContainerId, declaration.StudentResponse.ToString(), declaration.DecidedAt);
    }

    public async Task<EthicsApprovalDto> GetApprovalAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, requestingUserId);
        var approval = await db.EthicsApprovals.FirstOrDefaultAsync(a => a.PublicationContainerId == publicationContainerId, cancellationToken)
            ?? throw new NotFoundException(nameof(EthicsApproval), publicationContainerId);

        // The student's own answer with it. Everybody who rules on this is ruling on that answer,
        // and it lives in its own table, so it has to be asked for.
        var declaration = await db.EthicsDeclarations
            .FirstOrDefaultAsync(d => d.PublicationContainerId == publicationContainerId, cancellationToken);

        return ToDto(approval, declaration);
    }

    public async Task SubmitSupervisorRequirementDecisionAsync(Guid publicationContainerId, Guid supervisorId, SupervisorRequirementDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var approval = await GetApprovalForSupervisorAsync(publicationContainerId, supervisorId, cancellationToken);

        await commentPolicy.EnsureAsync(DecisionPoints.EthicsSupervisorRuling, request.Comments, cancellationToken);

        approval.IsRequiredPerSupervisor = request.IsRequired;
        approval.SupervisorDecisionComments = request.Comments;
        approval.SupervisorDecisionAt = DateTime.UtcNow;
        approval.Status = request.IsRequired ? EthicsStatus.PendingUpload : EthicsStatus.NotRequired;

        if (request.IsRequired)
        {
            await SnapshotRequiredDocumentsAsync(approval, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        var container = await db.PublicationContainers.FindAsync([publicationContainerId], cancellationToken);

        await auditService.LogActivityAsync(publicationContainerId, supervisorId, "SupervisorEthicsDecision",
            request.Comments, newStatus: approval.Status.ToString());

        if (request.IsRequired)
        {
            await notificationService.NotifyAsync(container!.StudentId, NotificationType.EthicsDocumentationRequired,
                "Ethics documentation required",
                "Your Supervisor has determined that ethics approval documentation is required. Please log in to upload the required documents.",
                nameof(PublicationContainer), publicationContainerId, cancellationToken);
        }
        else
        {
            await notificationService.NotifyAsync(container!.CoordinatorId, NotificationType.EthicsCoordinatorReviewRequested,
                "Ethics decision review requested",
                "A Supervisor has determined ethics approval is not required. Please log in to review this decision.",
                nameof(PublicationContainer), publicationContainerId, cancellationToken);
        }
    }

    public async Task<EthicsDocumentDto> UploadDocumentAsync(Guid publicationContainerId, Guid studentId, string documentType, Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var container = await GetOwnedContainerAsync(publicationContainerId, studentId, cancellationToken);
        var approval = await db.EthicsApprovals.Include(a => a.Documents)
            .FirstOrDefaultAsync(a => a.PublicationContainerId == container.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(EthicsApproval), publicationContainerId);

        if (approval.Status != EthicsStatus.PendingUpload)
        {
            throw new BusinessRuleException("Ethics documentation is not currently being requested.");
        }

        // Matched against this approval's own list rather than the master list: a requirement
        // added after this student was asked for documentation is not one of theirs.
        var required = await db.EthicsApprovalRequirements
            .Where(r => r.EthicsApprovalId == approval.Id)
            .Include(r => r.EthicsDocumentRequirement)
            .ToListAsync(cancellationToken);

        var requirement = required
            .Select(r => r.EthicsDocumentRequirement)
            .FirstOrDefault(r => r.Id.ToString() == documentType
                                 || string.Equals(r.Name, documentType, StringComparison.OrdinalIgnoreCase))
            ?? throw new BusinessRuleException($"'{documentType}' is not one of the documents requested for this publication.");

        var stored = await fileStorageService.SaveAsync(content, fileName, $"ethics/{container.Id}", cancellationToken: cancellationToken);
        var version = approval.Documents
            .Where(d => d.EthicsDocumentRequirementId == requirement.Id)
            .Select(d => d.Version).DefaultIfEmpty(0).Max() + 1;

        var document = new EthicsDocument
        {
            EthicsApprovalId = approval.Id,
            EthicsDocumentRequirementId = requirement.Id,
            FileName = stored.FileName,
            FilePath = stored.RelativePath,
            Version = version,
            UploadedByUserId = studentId,
            Status = EthicsDocumentStatus.PendingReview
        };

        db.EthicsDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        var uploaded = await db.EthicsDocuments
            .Where(d => d.EthicsApprovalId == approval.Id && d.Status != EthicsDocumentStatus.RevisionRequested)
            .Select(d => d.EthicsDocumentRequirementId).Distinct().ToListAsync(cancellationToken);

        if (required.All(r => uploaded.Contains(r.EthicsDocumentRequirementId)))
        {
            approval.Status = EthicsStatus.PendingVerification;
            await db.SaveChangesAsync(cancellationToken);

            if (container.AssignedSupervisorId is Guid supervisorId)
            {
                await notificationService.NotifyAsync(supervisorId, NotificationType.EthicsDocumentationReadyForReview,
                    "Ethics documentation ready for review",
                    "The student has uploaded all required ethics documentation. Please log in to review it.",
                    nameof(PublicationContainer), container.Id, cancellationToken);
            }
        }

        await auditService.LogActivityAsync(container.Id, studentId, "EthicsDocumentUploaded",
            $"Uploaded '{requirement.Name}' (version {version}).");

        // Built from the requirement already in hand. Reading it off document.EthicsDocumentRequirement
        // worked only where that entity happened to be tracked, which is not something to depend on.
        return new EthicsDocumentDto(document.Id, requirement.Name, document.FileName, document.Version,
            document.Status.ToString(), document.UploadedAt, document.ReviewComments);
    }

    public async Task<IReadOnlyList<EthicsDocumentDto>> GetDocumentsAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, requestingUserId);

        // Projected in the query rather than through ToDocumentDto. That helper reads
        // EthicsDocumentRequirement.Name off a navigation property, and nothing here loaded it, so
        // every call threw a NullReferenceException and the reviewers' document list came back as a
        // 500. Written this way the name is joined in SQL and there is no navigation to miss.
        return await db.EthicsDocuments
            .Where(d => d.EthicsApproval.PublicationContainerId == publicationContainerId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new EthicsDocumentDto(
                d.Id,
                d.EthicsDocumentRequirement.Name,
                d.FileName,
                d.Version,
                d.Status.ToString(),
                d.UploadedAt,
                d.ReviewComments))
            .ToListAsync(cancellationToken);
    }

    public async Task<(Stream Content, string FileName)> DownloadDocumentAsync(
        Guid publicationContainerId, Guid documentId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, requestingUserId);

        var document = await db.EthicsDocuments
            .Include(d => d.EthicsDocumentRequirement)
            .FirstOrDefaultAsync(
                d => d.Id == documentId && d.EthicsApproval.PublicationContainerId == publicationContainerId,
                cancellationToken)
            ?? throw new NotFoundException(nameof(EthicsDocument), documentId);

        var content = await fileStorageService.OpenReadAsync(document.FilePath, cancellationToken);

        // Named for the form it answers and its version. The stored name is the student's original
        // one, which is often "scan.pdf" three times over.
        return (content,
            $"{document.EthicsDocumentRequirement.Name} v{document.Version}{Path.GetExtension(document.FileName)}");
    }

    public async Task SupervisorReviewDocumentsAsync(Guid publicationContainerId, Guid supervisorId, DocumentReviewDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var approval = await GetApprovalForSupervisorAsync(publicationContainerId, supervisorId, cancellationToken, includeDocuments: true);

        if (approval.Status != EthicsStatus.PendingVerification)
        {
            throw new BusinessRuleException("There is no ethics documentation currently awaiting review.");
        }

        await commentPolicy.EnsureAsync(request.Accept
            ? DecisionPoints.EthicsSupervisorDocumentsAccept
            : DecisionPoints.EthicsSupervisorDocumentsReturn, request.Comments, cancellationToken);

        ApplyDocumentReviewOutcome(approval, request.Accept, request.Comments, request.DocumentIds);
        await db.SaveChangesAsync(cancellationToken);

        var container = await db.PublicationContainers.FindAsync([publicationContainerId], cancellationToken);

        await auditService.LogActivityAsync(publicationContainerId, supervisorId, "SupervisorEthicsDocumentReview",
            request.Comments, newStatus: approval.Status.ToString());

        if (request.Accept)
        {
            await notificationService.NotifyAsync(container!.CoordinatorId, NotificationType.EthicsCoordinatorReviewRequested,
                "Ethics documentation review requested",
                "A Supervisor has approved the submitted ethics documentation. Please log in to review it.",
                nameof(PublicationContainer), publicationContainerId, cancellationToken);
        }
        else
        {
            await notificationService.NotifyAsync(container!.StudentId, NotificationType.EthicsRevisionRequested,
                "Ethics documentation revision requested",
                "Your Supervisor has requested revisions to your ethics documentation. Please log in to review the comments.",
                nameof(PublicationContainer), publicationContainerId, cancellationToken);
        }
    }

    public async Task CoordinatorReviewNotRequiredAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorNotRequiredReviewRequest request, CancellationToken cancellationToken = default)
    {
        var (container, approval) = await GetApprovalForCoordinatorAsync(publicationContainerId, coordinatorId, cancellationToken);

        if (approval.Status != EthicsStatus.NotRequired)
        {
            throw new BusinessRuleException("This container's ethics decision is not awaiting Coordinator review.");
        }

        await commentPolicy.EnsureAsync(request.RequireDocumentation
            ? DecisionPoints.EthicsCoordinatorOverturnNotRequired
            : DecisionPoints.EthicsCoordinatorConfirmNotRequired, request.Comments, cancellationToken);

        approval.IsRequiredPerCoordinator = request.RequireDocumentation;
        approval.CoordinatorDecisionComments = request.Comments;
        approval.CoordinatorDecisionAt = DateTime.UtcNow;

        if (request.RequireDocumentation)
        {
            approval.Status = EthicsStatus.PendingUpload;
            await SnapshotRequiredDocumentsAsync(approval, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "CoordinatorRequiredEthicsDocumentation",
                request.Comments, newStatus: approval.Status.ToString());

            await notificationService.NotifyAsync(container.StudentId, NotificationType.EthicsDocumentationRequired,
                "Ethics documentation required",
                "The Coordinator has determined that ethics approval documentation is required. Please log in to upload the required documents.",
                nameof(PublicationContainer), publicationContainerId, cancellationToken);
        }
        else
        {
            // Agreeing that a piece of research needs no ethics approval is a decision in its own
            // right, and an institution may want its head of department to see it before the stage
            // closes. Where it does, this is not the end of the stage: it is the coordinator
            // handing on, exactly as approving a set of documents is.
            var workflow = await settingService.GetEthicsWorkflowSettingsAsync(cancellationToken);

            if (workflow.HeadOfDepartmentReviewsWhenNotRequired)
            {
                await db.SaveChangesAsync(cancellationToken);

                await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "CoordinatorAgreedEthicsNotRequired",
                    request.Comments, newStatus: EthicsStatus.NotRequired.ToString());

                await AssignHeadOfDepartmentAsync(container, approval, cancellationToken);
                return;
            }

            approval.FinalDecisionAt = DateTime.UtcNow;
            await AdvanceToResearchPaperPipelineAsync(container, cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "EthicsConfirmedNotRequired",
                request.Comments, newStatus: EthicsStatus.NotRequired.ToString());

            await NotifyStudentEthicsCompletedAsync(container, ethicsRequired: false, cancellationToken);
        }
    }

    public async Task CoordinatorReviewDocumentsAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorDocumentReviewRequest request, CancellationToken cancellationToken = default)
    {
        var (container, approval) = await GetApprovalForCoordinatorAsync(publicationContainerId, coordinatorId, cancellationToken, includeDocuments: true);

        // The supervisor reads the documents first. Their acceptance is what puts every document
        // past PendingReview, so anything still there means the set has not reached the
        // coordinator yet.
        //
        // Only the status was checked, and it says PendingVerification for the whole of the run
        // from upload to final decision. A coordinator opening the container by its id could
        // therefore approve documents nobody had read, and the head of department and the final
        // decision after that: the stage reached Verified with every document still sitting at
        // PendingReview, and the supervisor's screen went on offering a review of a stage that
        // had closed.
        if (approval.Status != EthicsStatus.PendingVerification
            || approval.Documents.Any(d => d.Status == EthicsDocumentStatus.PendingReview))
        {
            throw new BusinessRuleException("There is no ethics documentation currently awaiting Coordinator review.");
        }

        await commentPolicy.EnsureAsync(request.Approve
            ? DecisionPoints.EthicsCoordinatorDocumentsApprove
            : DecisionPoints.EthicsCoordinatorDocumentsReturn, request.Comments, cancellationToken);

        approval.IsRequiredPerCoordinator = true;
        approval.CoordinatorDecisionComments = request.Comments;
        approval.CoordinatorDecisionAt = DateTime.UtcNow;

        if (request.Approve)
        {
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "CoordinatorApprovedEthicsDocuments",
                request.Comments);

            var workflow = await settingService.GetEthicsWorkflowSettingsAsync(cancellationToken);

            if (workflow.HeadOfDepartmentReviews)
            {
                await AssignHeadOfDepartmentAsync(container, approval, cancellationToken);
            }
            else
            {
                // Nobody stands between the approval and the close, so the coordinator is told
                // the decision is now theirs rather than left waiting for a step that never comes.
                await notificationService.NotifyAsync(coordinatorId, NotificationType.EthicsFinalDecisionRequested,
                    "Final ethics decision requested",
                    "You have approved this student's ethics documentation. This institution does not use a Head of "
                    + "Department review, so the final decision is yours to make now.",
                    nameof(PublicationContainer), publicationContainerId, cancellationToken);
            }
        }
        else
        {
            ApplyDocumentReviewOutcome(approval, accept: false, request.Comments, request.DocumentIds);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "CoordinatorRequestedEthicsRevision",
                request.Comments, newStatus: approval.Status.ToString());

            await notificationService.NotifyAsync(container.StudentId, NotificationType.EthicsRevisionRequested,
                "Ethics documentation revision requested",
                "The Coordinator has requested revisions to your ethics documentation. Please log in to review the comments.",
                nameof(PublicationContainer), publicationContainerId, cancellationToken);
        }
    }

    public async Task HeadOfDepartmentReviewAsync(Guid publicationContainerId, Guid headOfDepartmentId, HeadOfDepartmentReviewRequest request, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, headOfDepartmentId);

        var approval = await db.EthicsApprovals.FirstOrDefaultAsync(a => a.PublicationContainerId == publicationContainerId, cancellationToken)
            ?? throw new NotFoundException(nameof(EthicsApproval), publicationContainerId);

        var workflow = await settingService.GetEthicsWorkflowSettingsAsync(cancellationToken);

        // Two routes reach this step and each has its own switch: documentation the coordinator
        // has approved, and a coordinator agreeing that none is needed. Both wait on the same
        // person and both end with the coordinator closing the stage.
        var onDocuments = approval.Status == EthicsStatus.PendingVerification && workflow.HeadOfDepartmentReviews;

        var onNotRequired = approval.Status == EthicsStatus.NotRequired
            && approval.FinalDecisionAt is null
            && workflow.HeadOfDepartmentReviewsWhenNotRequired;

        if (!onDocuments && !onNotRequired)
        {
            throw new BusinessRuleException(
                "This institution does not put the Head of Department between the coordinator's decision and the close of this stage.");
        }

        if (approval.CoordinatorDecisionAt is null || approval.HeadOfDepartmentReviewedAt is not null)
        {
            throw new BusinessRuleException("This container's ethics decision is not awaiting Head of Department review.");
        }

        // Whoever it was put to. Any other head of the department can see it, because they oversee
        // everything their department has in flight, but recording the comments is one person's
        // job: two heads answering the same review would leave only the second on the record.
        if (approval.HeadOfDepartmentUserId is { } named && named != headOfDepartmentId)
        {
            throw new BusinessRuleException(
                "This ethics decision was put to another head of your department. An administrator can move it if they are unavailable.");
        }

        await commentPolicy.EnsureAsync(DecisionPoints.EthicsHeadOfDepartmentReview, request.Comments, cancellationToken);

        approval.HeadOfDepartmentComments = request.Comments;
        approval.HeadOfDepartmentReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var container = await db.PublicationContainers.FindAsync([publicationContainerId], cancellationToken);

        await auditService.LogActivityAsync(publicationContainerId, headOfDepartmentId, "HeadOfDepartmentEthicsReview", request.Comments);

        await notificationService.NotifyAsync(container!.CoordinatorId, NotificationType.EthicsFinalDecisionRequested,
            "Final ethics decision requested",
            "The Head of Department has reviewed the ethics documentation. Please log in to make the final decision.",
            nameof(PublicationContainer), publicationContainerId, cancellationToken);
    }

    /// <summary>
    /// Puts the decision to one head of the student's own department, and tells them.
    ///
    /// Named rather than broadcast. A department can have more than one head, and a review nobody
    /// is named for belongs to all of them and so to nobody: each sees it on their queue and each
    /// can reasonably assume another has it. The one carrying the fewest reviews is chosen, so the
    /// work spreads instead of piling on whoever happens to be first alphabetically. An
    /// administrator can name somebody else afterwards, from the same department.
    /// </summary>
    private async Task AssignHeadOfDepartmentAsync(
        PublicationContainer container, EthicsApproval approval, CancellationToken cancellationToken)
    {
        var departmentId = await db.StudentProfiles
            .Where(s => s.UserId == container.StudentId)
            .Select(s => (Guid?)s.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);

        if (departmentId is null) return;

        // How many are already on each one's desk: reviews put to them that they have not answered
        // and that nobody has closed. Work already commented on is not work outstanding.
        var chosen = await db.HeadOfDepartmentProfiles
            .Where(h => h.DepartmentId == departmentId)
            .Select(h => new
            {
                h.UserId,
                Outstanding = db.EthicsApprovals.Count(a =>
                    a.HeadOfDepartmentUserId == h.UserId
                    && a.HeadOfDepartmentReviewedAt == null
                    && a.FinalDecisionAt == null)
            })
            .OrderBy(h => h.Outstanding)
            .ThenBy(h => h.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (chosen is null) return;

        approval.HeadOfDepartmentUserId = chosen.UserId;
        await db.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyAsync(chosen.UserId, NotificationType.EthicsHeadOfDepartmentReviewRequested,
            "Ethics decision awaiting your comments",
            "The Coordinator has passed a student's ethics decision to you. Please log in to review it and record your comments.",
            nameof(PublicationContainer), container.Id, cancellationToken);
    }

    public async Task CoordinatorFinalDecisionAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorFinalDecisionRequest request, CancellationToken cancellationToken = default)
    {
        // With the documents, because turning this down sends them back to the student and has to
        // write the reason onto them. Loaded without, the set was empty and the send-back marked
        // nothing.
        var (container, approval) = await GetApprovalForCoordinatorAsync(
            publicationContainerId, coordinatorId, cancellationToken, includeDocuments: true);

        // The Head of Department's reading is optional on both routes: some institutions have none
        // in the loop. Where it is off, the coordinator's own decision leads straight here, and
        // approvals already parked at that step move on with everything else, which is the point of
        // the switches.
        var workflow = await settingService.GetEthicsWorkflowSettingsAsync(cancellationToken);

        // The route where no documentation was needed only reaches this step at all when the Head
        // of Department has been through it; without that step the coordinator's agreement closed
        // the stage there and then.
        var afterNotRequired = approval.Status == EthicsStatus.NotRequired
            && approval.FinalDecisionAt is null
            && approval.CoordinatorDecisionAt is not null
            && approval.HeadOfDepartmentReviewedAt is not null;

        var afterDocuments = approval.Status == EthicsStatus.PendingVerification
            && approval.CoordinatorDecisionAt is not null
            && (approval.HeadOfDepartmentReviewedAt is not null || !workflow.HeadOfDepartmentReviews);

        if (!afterNotRequired && !afterDocuments)
        {
            throw new BusinessRuleException("This container's ethics decision is not awaiting a final Coordinator decision.");
        }

        await commentPolicy.EnsureAsync(request.Approve
            ? DecisionPoints.EthicsCoordinatorFinalApprove
            : DecisionPoints.EthicsCoordinatorFinalReturn, request.Comments, cancellationToken);

        if (request.Approve)
        {
            // Verified means documentation was produced and accepted, which on the other route it
            // was not. The stage closes either way; what it closes as is the truth about it.
            approval.Status = afterNotRequired ? EthicsStatus.NotRequired : EthicsStatus.Verified;
            approval.FinalDecisionAt = DateTime.UtcNow;
            await AdvanceToResearchPaperPipelineAsync(container, cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "EthicsFinallyApproved",
                request.Comments, newStatus: approval.Status.ToString());

            await NotifyStudentEthicsCompletedAsync(container, ethicsRequired: !afterNotRequired, cancellationToken);
        }
        else if (afterNotRequired)
        {
            // There is nothing to send back: no document was ever asked for. Turning this down
            // means the ruling itself was wrong, so the student is asked for documentation after
            // all, which is where the coordinator's own overturn leads.
            approval.IsRequiredPerCoordinator = true;
            approval.Status = EthicsStatus.PendingUpload;
            await SnapshotRequiredDocumentsAsync(approval, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "CoordinatorRequiredEthicsDocumentation",
                request.Comments, newStatus: approval.Status.ToString());

            await notificationService.NotifyAsync(container.StudentId, NotificationType.EthicsDocumentationRequired,
                "Ethics documentation required",
                "After review, the Coordinator has determined that ethics approval documentation is required after all. "
                + "Please log in to upload the required documents.",
                nameof(PublicationContainer), publicationContainerId, cancellationToken);
        }
        else
        {
            ApplyDocumentReviewOutcome(approval, accept: false, request.Comments, request.DocumentIds);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "EthicsFinalRevisionRequested",
                request.Comments, newStatus: approval.Status.ToString());

            await notificationService.NotifyAsync(container.StudentId, NotificationType.EthicsRevisionRequested,
                "Ethics documentation revision requested",
                "The Coordinator has requested further revisions to your ethics documentation. Please log in to review the comments.",
                nameof(PublicationContainer), publicationContainerId, cancellationToken);
        }
    }

    /// <summary>
    /// What a review does to the documents in front of it.
    ///
    /// Accepting takes the lot. Sending back takes the ones named, and no names means all of them,
    /// which is what a reviewer who has not singled any out is saying.
    ///
    /// The ones not named are accepted rather than left waiting. A reviewer has read the whole set
    /// to decide that two of them will not do; leaving the other three pending would mean asking
    /// the student to send those again as well, and reading them again afterwards. Accepted, the
    /// student is asked for exactly what was wrong, and uploading it puts the set back in front of
    /// the reviewer.
    /// </summary>
    private static void ApplyDocumentReviewOutcome(
        EthicsApproval approval, bool accept, string comments, IReadOnlyList<Guid>? documentIds = null)
    {
        // The set actually in front of this reviewer: the newest file against each requirement,
        // leaving out anything an earlier round sent back and the student has not replaced.
        //
        // Not "everything still pending review". Only the supervisor sees documents in that state:
        // by the time the set reaches the coordinator the supervisor has accepted all of it, so a
        // send-back from there matched nothing at all and returned the student a set with no
        // comment written against any of it.
        var underReview = approval.Documents
            .Where(d => d.Status != EthicsDocumentStatus.RevisionRequested)
            .GroupBy(d => d.EthicsDocumentRequirementId)
            .Select(versions => versions.OrderByDescending(d => d.Version).First())
            .ToList();

        if (accept)
        {
            foreach (var document in underReview)
            {
                document.Status = EthicsDocumentStatus.Accepted;
            }

            return;
        }

        var named = documentIds is { Count: > 0 } ? documentIds.ToHashSet() : null;

        foreach (var document in underReview)
        {
            if (named is null || named.Contains(document.Id))
            {
                document.Status = EthicsDocumentStatus.RevisionRequested;
                document.ReviewComments = comments;
            }
            else
            {
                document.Status = EthicsDocumentStatus.Accepted;
            }
        }

        approval.Status = EthicsStatus.PendingUpload;
    }

    public async Task<IReadOnlyList<RequiredEthicsDocumentDto>> GetRequiredDocumentsAsync(
        Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, requestingUserId);

        var approval = await db.EthicsApprovals
            .FirstOrDefaultAsync(a => a.PublicationContainerId == publicationContainerId, cancellationToken);

        if (approval is null)
        {
            return [];
        }

        // A document counts as supplied unless it was sent back: a revision request means the
        // student owes it again, which is exactly what an unticked box should mean.
        var accepted = await db.EthicsDocuments
            .Where(d => d.EthicsApprovalId == approval.Id && d.Status != EthicsDocumentStatus.RevisionRequested)
            .Select(d => d.EthicsDocumentRequirementId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return await db.EthicsApprovalRequirements
            .Where(r => r.EthicsApprovalId == approval.Id)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.EthicsDocumentRequirement.Name)
            .Select(r => new RequiredEthicsDocumentDto(
                r.EthicsDocumentRequirementId,
                r.EthicsDocumentRequirement.Name,
                r.EthicsDocumentRequirement.Description,
                r.SortOrder,
                accepted.Contains(r.EthicsDocumentRequirementId)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Copies today's active requirements onto this approval, once. Everything the student is then
    /// asked for, and everything anyone later checks them against, reads from this copy, so an
    /// administrator editing the master list changes what is asked of the next student, not of this
    /// one.
    ///
    /// Does nothing if a snapshot already exists: documentation can be requested more than once on
    /// the same approval (a Coordinator may ask after a Supervisor said it was unnecessary), and
    /// the first list asked for is the one that counts.
    /// </summary>
    private async Task SnapshotRequiredDocumentsAsync(EthicsApproval approval, CancellationToken cancellationToken)
    {
        if (await db.EthicsApprovalRequirements.AnyAsync(r => r.EthicsApprovalId == approval.Id, cancellationToken))
        {
            return;
        }

        var active = await db.EthicsDocumentRequirements
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        if (active.Count == 0)
        {
            throw new BusinessRuleException(
                "No ethics documents have been configured, so none can be requested. " +
                "An administrator must set them up under System settings.");
        }

        db.EthicsApprovalRequirements.AddRange(active.Select(r => new EthicsApprovalRequirement
        {
            EthicsApprovalId = approval.Id,
            EthicsDocumentRequirementId = r.Id,
            SortOrder = r.SortOrder
        }));
    }

    private async Task AdvanceToResearchPaperPipelineAsync(PublicationContainer container, CancellationToken cancellationToken)
    {
        container.CurrentPipeline = PipelineStage.ResearchPaper;
        container.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyStudentEthicsCompletedAsync(PublicationContainer container, bool ethicsRequired, CancellationToken cancellationToken)
    {
        var message = ethicsRequired
            ? "Your ethics approval process has been completed successfully. Please log in to continue with your research paper."
            : "Ethics approval documentation is not required for your research. Please log in to continue with your research paper.";

        await notificationService.NotifyAsync(container.StudentId, NotificationType.EthicsApprovalCompleted,
            "Ethics approval process complete", message, nameof(PublicationContainer), container.Id, cancellationToken);
    }

    private async Task<EthicsApproval> GetOrCreateApprovalAsync(Guid containerId, CancellationToken cancellationToken)
    {
        var approval = await db.EthicsApprovals.FirstOrDefaultAsync(a => a.PublicationContainerId == containerId, cancellationToken);
        if (approval is null)
        {
            approval = new EthicsApproval { PublicationContainerId = containerId, Status = EthicsStatus.PendingSupervisorDecision };
            db.EthicsApprovals.Add(approval);
        }

        return approval;
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

    private async Task<EthicsApproval> GetApprovalForSupervisorAsync(Guid containerId, Guid supervisorId, CancellationToken cancellationToken, bool includeDocuments = false)
    {
        var container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

        if (container.AssignedSupervisorId != supervisorId)
        {
            throw new ForbiddenException();
        }

        var query = db.EthicsApprovals.AsQueryable();
        if (includeDocuments) query = query.Include(a => a.Documents);

        return await query.FirstOrDefaultAsync(a => a.PublicationContainerId == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(EthicsApproval), containerId);
    }

    private async Task<(PublicationContainer Container, EthicsApproval Approval)> GetApprovalForCoordinatorAsync(
        Guid containerId, Guid coordinatorId, CancellationToken cancellationToken, bool includeDocuments = false)
    {
        var container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

        if (container.CoordinatorId != coordinatorId)
        {
            throw new ForbiddenException();
        }

        var query = db.EthicsApprovals.AsQueryable();
        if (includeDocuments) query = query.Include(a => a.Documents);

        var approval = await query.FirstOrDefaultAsync(a => a.PublicationContainerId == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(EthicsApproval), containerId);

        return (container, approval);
    }

    /// <summary>
    /// The answers as given, kept as JSON. Anything that is not one of the three answers is
    /// dropped rather than stored: a form filled in with something else is not a form, and a
    /// value nobody can read back is worse than none. Nothing at all is stored as nothing, so
    /// "they answered none of them" and "this was never asked" stay different facts.
    /// </summary>
    private static string? SerialiseScreening(IReadOnlyList<EthicsScreeningAnswerDto>? screening)
    {
        if (screening is null || screening.Count == 0) return null;

        var kept = screening
            .Where(a => a.Answer is "Yes" or "No" or "Unsure")
            .Where(a => !string.IsNullOrWhiteSpace(a.Question))
            .Select(a => new EthicsScreeningAnswerDto(a.Number, a.Question.Trim(), a.Answer))
            .Take(50)
            .ToList();

        return kept.Count == 0 ? null : JsonSerializer.Serialize(kept);
    }

    private static IReadOnlyList<EthicsScreeningAnswerDto>? DeserialiseScreening(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        try
        {
            return JsonSerializer.Deserialize<List<EthicsScreeningAnswerDto>>(stored);
        }
        catch (JsonException)
        {
            // Whatever is in there is not the form. The declaration itself is still readable, and
            // it is the declaration people are ruling on.
            return null;
        }
    }

    private static EthicsApprovalDto ToDto(EthicsApproval approval, EthicsDeclaration? declaration = null) => new(
        approval.Id, approval.PublicationContainerId, approval.Status.ToString(), approval.ReferenceNumber,
        approval.ApprovalDate, approval.ExpiryDate, approval.IsRequiredPerSupervisor, approval.SupervisorDecisionComments,
        approval.IsRequiredPerCoordinator, approval.CoordinatorDecisionComments, approval.HeadOfDepartmentComments,
        approval.HeadOfDepartmentReviewedAt, approval.FinalDecisionAt,
        declaration?.StudentResponse.ToString(), declaration?.DecidedAt,
        DeserialiseScreening(declaration?.ScreeningAnswers));
}
