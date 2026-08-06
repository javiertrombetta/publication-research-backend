using System.Text;
using FluentAssertions;
using PublicationSite.Api.DTOs.AuditLog;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class AuditLogQueryServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly AuditLogQueryService _sut;

    public AuditLogQueryServiceTests()
    {
        _sut = new AuditLogQueryService(_fixture.ServiceContext);
    }

    public void Dispose() => _fixture.Dispose();

    private void AddEntry(ApplicationUser actor, string entityType, DateTime timestamp)
    {
        _fixture.Context.AuditLogEntries.Add(new AuditLogEntry
        {
            ActorUserId = actor.Id, ActionType = "Did,Something \"quoted\"", EntityType = entityType, Timestamp = timestamp
        });
        _fixture.Context.SaveChanges();
    }

    [Fact]
    public async Task GetAsync_filters_by_actor_and_entity_type()
    {
        var actor1 = TestDataBuilder.User(_fixture.Context);
        var actor2 = TestDataBuilder.User(_fixture.Context);
        AddEntry(actor1, "Publication", DateTime.UtcNow);
        AddEntry(actor2, "Department", DateTime.UtcNow);

        var result = await _sut.GetAsync(new AuditLogQuery { UserId = actor1.Id });

        result.Items.Should().ContainSingle(e => e.EntityType == "Publication");
    }

    [Fact]
    public async Task GetAsync_filters_by_date_range()
    {
        var actor = TestDataBuilder.User(_fixture.Context);
        AddEntry(actor, "Old", DateTime.UtcNow.AddDays(-10));
        AddEntry(actor, "Recent", DateTime.UtcNow);

        var result = await _sut.GetAsync(new AuditLogQuery { From = DateTime.UtcNow.AddDays(-1) });

        result.Items.Should().ContainSingle(e => e.EntityType == "Recent");
    }

    [Fact]
    public async Task ExportCsvAsync_escapes_commas_and_quotes()
    {
        var actor = TestDataBuilder.User(_fixture.Context);
        AddEntry(actor, "Publication", DateTime.UtcNow);

        var bytes = await _sut.ExportCsvAsync(new AuditLogQuery());
        var csv = Encoding.UTF8.GetString(bytes);

        csv.Should().Contain("\"Did,Something \"\"quoted\"\"\"");
    }

    /// <summary>
    /// The export is opened in Excel, and Excel runs a cell that opens with =, +, - or @. Every
    /// column here is text somebody in the log typed, including their own name, so putting a
    /// formula in front of an administrator took nothing more than editing a profile.
    /// </summary>
    [Theory]
    [InlineData("=HYPERLINK(\"http://example.invalid\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A9)")]
    public async Task ExportCsvAsync_stops_a_name_from_being_run_as_a_formula(string formula)
    {
        var actor = TestDataBuilder.User(_fixture.Context);
        actor.FirstName = formula;
        _fixture.Context.SaveChanges();
        AddEntry(actor, "Publication", DateTime.UtcNow);

        var csv = Encoding.UTF8.GetString(await _sut.ExportCsvAsync(new AuditLogQuery()));

        // As the cell reads in the file: quotes inside a value are doubled by CSV itself.
        var escaped = formula.Replace("\"", "\"\"");

        csv.Should().Contain($"\"'{escaped}");
        csv.Should().NotContain($"\"{escaped}");
    }
}
