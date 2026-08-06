using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

/// <summary>
/// Filling in a page of publications in one request instead of two per row.
///
/// The queues that show ethics, and the coordinator's decision queue, used to ask the API about
/// each row on the page: ten rows was twenty requests and around seventy database queries for one
/// screen. Invisible on a demonstration set where those queues hold one or two, and it grows with
/// the department.
///
/// The part worth pinning is not the speed but who gets to see what. A request that reads a set is
/// a request that could read somebody else's set, and the rule that decides has to be the same one
/// that answers about a single publication.
/// </summary>
public class BulkQueueReadsTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly EthicsService _ethics;
    private readonly PublicationService _publications;

    public BulkQueueReadsTests()
    {
        var access = new ContainerAccessService(_fixture.ServiceContext);
        var settings = new Mock<ISystemSettingService>();
        var comments = new DecisionCommentPolicy(
            new SystemSettingsProvider(_fixture.Context, new MemoryCache(new MemoryCacheOptions())));

        _ethics = new EthicsService(_fixture.ServiceContext, access, Mock.Of<IAuditService>(),
            Mock.Of<INotificationService>(), Mock.Of<IFileStorageService>(),
            comments, settings.Object, NullLogger<EthicsService>.Instance);

        _publications = new PublicationService(_fixture.ServiceContext, access, Mock.Of<IAuditService>(),
            Mock.Of<INotificationService>(), Mock.Of<IFileStorageService>(),
            comments, settings.Object, NullLogger<PublicationService>.Instance);
    }

    public void Dispose() => _fixture.Dispose();

    private PublicationContainer SeedContainerWithEthics(ApplicationUser coordinator, string documentName)
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);

        var requirement = new EthicsDocumentRequirement { Name = documentName, IsActive = true };
        _fixture.Context.EthicsDocumentRequirements.Add(requirement);

        var approval = new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = EthicsStatus.PendingVerification
        };
        _fixture.Context.EthicsApprovals.Add(approval);
        _fixture.Context.SaveChanges();

        _fixture.Context.EthicsDocuments.Add(new EthicsDocument
        {
            EthicsApprovalId = approval.Id,
            EthicsDocumentRequirementId = requirement.Id,
            FileName = $"{documentName}.pdf",
            FilePath = "local:ethics/one.pdf",
            UploadedByUserId = student.Id,
            Version = 1,
            Status = EthicsDocumentStatus.Accepted
        });
        _fixture.Context.SaveChanges();

        return container;
    }

    [Fact]
    public async Task The_ethics_of_a_whole_page_comes_back_in_one_call()
    {
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var first = SeedContainerWithEthics(coordinator, "Consent Form");
        var second = SeedContainerWithEthics(coordinator, "Participant Information Sheet");
        _fixture.Context.ChangeTracker.Clear();

        var result = await _ethics.GetEthicsForAsync([first.Id, second.Id], coordinator.Id);

        result.Should().HaveCount(2);
        result.Select(e => e.PublicationContainerId).Should().BeEquivalentTo([first.Id, second.Id]);
        result.Should().OnlyContain(e => e.Approval != null);
        result.SelectMany(e => e.Documents).Select(d => d.DocumentType)
            .Should().BeEquivalentTo(["Consent Form", "Participant Information Sheet"],
                "each publication's documents must land on that publication and no other");
    }

    /// <summary>
    /// The whole point of the endpoint, and the thing a per-row loop got for free. Somebody else's
    /// publication is absent from the answer rather than refusing the request: these ids come from
    /// a listing that was already the caller's, so an id that is not theirs means it moved.
    /// </summary>
    [Fact]
    public async Task Somebody_elses_publication_is_not_in_the_answer()
    {
        var mine = TestDataBuilder.User(_fixture.Context);
        var theirs = TestDataBuilder.User(_fixture.Context);
        // Different names, because the requirement's name is unique across the institution: two
        // publications share a requirement, they do not each get their own copy of one.
        var ours = SeedContainerWithEthics(mine, "Consent Form");
        var notOurs = SeedContainerWithEthics(theirs, "Participant Information Sheet");
        _fixture.Context.ChangeTracker.Clear();

        var result = await _ethics.GetEthicsForAsync([ours.Id, notOurs.Id], mine.Id);

        result.Should().ContainSingle().Which.PublicationContainerId.Should().Be(ours.Id);
    }

    [Fact]
    public async Task Asking_about_nothing_asks_the_database_nothing()
    {
        var anybody = TestDataBuilder.User(_fixture.Context);

        (await _ethics.GetEthicsForAsync([], anybody.Id)).Should().BeEmpty();
        (await _publications.GetPapersForAsync([], anybody.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_publication_with_no_ethics_yet_is_simply_absent()
    {
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var withEthics = SeedContainerWithEthics(coordinator, "Consent Form");

        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var withoutEthics = TestDataBuilder.Container(_fixture.Context, student, coordinator);
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.ChangeTracker.Clear();

        var result = await _ethics.GetEthicsForAsync([withEthics.Id, withoutEthics.Id], coordinator.Id);

        result.Should().ContainSingle().Which.PublicationContainerId.Should().Be(withEthics.Id);
    }

    [Fact]
    public async Task The_papers_of_a_whole_page_come_back_in_one_call_with_their_reviews()
    {
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator,
            stage: PipelineStage.ResearchPaper);

        var paper = new Publication
        {
            PublicationContainerId = container.Id,
            Title = "Latency perception in progressive web applications",
            Status = PublicationStatus.UnderReview
        };
        _fixture.Context.Publications.Add(paper);
        _fixture.Context.SaveChanges();

        var version = new PublicationVersion
        {
            PublicationId = paper.Id, VersionNumber = 1,
            FilePath = "local:papers/one.pdf",
            UploadedByUserId = student.Id
        };
        _fixture.Context.PublicationVersions.Add(version);
        _fixture.Context.SaveChanges();

        var reviewer = TestDataBuilder.User(_fixture.Context);
        _fixture.Context.Reviews.Add(new Review
        {
            PublicationVersionId = version.Id,
            ReviewerUserId = reviewer.Id,
            ReviewerType = ReviewerType.CommitteeMember,
            Decision = ReviewDecision.Approve,
            Comments = "Clear and well argued."
        });
        _fixture.Context.SaveChanges();
        _fixture.Context.ChangeTracker.Clear();

        var result = await _publications.GetPapersForAsync([container.Id], coordinator.Id);

        var only = result.Should().ContainSingle().Subject;
        only.PublicationContainerId.Should().Be(container.Id);
        only.Paper.Title.Should().Be("Latency perception in progressive web applications");
        only.Reviews.Should().ContainSingle().Which.Comments.Should().Be("Clear and well argued.");
    }
}
