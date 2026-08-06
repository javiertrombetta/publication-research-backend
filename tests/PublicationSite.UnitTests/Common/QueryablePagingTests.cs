using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.Entities;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Common;

/// <summary>
/// Which page comes back when the number asked for does not exist.
///
/// Below one was already brought back to the first page. Past the end was not: page forty of four
/// came back as page forty, empty, with the real total beside it, and the pager drew "Page 40 of 4"
/// over nothing. It happens on an ordinary path rather than only from a hand-typed URL: somebody
/// working through the last page of a queue empties it, and the link they follow next names a page
/// that no longer exists.
/// </summary>
public class QueryablePagingTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();

    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Against a real provider rather than a list: the clamp reads the count the database gives,
    /// and EF's async operators refuse a plain in-memory queryable, so a test on one would be
    /// testing something else.
    /// </summary>
    private IQueryable<Department> Rows(int howMany)
    {
        for (var n = 1; n <= howMany; n++)
        {
            _fixture.Context.Departments.Add(new Department { Name = $"Department {n:D4}", Code = $"D{n:D4}" });
        }

        _fixture.Context.SaveChanges();
        return _fixture.Context.Departments.OrderBy(d => d.Code);
    }

    private static PageRequest Ask(int page, int size = 10) => new() { Page = page, PageSize = size };

    [Fact]
    public async Task A_page_past_the_end_comes_back_as_the_last_page_that_exists()
    {
        var result = await Rows(32).ToPageAsync(Ask(99999));

        result.Page.Should().Be(4);
        result.Items.Select(d => d.Code).Should().Equal("D0031", "D0032");
        result.TotalCount.Should().Be(32);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_page_below_the_first_still_comes_back_as_the_first(int asked)
    {
        var result = await Rows(32).ToPageAsync(Ask(asked));

        result.Page.Should().Be(1);
        result.Items.First().Code.Should().Be("D0001");
    }

    [Fact]
    public async Task The_page_asked_for_is_left_alone_when_it_exists()
    {
        var result = await Rows(32).ToPageAsync(Ask(3));

        result.Page.Should().Be(3);
        result.Items.Select(d => d.Code).Should().Equal(
            "D0021", "D0022", "D0023", "D0024", "D0025", "D0026", "D0027", "D0028", "D0029", "D0030");
    }

    /// <summary>
    /// The one case where an empty page is the truth. Clamping to a last page that does not exist
    /// would report page one of nothing, which reads as a listing that failed to load.
    /// </summary>
    [Fact]
    public async Task Nothing_matching_comes_back_as_the_first_page_and_no_rows()
    {
        var result = await Rows(0).ToPageAsync(Ask(7));

        result.Page.Should().Be(1);
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    /// <summary>A last page that is exactly full is still the last page, not the one after it.</summary>
    [Fact]
    public async Task An_exact_multiple_does_not_gain_a_page()
    {
        var result = await Rows(30).ToPageAsync(Ask(99));

        result.Page.Should().Be(3);
        result.Items.Should().HaveCount(10);
    }

    [Fact]
    public async Task The_projecting_overload_clamps_the_same_way()
    {
        var result = await Rows(32).ToPageAsync(d => d.Code, Ask(99999));

        result.Page.Should().Be(4);
        result.Items.Should().Equal("D0031", "D0032");
    }
}
