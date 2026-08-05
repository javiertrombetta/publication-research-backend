using System.Net;
using FluentAssertions;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.DTOs.Containers;
using PublicationSite.Api.DTOs.Proposals;
using PublicationSite.Api.DTOs.Publications;
using PublicationSite.IntegrationTests.Infrastructure;
using Xunit;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.IntegrationTests.Flows;

/// <summary>
/// Walks one student's research paper through all three pipelines exactly as the client document
/// describes them, entirely over real HTTP against a real MySQL database, the strongest available
/// check that the whole system is wired together correctly.
/// </summary>
[Collection(ApiCollection.Name)]
public class FullPublicationJourneyTests(ApiTestFactory factory)
{
    [Fact]
    public async Task Student_paper_flows_through_all_three_pipelines_and_is_published()
    {
        var department = await TestSeeder.CreateDepartmentAsync(factory);
        var coordinator = await TestSeeder.CreateEnabledUserAsync(factory, RoleNames.Coordinator, departmentId: department.Id);
        var supervisor = await TestSeeder.CreateEnabledUserAsync(factory, RoleNames.Supervisor, departmentId: department.Id);
        var headOfDepartment = await TestSeeder.CreateEnabledUserAsync(factory, RoleNames.HeadOfDepartment, departmentId: department.Id);
        var internalMember = await TestSeeder.CreateEnabledUserAsync(factory, RoleNames.Reviewer);
        var externalMember = await TestSeeder.CreateEnabledUserAsync(factory, RoleNames.ExternalCommitteeMember);
        var admin = await TestSeeder.CreateEnabledUserAsync(factory, RoleNames.Admin);

        var studentClient = factory.CreateClient();
        var studentEmail = $"student-{Guid.NewGuid():N}@aisstudent.ac.nz";
        var (registerStatus, _) = await studentClient.PostAsync<object>("/api/auth/register", new
        {
            email = studentEmail,
            password = "SuperSecret123!",
            firstName = "Ada",
            lastName = "Lovelace",
            departmentId = department.Id,
            cohort = "2026",
            studentIdNumber = $"S-{Guid.NewGuid():N}"[..12],
            programme = "MSc Computer Science"
        });
        registerStatus.Should().Be(HttpStatusCode.OK);
        await TestSeeder.ConfirmEmailAsync(factory, studentEmail);

        studentClient.AuthenticateWith(await LoginAsync(studentClient, studentEmail));
        var coordinatorClient = AuthenticatedClient(coordinator.Email!);
        var supervisorClient = AuthenticatedClient(supervisor.Email!);
        var headOfDepartmentClient = AuthenticatedClient(headOfDepartment.Email!);
        var internalMemberClient = AuthenticatedClient(internalMember.Email!);
        var externalMemberClient = AuthenticatedClient(externalMember.Email!);
        var adminClient = AuthenticatedClient(admin.Email!);

        // Set before the container exists, deliberately: a container records the committee rules
        // in force when it is opened, so configuring them afterwards would not reach it. This
        // journey has one member of each type.
        var (committeeSettingsStatus, _) = await adminClient.PutAsync<object>("/api/settings/committees", new
        {
            reviewerMembers = 1, externalMembers = 1, minimumApprovals = 2
        });
        committeeSettingsStatus.Should().Be(HttpStatusCode.OK);

        // ---------- Pipeline 1: Research Proposals ----------
        var (createContainerStatus, containerBody) = await studentClient.PostAsync<PublicationContainerDto>("/api/containers", new { });
        createContainerStatus.Should().Be(HttpStatusCode.Created);
        var container = containerBody!.Data!;
        container.CoordinatorId.Should().Be(coordinator.Id);

        var (_, proposalBody) = await studentClient.PostAsync<ProposalDto>(
            $"/api/containers/{container.Id}/proposals", new { title = "Efficient Graph Algorithms", @abstract = "A study of graph traversal." });
        var proposal = proposalBody!.Data!;

        var (finishStatus, _) = await studentClient.PostAsync<object>($"/api/containers/{container.Id}/proposals/finish-submission", new { });
        finishStatus.Should().Be(HttpStatusCode.OK);

        // Paged now, as every queue endpoint is: the coordinator asks for a page rather than for
        // every proposal in the institution.
        var (_, pendingBody) = await coordinatorClient.GetAsync<PagedResult<ProposalDto>>("/api/proposals/pending");
        pendingBody!.Data!.Items.Should().ContainSingle(p => p.Id == proposal.Id);

        // A round has to say when it runs out. Without a date it would never end, so the supervisors
        // in it could be waited on for ever and nothing would notice.
        var (sendStatus, _) = await coordinatorClient.PostAsync<object>("/api/proposals/send-to-supervisors", new
        {
            proposalIds = new[] { proposal.Id },
            supervisorIds = new[] { supervisor.Id },
            comments = "Please evaluate",
            respondBy = DateTime.UtcNow.AddDays(14)
        });
        sendStatus.Should().Be(HttpStatusCode.OK);

        // And the same send without one is refused, which is what makes the line above a rule
        // rather than a habit.
        var (undatedStatus, _) = await coordinatorClient.PostAsync<object>("/api/proposals/send-to-supervisors", new
        {
            proposalIds = new[] { proposal.Id }, supervisorIds = new[] { supervisor.Id }, comments = "Please evaluate"
        });
        undatedStatus.Should().Be(HttpStatusCode.BadRequest);

        var (_, invitedBody) = await supervisorClient.GetAsync<PagedResult<ProposalDto>>("/api/proposals/invited");
        invitedBody!.Data!.Items.Should().ContainSingle(p => p.Id == proposal.Id);

        var (selectStatus, _) = await supervisorClient.PostAsync<object>(
            $"/api/proposals/{proposal.Id}/supervisor-selection", new { comments = "Happy to supervise" });
        selectStatus.Should().Be(HttpStatusCode.OK);

        var (assignStatus, _) = await coordinatorClient.PostAsync<object>(
            $"/api/proposals/{proposal.Id}/assign-supervisor", new { supervisorId = supervisor.Id, comments = "Great match" });
        assignStatus.Should().Be(HttpStatusCode.OK);

        var (_, containerAfterAssignBody) = await studentClient.GetAsync<PublicationContainerDto>($"/api/containers/{container.Id}");
        containerAfterAssignBody!.Data!.AssignedSupervisorId.Should().Be(supervisor.Id);
        containerAfterAssignBody.Data.CurrentPipeline.Should().Be(2); // EthicsApproval

        // ---------- Pipeline 2: Ethics Approval (not required path) ----------
        var (declareStatus, _) = await studentClient.PostAsync<object>(
            $"/api/containers/{container.Id}/ethics/declaration", new { response = "No" });
        declareStatus.Should().Be(HttpStatusCode.OK);

        var (supervisorDecisionStatus, _) = await supervisorClient.PostAsync<object>(
            $"/api/containers/{container.Id}/ethics/supervisor-decision", new { isRequired = false, comments = "Not applicable" });
        supervisorDecisionStatus.Should().Be(HttpStatusCode.OK);

        var (coordinatorEthicsStatus, _) = await coordinatorClient.PostAsync<object>(
            $"/api/containers/{container.Id}/ethics/coordinator-not-required-review", new { requireDocumentation = false, comments = "Agreed" });
        coordinatorEthicsStatus.Should().Be(HttpStatusCode.OK);

        // Agreeing that no documentation is needed does not close the stage on its own: this
        // institution runs the Head of Department step on that route too, so the ruling goes to
        // them and comes back for the coordinator to close.
        var (_, awaitingHeadBody) = await studentClient.GetAsync<PublicationContainerDto>($"/api/containers/{container.Id}");
        awaitingHeadBody!.Data!.CurrentPipeline.Should().Be(2); // still EthicsApproval
        awaitingHeadBody.Data.EthicsAwaitingStep.Should().Be(EthicsSteps.HeadOfDepartmentReview);

        var (headReviewStatus, headReviewBody) = await headOfDepartmentClient.PostAsync<object>(
            $"/api/containers/{container.Id}/ethics/hod-review", new { comments = "No concerns about the ruling" });
        headReviewStatus.Should().Be(HttpStatusCode.OK, headReviewBody?.Message);

        var (closeEthicsStatus, closeEthicsBody) = await coordinatorClient.PostAsync<object>(
            $"/api/containers/{container.Id}/ethics/coordinator-final-decision", new { approve = true, comments = "Closed" });
        closeEthicsStatus.Should().Be(HttpStatusCode.OK, closeEthicsBody?.Message);

        var (_, containerAfterEthicsBody) = await studentClient.GetAsync<PublicationContainerDto>($"/api/containers/{container.Id}");
        containerAfterEthicsBody!.Data!.CurrentPipeline.Should().Be(3); // ResearchPaper
        containerAfterEthicsBody.Data.EthicsStatus.Should().Be("NotRequired");

        // ---------- Pipeline 3: Research Paper ----------
        var (_, draftBody) = await studentClient.PostAsync<PublicationDto>($"/api/containers/{container.Id}/publications", new { });
        var publication = draftBody!.Data!;

        var (updateMetadataStatus, updateMetadataBody) = await studentClient.PutAsync<PublicationDto>($"/api/publications/{publication.Id}", new
        {
            title = "Efficient Graph Algorithms", @abstract = "A full study of graph traversal techniques.",
            publicationType = "Thesis", publicationYear = 2026, keywords = new[] { "graphs", "algorithms" }
        });
        updateMetadataStatus.Should().Be(HttpStatusCode.OK, updateMetadataBody?.Message);

        var (uploadStatus, _) = await studentClient.PostFileAsync<PublicationVersionDto>(
            $"/api/publications/{publication.Id}/versions", "file", "paper.pdf", [1, 2, 3, 4]);
        uploadStatus.Should().Be(HttpStatusCode.OK);

        var (submitStatus, _) = await studentClient.PostAsync<object>($"/api/publications/{publication.Id}/submit", new { });
        submitStatus.Should().Be(HttpStatusCode.OK);

        var (_, pendingPapersBody) = await supervisorClient.GetAsync<PagedResult<PublicationDto>>("/api/publications/pending");
        pendingPapersBody!.Data!.Items.Should().ContainSingle(p => p.Id == publication.Id);

        var (supervisorReviewStatus, _) = await supervisorClient.PostAsync<object>(
            $"/api/publications/{publication.Id}/supervisor-review", new { accept = true, comments = "Well written" });
        supervisorReviewStatus.Should().Be(HttpStatusCode.OK);

        var (assignCommitteeStatus, committeeBody) = await adminClient.PostAsync<CommitteeDto>(
            $"/api/publications/{publication.Id}/assign-committee", new
            {
                memberUserIds = new[] { internalMember.Id, externalMember.Id }, minApprovalsRequired = 2, comments = "Assigning committee"
            });
        assignCommitteeStatus.Should().Be(HttpStatusCode.OK);
        var committee = committeeBody!.Data!;

        var (internalReviewStatus, _) = await internalMemberClient.PostAsync<object>(
            $"/api/committees/{committee.Id}/review", new { approve = true, comments = "Approved" });
        internalReviewStatus.Should().Be(HttpStatusCode.OK);

        var (externalReviewStatus, _) = await externalMemberClient.PostAsync<object>(
            $"/api/committees/{committee.Id}/review", new { approve = true, comments = "Approved" });
        externalReviewStatus.Should().Be(HttpStatusCode.OK);

        var (finalDecisionStatus, _) = await coordinatorClient.PostAsync<object>(
            $"/api/publications/{publication.Id}/coordinator-final-decision", new { accept = true, comments = "Final approval" });
        finalDecisionStatus.Should().Be(HttpStatusCode.OK);

        var (publishStatus, _) = await studentClient.PostAsync<object>(
            $"/api/publications/{publication.Id}/publish", new { publish = true, comments = (string?)null });
        publishStatus.Should().Be(HttpStatusCode.OK);

        // ---------- Public catalogue ----------
        var anonymousClient = factory.CreateClient();
        var (catalogueStatus, catalogueBody) = await anonymousClient.GetAsync<PagedResult<CatalogueEntryDto>>("/api/catalogue");
        catalogueStatus.Should().Be(HttpStatusCode.OK);
        catalogueBody!.Data!.Items.Should().Contain(p => p.Id == publication.Id && p.Title == "Efficient Graph Algorithms");

        var (citationStatus, citationBody) = await anonymousClient.GetAsync<CitationDto>($"/api/catalogue/{publication.Id}/citation");
        citationStatus.Should().Be(HttpStatusCode.OK);
        citationBody!.Data!.Apa.Should().Contain("2026");
        return;

        static async Task<string> LoginAsync(HttpClient client, string email)
        {
            var (_, body) = await client.PostAsync<AuthResponse>("/api/auth/login", new { email, password = "SuperSecret123!" });
            return body!.Data!.AccessToken;
        }
    }

    private HttpClient AuthenticatedClient(string email)
    {
        var client = factory.CreateClient();
        var (_, body) = client.PostAsync<AuthResponse>("/api/auth/login", new { email, password = "SuperSecret123!" }).GetAwaiter().GetResult();
        client.AuthenticateWith(body!.Data!.AccessToken);
        return client;
    }
}
