using FluentAssertions;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly AuditService _sut;

    public AuditServiceTests()
    {
        _sut = new AuditService(_fixture.Context);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task LogAuditAsync_persists_an_entry()
    {
        var actor = TestDataBuilder.User(_fixture.Context);

        await _sut.LogAuditAsync(actor.Id, "SomethingHappened", nameof(ApplicationUser), actor.Id, comments: "Because reasons");

        var entry = _fixture.Context.AuditLogEntries.Single();
        entry.ActorUserId.Should().Be(actor.Id);
        entry.ActionType.Should().Be("SomethingHappened");
        entry.Comments.Should().Be("Because reasons");
    }

    [Fact]
    public async Task LogActivityAsync_writes_both_activity_history_and_audit_log()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);

        await _sut.LogActivityAsync(container.Id, coordinator.Id, "CoordinatorDidSomething", "Explaining why",
            previousStatus: "A", newStatus: "B");

        _fixture.Context.ActivityHistoryEntries.Should().ContainSingle(a =>
            a.PublicationContainerId == container.Id && a.Action == "CoordinatorDidSomething" && a.Comments == "Explaining why");
        _fixture.Context.AuditLogEntries.Should().ContainSingle(a =>
            a.EntityType == nameof(PublicationContainer) && a.EntityId == container.Id && a.ActionType == "CoordinatorDidSomething");
    }

    [Fact]
    public async Task LogActivityAsync_records_on_behalf_of_user()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var admin = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);

        await _sut.LogActivityAsync(container.Id, admin.Id, "PublishedOnBehalf", "Student unreachable",
            onBehalfOfUserId: student.Id);

        var entry = _fixture.Context.ActivityHistoryEntries.Single();
        entry.ActorUserId.Should().Be(admin.Id);
        entry.OnBehalfOfUserId.Should().Be(student.Id);
    }
}
