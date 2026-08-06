using System.Text;
using FluentAssertions;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class SupportServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<ISystemSettingsProvider> _settings = new();
    private readonly SupportService _sut;
    private readonly ApplicationUser _student;

    public SupportServiceTests()
    {
        _settings.Setup(s => s.GetStringAsync(SettingKeys.ItSupportEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync("itsupport@ais.ac.nz");
        _settings.Setup(s => s.GetStringAsync(SettingKeys.InstitutionName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Auckland Institute of Studies");

        _emailSender.Setup(e => e.IsConfiguredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _emailSender.Setup(e => e.ForwardAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<EmailAttachment>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _student = TestDataBuilder.User(_fixture.Context, "fatima@aisstudent.ac.nz");

        _sut = new SupportService(
            _fixture.ServiceContext, _emailSender.Object, _auditService.Object, _settings.Object);
    }

    public void Dispose() => _fixture.Dispose();

    private static (Stream, string, long) File(string name, int bytes = 8) =>
        (new MemoryStream(Encoding.UTF8.GetBytes(new string('x', bytes))), name, bytes);

    private Task Send(string body = "The upload button does nothing.", params (Stream, string, long)[] files) =>
        _sut.SendToItSupportAsync(_student.Id, "Cannot upload", body, files);

    [Fact]
    public async Task It_goes_to_the_configured_address_with_the_sender_as_the_reply_to()
    {
        await Send();

        _emailSender.Verify(e => e.ForwardAsync(
            "itsupport@ais.ac.nz",
            It.Is<string>(subject => subject.Contains("Cannot upload")),
            It.IsAny<string>(),
            "fatima@aisstudent.ac.nz",
            It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<EmailAttachment>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_files_go_with_it()
    {
        await Send("Here is what I see.", File("screenshot.png"), File("log.txt"));

        _emailSender.Verify(e => e.ForwardAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.Is<IReadOnlyList<EmailAttachment>?>(files =>
                files != null && files.Count == 2 && files[0].FileName == "screenshot.png"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task What_the_person_wrote_is_encoded_before_it_becomes_an_email()
    {
        // A support desk's mail client renders what it is sent, and a message somebody typed is not
        // a place to trust markup.
        await Send("<script>alert('hello')</script> and the button still does nothing.");

        _emailSender.Verify(e => e.ForwardAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<string>(html => !html.Contains("<script>") && html.Contains("&lt;script&gt;")),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<EmailAttachment>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Nothing_is_taken_when_there_is_no_address_to_send_to()
    {
        _settings.Setup(s => s.GetStringAsync(SettingKeys.ItSupportEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var act = () => Send();

        await act.Should().ThrowAsync<BusinessRuleException>();
        _emailSender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Nothing_is_taken_when_there_is_no_mail_server()
    {
        // The whole delivery is the email here. A form that accepts a message it cannot send is
        // worse than a form that says so.
        _emailSender.Setup(e => e.IsConfiguredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => Send();

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("itsupport@ais.ac.nz");
    }

    [Fact]
    public async Task A_refusal_by_the_mail_server_is_said_out_loud()
    {
        // Everywhere else a failed email is a copy of a notification already in the database.
        // Here there is no copy, so somebody told it went is owed the truth when it did not.
        _emailSender.Setup(e => e.ForwardAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<EmailAttachment>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => Send();

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task An_empty_message_is_refused()
    {
        var act = () => Send("   ");

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task More_files_than_it_takes_is_refused()
    {
        var files = Enumerable.Range(0, SettingKeys.SupportMaximumAttachments + 1)
            .Select(i => File($"shot{i}.png"))
            .ToArray();

        var act = () => Send("Here.", files);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task A_file_larger_than_it_takes_is_refused()
    {
        var tooBig = (Stream)new MemoryStream(1);
        var length = (SettingKeys.SupportMaximumAttachmentMegabytes + 1) * 1024L * 1024L;

        var act = () => Send("Here.", (tooBig, "recording.mov", length));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("recording.mov");
    }

    [Fact]
    public async Task It_is_recorded_that_somebody_wrote_but_not_what_they_wrote()
    {
        await Send("My password is hunter2 and nothing works.");

        _auditService.Verify(a => a.LogAuditAsync(
            _student.Id,
            "ItSupportContacted",
            "Support",
            null,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.Is<string?>(comments => comments != null && !comments.Contains("hunter2")),
            It.IsAny<Guid?>()), Times.Once);
    }

    [Fact]
    public async Task The_options_say_to_write_directly_when_there_is_no_mail_server()
    {
        _emailSender.Setup(e => e.IsConfiguredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var options = await _sut.GetContactOptionsAsync();

        options.ThroughTheSite.Should().BeFalse();
        options.EmailAddress.Should().Be("itsupport@ais.ac.nz");
    }

    [Fact]
    public async Task The_options_offer_nothing_at_all_when_no_address_is_set()
    {
        _settings.Setup(s => s.GetStringAsync(SettingKeys.ItSupportEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var options = await _sut.GetContactOptionsAsync();

        options.ThroughTheSite.Should().BeFalse();
        options.EmailAddress.Should().BeNull();
    }
}
