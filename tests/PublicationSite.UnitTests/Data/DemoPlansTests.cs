using FluentAssertions;
using PublicationSite.Api.Data;
using Xunit;

namespace PublicationSite.UnitTests.Data;

/// <summary>
/// What the demonstration dataset promises about itself, asserted rather than believed.
///
/// The seed can only be checked properly by building it against a database, and that is where its
/// dates and its workflow states are verified. What can be checked here is the part that rotted
/// last time: the words. Every comment used to be a shared constant, so one sentence appeared on
/// eighteen publications and the set read as a form letter. These tests hold the line on that, and
/// on the smaller promises a reader would otherwise have to take on trust: that a publication is
/// the proposal it chose, that a committee votes as its plan says, and that a plan reaching a step
/// carries something for somebody to have said at it.
/// </summary>
public class DemoPlansTests
{
    private static readonly DemoPublicationPlan[] All =
    [
        .. DemoPlans.ForAlexMoreau,
        .. DemoPlans.ForFatimaAlRashid,
        .. DemoPlans.ForNoahKingi,
        .. DemoPlans.ForYukiTanaka,
        .. DemoPlans.ForMateoRossi,
        .. DemoPlans.ForLucasFerreira,
        .. DemoPlans.ForAmaraOkafor
    ];

    /// <summary>Everything a person is recorded as having written, across the whole dataset.</summary>
    private static IEnumerable<string> Sentences =>
        All.SelectMany(p => new[]
            {
                p.Words.Dispatch, p.Words.PrimaryOffer, p.Words.AlternateOffer, p.Words.Discard,
                p.Words.Allocation, p.Words.EthicsRequirement, p.Words.EthicsNotRequired,
                p.Words.EthicsDocuments, p.Words.EthicsCoordinator, p.Words.EthicsHead,
                p.Words.EthicsFinal, p.Words.PaperNotes, p.Words.PaperSupervisor,
                p.Words.CommitteeAppointment, p.Words.PaperDecision, p.Words.PublishDecision
            }
            .Concat(p.Votes.Select(v => v.Comments))
            .Concat(p.Proposals.Select(i => i.Title))
            .Concat(p.Proposals.Select(i => i.Abstract)))
        .Where(s => !string.IsNullOrWhiteSpace(s))!;

    /// <summary>
    /// The fault this dataset was rebuilt to remove. A shared sentence is invisible in the source,
    /// where it reads as one tidy constant, and obvious on screen, where the same supervisor
    /// appears to have written the same thing about twenty unrelated studies.
    /// </summary>
    [Fact]
    public void No_sentence_is_used_on_two_publications()
    {
        var repeated = Sentences
            .GroupBy(s => s)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Count()}x {g.Key[..Math.Min(60, g.Key.Length)]}")
            .ToList();

