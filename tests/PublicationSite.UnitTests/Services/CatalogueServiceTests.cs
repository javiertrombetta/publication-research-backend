using FluentAssertions;
using Moq;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class CatalogueServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IFileStorageService> _fileStorageService = new();
    private readonly CatalogueService _sut;

    public CatalogueServiceTests()
    {
        _sut = new CatalogueService(_fixture.Context, _fileStorageService.Object);
    }

    public void Dispose() => _fixture.Dispose();

    private Publication SeedPublication(string title, bool isPublished, int? year = 2026, string authorFirstName = "Ada")
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        student.FirstName = authorFirstName;
        _fixture.Context.SaveChanges();
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator, stage: PipelineStage.ResearchPaper);

        var publication = new Publication
        {
            PublicationContainerId = container.Id,
            Title = title,
            Abstract = "Some abstract",
            IsPublished = isPublished,
            PublishedAt = isPublished ? DateTime.UtcNow : null,
            PublicationYear = year,
            Status = isPublished ? PublicationStatus.Published : PublicationStatus.Accepted
        };
        _fixture.Context.Publications.Add(publication);
        _fixture.Context.SaveChanges();
        return publication;
    }

    [Fact]
    public async Task SearchAsync_only_returns_published_publications()
    {
        SeedPublication("Published Paper", isPublished: true);
        SeedPublication("Unpublished Paper", isPublished: false);

        var result = await _sut.SearchAsync(new CatalogueSearchRequest());

        result.Items.Should().ContainSingle(p => p.Title == "Published Paper");
    }

    [Fact]
    public async Task SearchAsync_filters_by_year()
    {
        SeedPublication("Paper 2025", isPublished: true, year: 2025);
        SeedPublication("Paper 2026", isPublished: true, year: 2026);

        var result = await _sut.SearchAsync(new CatalogueSearchRequest { Year = 2025 });

        result.Items.Should().ContainSingle(p => p.Title == "Paper 2025");
    }

    [Fact]
    public async Task SearchAsync_filters_by_author_name()
    {
        SeedPublication("By Ada", isPublished: true, authorFirstName: "Ada");
        SeedPublication("By Grace", isPublished: true, authorFirstName: "Grace");

        var result = await _sut.SearchAsync(new CatalogueSearchRequest { Author = "Ada" });

        result.Items.Should().ContainSingle(p => p.Title == "By Ada");
    }

    [Fact]
    public async Task SearchAsync_paginates_results()
    {
        for (var i = 0; i < 5; i++)
        {
            SeedPublication($"Paper {i}", isPublished: true);
        }

        var page1 = await _sut.SearchAsync(new CatalogueSearchRequest { Page = 1, PageSize = 2 });
        var page2 = await _sut.SearchAsync(new CatalogueSearchRequest { Page = 2, PageSize = 2 });

        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(5);
        page1.Items.Select(i => i.Id).Should().NotIntersectWith(page2.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetByIdAsync_throws_for_unpublished_publication()
    {
        var publication = SeedPublication("Draft", isPublished: false);

        var act = () => _sut.GetByIdAsync(publication.Id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetCitationAsync_formats_apa_and_mla()
    {
        var publication = SeedPublication("Great Research", isPublished: true, year: 2026);

        var citation = await _sut.GetCitationAsync(publication.Id);

        citation.Apa.Should().Contain("2026").And.Contain("Great Research");
        citation.Mla.Should().Contain("Great Research").And.Contain("2026");
    }
}
