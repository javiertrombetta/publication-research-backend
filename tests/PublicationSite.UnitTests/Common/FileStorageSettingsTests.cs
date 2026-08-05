using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PublicationSite.Api.Common.Options;
using Xunit;

namespace PublicationSite.UnitTests.Common;

/// <summary>
/// The configuration binder adds to a collection property rather than replacing it, so an array
/// written as a default in the settings class survives whatever configuration says and is appended
/// to. With the same four extensions in both places a refused upload used to name each of them
/// twice, and a deployment that narrowed the list to PDFs alone would still have accepted the three
/// it thought it had removed.
/// </summary>
public class FileStorageSettingsTests
{
    private static FileStorageSettings Bind(params string[] extensions)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < extensions.Length; i++)
        {
            values[$"FileStorage:AllowedExtensions:{i}"] = extensions[i];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection(FileStorageSettings.SectionName)
            .Get<FileStorageSettings>() ?? new FileStorageSettings();
    }

    [Fact]
    public void Configuration_replaces_the_permitted_document_types_rather_than_adding_to_them()
    {
        Bind(".pdf").DocumentExtensions.Should().Equal(".pdf");
    }

    [Fact]
    public void The_shipped_list_is_named_once_each()
    {
        Bind(".pdf", ".doc", ".docx", ".zip").DocumentExtensions
            .Should().Equal(".pdf", ".doc", ".docx", ".zip");
    }

    /// <summary>
    /// Configuration that says nothing about uploads must not leave the site unable to accept any
    /// file at all, which is what removing the defaults outright would have done.
    /// </summary>
    [Fact]
    public void Saying_nothing_leaves_the_built_in_list_in_place()
    {
        Bind().DocumentExtensions.Should().Equal(FileStorageSettings.DefaultDocumentExtensions);
        Bind().ImageExtensions.Should().Equal(FileStorageSettings.DefaultImageExtensions);
    }
}
