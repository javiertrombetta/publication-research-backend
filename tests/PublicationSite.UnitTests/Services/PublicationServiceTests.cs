using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Publications;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class PublicationServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IContainerAccessService> _accessService = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IFileStorageService> _fileStorageService = new();
    private readonly PublicationService _sut;

    public PublicationServiceTests()
    {
        _accessService.Setup(a => a.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);
        _fileStorageService.Setup(f => f.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string fileName, string _, IReadOnlyCollection<string>? _, CancellationToken _) =>
                new StoredFile($"stored/{fileName}", fileName));

        _sut = new PublicationService(_fixture.Context, _accessService.Object, _auditService.Object, _notificationService.Object, _fileStorageService.Object);
    }

    public void Dispose() => _fixture.Dispose();

    private (ApplicationUser Student, ApplicationUser Supervisor, ApplicationUser Coordinator, PublicationContainer Container) SeedAtResearchPaperStage(EthicsStatus ethicsStatus = EthicsStatus.NotRequired)
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var supervisor = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator, supervisor, PipelineStage.ResearchPaper);

        _fixture.Context.EthicsApprovals.Add(new EthicsApproval { PublicationContainerId = container.Id, Status = ethicsStatus });
        _fixture.Context.SaveChanges();

        return (student, supervisor, coordinator, container);
    }

    [Fact]
    public async Task GetOrCreateDraftAsync_rejects_when_pipeline_not_yet_at_research_paper()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator, stage: PipelineStage.EthicsApproval);

        var act = () => _sut.GetOrCreateDraftAsync(container.Id, student.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetOrCreateDraftAsync_is_idempotent()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();

        var first = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        var second = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);

        first.Id.Should().Be(second.Id);
        (await _fixture.Context.Publications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateMetadataAsync_rejects_when_no_longer_draft()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "paper.pdf", null, null, null);
        await _sut.SubmitAsync(publication.Id, student.Id);

        var act = () => _sut.UpdateMetadataAsync(publication.Id, student.Id,
            new UpdatePublicationMetadataRequest("New Title", "New Abstract", null, null, null, null));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SubmitAsync_rejects_without_any_version_uploaded()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);

        var act = () => _sut.SubmitAsync(publication.Id, student.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SubmitAsync_rejects_when_ethics_not_resolved()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage(EthicsStatus.PendingVerification);
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "paper.pdf", null, null, null);

        var act = () => _sut.SubmitAsync(publication.Id, student.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SubmitAsync_succeeds_and_notifies_supervisor()
    {
        var (student, supervisor, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "paper.pdf", null, null, null);

        await _sut.SubmitAsync(publication.Id, student.Id);

        var updated = await _sut.GetByContainerAsync(container.Id, student.Id);
        updated.Status.Should().Be(PublicationStatus.UnderReview.ToString());
        _notificationService.Verify(n => n.NotifyAsync(
            supervisor.Id, It.IsAny<NotificationType>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadVersionAsync_after_revision_request_marks_resubmitted()
    {
        var (student, supervisor, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "v1.pdf", null, null, null);
        await _sut.SubmitAsync(publication.Id, student.Id);
        await _sut.SupervisorReviewAsync(publication.Id, supervisor.Id, new PaperReviewDecisionRequest(false, "Needs more detail"));

        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "v2.pdf", null, null, null);

        var updated = await _sut.GetByContainerAsync(container.Id, student.Id);
        updated.Status.Should().Be(PublicationStatus.Resubmitted.ToString());
        var versions = await _sut.GetVersionsAsync(publication.Id, student.Id);
        versions.Should().HaveCount(2);
    }

    [Fact]
    public async Task SupervisorReviewAsync_rejects_reviewer_who_is_not_the_assigned_supervisor()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "v1.pdf", null, null, null);
        await _sut.SubmitAsync(publication.Id, student.Id);

        var impostor = TestDataBuilder.User(_fixture.Context);
        var act = () => _sut.SupervisorReviewAsync(publication.Id, impostor.Id, new PaperReviewDecisionRequest(true, "x"));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task PublishDecisionAsync_by_owner_does_not_require_comments()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await ForceStatusAsync(publication.Id, PublicationStatus.Accepted);

        await _sut.PublishDecisionAsync(publication.Id, student.Id, new PublishDecisionRequest(true, null));

        var updated = await _sut.GetByContainerAsync(container.Id, student.Id);
        updated.IsPublished.Should().BeTrue();
        updated.Status.Should().Be(PublicationStatus.Published.ToString());
    }

    [Fact]
    public async Task PublishDecisionAsync_on_behalf_of_student_requires_comments()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await ForceStatusAsync(publication.Id, PublicationStatus.Accepted);

        var admin = Guid.NewGuid();
        var act = () => _sut.PublishDecisionAsync(publication.Id, admin, new PublishDecisionRequest(true, null));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task PublishDecisionAsync_rejects_when_not_yet_accepted()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);

        var act = () => _sut.PublishDecisionAsync(publication.Id, student.Id, new PublishDecisionRequest(true, null));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task PublishDecisionAsync_marks_container_completed_even_when_not_published()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await ForceStatusAsync(publication.Id, PublicationStatus.Accepted);

        await _sut.PublishDecisionAsync(publication.Id, student.Id, new PublishDecisionRequest(false, null));

        var updatedContainer = await _fixture.Context.PublicationContainers.FindAsync(container.Id);
        updatedContainer!.Status.Should().Be(ContainerStatus.Completed);
        var updatedPublication = await _sut.GetByContainerAsync(container.Id, student.Id);
        updatedPublication.IsPublished.Should().BeFalse();
    }

    private async Task ForceStatusAsync(Guid publicationId, PublicationStatus status)
    {
        var publication = await _fixture.Context.Publications.FindAsync(publicationId);
        publication!.Status = status;
        await _fixture.Context.SaveChangesAsync();
    }
}
