using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Publications;
using PublicationSite.Api.Entities;
using PublicationSite.Api.DTOs.Settings;
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
    private readonly Mock<ISystemSettingService> _settingService = new();
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

        // The stage as it ships: every reading runs, so these tests walk the full sequence.
        _settingService.Setup(s => s.GetPaperWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperWorkflowSettingsDto(true, true, true, true));

        _sut = new PublicationService(_fixture.Context, _accessService.Object, _auditService.Object, _notificationService.Object, _fileStorageService.Object,
            new DecisionCommentPolicy(new SystemSettingsProvider(_fixture.Context, new MemoryCache(new MemoryCacheOptions()))),
            _settingService.Object,
            NullLogger<PublicationService>.Instance);
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

        // A settled stage carries the date it was settled on, and that is what the paper waits for:
        // the status on its own is reached before the coordinator has confirmed anything. The
        // unsettled cases below pass a status that is not one of the two that close it, so they
        // are unaffected by the date being there.
        _fixture.Context.EthicsApprovals.Add(new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = ethicsStatus,
            FinalDecisionAt = ethicsStatus is EthicsStatus.Verified or EthicsStatus.NotRequired
                ? DateTime.UtcNow
                : null
        });
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

    /// <summary>
    /// A supervisor ruling that ethics is not required puts the record into NotRequired straight
    /// away, but the stage is not closed until the coordinator has confirmed it. The status alone
    /// used to be enough here, which would have let a paper out of a decision still being made.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_rejects_while_the_ethics_decision_is_still_open()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "paper.pdf", null, null, null);

        // Not required, and nobody has confirmed it.
        var approval = _fixture.Context.EthicsApprovals.Single(a => a.PublicationContainerId == container.Id);
        approval.FinalDecisionAt = null;
        _fixture.Context.SaveChanges();

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
    public async Task A_paper_sent_back_can_be_edited_reuploaded_and_submitted_again()
    {
        var (student, supervisor, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "v1.pdf", null, null, null);
        await _sut.SubmitAsync(publication.Id, student.Id);
        await _sut.SupervisorReviewAsync(publication.Id, supervisor.Id, new PaperReviewDecisionRequest(false, "Needs more detail"));

        // What a student acting on those comments actually does, in the order the screen does it.
        // The title and the abstract are as much a part of a revision as the file is, and editing
        // them was refused outright.
        await _sut.UpdateMetadataAsync(publication.Id, student.Id,
            new UpdatePublicationMetadataRequest("A revised title", "A fuller abstract.", null, null, null, null));

        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "v2.pdf", null, null, null);

        // Uploading leaves it with the student, the same way a first draft does.
        (await _sut.GetByContainerAsync(container.Id, student.Id)).Status
            .Should().Be(PublicationStatus.RevisionsRequested.ToString());

        await _sut.SubmitAsync(publication.Id, student.Id);

        var updated = await _sut.GetByContainerAsync(container.Id, student.Id);
        updated.Status.Should().Be(PublicationStatus.Resubmitted.ToString());
        updated.Title.Should().Be("A revised title");
        (await _sut.GetVersionsAsync(publication.Id, student.Id)).Should().HaveCount(2);
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
        var act = () => _sut.PublishDecisionAsync(
            publication.Id, admin, new PublishDecisionRequest(true, null), actingAsAdmin: true);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    /// <summary>
    /// Publishing is the author's decision. Holding the Student role said nothing about whose
    /// paper this is, and the only question the method asked was whether to insist on a reason,
    /// so answering it let any student publish anybody's accepted work.
    /// </summary>
    [Fact]
    public async Task PublishDecisionAsync_refuses_a_student_who_is_not_the_author()
    {
        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await ForceStatusAsync(publication.Id, PublicationStatus.Accepted);

        var somebodyElse = Guid.NewGuid();
        var act = () => _sut.PublishDecisionAsync(
            publication.Id, somebodyElse, new PublishDecisionRequest(true, "Not mine to publish."));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    /// <summary>The publication's own coordinator may, with a reason on record.</summary>
    [Fact]
    public async Task PublishDecisionAsync_allows_the_coordinator_of_that_publication()
    {
        var (student, _, coordinator, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await ForceStatusAsync(publication.Id, PublicationStatus.Accepted);

        await _sut.PublishDecisionAsync(publication.Id, coordinator.Id,
            new PublishDecisionRequest(true, "Published on the author's written instruction."));

        var updated = await _sut.GetByIdAsync(publication.Id, student.Id);
        updated.Status.Should().Be(PublicationStatus.Published.ToString());
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

    [Fact]
    public async Task The_supervisor_accepts_the_paper_outright_where_nothing_follows_them()
    {
        // No committee, no coordinator decision: the supervisor is the whole of the stage.
        _settingService.Setup(s => s.GetPaperWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperWorkflowSettingsDto(SupervisorReviews: true, CommitteeEvaluates: false, CoordinatorDecides: false));

        var (student, supervisor, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "v1.pdf", null, null, null);
        await _sut.SubmitAsync(publication.Id, student.Id);

        await _sut.SupervisorReviewAsync(publication.Id, supervisor.Id,
            new PaperReviewDecisionRequest(true, "Ready to publish"));

        (await _fixture.Context.Publications.FindAsync(publication.Id))!.Status
            .Should().Be(PublicationStatus.Accepted);
    }

    [Fact]
    public async Task The_supervisor_cannot_read_a_paper_where_that_step_is_off()
    {
        _settingService.Setup(s => s.GetPaperWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperWorkflowSettingsDto(SupervisorReviews: false, CommitteeEvaluates: true, CoordinatorDecides: true));

        var (student, supervisor, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "v1.pdf", null, null, null);
        await _sut.SubmitAsync(publication.Id, student.Id);

        var act = () => _sut.SupervisorReviewAsync(publication.Id, supervisor.Id,
            new PaperReviewDecisionRequest(true, "Looks fine"));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task No_paper_is_offered_for_a_committee_where_this_institution_appoints_none()
    {
        var (student, supervisor, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "v1.pdf", null, null, null);
        await _sut.SubmitAsync(publication.Id, student.Id);
        await _sut.SupervisorReviewAsync(publication.Id, supervisor.Id, new PaperReviewDecisionRequest(true, "Fine"));

        (await _sut.GetAwaitingCommitteeAsync()).Should().HaveCount(1);

        _settingService.Setup(s => s.GetPaperWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperWorkflowSettingsDto(SupervisorReviews: true, CommitteeEvaluates: false, CoordinatorDecides: true));

        // Offering it would be offering work the assignment itself refuses.
        (await _sut.GetAwaitingCommitteeAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_paper_reaches_a_committee_where_no_supervisor_reads_it_first()
    {
        _settingService.Setup(s => s.GetPaperWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperWorkflowSettingsDto(SupervisorReviews: false, CommitteeEvaluates: true, CoordinatorDecides: true));

        var (student, _, _, container) = SeedAtResearchPaperStage();
        var publication = await _sut.GetOrCreateDraftAsync(container.Id, student.Id);
        await _sut.UploadVersionAsync(publication.Id, student.Id, new MemoryStream([1]), "v1.pdf", null, null, null);
        await _sut.SubmitAsync(publication.Id, student.Id);

        // No supervisor approval exists, and none is coming. Asking for one regardless hid the
        // paper from the only screen that could appoint its committee.
        (await _sut.GetAwaitingCommitteeAsync()).Should().ContainSingle(p => p.Id == publication.Id);
    }
}
