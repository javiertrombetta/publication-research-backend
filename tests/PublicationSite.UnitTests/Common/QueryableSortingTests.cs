using System.Linq.Expressions;
using FluentAssertions;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using Xunit;

namespace PublicationSite.UnitTests.Common;

/// <summary>
/// Nothing in this system is sorted by a column whose values are all different. Listings order by
/// a date, a status, a role, a name, and rows that tie were left wherever the database found them,
/// which it is under no obligation to decide the same way twice. Each page is its own query, so
/// two rows tying across the boundary between page one and page two could both come back on page
/// one, and the other would not appear at all. Nobody reports that, because the row that went
/// missing is the one they did not know to look for.
/// </summary>
public class QueryableSortingTests
{
    private record Row(Guid Id, string Status);

    private static readonly Guid First = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Second = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Third = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Dictionary<string, Expression<Func<Row, object?>>> Columns =
        new(StringComparer.OrdinalIgnoreCase) { ["status"] = r => r.Status };

    /// <summary>Every row ties on the column asked for, so only the tiebreaker settles the order.</summary>
    private static List<Guid> SortedIds(IEnumerable<Row> rows, bool descending = false) =>
        rows.AsQueryable()
            .SortBy(new PageRequest { SortBy = "status", SortDescending = descending },
                r => r.Status, Columns, r => r.Id)
            .Select(r => r.Id)
            .ToList();

    [Fact]
    public void Rows_that_tie_are_returned_in_a_settled_order()
    {
        var rows = new[] { new Row(Third, "Open"), new Row(First, "Open"), new Row(Second, "Open") };

        SortedIds(rows).Should().Equal(First, Second, Third);
    }

    /// <summary>
    /// Whichever way round the source arrives, which is the part that matters: the database is
    /// free to hand back tied rows in a different order for the second page than for the first.
    /// </summary>
    [Fact]
    public void The_order_does_not_depend_on_the_order_they_arrived_in()
    {
        var rows = new[] { new Row(Third, "Open"), new Row(First, "Open"), new Row(Second, "Open") };

        SortedIds(rows).Should().Equal(SortedIds(rows.Reverse()));
    }

    /// <summary>
    /// Reversing the sort reverses the column, not the tiebreaker. The tiebreaker exists to settle
    /// an order, and one that flipped with the direction would settle a different one each way.
    /// </summary>
    [Fact]
    public void Descending_still_settles_ties_the_same_way()
    {
        var rows = new[] { new Row(Second, "Open"), new Row(Third, "Open"), new Row(First, "Open") };

        SortedIds(rows, descending: true).Should().Equal(First, Second, Third);
    }

    [Fact]
    public void The_fallback_order_is_settled_too()
    {
        var rows = new[] { new Row(Third, "Open"), new Row(First, "Open"), new Row(Second, "Open") }.AsQueryable();

        var sorted = rows
            .SortBy(new PageRequest(), r => r.Status, Columns, r => r.Id)
            .Select(r => r.Id)
            .ToList();

        sorted.Should().Equal(First, Second, Third);
    }
}
