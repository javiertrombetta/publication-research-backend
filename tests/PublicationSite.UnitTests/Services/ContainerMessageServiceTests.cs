using System.Text;
using FluentAssertions;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Messages;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class ContainerMessageServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IContainerAccessService> _accessService = new();
    private readonly Mock<IFileStorageService> _fileStorageService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<ISystemSettingsProvider> _settings = new();
    private readonly Mock<ISystemSettingService> _settingService = new();
    private readonly ContainerMessageService _sut;

    private readonly Department _department;
    private readonly ApplicationUser _student;
    private readonly ApplicationUser _coordinator;
    private readonly ApplicationUser _supervisor;
    private readonly ApplicationUser _head;
    private readonly PublicationContainer _container;

    public ContainerMessageServiceTests()
    {
        _accessService.Setup(a => a.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);

        _fileStorageService.Setup(f => f.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string fileName, string _, IReadOnlyCollection<string>? _, CancellationToken _) =>
                new StoredFile($"stored/{fileName}", fileName));

        // On unless a test says otherwise, which is how it is configured.
        _settings.Setup(s => s.GetBoolAsync(SettingKeys.MessagingEnabled, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _settings.Setup(s => s.GetBoolAsync(SettingKeys.MessagingRecordedInActivityHistory, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _settings.Setup(s => s.GetStringAsync(SettingKeys.MessagingAllowedExtensions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SettingKeys.DefaultMessagingAllowedExtensions);

        _department = TestDataBuilder.Department(_fixture.Context);

        _student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, _student, _department);
        TestDataBuilder.GrantRole(_fixture.Context, _student, RoleNames.Student);

        _coordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, _coordinator, _department);
        TestDataBuilder.GrantRole(_fixture.Context, _coordinator, RoleNames.Coordinator);

        _supervisor = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.SupervisorProfile(_fixture.Context, _supervisor, _department);
        TestDataBuilder.GrantRole(_fixture.Context, _supervisor, RoleNames.Supervisor);

        _head = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.HeadOfDepartmentProfile(_fixture.Context, _head, _department);
        TestDataBuilder.GrantRole(_fixture.Context, _head, RoleNames.HeadOfDepartment);

        _container = TestDataBuilder.Container(_fixture.Context, _student, _coordinator, _supervisor);

        // The shipped rules unless a test says otherwise: both directions open, the student
        // writing to the three people responsible for their publication, everybody with a job here
        // able to write to the student.
        Rules();

        _sut = new ContainerMessageService(
            _fixture.ServiceContext, _accessService.Object, _fileStorageService.Object,
            _notificationService.Object, _auditService.Object, _settingService.Object, _settings.Object);
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>Sets what an administrator has decided about who may write to whom.</summary>
    private void Rules(
        bool studentsMayWrite = true,
        string[]? studentMayWriteTo = null,
        bool staffMayWrite = true,
        string[]? staffMayWriteTo = null) =>
        _settingService.Setup(s => s.GetMessagingSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessagingSettingsDto(
                true, false, SettingKeys.DefaultMessagingAllowedExtensions,
                studentsMayWrite,
                studentMayWriteTo ?? SettingKeys.DefaultMessagingStudentMayWriteToRoles,
                staffMayWrite,
                staffMayWriteTo ?? SettingKeys.DefaultMessagingStaffMayWriteToStudentRoles,
                SettingKeys.SelectableStudentMessagingRoles,
                SettingKeys.SelectableStaffMessagingRoles));

    private static PageRequest Page(int page = 1, int size = 50) => new() { Page = page, PageSize = size };

    private Task<ContainerMessageDto> Send(ApplicationUser from, ApplicationUser to, string body = "A question.") =>
        _sut.SendAsync(_container.Id, from.Id, new SendContainerMessageRequest(to.Id, body), []);

    [Fact]
    public async Task A_student_may_write_to_their_supervisor_coordinator_and_head_of_department()
    {
        var context = await _sut.GetMessagingAsync(_container.Id, _student.Id);

        context.Counterparts.Select(c => c.UserId)
            .Should().BeEquivalentTo(new[] { _supervisor.Id, _coordinator.Id, _head.Id });
        context.Counterparts.Should().Contain(c => c.UserId == _head.Id && c.Role == "Head of Department");
    }

    [Fact]
    public async Task A_member_of_staff_may_write_to_the_student()
    {
        var context = await _sut.GetMessagingAsync(_container.Id, _supervisor.Id);

        context.Counterparts.Should().ContainSingle(c => c.UserId == _student.Id && c.Role == "Student");
    }

    [Fact]
    public async Task Somebody_holding_only_the_placeholder_staff_role_has_nobody_to_write_to()
    {
        // Staff is what an @ais.ac.nz address holds before an administrator says what the person
        // actually is. There is no job there yet, so there is nothing for them to say.
        var placeholder = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, placeholder, RoleNames.Staff);

        var context = await _sut.GetMessagingAsync(_container.Id, placeholder.Id);

        context.Counterparts.Should().BeEmpty();
    }

    [Fact]
    public async Task Writing_to_somebody_not_on_the_list_is_refused()
    {
        var stranger = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, stranger, RoleNames.Supervisor);

        var act = () => Send(_student, stranger);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Whoever_writes_first_can_be_written_back_to()
    {
        // A reviewer is not on the student's list. Once they have written, they are: a message
        // nobody can answer is worse than one that could not be sent.
        var reviewer = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, reviewer, RoleNames.Reviewer);

        await Send(reviewer, _student, "A question about your method.");
        _fixture.Reread();

        var context = await _sut.GetMessagingAsync(_container.Id, _student.Id);

        context.Counterparts.Should().Contain(c => c.UserId == reviewer.Id && c.Role == "Reviewer");
    }

    [Fact]
    public async Task A_message_lands_in_both_of_their_listings_and_nobody_elses()
    {
        await Send(_student, _supervisor);
        _fixture.Reread();

        var studentsView = await _sut.GetMessagesAsync(_container.Id, _student.Id, null, Page());
        var supervisorsView = await _sut.GetMessagesAsync(_container.Id, _supervisor.Id, null, Page());
        var coordinatorsView = await _sut.GetMessagesAsync(_container.Id, _coordinator.Id, null, Page());

        studentsView.Items.Should().ContainSingle().Which.Outgoing.Should().BeTrue();
        supervisorsView.Items.Should().ContainSingle().Which.Outgoing.Should().BeFalse();

        // The coordinator can read this publication's proposals, its ethics file and its paper.
        // That is not the same as reading what the student wrote to their supervisor.
        coordinatorsView.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task The_recipient_is_told_and_the_notification_does_not_repeat_what_was_said()
    {
        await Send(_student, _supervisor, "My participant withdrew and I do not know what to do.");

        _notificationService.Verify(n => n.NotifyAsync(
            _supervisor.Id,
            NotificationType.MessageReceived,
            It.IsAny<string>(),
            It.Is<string>(message => !message.Contains("participant")),
            "ContainerMessages",
            _container.Id,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_activity_history_says_nothing_unless_the_institution_asked_it_to()
    {
        await Send(_student, _supervisor);

        _auditService.Verify(a => a.LogActivityAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task When_the_institution_asks_the_history_records_the_contact_and_not_the_contents()
    {
        _settings.Setup(s => s.GetBoolAsync(SettingKeys.MessagingRecordedInActivityHistory, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Send(_student, _supervisor, "My participant withdrew and I do not know what to do.");

        _auditService.Verify(a => a.LogActivityAsync(
            _container.Id,
            _student.Id,
            "Message sent",
            It.Is<string>(comments => !comments.Contains("participant")),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>()), Times.Once);
    }

    [Fact]
    public async Task Nothing_is_sent_while_an_administrator_has_this_switched_off()
    {
        _settings.Setup(s => s.GetBoolAsync(SettingKeys.MessagingEnabled, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => Send(_student, _supervisor);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task What_was_already_written_is_still_readable_once_it_is_switched_off()
    {
        await Send(_student, _supervisor);
        _fixture.Reread();

        _settings.Setup(s => s.GetBoolAsync(SettingKeys.MessagingEnabled, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var listing = await _sut.GetMessagesAsync(_container.Id, _supervisor.Id, null, Page());
        var context = await _sut.GetMessagingAsync(_container.Id, _supervisor.Id);

        listing.Items.Should().ContainSingle();
        context.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_message_is_refused()
    {
        var act = () => Send(_student, _supervisor, "   ");

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task A_message_longer_than_the_limit_is_refused()
    {
        var act = () => Send(_student, _supervisor, new string('a', SettingKeys.MessageMaximumLength + 1));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task More_files_than_a_message_may_carry_is_refused()
    {
        var files = Enumerable.Range(0, SettingKeys.MessageMaximumAttachments + 1)
            .Select(i => ((Stream)new MemoryStream(Encoding.UTF8.GetBytes("x")), $"shot{i}.png"))
            .ToList();

        var act = () => _sut.SendAsync(
            _container.Id, _student.Id, new SendContainerMessageRequest(_supervisor.Id, "Here."), files);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Files_are_stored_against_the_messaging_list_rather_than_the_document_one()
    {
        // A screenshot is what most questions arrive with, and the document list has no business
        // being widened to allow one.
        await _sut.SendAsync(
            _container.Id, _student.Id, new SendContainerMessageRequest(_supervisor.Id, "Here is what I see."),
            [((Stream)new MemoryStream(Encoding.UTF8.GetBytes("x")), "screenshot.png")]);

        _fileStorageService.Verify(f => f.SaveAsync(
            It.IsAny<Stream>(),
            "screenshot.png",
            $"messages/{_container.Id}",
            It.Is<IReadOnlyCollection<string>?>(list => list != null && list.Contains(".png")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_attachment_opens_for_the_two_people_in_the_conversation_and_nobody_else()
    {
        _fileStorageService.Setup(f => f.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("x")));

        var sent = await _sut.SendAsync(
            _container.Id, _student.Id, new SendContainerMessageRequest(_supervisor.Id, "Here is what I see."),
            [((Stream)new MemoryStream(Encoding.UTF8.GetBytes("x")), "screenshot.png")]);
        _fixture.Reread();

        var attachmentId = sent.Attachments.Single().Id;

        var (_, fileName) = await _sut.OpenAttachmentAsync(_container.Id, _supervisor.Id, attachmentId);
        fileName.Should().Be("screenshot.png");

        var act = () => _sut.OpenAttachmentAsync(_container.Id, _coordinator.Id, attachmentId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Opening_a_conversation_marks_what_the_other_person_sent()
    {
        await Send(_student, _supervisor);
        _fixture.Reread();

        (await _sut.GetUnreadCountAsync(_container.Id, _supervisor.Id)).Should().Be(1);

        var marked = await _sut.MarkReadAsync(_container.Id, _supervisor.Id, _student.Id);
        _fixture.Reread();

        marked.Should().Be(1);
        (await _sut.GetUnreadCountAsync(_container.Id, _supervisor.Id)).Should().Be(0);

        // And not the sender's own, which were never unread to begin with.
        (await _sut.GetUnreadCountAsync(_container.Id, _student.Id)).Should().Be(0);
    }

    [Fact]
    public async Task A_conversation_can_be_read_on_its_own()
    {
        await Send(_student, _supervisor, "For the supervisor.");
        await Send(_student, _coordinator, "For the coordinator.");
        _fixture.Reread();

        var withSupervisor = await _sut.GetMessagesAsync(_container.Id, _student.Id, _supervisor.Id, Page());
        var everything = await _sut.GetMessagesAsync(_container.Id, _student.Id, null, Page());

        withSupervisor.Items.Should().ContainSingle().Which.Body.Should().Be("For the supervisor.");
        everything.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task The_listing_pages()
    {
        for (var i = 0; i < 12; i++)
        {
            await Send(_student, _supervisor, $"Question {i}.");
        }

        _fixture.Reread();

        var page = await _sut.GetMessagesAsync(_container.Id, _student.Id, null, Page(page: 2, size: 10));

        page.Items.Should().HaveCount(2);
        page.TotalCount.Should().Be(12);
    }

    [Fact]
    public async Task Unread_messages_are_counted_against_the_person_who_sent_them()
    {
        await Send(_supervisor, _student, "One.");
        await Send(_supervisor, _student, "Two.");
        await Send(_coordinator, _student, "Three.");
        _fixture.Reread();

        var context = await _sut.GetMessagingAsync(_container.Id, _student.Id);

        context.Counterparts.Single(c => c.UserId == _supervisor.Id).UnreadFromThem.Should().Be(2);
        context.Counterparts.Single(c => c.UserId == _coordinator.Id).UnreadFromThem.Should().Be(1);
        context.Counterparts.Single(c => c.UserId == _head.Id).UnreadFromThem.Should().Be(0);
    }

    // ---------- What an administrator has decided about who may write ----------

    [Fact]
    public async Task A_student_writes_only_to_the_roles_the_institution_named()
    {
        Rules(studentMayWriteTo: [RoleNames.Supervisor]);

        var context = await _sut.GetMessagingAsync(_container.Id, _student.Id);

        context.Counterparts.Should().ContainSingle(c => c.UserId == _supervisor.Id);
    }

    [Fact]
    public async Task A_student_writes_to_nobody_when_the_list_is_empty()
    {
        Rules(studentMayWriteTo: []);

        var context = await _sut.GetMessagingAsync(_container.Id, _student.Id);

        context.Counterparts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_student_writes_to_nobody_when_that_direction_is_switched_off()
    {
        Rules(studentsMayWrite: false);

        var context = await _sut.GetMessagingAsync(_container.Id, _student.Id);
        var act = () => Send(_student, _supervisor);

        context.Counterparts.Should().BeEmpty();
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Staff_write_to_nobody_when_that_direction_is_switched_off()
    {
        Rules(staffMayWrite: false);

        var context = await _sut.GetMessagingAsync(_container.Id, _supervisor.Id);
        var act = () => Send(_supervisor, _student);

        context.Counterparts.Should().BeEmpty();
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Only_the_staff_roles_the_institution_named_may_write_to_the_student()
    {
        Rules(staffMayWriteTo: [RoleNames.Coordinator]);

        var coordinatorSees = await _sut.GetMessagingAsync(_container.Id, _coordinator.Id);
        var supervisorSees = await _sut.GetMessagingAsync(_container.Id, _supervisor.Id);

        coordinatorSees.Counterparts.Should().ContainSingle(c => c.UserId == _student.Id);
        supervisorSees.Counterparts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_student_may_write_to_the_committee_when_the_institution_allows_it()
    {
        // The seats actually filled on this publication, not the role at large: a reviewer
        // elsewhere in the institution is not somebody this student has business with.
        var reviewer = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, reviewer, RoleNames.Reviewer);
        var elsewhere = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, elsewhere, RoleNames.Reviewer);
        TestDataBuilder.CommitteeWith(_fixture.Context, _container, reviewer);

        Rules(studentMayWriteTo: [RoleNames.Supervisor, RoleNames.Reviewer]);

        var context = await _sut.GetMessagingAsync(_container.Id, _student.Id);

        context.Counterparts.Select(c => c.UserId).Should().BeEquivalentTo(new[] { _supervisor.Id, reviewer.Id });
        context.Counterparts.Should().Contain(c => c.UserId == reviewer.Id && c.Role == "Reviewer");
    }

    [Fact]
    public async Task Narrowing_a_list_does_not_gag_a_conversation_already_under_way()
    {
        await Send(_student, _coordinator, "A question.");
        _fixture.Reread();

        Rules(studentMayWriteTo: [RoleNames.Supervisor]);

        var context = await _sut.GetMessagingAsync(_container.Id, _student.Id);

        // The coordinator is off the list, so no new conversation could be started with them.
        // The one already open stays answerable, which is the difference between narrowing a rule
        // and leaving somebody with a message they cannot reply to.
        context.Counterparts.Should().Contain(c => c.UserId == _coordinator.Id);
    }

    [Fact]
    public async Task Switching_a_direction_off_does_gag_a_conversation_already_under_way()
    {
        await Send(_student, _coordinator, "A question.");
        _fixture.Reread();

        Rules(studentsMayWrite: false);

        var context = await _sut.GetMessagingAsync(_container.Id, _student.Id);

        context.Counterparts.Should().BeEmpty();
    }
}
