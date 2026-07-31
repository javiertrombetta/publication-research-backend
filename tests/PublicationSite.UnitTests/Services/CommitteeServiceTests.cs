using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.DTOs.Settings;
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
    private readonly Mock<ISystemSettingService> _settingService = new();
    private readonly CommitteeService _sut;

    public CommitteeServiceTests()
    {
        _accessService.Setup(a => a.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);

        // These tests are about assignment and review mechanics, so they work with the smallest
        // committee that exists: one internal member. The composition rule is exercised by its
        // own test below.
        _settingService.Setup(s => s.GetCommitteeSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitteeSettingsDto(1, 0, 1));

        _sut = new CommitteeService(_fixture.Context, _accessService.Object, _auditService.Object,
            _notificationService.Object, _settingService.Object);
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

    /// <summary>
    /// Says what composition the publication under test requires. The class default is the
    /// smallest committee that exists — one internal member — so a test only says otherwise when
    /// the mix is the point.
    /// </summary>
    private void RequireCommitteeOf(int internalMembers, int externalMembers, int approvals) =>
        _settingService.Setup(s => s.GetCommitteeSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitteeSettingsDto(internalMembers, externalMembers, approvals));

    private ApplicationUser SeedCommitteeMember(CommitteeMemberRoleType type = CommitteeMemberRoleType.Internal)
    {
        var user = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CommitteeMemberProfile(_fixture.Context, user, type);

        // The role as well as the profile: assignment checks both, because a profile outlives the
        // role that created it and must not keep someone assignable after it is taken away.
        TestDataBuilder.GrantRole(_fixture.Context, user, type == CommitteeMemberRoleType.Internal
            ? RoleNames.InternalCommitteeMember
            : RoleNames.ExternalCommitteeMember);

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
    public async Task AssignAsync_accepts_a_member_with_no_committee_role_at_all()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();

        // No committee-member role and no profile: an ordinary member of staff. Anyone who works
        // here can be asked to evaluate a paper, so holding an extra role is not the entry ticket.
        var staff = TestDataBuilder.User(_fixture.Context);

        var act = () => _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([staff.Id], 1, "Assign"), coordinator.Id);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AssignAsync_counts_someone_without_the_external_role_as_internal()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        var staff = TestDataBuilder.User(_fixture.Context);

        await _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([staff.Id], 1, "Assign"), coordinator.Id);

        var member = _fixture.Context.CommitteeMembers.Single(m => m.UserId == staff.Id);
        member.RoleType.Should().Be(CommitteeMemberRoleType.Internal);
    }

    [Fact]
    public async Task AssignAsync_counts_someone_holding_the_external_role_as_external()
    {
        var (publication, container, coordinator) = SeedApprovedPublication();

        // This publication was opened needing one external member and no internal one, so that the
        // assignment is testing how the person is classified rather than the composition rule.
        container.RequiredInternalCommitteeMembers = 0;
        container.RequiredExternalCommitteeMembers = 1;
        _fixture.Context.SaveChanges();

        var outsider = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, outsider, RoleNames.ExternalCommitteeMember);

        await _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([outsider.Id], 1, "Assign"), coordinator.Id);

        var member = _fixture.Context.CommitteeMembers.Single(m => m.UserId == outsider.Id);
        member.RoleType.Should().Be(CommitteeMemberRoleType.External);
    }

    [Fact]
    public async Task AssignAsync_refuses_a_student()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, student, RoleNames.Student);

        var act = () => _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([student.Id], 1, "Assign"), coordinator.Id);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("cannot include students");
    }


    [Fact]
    public async Task AssignAsync_creates_committee_and_notifies_members()
    {
        RequireCommitteeOf(1, 1, 2);

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
        RequireCommitteeOf(1, 1, 2);

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

    [Fact]
    public async Task AssignAsync_rejects_a_committee_that_does_not_match_the_required_composition()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();
        var member = SeedCommitteeMember();

        RequireCommitteeOf(2, 1, 2);

        var act = () => _sut.AssignAsync(publication.Id, new AssignCommitteeRequest([member.Id], 1, "Assign"), coordinator.Id);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("2 internal and 1 external");
    }

    [Fact]
    public async Task AssignAsync_accepts_someone_whose_committee_role_was_taken_away()
    {
        var (publication, _, coordinator) = SeedApprovedPublication();

        // Profile but no role: what a demotion leaves behind, since profiles are never deleted.
        // It used to disqualify them; they are still a member of staff, so it no longer does.
        var formerMember = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CommitteeMemberProfile(_fixture.Context, formerMember);

        var act = () => _sut.AssignAsync(
            publication.Id, new AssignCommitteeRequest([formerMember.Id], 1, "Assign"), coordinator.Id);

        await act.Should().NotThrowAsync();
    }
}