        repeated.Should().BeEmpty();
    }

    [Fact]
    public void Every_publication_is_the_proposal_it_chose()
    {
        foreach (var plan in All.Where(p => p.Proposals.Length > 0))
        {
            plan.Chosen.Should().BeInRange(0, plan.Proposals.Length - 1, plan.Title);
            plan.Proposals[plan.Chosen].Title.Should().Be(plan.Title);
        }
    }

    [Fact]
    public void Every_publication_that_reaches_its_supervisors_submits_three_proposals()
    {
        foreach (var plan in All.Where(p => p.Stage != DemoStage.ProposalsDrafted))
        {
            plan.Proposals.Should().HaveCount(3, plan.Title);
        }
    }

    [Fact]
    public void Every_committee_that_has_voted_has_one_vote_per_seat()
    {
        foreach (var plan in All.Where(p => p.Votes.Length > 0))
        {
            plan.Votes.Select(v => v.Seat).Should().BeEquivalentTo(plan.Committee, plan.Title);
        }
    }

    /// <summary>
    /// Two reviewers and one external is what the institution asks for, so a plan naming anything
    /// else would be refused when the seed ran, one restart into a build somebody else started.
    /// </summary>
    [Fact]
    public void Every_committee_is_two_reviewers_and_one_external()
    {
        foreach (var plan in All.Where(p => p.Committee.Length > 0))
        {
            plan.Committee.Count(s => s is DemoSeat.ReviewerOne or DemoSeat.ReviewerTwo or DemoSeat.ReviewerThree)
                .Should().Be(2, plan.Title);
            plan.Committee.Count(s => s is DemoSeat.ExternalOne or DemoSeat.ExternalTwo)
                .Should().Be(1, plan.Title);
            plan.Committee.Should().OnlyHaveUniqueItems(plan.Title);
        }
    }

    /// <summary>
    /// Not every committee agrees, and a dataset where every vote is an approval cannot show the
    /// coordinator's final decision being a decision at all.
    /// </summary>
    [Fact]
    public void Some_committee_member_has_voted_against()
    {
        All.SelectMany(p => p.Votes).Should().Contain(v => !v.Approve);
    }

    /// <summary>
    /// Dates are what a listing is ordered by, and a set that all starts on one day sorts the same
    /// both ways, so the control that orders by it cannot be told from a broken one.
    /// </summary>
    [Fact]
    public void Publications_start_on_days_of_their_own()
    {
        All.Select(p => p.StartedDaysAgo).Distinct().Count()
            .Should().BeGreaterThan(All.Length * 3 / 4);

        All.Should().OnlyContain(p => p.StartedDaysAgo > 0);
    }

    private static readonly DemoStage[] ReachesPaper =
    [
        DemoStage.PaperWithSupervisor, DemoStage.PaperAwaitingCommittee, DemoStage.CommitteeReviewing,
        DemoStage.PaperAwaitingFinalDecision, DemoStage.PaperAccepted, DemoStage.Published
    ];

    [Fact]
    public void Every_paper_names_its_keywords_and_its_year()
    {
        foreach (var plan in All.Where(p => ReachesPaper.Contains(p.Stage)))
        {
            plan.Keywords.Should().NotBeNullOrEmpty(plan.Title);
            plan.Year.Should().NotBeNull(plan.Title);
        }
    }

    /// <summary>
    /// Both of these were filled in by the builder rather than by the plan, and both came out the
    /// same on every publication: the first two research areas in the table, and a publication type
    /// that was not one of the four the student's form offers. A paper on procurement was filed
    /// under Computing Education, four of the six areas were attached to nothing, and the
    /// catalogue's two filters each had one value in them.
    /// </summary>
    [Fact]
    public void Every_paper_says_what_it_is_and_what_it_is_about()
    {
        foreach (var plan in All.Where(p => ReachesPaper.Contains(p.Stage)))
        {
            plan.Areas.Should().NotBeNullOrEmpty(plan.Title);
            plan.Areas!.Should().BeSubsetOf(DemoDataSeeder.ResearchAreaNames, plan.Title);
            plan.Areas.Should().OnlyHaveUniqueItems(plan.Title);

            plan.Type.Should().NotBeNullOrWhiteSpace(plan.Title);
            DemoDataSeeder.PublicationTypes.Should().Contain(plan.Type!, plan.Title);
        }
    }

    [Fact]
    public void The_papers_between_them_use_more_than_one_area_and_more_than_one_kind()
    {
        var papers = All.Where(p => ReachesPaper.Contains(p.Stage)).ToList();

        papers.SelectMany(p => p.Areas!).Distinct().Should().HaveCountGreaterThan(3);
        papers.Select(p => p.Type).Distinct().Should().HaveCountGreaterThan(1);
    }

    /// <summary>
    /// The institution writes British English, and an em dash reads as something a person did not
    /// type. Both are easy to reintroduce one publication at a time and hard to notice afterwards.
    /// </summary>
    [Fact]
    public void The_prose_is_british_english_and_free_of_em_dashes()
    {
        string[] american = ["color", "behavior", "favor", "center", "canceled", "organiz", "recogniz", "analyz"];

        foreach (var sentence in Sentences)
        {
            sentence.Should().NotContain("—", sentence);
            foreach (var word in american)
            {
                sentence.ToLowerInvariant().Should().NotContain(word, sentence);
            }
        }
    }

    [Fact]
    public void Every_publication_has_a_title_and_an_abstract_of_its_own()
    {
        All.Select(p => p.Title).Should().OnlyHaveUniqueItems();
        All.Select(p => p.Abstract).Should().OnlyHaveUniqueItems();
        All.Should().OnlyContain(p => p.Title.Length > 0 && p.Abstract.EndsWith('.'));
    }

    /// <summary>Every point in the process has a publication parked at it, which is the whole purpose.</summary>
    [Fact]
    public void Every_stage_has_a_publication_parked_at_it()
    {
        var covered = All.Select(p => p.Stage).Distinct().ToList();
        Enum.GetValues<DemoStage>().Should().BeSubsetOf(covered);
    }
}
