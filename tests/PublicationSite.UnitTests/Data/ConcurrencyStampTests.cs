using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Data;

/// <summary>
/// Two people deciding on the same publication in the same second.
///
/// Every workflow decision reads a record, satisfies itself the step is still open, and writes.
/// The guards cannot see each other, so two requests that both read before either wrote used to
/// both go through: the publication's history recorded the same decision twice, and where the two
/// disagreed the later one silently won.
///
/// The stamp is part of the WHERE clause of the UPDATE and moves on every save, so the second
/// writer matches no rows and is told so.
/// </summary>
public class ConcurrencyStampTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();

    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Two contexts over one database, which is what two requests are: each read the row before
    /// either wrote it.
    /// </summary>
    private (ApplicationDbContextPair Pair, Guid ApprovalId) TwoReadersOfOneApproval()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context));

        var approval = new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = EthicsStatus.PendingSupervisorDecision
        };

        _fixture.Context.EthicsApprovals.Add(approval);
        _fixture.Context.SaveChanges();
        _fixture.Context.ChangeTracker.Clear();

        return (new ApplicationDbContextPair(_fixture), approval.Id);
    }

    /// <summary>Two contexts on the same connection, so both see one database.</summary>
    private sealed class ApplicationDbContextPair(SqliteDbContextFactory fixture)
    {
        public Api.Data.ApplicationDbContext First { get; } = fixture.NewContext();
        public Api.Data.ApplicationDbContext Second { get; } = fixture.NewContext();
    }

    [Fact]
    public async Task The_second_of_two_simultaneous_decisions_is_refused()
    {
        var (pair, approvalId) = TwoReadersOfOneApproval();

        // Both read while the step was open, which is what makes this a race rather than a repeat.
        var mine = await pair.First.EthicsApprovals.FirstAsync(a => a.Id == approvalId);
        var theirs = await pair.Second.EthicsApprovals.FirstAsync(a => a.Id == approvalId);

        mine.Status = EthicsStatus.PendingUpload;
        mine.SupervisorDecisionAt = DateTime.UtcNow;
        await pair.First.SaveChangesAsync();

        theirs.Status = EthicsStatus.NotRequired;
        theirs.SupervisorDecisionAt = DateTime.UtcNow;
        var act = () => pair.Second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        _fixture.Context.ChangeTracker.Clear();
        (await _fixture.Context.EthicsApprovals.FirstAsync(a => a.Id == approvalId))
            .Status.Should().Be(EthicsStatus.PendingUpload, "the decision that arrived first stands");
    }

    [Fact]
    public async Task The_stamp_moves_on_every_save()
    {
        var (pair, approvalId) = TwoReadersOfOneApproval();

        var approval = await pair.First.EthicsApprovals.FirstAsync(a => a.Id == approvalId);
        var before = approval.ConcurrencyStamp;

        approval.ReferenceNumber = "ETH-2026-001";
        await pair.First.SaveChangesAsync();

        approval.ConcurrencyStamp.Should().NotBe(before,
            "a token the first writer leaves unchanged refuses nobody");
    }

    /// <summary>One after the other is ordinary work, and must keep working.</summary>
    [Fact]
    public async Task Deciding_after_reading_afresh_goes_through()
    {
        var (pair, approvalId) = TwoReadersOfOneApproval();

        var mine = await pair.First.EthicsApprovals.FirstAsync(a => a.Id == approvalId);
        mine.Status = EthicsStatus.PendingUpload;
        await pair.First.SaveChangesAsync();

        var afresh = await pair.Second.EthicsApprovals.FirstAsync(a => a.Id == approvalId);
        afresh.Status = EthicsStatus.PendingVerification;
        var act = () => pair.Second.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Every row a decision is written on, not only the ethics one. Named here so adding an entity
    /// to the workflow without a stamp fails a test rather than quietly going unprotected.
    /// </summary>
    [Theory]
    [InlineData(typeof(EthicsApproval))]
    [InlineData(typeof(Publication))]
    [InlineData(typeof(Committee))]
    [InlineData(typeof(CommitteeMember))]
    [InlineData(typeof(ResearchProposal))]
    [InlineData(typeof(PublicationContainer))]
    [InlineData(typeof(ProposalSupervisorSelection))]
    public void The_rows_a_decision_writes_all_carry_a_stamp(Type entity)
    {
        typeof(IHaveAConcurrencyStamp).IsAssignableFrom(entity).Should().BeTrue();

        _fixture.Context.Model.FindEntityType(entity)!
            .FindProperty(nameof(IHaveAConcurrencyStamp.ConcurrencyStamp))!
            .IsConcurrencyToken.Should().BeTrue();
    }
}
