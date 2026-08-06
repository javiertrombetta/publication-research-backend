using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Services.Implementations.Storage;
using PublicationSite.Api.Services.Interfaces;
using Xunit;

namespace PublicationSite.UnitTests.Services;

/// <summary>
/// The one gate every upload in the system passes through.
///
/// What may be uploaded and how large it may be is decided here and nowhere else: the ethics
/// documents, the paper versions and the profile photos all arrive at this method, and none of
/// them checks anything for itself. Nothing pinned that down, so a change here could have quietly
/// opened every upload in the system and no test would have said so.
/// </summary>
public class FileStorageServiceTests
{
    /// <summary>Remembers what it was asked to write, and answers as a real backend would.</summary>
    private sealed class Backend : IFileStorageBackend
    {
        public string Name => "local";
        public string? LastStoredName { get; private set; }
        public string? LastSubFolder { get; private set; }
        public long BytesWritten { get; private set; }

        public Task<string> WriteAsync(Stream content, string subFolder, string storedFileName, CancellationToken ct = default)
        {
            LastStoredName = storedFileName;
            LastSubFolder = subFolder;
            BytesWritten = content.Length;
            return Task.FromResult($"{subFolder}/{storedFileName}");
        }

        public Task<Stream> ReadAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
        public Task CheckAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (FileStorageService Service, Backend Backend) Build(
        string? configuredExtensions = null, int configuredMegabytes = 0)
    {
        var backend = new Backend();

        var settings = new Mock<ISystemSettingsProvider>();
        settings.Setup(s => s.GetStringAsync(SettingKeys.AllowedUploadExtensions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configuredExtensions);
        settings.Setup(s => s.GetStringAsync(SettingKeys.StorageProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync("local");
        settings.Setup(s => s.GetIntAsync(SettingKeys.MaxUploadMegabytes, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(configuredMegabytes);

        var options = Options.Create(new FileStorageSettings { MaxFileSizeBytes = 1024 });

        return (new FileStorageService([backend], settings.Object, options), backend);
    }

    private static MemoryStream Bytes(int howMany) => new(new byte[howMany]);

    /// <summary>A stream that will not say how long it is, which is what a chunked upload looks like.</summary>
    private sealed class Unmeasurable(int length) : MemoryStream(new byte[length])
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("notes.exe")]
    [InlineData("script.sh")]
    [InlineData("page.html")]
    [InlineData("noextensionatall")]
    public async Task A_type_that_is_not_allowed_is_refused(string fileName)
    {
        var (service, backend) = Build();

        var act = () => service.SaveAsync(Bytes(10), fileName, "ethics");

        await act.Should().ThrowAsync<BusinessRuleException>();
        backend.LastStoredName.Should().BeNull("nothing should reach the backend");
    }

    [Theory]
    [InlineData("paper.pdf")]
    [InlineData("paper.PDF")]
    [InlineData("notes.docx")]
    public async Task A_type_that_is_allowed_goes_through_whatever_its_spelling(string fileName)
    {
        var (service, _) = Build();

        var stored = await service.SaveAsync(Bytes(10), fileName, "ethics");

        stored.FileName.Should().Be(fileName, "the name the person chose is what the screens show");
    }

    /// <summary>
    /// The stored name is a fresh identifier, so a name carrying a path cannot decide where the
    /// bytes land, and two people uploading "paper.pdf" cannot overwrite each other.
    /// </summary>
    [Theory]
    [InlineData("../../../etc/passwd.pdf")]
    [InlineData("..\\..\\windows\\system32\\thing.pdf")]
    [InlineData("ordinary.pdf")]
    public async Task The_name_on_disk_is_never_the_name_that_was_sent(string fileName)
    {
        var (service, backend) = Build();

        var stored = await service.SaveAsync(Bytes(10), fileName, "ethics");

        backend.LastStoredName.Should().MatchRegex(@"^[0-9a-f\-]{36}\.pdf$");
        stored.RelativePath.Should().StartWith("local:ethics/");
        stored.RelativePath.Should().NotContain("..");
    }

    [Fact]
    public async Task A_file_larger_than_the_limit_is_refused()
    {
        var (service, backend) = Build();

        var act = () => service.SaveAsync(Bytes(1025), "paper.pdf", "papers");

        await act.Should().ThrowAsync<BusinessRuleException>();
        backend.LastStoredName.Should().BeNull();
    }

    /// <summary>
    /// The limit has to hold for a stream that will not admit its length, or it is advisory for
    /// exactly the uploads most likely to abuse it.
    /// </summary>
    [Fact]
    public async Task A_stream_that_will_not_say_its_length_is_still_held_to_the_limit()
    {
        var (service, backend) = Build();

        var act = () => service.SaveAsync(new Unmeasurable(4096), "paper.pdf", "papers");

        await act.Should().ThrowAsync<BusinessRuleException>();
        backend.LastStoredName.Should().BeNull();
    }

    [Fact]
    public async Task The_configured_limit_wins_over_the_one_in_the_file()
    {
        var (service, backend) = Build(configuredMegabytes: 1);

        await service.SaveAsync(Bytes(2048), "paper.pdf", "papers");

        backend.BytesWritten.Should().Be(2048, "the setting raised the ceiling above the built-in one");
    }

    [Fact]
    public async Task The_configured_list_replaces_the_built_in_one()
    {
        var (service, _) = Build(configuredExtensions: ".txt");

        await service.SaveAsync(Bytes(10), "notes.txt", "ethics");

        var act = () => service.SaveAsync(Bytes(10), "paper.pdf", "ethics");
        await act.Should().ThrowAsync<BusinessRuleException>("the administrator narrowed it to text files");
    }

    /// <summary>
    /// A profile photo passes its own list, and must not widen when an administrator adds a
    /// document type. The two questions are unrelated.
    /// </summary>
    [Fact]
    public async Task An_explicit_list_is_not_widened_by_the_configured_one()
    {
        var (service, _) = Build(configuredExtensions: ".pdf,.exe");

        var act = () => service.SaveAsync(Bytes(10), "trouble.exe", "profile-photos", [".png", ".jpg"]);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
