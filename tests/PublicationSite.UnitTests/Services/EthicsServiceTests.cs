using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Ethics;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class EthicsServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IContainerAccessService> _accessService = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IFileStorageService> _fileStorageService = new();
    private readonly EthicsService _sut;

    public EthicsServiceTests()
    {
        _accessService.Setup(a => a.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);
        _fileStorageService.Setup(f => f.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string fileName, string _, IReadOnlyCollection<string>? _, CancellationToken _) =>
                new StoredFile($"stored/{fileName}", fileName));

        _sut = new EthicsService(_fixture.Context, _accessService.Object, _auditService.Object, _notificationService.Object, _fileStorageService.Object);
    }

    public void Dispose() => _fixture.Dispose();

    private (ApplicationUser Student, ApplicationUser Supervisor, ApplicationUser Coordinator, PublicationContainer Container) SeedAssignedContainer()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var supervisor = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator, supervisor);
        return (student, supervisor, coordinator, container);
    }

    [Fact]
    public async Task SubmitDeclarationAsync_with_Unsure_does_not_create_approval_or_notify()
    {
        var (student, supervisor, _, container) = SeedAssignedContainer();

        await _sut.SubmitDeclarationAsync(container.Id, student.Id, new EthicsDeclarationRequest("Unsure"));

        (await _fixture.Context.EthicsApprovals.CountAsync()).Should().Be(0);
        _notificationService.Verify(n => n.NotifyAsync(
            supervisor.Id, It.IsAny<NotificationType>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitDeclarationAsync_with_Yes_creates_approval_and_notifies_supervisor()
    {
        var (student, supervisor, _, container) = SeedAssignedContainer();

        await _sut.SubmitDeclarationAsync(container.Id, student.Id, new EthicsDeclarationRequest("Yes"));

        (await _fixture.Context.EthicsApprovals.CountAsync(a => a.PublicationContainerId == container.Id)).Should().Be(1);
        _notificationService.Verify(n => n.NotifyAsync(
            supervisor.Id, NotificationType.EthicsEvaluationRequested, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitDeclarationAsync_rejects_invalid_response()
    {
        var (student, _, _, container) = SeedAssignedContainer();

        var act = () => _sut.SubmitDeclarationAsync(container.Id, student.Id, new EthicsDeclarationRequest("Maybe"));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SupervisorRequirementDecision_required_moves_to_pending_upload_and_notifies_student()
    {
        var (student, supervisor, _, container) = SeedAssignedContainer();
        await _sut.SubmitDeclarationAsync(container.Id, student.Id, new EthicsDeclarationRequest("Yes"));

        await _sut.SubmitSupervisorRequirementDecisionAsync(container.Id, supervisor.Id,
            new SupervisorRequirementDecisionRequest(true, "Needs approval"));

        var approval = await _sut.GetApprovalAsync(container.Id, student.Id);
        approval.Status.Should().Be(EthicsStatus.PendingUpload.ToString());
        _notificationService.Verify(n => n.NotifyAsync(
            student.Id, NotificationType.EthicsDocumentationRequired, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SupervisorRequirementDecision_not_required_notifies_coordinator()
    {
        var (student, supervisor, coordinator, container) = SeedAssignedContainer();
        await _sut.SubmitDeclarationAsync(container.Id, student.Id, new EthicsDeclarationRequest("No"));

        await _sut.SubmitSupervisorRequirementDecisionAsync(container.Id, supervisor.Id,
            new SupervisorRequirementDecisionRequest(false, "Not needed"));

        var approval = await _sut.GetApprovalAsync(container.Id, student.Id);
        approval.Status.Should().Be(EthicsStatus.NotRequired.ToString());
        _notificationService.Verify(n => n.NotifyAsync(
            coordinator.Id, NotificationType.EthicsCoordinatorReviewRequested, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadDocumentAsync_rejects_when_not_pending_upload()
    {
        var (student, _, _, container) = SeedAssignedContainer();
        await _sut.SubmitDeclarationAsync(container.Id, student.Id, new EthicsDeclarationRequest("Yes"));

        var act = () => _sut.UploadDocumentAsync(container.Id, student.Id, "ApprovalCertificate",
            new MemoryStream([1, 2, 3]), "cert.pdf");

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task UploadDocumentAsync_moves_to_pending_verification_once_all_required_docs_uploaded()
    {
        var (student, supervisor, _, container) = await RequireEthicsAsync();

        await _sut.UploadDocumentAsync(container.Id, student.Id, "ApprovalCertificate", new MemoryStream([1]), "a.pdf");
        (await _sut.GetApprovalAsync(container.Id, student.Id)).Status.Should().Be(EthicsStatus.PendingUpload.ToString());

        await _sut.UploadDocumentAsync(container.Id, student.Id, "ApplicationForm", new MemoryStream([1]), "b.pdf");
        await _sut.UploadDocumentAsync(container.Id, student.Id, "ParticipantConsentForm", new MemoryStream([1]), "c.pdf");

        (await _sut.GetApprovalAsync(container.Id, student.Id)).Status.Should().Be(EthicsStatus.PendingVerification.ToString());
        _notificationService.Verify(n => n.NotifyAsync(
            supervisor.Id, NotificationType.EthicsDocumentationReadyForReview, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SupervisorReviewDocuments_reject_sends_back_to_pending_upload()
    {
        var (student, supervisor, _, container) = await AllDocumentsUploadedAsync();

        await _sut.SupervisorReviewDocumentsAsync(container.Id, supervisor.Id, new DocumentReviewDecisionRequest(false, "Missing signature"));

        (await _sut.GetApprovalAsync(container.Id, student.Id)).Status.Should().Be(EthicsStatus.PendingUpload.ToString());
        var docs = await _sut.GetDocumentsAsync(container.Id, student.Id);
        docs.Should().OnlyContain(d => d.Status == EthicsDocumentStatus.RevisionRequested.ToString());
    }

    [Fact]
    public async Task SupervisorReviewDocuments_accept_notifies_coordinator()
    {
        var (student, supervisor, coordinator, container) = await AllDocumentsUploadedAsync();

        await _sut.SupervisorReviewDocumentsAsync(container.Id, supervisor.Id, new DocumentReviewDecisionRequest(true, "All good"));

        var docs = await _sut.GetDocumentsAsync(container.Id, student.Id);
        docs.Should().OnlyContain(d => d.Status == EthicsDocumentStatus.Accepted.ToString());
        _notificationService.Verify(n => n.NotifyAsync(
            coordinator.Id, NotificationType.EthicsCoordinatorReviewRequested, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CoordinatorReviewNotRequired_confirming_not_required_advances_pipeline()
    {
        var (student, supervisor, coordinator, container) = SeedAssignedContainer();
        await _sut.SubmitDeclarationAsync(container.Id, student.Id, new EthicsDeclarationRequest("No"));
        await _sut.SubmitSupervisorRequirementDecisionAsync(container.Id, supervisor.Id, new SupervisorRequirementDecisionRequest(false, "Not needed"));

        await _sut.CoordinatorReviewNotRequiredAsync(container.Id, coordinator.Id, new CoordinatorNotRequiredReviewRequest(false, "Agreed"));

        var updatedContainer = await _fixture.Context.PublicationContainers.FindAsync(container.Id);
        updatedContainer!.CurrentPipeline.Should().Be(PipelineStage.ResearchPaper);
        (await _sut.GetApprovalAsync(container.Id, student.Id)).Status.Should().Be(EthicsStatus.NotRequired.ToString());
    }

    [Fact]
    public async Task CoordinatorReviewNotRequired_overriding_to_required_requests_documents()
    {
        var (student, supervisor, coordinator, container) = SeedAssignedContainer();
        await _sut.SubmitDeclarationAsync(container.Id, student.Id, new EthicsDeclarationRequest("No"));
        await _sut.SubmitSupervisorRequirementDecisionAsync(container.Id, supervisor.Id, new SupervisorRequirementDecisionRequest(false, "Not needed"));

        await _sut.CoordinatorReviewNotRequiredAsync(container.Id, coordinator.Id, new CoordinatorNotRequiredReviewRequest(true, "Actually needed"));

        (await _sut.GetApprovalAsync(container.Id, student.Id)).Status.Should().Be(EthicsStatus.PendingUpload.ToString());
    }

    [Fact]
    public async Task CoordinatorReviewDocuments_approve_notifies_head_of_department()
    {
        var (student, _, coordinator, container) = await SupervisorApprovedDocumentsAsync();

        var department = await _fixture.Context.StudentProfiles.Where(s => s.UserId == student.Id).Select(s => s.DepartmentId).FirstAsync();
        var hodDept = await _fixture.Context.Departments.FindAsync(department);
        var hodUser = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.HeadOfDepartmentProfile(_fixture.Context, hodUser, hodDept!);

        await _sut.CoordinatorReviewDocumentsAsync(container.Id, coordinator.Id, new CoordinatorDocumentReviewRequest(true, "Looks good"));

        _notificationService.Verify(n => n.NotifyAsync(
            hodUser.Id, NotificationType.EthicsHeadOfDepartmentReviewRequested, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Full_ethics_chain_ends_verified_and_advances_pipeline()
    {
        var (student, _, coordinator, container) = await SupervisorApprovedDocumentsAsync();

        var departmentId = await _fixture.Context.StudentProfiles.Where(s => s.UserId == student.Id).Select(s => s.DepartmentId).FirstAsync();
        var department = await _fixture.Context.Departments.FindAsync(departmentId);
        var hod = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.HeadOfDepartmentProfile(_fixture.Context, hod, department!);

        await _sut.CoordinatorReviewDocumentsAsync(container.Id, coordinator.Id, new CoordinatorDocumentReviewRequest(true, "Looks good"));
        await _sut.HeadOfDepartmentReviewAsync(container.Id, hod.Id, new HeadOfDepartmentReviewRequest("No concerns"));
        await _sut.CoordinatorFinalDecisionAsync(container.Id, coordinator.Id, new CoordinatorFinalDecisionRequest(true, "Approved"));

        (await _sut.GetApprovalAsync(container.Id, student.Id)).Status.Should().Be(EthicsStatus.Verified.ToString());
        var updatedContainer = await _fixture.Context.PublicationContainers.FindAsync(container.Id);
        updatedContainer!.CurrentPipeline.Should().Be(PipelineStage.ResearchPaper);
    }

    [Fact]
    public async Task CoordinatorFinalDecisionAsync_rejects_when_hod_has_not_reviewed_yet()
    {
        var (student, _, coordinator, container) = await SupervisorApprovedDocumentsAsync();
        await _sut.CoordinatorReviewDocumentsAsync(container.Id, coordinator.Id, new CoordinatorDocumentReviewRequest(true, "Looks good"));

        var act = () => _sut.CoordinatorFinalDecisionAsync(container.Id, coordinator.Id, new CoordinatorFinalDecisionRequest(true, "Approved"));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    private async Task<(ApplicationUser Student, ApplicationUser Supervisor, ApplicationUser Coordinator, PublicationContainer Container)> RequireEthicsAsync()
    {
        var (student, supervisor, coordinator, container) = SeedAssignedContainer();
        await _sut.SubmitDeclarationAsync(container.Id, student.Id, new EthicsDeclarationRequest("Yes"));
        await _sut.SubmitSupervisorRequirementDecisionAsync(container.Id, supervisor.Id, new SupervisorRequirementDecisionRequest(true, "Required"));
        return (student, supervisor, coordinator, container);
    }

    private async Task<(ApplicationUser Student, ApplicationUser Supervisor, ApplicationUser Coordinator, PublicationContainer Container)> AllDocumentsUploadedAsync()
    {
        var (student, supervisor, coordinator, container) = await RequireEthicsAsync();
        await _sut.UploadDocumentAsync(container.Id, student.Id, "ApprovalCertificate", new MemoryStream([1]), "a.pdf");
        await _sut.UploadDocumentAsync(container.Id, student.Id, "ApplicationForm", new MemoryStream([1]), "b.pdf");
        await _sut.UploadDocumentAsync(container.Id, student.Id, "ParticipantConsentForm", new MemoryStream([1]), "c.pdf");
        return (student, supervisor, coordinator, container);
    }

    private async Task<(ApplicationUser Student, ApplicationUser Supervisor, ApplicationUser Coordinator, PublicationContainer Container)> SupervisorApprovedDocumentsAsync()
    {
        var (student, supervisor, coordinator, container) = await AllDocumentsUploadedAsync();
        await _sut.SupervisorReviewDocumentsAsync(container.Id, supervisor.Id, new DocumentReviewDecisionRequest(true, "All good"));
        return (student, supervisor, coordinator, container);
    }
}
