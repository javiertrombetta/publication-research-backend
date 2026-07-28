using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class CommitteeServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IContainerAccessService> _accessService = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly CommitteeService _sut;

    public CommitteeServiceTests()
    {
        _accessService.Setup(a => a.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);
        _sut = new CommitteeService(_fixture.Context, _accessService.Object, _auditService.Object, _notificationService.Object);
    }

    public void Dispose() => _fixture.Dispose();

    private (Publication Publication, PublicationContainer Container, ApplicationUser Coordinator) SeedApprovedPublication()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var supervisor = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator, supervisor, PipelineStage.ResearchPaper);

        var publication = new Publication
        {
            PublicationContainerId = container.Id, Title = "T", Abstract = "A", Status = PublicationStatus.UnderReview
        };
        _fixture.Context.Publications.Add(publication);
        _fixture.Context.SaveChanges();

        var version = new PublicationVersion
        {
            PublicationId = publication.Id, VersionNumber = 1, FilePath = "x.pdf", UploadedByUserId = student.Id
        };
        _fixture.Context.PublicationVersions.Add(version);
        _fixture.Context.SaveChanges();

        _fixture.Context.Reviews.Add(new Review
        {
            PublicationVersionId = version.Id, ReviewerUserId = supervisor.Id, ReviewerType = ReviewerType.Supervisor,
            Decision = ReviewDecision.Approve, Comments = "Approved"
        });
        _fixture.Context.SaveChanges();

        return (publication, container, coordinator);
    }

    private ApplicationUser SeedCommitteeMember(CommitteeMemberRoleType type = CommitteeMemberRoleType.Internal)
    {
        var user = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CommitteeMemberProfile(_fixture.Context, user, type);
        return user;
    }

    [Fact]
    public async Task AssignAsync_rejects_when_publication_not_under_review()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        publication.Status = PublicationStatus.Draft;
        await _fixture.Context.SaveChangesAsync();
        var member = SeedCommitteeMember();

        var act = () => _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([member.Id], 1, "Assign"), coordinator.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task AssignAsync_rejects_when_supervisor_has_not_approved_latest_version()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator, stage: PipelineStage.ResearchPaper);
        var publication = new Publication { PublicationContainerId = container.Id, Title = "T", Abstract = "A", Status = PublicationStatus.UnderReview };
        _fixture.Context.Publications.Add(publication);
        await _fixture.Context.SaveChangesAsync();

        var member = SeedCommitteeMember();
        var act = () => _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([member.Id], 1, "Assign"), coordinator.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task AssignAsync_rejects_member_without_committee_profile()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        var nonMember = TestDataBuilder.User(_fixture.Context);

        var act = () => _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([nonMember.Id], 1, "Assign"), coordinator.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task AssignAsync_creates_committee_and_notifies_members()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        var internalMember = SeedCommitteeMember(CommitteeMemberRoleType.Internal);
        var externalMember = SeedCommitteeMember(CommitteeMemberRoleType.External);

        var result = await _sut.AssignAsync(publication.Id,
            new AssignCommitteeRequest([internalMember.Id, externalMember.Id], 2, "Assigning committee"), coordinator.Id);

        result.Members.Should().HaveCount(2);
        _notificationService.Verify(n => n.NotifyAsync(
            internalMember.Id, NotificationType.CommitteeReviewRequested, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.NotifyAsync(
            externalMember.Id, NotificationType.CommitteeReviewRequested, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_rejects_duplicate_committee_for_same_publication()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        var member = SeedCommitteeMember();
        await _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([member.Id], 1, "First"), coordinator.Id);

        var act = () => _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([member.Id], 1, "Second"), coordinator.Id);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task MemberReviewAsync_rejects_non_member()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        var member = SeedCommitteeMember();
        var committee = await _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([member.Id], 1, "x"), coordinator.Id);

        var stranger = TestDataBuilder.User(_fixture.Context);
        var act = () => _sut.MemberReviewAsync(committee.Id, stranger.Id, new CommitteeMemberReviewRequest(true, "x"));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task MemberReviewAsync_rejects_duplicate_decision()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        var member = SeedCommitteeMember();
        var committee = await _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([member.Id], 1, "x"), coordinator.Id);
        await _sut.MemberReviewAsync(committee.Id, member.Id, new CommitteeMemberReviewRequest(true, "Good"));

        var act = () => _sut.MemberReviewAsync(committee.Id, member.Id, new CommitteeMemberReviewRequest(true, "Again"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task MemberReviewAsync_completes_committee_and_notifies_coordinator_once_all_members_decided()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        var member1 = SeedCommitteeMember();
        var member2 = SeedCommitteeMember(CommitteeMemberRoleType.External);
        var committee = await _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([member1.Id, member2.Id], 2, "x"), coordinator.Id);

        await _sut.MemberReviewAsync(committee.Id, member1.Id, new CommitteeMemberReviewRequest(true, "Good"));
        _notificationService.Verify(n => n.NotifyAsync(
            coordinator.Id, NotificationType.CommitteeFinalReviewRequested, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);

        await _sut.MemberReviewAsync(committee.Id, member2.Id, new CommitteeMemberReviewRequest(false, "Concerns"));

        var updatedCommittee = await _fixture.Context.Committees.FindAsync(committee.Id);
        updatedCommittee!.Status.Should().Be(CommitteeStatus.Completed);
        _notificationService.Verify(n => n.NotifyAsync(
            coordinator.Id, NotificationType.CommitteeFinalReviewRequested, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Default_and_per_committee_role_configuration_round_trip()
    {
        await _sut.SetDefaultConfigAsync(new SetCommitteeRoleConfigRequest("Internal", 2));
        var defaults = await _sut.GetDefaultConfigAsync();
        defaults.Should().ContainSingle(c => c.RoleType == "Internal" && c.RequiredCount == 2);

        var (publication, _, coordinator) = SeedApprovedPublication();
        var member = SeedCommitteeMember();
        var committee = await _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([member.Id], 1, "x"), coordinator.Id);

        await _sut.SetCommitteeConfigAsync(committee.Id, new SetCommitteeRoleConfigRequest("External", 1), coordinator.Id);
        var committeeConfig = await _sut.GetCommitteeConfigAsync(committee.Id);
        committeeConfig.Should().ContainSingle(c => c.RoleType == "External" && c.RequiredCount == 1);
    }
}
