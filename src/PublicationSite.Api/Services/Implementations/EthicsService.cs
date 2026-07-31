using Microsoft.EntityFrameworkCore;
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
    IFileStorageService fileStorageService) : IEthicsService
{
    private static readonly EthicsDocumentType[] RequiredDocumentTypes =
    [
        EthicsDocumentType.ApprovalCertificate,
        EthicsDocumentType.ApplicationForm,
        EthicsDocumentType.ParticipantConsentForm
    ];

    public EthicsGuidanceDto GetGuidance() => new(
        "Understanding Research Ethics Approval",
        "Research involving human participants, animals, sensitive data, or potential conflicts of interest " +
        "generally requires ethics approval under AIS's institutional research ethics policy. If you are unsure " +
        "whether your project needs approval, discuss it with your Supervisor. Selecting 'Unsure' keeps this " +
        "guidance available until you are ready to answer Yes or No.");

    public async Task<EthicsDeclarationDto> SubmitDeclarationAsync(Guid publicationContainerId, Guid studentId, EthicsDeclarationRequest request, CancellationToken cancellationToken = default)
    {
        var container = await GetOwnedContainerAsync(publicationContainerId, studentId, cancellationToken);

        if (!Enum.TryParse<EthicsStudentResponse>(request.Response, true, out var response))
        {
            throw new BusinessRuleException("Response must be Yes, No or Unsure.");
        }

        var declaration = await db.EthicsDeclarations.FirstOrDefaultAsync(d => d.PublicationContainerId == container.Id, cancellationToken);
        if (declaration is null)
        {
            declaration = new EthicsDeclaration { PublicationContainerId = container.Id, StudentResponse = response };
            db.EthicsDeclarations.Add(declaration);
        }
        else
        {
            declaration.StudentResponse = response;
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

        return ToDto(approval);
    }

    public async Task SubmitSupervisorRequirementDecisionAsync(Guid publicationContainerId, Guid supervisorId, SupervisorRequirementDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var approval = await GetApprovalForSupervisorAsync(publicationContainerId, supervisorId, cancellationToken);

        approval.IsRequiredPerSupervisor = request.IsRequired;
        approval.SupervisorDecisionComments = request.Comments;
        approval.SupervisorDecisionAt = DateTime.UtcNow;
        approval.Status = request.IsRequired ? EthicsStatus.PendingUpload : EthicsStatus.NotRequired;
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

        if (!Enum.TryParse<EthicsDocumentType>(documentType, true, out var type))
        {
            throw new BusinessRuleException($"'{documentType}' is not a recognised ethics document type.");
        }

        var stored = await fileStorageService.SaveAsync(content, fileName, $"ethics/{container.Id}", cancellationToken: cancellationToken);
        var version = approval.Documents.Where(d => d.DocumentType == type).Select(d => d.Version).DefaultIfEmpty(0).Max() + 1;

        var document = new EthicsDocument
        {
            EthicsApprovalId = approval.Id,
            DocumentType = type,
            FileName = stored.FileName,
            FilePath = stored.RelativePath,
            Version = version,
            UploadedByUserId = studentId,
            Status = EthicsDocumentStatus.PendingReview
        };

        db.EthicsDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        var uploadedTypes = await db.EthicsDocuments
            .Where(d => d.EthicsApprovalId == approval.Id && d.Status != EthicsDocumentStatus.RevisionRequested)
            .Select(d => d.DocumentType).Distinct().ToListAsync(cancellationToken);

        if (RequiredDocumentTypes.All(uploadedTypes.Contains))
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
            $"Uploaded '{type}' (version {version}).");

        return ToDocumentDto(document);
    }

    public async Task<IReadOnlyList<EthicsDocumentDto>> GetDocumentsAsync(Guid publicationContainerId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(publicationContainerId, requestingUserId);

        return await db.EthicsDocuments
            .Where(d => d.EthicsApproval.PublicationContainerId == publicationContainerId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => ToDocumentDto(d))
            .ToListAsync(cancellationToken);
    }

    public async Task SupervisorReviewDocumentsAsync(Guid publicationContainerId, Guid supervisorId, DocumentReviewDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var approval = await GetApprovalForSupervisorAsync(publicationContainerId, supervisorId, cancellationToken, includeDocuments: true);

        if (approval.Status != EthicsStatus.PendingVerification)
        {
            throw new BusinessRuleException("There is no ethics documentation currently awaiting review.");
        }

        ApplyDocumentReviewOutcome(approval, request.Accept, request.Comments);
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

        approval.IsRequiredPerCoordinator = request.RequireDocumentation;
        approval.CoordinatorDecisionComments = request.Comments;
        approval.CoordinatorDecisionAt = DateTime.UtcNow;

        if (request.RequireDocumentation)
        {
            approval.Status = EthicsStatus.PendingUpload;
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

        if (approval.Status != EthicsStatus.PendingVerification)
        {
            throw new BusinessRuleException("There is no ethics documentation currently awaiting Coordinator review.");
        }

        approval.IsRequiredPerCoordinator = true;
        approval.CoordinatorDecisionComments = request.Comments;
        approval.CoordinatorDecisionAt = DateTime.UtcNow;

        if (request.Approve)
        {
            await db.SaveChangesAsync(cancellationToken);

            var headOfDepartment = await db.StudentProfiles
                .Where(s => s.UserId == container.StudentId)
                .Select(s => s.Department.HeadOfDepartment)
                .FirstOrDefaultAsync(cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "CoordinatorApprovedEthicsDocuments",
                request.Comments);

            if (headOfDepartment is not null)
            {
                await notificationService.NotifyAsync(headOfDepartment.UserId, NotificationType.EthicsHeadOfDepartmentReviewRequested,
                    "Ethics documentation review requested",
                    "The Coordinator has approved a student's ethics documentation. Please log in to review it and record your comments.",
                    nameof(PublicationContainer), publicationContainerId, cancellationToken);
            }
        }
        else
        {
            ApplyDocumentReviewOutcome(approval, accept: false, request.Comments);
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

        if (approval.Status != EthicsStatus.PendingVerification || approval.CoordinatorDecisionAt is null)
        {
            throw new BusinessRuleException("This container's ethics documentation is not awaiting Head of Department review.");
        }

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

    public async Task CoordinatorFinalDecisionAsync(Guid publicationContainerId, Guid coordinatorId, CoordinatorFinalDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var (container, approval) = await GetApprovalForCoordinatorAsync(publicationContainerId, coordinatorId, cancellationToken);

        if (approval.Status != EthicsStatus.PendingVerification || approval.HeadOfDepartmentReviewedAt is null)
        {
            throw new BusinessRuleException("This container's ethics documentation is not awaiting a final Coordinator decision.");
        }

        if (request.Approve)
        {
            approval.Status = EthicsStatus.Verified;
            approval.FinalDecisionAt = DateTime.UtcNow;
            await AdvanceToResearchPaperPipelineAsync(container, cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "EthicsFinallyApproved",
                request.Comments, newStatus: EthicsStatus.Verified.ToString());

            await NotifyStudentEthicsCompletedAsync(container, ethicsRequired: true, cancellationToken);
        }
        else
        {
            ApplyDocumentReviewOutcome(approval, accept: false, request.Comments);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(publicationContainerId, coordinatorId, "EthicsFinalRevisionRequested",
                request.Comments, newStatus: approval.Status.ToString());

            await notificationService.NotifyAsync(container.StudentId, NotificationType.EthicsRevisionRequested,
                "Ethics documentation revision requested",
                "The Coordinator has requested further revisions to your ethics documentation. Please log in to review the comments.",
                nameof(PublicationContainer), publicationContainerId, cancellationToken);
        }
    }

    private static void ApplyDocumentReviewOutcome(EthicsApproval approval, bool accept, string comments)
    {
        if (accept)
        {
            foreach (var document in approval.Documents.Where(d => d.Status == EthicsDocumentStatus.PendingReview))
            {
                document.Status = EthicsDocumentStatus.Accepted;
            }
        }
        else
        {
            foreach (var document in approval.Documents.Where(d => d.Status == EthicsDocumentStatus.PendingReview))
            {
                document.Status = EthicsDocumentStatus.RevisionRequested;
                document.ReviewComments = comments;
            }

            approval.Status = EthicsStatus.PendingUpload;
        }
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

    private static EthicsApprovalDto ToDto(EthicsApproval approval) => new(
        approval.Id, approval.PublicationContainerId, approval.Status.ToString(), approval.ReferenceNumber,
        approval.ApprovalDate, approval.ExpiryDate, approval.IsRequiredPerSupervisor, approval.SupervisorDecisionComments,
        approval.IsRequiredPerCoordinator, approval.CoordinatorDecisionComments, approval.HeadOfDepartmentComments,
        approval.HeadOfDepartmentReviewedAt, approval.FinalDecisionAt);

    private static EthicsDocumentDto ToDocumentDto(EthicsDocument document) => new(
        document.Id, document.DocumentType.ToString(), document.FileName, document.Version,
        document.Status.ToString(), document.UploadedAt, document.ReviewComments);
}
