namespace PublicationSite.Api.Data;

/// <summary>
/// The publications the demonstration dataset contains, and everything anybody said about them.
///
/// Kept apart from <see cref="DemoDataSeeder"/>, which builds the institution, and from
/// <see cref="DemoPipelineBuilder"/>, which knows how a publication moves. This file is the prose:
/// thirty pieces of research, the proposals each student submitted, and the words each supervisor,
/// coordinator, head of department and committee member wrote about that particular piece of work.
///
/// Written out one by one rather than generated, because generated demonstration data reads as
/// generated. A dataset where every supervisor writes the same sentence and every student submits
/// the same title with two suffixes attached cannot be used to judge whether a screen reads well,
/// and it hides the faults it is there to expose: a listing ordered by a column of identical values
/// looks broken, and nobody can tell a search that works from one that does not when every row says
/// the same thing.
/// </summary>
public static class DemoPlans
{
    /// <summary>
    /// The account a walkthrough starts from. Its publications are the stages a student acts on, so
    /// one sign-in covers an empty publication, a supervised one, an ethics stage in progress, a
    /// paper being written, one accepted and one already in the catalogue.
    /// </summary>
    public static DemoPublicationPlan[] ForAlexMoreau =>
    [
        new()
        {
            Title = "Automated accessibility testing in continuous integration",
            Abstract = "Whether accessibility checks running on every build change what developers fix, and when.",
            Stage = DemoStage.ProposalsDrafted,
            StartedDaysAgo = 2,
            LastActionDaysAgo = 2
        },

        new()
        {
            Title = "Pair programming and defect density in student projects",
            Abstract = "A comparison of defect rates between paired and solo work across one teaching semester.",
            Stage = DemoStage.SupervisorAssigned,
            StartedDaysAgo = 13,
            LastActionDaysAgo = 4,
            Chosen = 1,
            Proposals =
            [
                new("Refactoring habits in second-year programming projects",
                    "Whether students who refactor as they go finish closer to the brief than those who leave it to the end."),
                new("Pair programming and defect density in student projects",
                    "A comparison of defect rates between paired and solo work across one teaching semester."),
                new("Code comprehension under time pressure",
                    "How accurately students describe unfamiliar code when the reading time is capped.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Three proposals from a first-semester student. The middle one is the strongest and the other two would both need narrowing.",
                PrimaryOffer = "I have run the paired-work experiment before and can hand over the instrumentation, so the measurement is not the risky part.",
                AlternateOffer = "The refactoring one interests me, though I would want it framed around habits rather than tool use.",
                Allocation = "Allocated to Thomas Okoro: he has the instrumentation for the defect counts and the student is asking a measurement question."
            }
        },

        new()
        {
            Title = "Interview study of code review practice in small teams",
            Abstract = "What reviewers in teams of fewer than ten people actually look for, in their own words.",
            Stage = DemoStage.EthicsDocumentsRequested,
            StartedDaysAgo = 24,
            LastActionDaysAgo = 11,
            Chosen = 2,
            Proposals =
            [
                new("Review latency and its effect on branch lifetime",
                    "Whether slow reviews explain long-lived branches, using repository history rather than self-report."),
                new("Checklists in code review",
                    "Whether a written checklist changes what reviewers comment on, or only how long they take."),
                new("Interview study of code review practice in small teams",
                    "What reviewers in teams of fewer than ten people actually look for, in their own words.")
            ],
            Words = new DemoWords
            {
                Dispatch = "The third is an interview study and will need ethics approval. Sending all three so the choice is yours.",
                PrimaryOffer = "The interview study is the one worth doing. Small-team review is under-described and the student writes well.",
                AlternateOffer = "I could take the latency one, though it is closer to repository mining than to my area.",
                Allocation = "Allocated to Thomas Okoro on the interview study, which is the proposal both of you rated highest.",
                EthicsRequirement = "Semi-structured interviews with named practitioners about their own workplace. Full documentation, and the consent form needs a withdrawal clause."
            }
        },

        new()
        {
            Title = "Latency perception in progressive web applications",
            Abstract = "How long an interface can take to respond before people report it as slow.",
            Stage = DemoStage.EthicsCompleted,
            EthicsRequired = false,
            StartedDaysAgo = 47,
            LastActionDaysAgo = 30,
            Chosen = 0,
            Proposals =
            [
                new("Latency perception in progressive web applications",
                    "How long an interface can take to respond before people report it as slow."),
                new("Perceived speed and loading placeholders",
                    "Whether skeleton screens change reported waiting time, or only what people look at while they wait."),
                new("Offline behaviour in installed web applications",
                    "What users expect an installed web application to still do when the connection drops.")
            ],
            Words = new DemoWords
            {
                Dispatch = "All three are measurement studies on existing published traces, so none of them should need documentation.",
                PrimaryOffer = "The first is well posed and the thresholds are already established in the literature, so the student has something to argue against.",
                AlternateOffer = "The placeholder study overlaps with work I supervised last year, which would make me a poor second reader on it.",
                Allocation = "Allocated to Thomas Okoro, who has supervised perception thresholds before.",
                EthicsRequirement = "The study reuses a published, fully anonymised trace set. No participants and no identifiable data, so no approval is needed.",
                EthicsNotRequired = "Agreed. I checked the source dataset is the published one and its licence permits reuse.",
                EthicsHead = "Confirmed. Reuse of a published trace set is exactly the case this route exists for.",
                EthicsFinal = "Ethics stage closed as not required. The research paper stage is open."
            }
        },

        new()
        {
            Title = "Onboarding documentation and time to first contribution",
            Abstract = "Measuring how documentation quality affects how quickly new contributors ship something.",
            Stage = DemoStage.PaperAccepted,
            EthicsRequired = false,
            StartedDaysAgo = 119,
            LastActionDaysAgo = 12,
            Chosen = 1,
            Year = 2026,
            Keywords = ["developer onboarding", "documentation", "open source"],
            Areas = ["Software Engineering"],
            Type = "Conference Proceeding",
            Committee = [DemoSeat.ReviewerOne, DemoSeat.ReviewerTwo, DemoSeat.ExternalOne],
            Proposals =
            [
                new("Issue labelling and newcomer retention",
                    "Whether projects that label beginner-friendly issues keep more first-time contributors."),
                new("Onboarding documentation and time to first contribution",
                    "Measuring how documentation quality affects how quickly new contributors ship something."),
                new("Mentorship arrangements in volunteer-run projects",
                    "How informal mentoring is organised where nobody is paid to do it.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Two of these are repository studies and one would need interviews. Worth splitting between you if you both have room.",
                PrimaryOffer = "I would take the onboarding one. The measure is defensible and the data is already public.",
                AlternateOffer = "The labelling study is close to my area but I am at capacity until the next cycle.",
                Allocation = "Allocated to Thomas Okoro. The alternative is a mentorship study neither of you has capacity for this cycle.",
                EthicsRequirement = "Repository metadata only, all of it already public, and no contributor is identified by name in the analysis.",
                EthicsNotRequired = "Agreed, on the basis that no contributor is named and nothing is joined back to an individual.",
                EthicsHead = "No concerns. Public repository metadata with no individual named is outside our remit.",
                EthicsFinal = "Closed as not required. Please keep the anonymisation note in the methods section.",
                PaperNotes = "First complete draft. The retention figures in section 4 are recalculated since the proposal, using the corrected join.",
                PaperSupervisor = "The corrected join makes a real difference and the student has been honest about it in the text. Ready for the committee.",
                CommitteeAppointment = "Two reviewers who read repository studies and an external who has published on contributor retention.",
                PaperDecision = "Accepted. The dissent was about how far the result generalises, and the revised limitations section now says exactly that.",
            },
            Votes =
            [
                new(DemoSeat.ReviewerOne, true, "Careful work. The threat-to-validity section is unusually honest about the sampling frame."),
                new(DemoSeat.ReviewerTwo, true, "I would have liked a second project family in the sample, but the claim made is supported by the data shown."),
                new(DemoSeat.ExternalOne, false, "The measure conflates first commit with first merged contribution, and the difference matters for the central claim.")
            ]
        },

        new()
        {
            Title = "Static analysis adoption in New Zealand software teams",
            Abstract = "A survey of which static analysis tools are adopted, which are abandoned, and why.",
            Stage = DemoStage.Published,
            StartedDaysAgo = 264,
            LastActionDaysAgo = 232,
            Chosen = 1,
            Year = 2025,
            Keywords = ["static analysis", "software quality", "developer practice"],
            Areas = ["Software Engineering", "Information Security"],
            Type = "Journal Article",
            Committee = [DemoSeat.ReviewerTwo, DemoSeat.ReviewerThree, DemoSeat.ExternalOne],
            Proposals =
            [
                new("False positives and tool abandonment",
                    "Whether the rate of spurious warnings predicts which analysis tools teams stop running."),
                new("Static analysis adoption in New Zealand software teams",
                    "A survey of which static analysis tools are adopted, which are abandoned, and why."),
                new("Analysis in the editor against analysis in the pipeline",
                    "Where teams place their checks, and what they say they gain by moving them.")
            ],
            Words = new DemoWords
            {
                Dispatch = "A survey of practitioners, so it will need ethics documentation. Both of you have supervised survey work before.",
                PrimaryOffer = "The survey is the one with a contribution in it. I have contacts at four of the firms the student would want to reach.",
                AlternateOffer = "The false-positive study is closer to my area, though I think it is really a sub-question of the survey.",
                Allocation = "Allocated to Thomas Okoro, whose contacts make the sampling frame realistic rather than aspirational.",
                EthicsRequirement = "A survey of named practitioners at identifiable firms. Documentation required, and the information sheet must say what happens to firm names.",
                EthicsDocuments = "Information sheet, consent form and instrument all read well together. The firm-anonymisation wording is exactly right.",
                EthicsCoordinator = "Checked against the institutional policy. The retention period is stated and the withdrawal route is workable.",
                EthicsHead = "No concerns from the department. I would only note that the firms are small enough that role plus sector could identify someone, and the student has already handled that.",
                EthicsFinal = "Approved, reference AIS-ETH-2025-041. Please quote it on any correspondence with the participating firms.",
                PaperNotes = "Final draft. Response rate was 61 per cent, better than planned for, so the sub-group analysis in section 5 is now viable.",
                PaperSupervisor = "The response rate carries the sub-group analysis and the write-up is clear about where it stops. Ready for the committee.",
                CommitteeAppointment = "A committee weighted towards survey methodology, since that is where this paper stands or falls.",
                PaperDecision = "Accepted unanimously and with genuine enthusiasm from the external. Congratulations to the author.",
                PublishDecision = "Happy for this to be public. Several of the firms that took part have asked to read it."
            },
            Votes =
            [
                new(DemoSeat.ReviewerTwo, true, "The instrument is sound and the non-response analysis is better than most published survey work in this area."),
                new(DemoSeat.ReviewerThree, true, "Clear, well argued, and the abandonment findings are genuinely new for this setting."),
                new(DemoSeat.ExternalOne, true, "I would cite this. The comparison against the Australian figures is handled carefully.")
            ]
        }
    ];

    /// <summary>
    /// The proposals stage, in every state it has: submitted and undispatched, out with supervisors,
    /// answered and awaiting allocation, and a round that came back with nobody worth allocating.
    /// </summary>
    public static DemoPublicationPlan[] ForFatimaAlRashid =>
    [
        new()
        {
            Title = "Energy cost of client-side rendering on low-end devices",
            Abstract = "Measuring battery consumption of comparable interfaces rendered on the client and on the server.",
            Stage = DemoStage.ProposalsSubmitted,
            Chosen = 0,
            StartedDaysAgo = 3,
            LastActionDaysAgo = 2,
            Proposals =
            [
                new("Energy cost of client-side rendering on low-end devices",
                    "Measuring battery consumption of comparable interfaces rendered on the client and on the server."),
                new("Bundle size and first interaction on constrained networks",
                    "How much of the delay to first interaction is explained by payload size alone."),
                new("Battery-aware interface degradation",
                    "Whether interfaces that simplify themselves at low battery are noticed, and whether they are welcome.")
            ]
        },

        new()
        {
            Title = "Test flakiness and developer trust in build pipelines",
            Abstract = "Whether intermittent test failures change how teams respond to a red build.",
            Stage = DemoStage.ProposalsWithSupervisors,
            Chosen = 0,
            StartedDaysAgo = 6,
            LastActionDaysAgo = 4,
            Proposals =
            [
                new("Test flakiness and developer trust in build pipelines",
                    "Whether intermittent test failures change how teams respond to a red build."),
                new("Quarantine policies for unreliable tests",
                    "What teams do with a test they no longer trust, and how often it comes back."),
                new("Build failure triage in small teams",
                    "Who picks up a broken build when nobody is assigned to, and how long it takes.")
            ],
            Words = new DemoWords
            {
                Dispatch = "All three are about the same underlying problem. Whoever takes it will need to help the student pick one."
            }
        },

        new()
        {
            Title = "Data minimisation in student information systems",
            Abstract = "An audit of what personal data teaching systems collect against what they demonstrably use.",
            Stage = DemoStage.ProposalSelected,
            StartedDaysAgo = 11,
            LastActionDaysAgo = 3,
            Chosen = 2,
            Proposals =
            [
                new("Retention periods in institutional record keeping",
                    "How long teaching systems keep personal data against how long their policies say they should."),
                new("Access logging in student-facing systems",
                    "Who reads a student record, how often, and whether the logs would show it."),
                new("Data minimisation in student information systems",
                    "An audit of what personal data teaching systems collect against what they demonstrably use.")
            ],
            Words = new DemoWords
            {
                Dispatch = "A privacy audit across three proposals. The third is the one with a method attached to it.",
                PrimaryOffer = "The minimisation audit is well scoped and I can open the right doors inside the institution for it.",
                AlternateOffer = "I would take the access logging one. It is the more technical of the three and closer to what I teach."
            }
        },

        new()
        {
            Title = "Consent fatigue in mobile application permissions",
            Abstract = "Whether repeated permission prompts change what people agree to, and what they remember agreeing to.",
            Stage = DemoStage.ProposalsReturnedUnwanted,
            Chosen = 0,
            StartedDaysAgo = 19,
            LastActionDaysAgo = 6,
            Proposals =
            [
                new("Consent fatigue in mobile application permissions",
                    "Whether repeated permission prompts change what people agree to, and what they remember agreeing to."),
                new("Permission wording and comprehension",
                    "Whether people can say, immediately afterwards, what they have just granted."),
                new("Revocation in practice",
                    "How many people ever withdraw a permission once it has been granted, and what prompts them to.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Consent and permissions research. Neither of you works in this area exactly, so please say so if it is not for you.",
                PrimaryOffer = "I would supervise the first one, though I should say plainly that consent research is at the edge of what I know well.",
                Discard = "Thank you both. I am sending this back rather than allocating it: the one offer we have comes with a stated reservation about the area, and this student deserves a supervisor who works in it. Priya returns from leave next month."
            }
        },

        new()
        {
            Title = "Retrieval practice in introductory programming courses",
            Abstract = "A controlled comparison of retrieval practice against re-reading in a first programming paper.",
            Stage = DemoStage.Published,
            StartedDaysAgo = 331,
            LastActionDaysAgo = 300,
            Chosen = 0,
            Year = 2025,
            Keywords = ["IT education", "retrieval practice", "assessment"],
            Areas = ["Computing Education"],
            Type = "Journal Article",
            Committee = [DemoSeat.ReviewerOne, DemoSeat.ReviewerThree, DemoSeat.ExternalTwo],
            Proposals =
            [
                new("Retrieval practice in introductory programming courses",
                    "A controlled comparison of retrieval practice against re-reading in a first programming paper."),
                new("Worked examples and transfer in first-year programming",
                    "Whether students who study worked examples transfer better to unseen problems."),
                new("Spacing effects across a teaching semester",
                    "Whether distributing practice across a semester survives contact with an assessment calendar.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Education research with students as participants. All three will need documentation; please only offer if you can supervise that.",
                PrimaryOffer = "I have run a controlled study in this course before and can advise on the consent problem of teaching your own participants.",
                AlternateOffer = "The spacing study is the most interesting to me but the calendar makes it very hard to run cleanly.",
                Allocation = "Allocated to Thomas Okoro, who has run a controlled study in this same course and knows where the consent difficulty is.",
                EthicsRequirement = "Students of the course as participants, taught by the supervisor. Documentation required and the power imbalance has to be addressed explicitly.",
                EthicsDocuments = "The independent-recruiter arrangement resolves the power imbalance properly. Documentation is complete.",
                EthicsCoordinator = "Checked. The opt-out route does not disadvantage anyone in the assessment, which was my only concern.",
                EthicsHead = "The department is satisfied. Using an independent recruiter is the right call and I would like it recorded as a precedent.",
                EthicsFinal = "Approved. Please keep the recruiter arrangement in the write-up, since it is part of the contribution.",
                PaperNotes = "Final draft, with the pre-registration deviation documented in appendix B.",
                PaperSupervisor = "The deviation is small and properly reported. This is publishable work and the analysis is honest.",
                CommitteeAppointment = "Two reviewers who read education research and an external who works in the same field at another institution.",
                PaperDecision = "Accepted. The committee split on the effect size but not on the quality of the work, and the revised discussion covers it.",
                PublishDecision = "Yes, publish. The course team wants to cite it when the redesign goes to the board."
            },
            Votes =
            [
                new(DemoSeat.ReviewerOne, true, "Well designed and honestly reported. The pre-registration deviation is exactly the kind of thing people usually hide."),
                new(DemoSeat.ReviewerThree, false, "The effect is real but the paper reads it as larger than the confidence interval supports. I would want section 6 rewritten before this goes out."),
                new(DemoSeat.ExternalTwo, true, "A useful replication in a setting that badly needs one. I have no substantive objection.")
            ]
        }
    ];

    /// <summary>
    /// The ethics stage, step by step, so every screen that reads an ethics queue has something in
    /// it. One of these has been sitting with the Head of Department for longer than the
    /// institution's review window allows, which is what puts an overdue publication on a screen.
    /// </summary>
    public static DemoPublicationPlan[] ForNoahKingi =>
    [
        new()
        {
            Title = "Screen reader compatibility of learning management systems",
            Abstract = "An evaluation of three widely deployed platforms against WCAG 2.2 success criteria.",
            Stage = DemoStage.EthicsDeclared,
            StartedDaysAgo = 5,
            LastActionDaysAgo = 2,
            Chosen = 0,
            Proposals =
            [
                new("Screen reader compatibility of learning management systems",
                    "An evaluation of three widely deployed platforms against WCAG 2.2 success criteria."),
                new("Keyboard-only navigation in assessment tools",
                    "Whether timed assessments can be completed without a pointing device."),
                new("Alternative text quality in course materials",
                    "How much of the alternative text in published course material describes anything useful.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Accessibility evaluations, all three. The first is the broadest and probably the most useful to the institution.",
                PrimaryOffer = "I will take the platform evaluation. It needs someone who can read the criteria properly and I have done the audit training.",
                AlternateOffer = "The alternative text study is appealing but I would be a weak supervisor for the accessibility criteria themselves.",
                Allocation = "Allocated to Thomas Okoro, who holds the audit training this needs."
            }
        },

        new()
        {
            Title = "Open data reuse in institutional research repositories",
            Abstract = "How often deposited datasets are cited, and by whom, across five years of deposits.",
            Stage = DemoStage.EthicsNotRequiredAwaitingCoordinator,
            EthicsRequired = false,
            StartedDaysAgo = 9,
            LastActionDaysAgo = 5,
            Chosen = 1,
            Proposals =
            [
                new("Deposit rates across faculties",
                    "Which faculties deposit data, which do not, and what the stated reasons are."),
                new("Open data reuse in institutional research repositories",
                    "How often deposited datasets are cited, and by whom, across five years of deposits."),
                new("Licence choice in institutional deposits",
                    "What licences depositors choose when the repository does not choose for them.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Bibliometric work across all three. None of them should need ethics documentation but that is the supervisor's call.",
                PrimaryOffer = "The reuse study is the strongest and the citation data is already available to us.",
                AlternateOffer = "I could supervise the licence study, though it is more a policy review than a research project as written.",
                Allocation = "Allocated to Thomas Okoro on the reuse study.",
                EthicsRequirement = "Published citation records only. No participants, no personal data, and nothing that could be traced to an individual depositor."
            }
        },

        new()
        {
            Title = "Wellbeing and workload in postgraduate research cohorts",
            Abstract = "A mixed-methods study of reported workload against supervision arrangements.",
            Stage = DemoStage.EthicsDocumentsUploaded,
            StartedDaysAgo = 14,
            LastActionDaysAgo = 4,
            Chosen = 0,
            Proposals =
            [
                new("Wellbeing and workload in postgraduate research cohorts",
                    "A mixed-methods study of reported workload against supervision arrangements."),
                new("Supervision meeting frequency and progress",
                    "Whether how often a student meets their supervisor predicts anything about how they get on."),
                new("Isolation in remote postgraduate study",
                    "What students studying at a distance say they lose, and what they say they gain.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Sensitive area, all three. Whoever takes it should expect a careful ethics process.",
                PrimaryOffer = "I will take the first. Wellbeing work needs a supervisor who will read the distress protocol properly and I will.",
                AlternateOffer = "I do not think I am the right supervisor for any of these. The subject needs someone closer to the pastoral side.",
                Allocation = "Allocated to Thomas Okoro, who has undertaken to review the distress protocol himself.",
                EthicsRequirement = "Wellbeing data from students about their own supervision. Full documentation, and I want a distress protocol and a named support contact in the information sheet."
            }
        },

        new()
        {
            Title = "Peer feedback quality in online group assessment",
            Abstract = "Whether structured prompts improve the specificity of feedback students give one another.",
            Stage = DemoStage.EthicsDocumentsWithCoordinator,
            StartedDaysAgo = 18,
            LastActionDaysAgo = 8,
            Chosen = 2,
            Proposals =
            [
                new("Group formation and free riding",
                    "Whether how groups are formed predicts the complaints that arrive later."),
                new("Anonymity in peer assessment",
                    "What changes in what students write when their name is not attached to it."),
                new("Peer feedback quality in online group assessment",
                    "Whether structured prompts improve the specificity of feedback students give one another.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Three related proposals on group assessment. The third has the clearest intervention in it.",
                PrimaryOffer = "The prompts study has a real intervention and a measurable outcome. I would take it.",
                AlternateOffer = "The anonymity question is interesting but I would want it run in a course I do not teach.",
                Allocation = "Allocated to Thomas Okoro on the prompts study.",
                EthicsRequirement = "Student coursework used as data. Documentation required, and the consent has to be sought after the marks are released.",
                EthicsDocuments = "Complete, and the post-marking consent point is handled the way I asked. Passing it on."
            }
        },

        new()
        {
            Title = "Digital literacy on entry to postgraduate study",
            Abstract = "Establishing a baseline of incoming digital skills and where the gaps cluster.",
            Stage = DemoStage.EthicsWithHeadOfDepartment,
            StartedDaysAgo = 26,
            LastActionDaysAgo = 24,
            Chosen = 0,
            Proposals =
            [
                new("Digital literacy on entry to postgraduate study",
                    "Establishing a baseline of incoming digital skills and where the gaps cluster."),
                new("Self-assessed against demonstrated skill",
                    "Whether students can predict their own performance on a practical skills test."),
                new("Preparatory materials and first-assignment outcomes",
                    "Whether students who complete optional preparation do better on the first assessment.")
            ],
            Words = new DemoWords
            {
                Dispatch = "A skills baseline the programme committee has been asking for. Please treat it as a priority if either of you has room.",
                PrimaryOffer = "I will take the baseline study. The programme committee wants it and I would rather it were done properly than quickly.",
                AlternateOffer = "The self-assessment comparison is the more interesting research question, but it depends on the baseline existing first.",
                Allocation = "Allocated to Thomas Okoro. The other two both depend on this one being done first.",
                EthicsRequirement = "A skills test taken by incoming students. Documentation required, and results must not reach anyone making admission decisions.",
                EthicsDocuments = "The separation from admissions is written in clearly. No concerns.",
                EthicsCoordinator = "Checked against policy. The data flows in section 3 satisfy the separation the supervisor asked for."
            }
        },

        new()
        {
            Title = "Attendance patterns and outcomes in blended delivery",
            Abstract = "Relating attendance in blended courses to final outcomes, controlling for prior attainment.",
            Stage = DemoStage.EthicsAwaitingFinalDecision,
            StartedDaysAgo = 12,
            LastActionDaysAgo = 3,
            Chosen = 1,
            Proposals =
            [
                new("Lecture capture use and attendance",
                    "Whether recordings substitute for attendance or supplement it."),
                new("Attendance patterns and outcomes in blended delivery",
                    "Relating attendance in blended courses to final outcomes, controlling for prior attainment."),
                new("Timetabling and non-attendance",
                    "How much of non-attendance is explained by the timetable rather than by the student.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Attendance data, so identifiable records are involved in all three. Ethics documentation is near certain.",
                PrimaryOffer = "The second is the one with a real control in it. I can supervise the statistics.",
                AlternateOffer = "The timetabling question is the one I would most like answered, but the modelling is closer to Thomas's area than mine.",
                Allocation = "Allocated to Thomas Okoro, on the proposal with an actual control variable in it.",
                EthicsRequirement = "Identifiable attendance and attainment records linked together. Full documentation and a clear statement of who holds the key.",
                EthicsDocuments = "The linkage is described precisely and the key is held outside the research team. Complete.",
                EthicsCoordinator = "Checked. I am satisfied the linkage key is genuinely held outside the team.",
                EthicsHead = "No concerns from the department. The linkage arrangement is the strictest I have seen from this programme and I would like it used as the example."
            }
        }
    ];

    /// <summary>
    /// The research paper stage: one paper with each of the people who read it, and a committee
    /// that did not agree with itself, so the coordinator's final decision is a decision rather
    /// than a formality.
    /// </summary>
    public static DemoPublicationPlan[] ForYukiTanaka =>
    [
        new()
        {
            Title = "Version control practice among first-year students",
            Abstract = "What students do with version control when nobody is grading how they use it.",
            Stage = DemoStage.PaperWithSupervisor,
            EthicsRequired = false,
            StartedDaysAgo = 34,
            LastActionDaysAgo = 9,
            Chosen = 0,
            Year = 2026,
            Keywords = ["version control", "novice programmers", "computing education"],
            Areas = ["Computing Education", "Software Engineering"],
            Type = "Conference Proceeding",
            Proposals =
            [
                new("Version control practice among first-year students",
                    "What students do with version control when nobody is grading how they use it."),
                new("Commit message quality without instruction",
                    "What students write in commit messages when they have never been told what to write."),
                new("Branching among novices",
                    "Whether first-year students branch at all, and what happens when they do.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Repository analysis on anonymised student work, so probably no documentation needed. Three variations on one idea.",
                PrimaryOffer = "The first covers the other two. I would take it and fold the commit-message question in as a sub-analysis.",
                AlternateOffer = "Agreed that the first subsumes the others. I have no capacity this cycle in any case.",
                Allocation = "Allocated to Thomas Okoro, who will fold the other two questions in as sub-analyses.",
                EthicsRequirement = "Anonymised repository snapshots already collected for teaching, with no identifiers retained. No approval needed.",
                EthicsNotRequired = "Agreed. The snapshots were anonymised at collection and there is no re-identification route.",
                EthicsHead = "Satisfied. Anonymising at collection rather than afterwards is the right order and I would like that noted.",
                EthicsFinal = "Closed as not required. Nothing further is needed before the paper is written.",
                PaperNotes = "First complete draft. Section 3 is longer than planned because the branching data turned out to be more interesting than expected."
            }
        },

        new()
        {
            Title = "Continuous deployment in regulated environments",
            Abstract = "How teams under audit requirements reconcile them with deploying several times a day.",
            Stage = DemoStage.PaperAwaitingCommittee,
            EthicsRequired = false,
            StartedDaysAgo = 52,
            LastActionDaysAgo = 15,
            Chosen = 1,
            Year = 2026,
            Keywords = ["continuous deployment", "regulated software", "audit"],
            Areas = ["Software Engineering"],
            Type = "Technical Report",
            Proposals =
            [
                new("Change advisory boards and deployment frequency",
                    "Whether a formal approval board measurably slows down the teams it governs."),
                new("Continuous deployment in regulated environments",
                    "How teams under audit requirements reconcile them with deploying several times a day."),
                new("Evidence collection as a by-product of the pipeline",
                    "Whether audit evidence can be produced by the pipeline rather than assembled afterwards.")
            ],
            Words = new DemoWords
            {
                Dispatch = "All three are document and artefact studies of published compliance material. No participants expected.",
                PrimaryOffer = "The second is the strongest framing and the other two are really findings that would come out of it.",
                AlternateOffer = "I would take the evidence-collection one if it were standalone, but I agree it belongs inside the second.",
                Allocation = "Allocated to Thomas Okoro on the framing both of you preferred.",
                EthicsRequirement = "Published compliance documentation and pipeline artefacts from open projects. No participants and nothing personal.",
                EthicsNotRequired = "Agreed, all sources are published and no individual is identifiable in them.",
                EthicsHead = "No concerns from the department. Published compliance material carries no participants.",
                EthicsFinal = "Closed as not required. Nothing here needs an approval number.",
                PaperNotes = "Complete draft. The three case organisations are described by sector only, at their request.",
                PaperSupervisor = "The sector-only description is a limitation and the paper says so. The argument is well evidenced. Ready for a committee."
            }
        },

        new()
        {
            Title = "Technical debt reporting and its effect on planning",
            Abstract = "Whether making technical debt visible in planning changes what teams schedule.",
            Stage = DemoStage.CommitteeReviewing,
            EthicsRequired = false,
            StartedDaysAgo = 16,
            LastActionDaysAgo = 8,
            Chosen = 0,
            Year = 2026,
            Keywords = ["technical debt", "planning", "software maintenance"],
            Areas = ["Software Engineering"],
            Type = "Conference Proceeding",
            Committee = [DemoSeat.ReviewerOne, DemoSeat.ReviewerThree, DemoSeat.ExternalTwo],
            Proposals =
            [
                new("Technical debt reporting and its effect on planning",
                    "Whether making technical debt visible in planning changes what teams schedule."),
                new("Debt metaphors and how teams talk about maintenance",
                    "Whether the language a team uses for maintenance predicts what they do about it."),
                new("Maintenance work in sprint records",
                    "How much of a sprint is maintenance once the labels are read rather than trusted.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Sprint records and planning artefacts from projects that have already published them. No participants.",
                PrimaryOffer = "The reporting study has a before-and-after in it, which the other two lack. I would take that one.",
                AlternateOffer = "The language study is the one I would enjoy, but the reporting study is the one with a result in it.",
                Allocation = "Allocated to Thomas Okoro, on the only one of the three with a before-and-after design.",
                EthicsRequirement = "Planning artefacts already published by the projects themselves. No participants and no personal data.",
                EthicsNotRequired = "Agreed. Everything used here is already in the public record.",
                EthicsHead = "Agreed. Sprint records the projects themselves publish are not our concern to approve.",
                EthicsFinal = "Closed as not required. Noting for the file that the artefacts were already public before this project began.",
                PaperNotes = "Complete draft. The before-and-after covers eleven sprints either side of the reporting change.",
                PaperSupervisor = "Eleven sprints either side is enough to say something and the paper does not overclaim. Ready for the committee.",
                CommitteeAppointment = "Appointed with an external who works on maintenance economics, since that is where the sharpest questions will come from."
            }
        },

        new()
        {
            Title = "Search behaviour in institutional publication catalogues",
            Abstract = "Log analysis of how readers actually search a research catalogue, against how it is designed.",
            Stage = DemoStage.PaperAwaitingFinalDecision,
            EthicsRequired = false,
            StartedDaysAgo = 73,
            LastActionDaysAgo = 5,
            Chosen = 2,
            Year = 2026,
            Keywords = ["search behaviour", "digital libraries", "log analysis"],
            Areas = ["Human-Computer Interaction"],
            Type = "Technical Report",
            Committee = [DemoSeat.ReviewerTwo, DemoSeat.ReviewerThree, DemoSeat.ExternalOne],
            Proposals =
            [
                new("Faceted browsing in research catalogues",
                    "Whether the facets a catalogue offers are the ones readers use."),
                new("Zero-result searches and what follows them",
                    "What a reader does next when a catalogue returns nothing."),
                new("Search behaviour in institutional publication catalogues",
                    "Log analysis of how readers actually search a research catalogue, against how it is designed.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Log analysis on aggregated, de-identified search logs. The third is the broadest of the three.",
                PrimaryOffer = "I will take the log analysis. The other two are questions this one can answer along the way.",
                AlternateOffer = "No capacity from me this cycle, but I agree the third is the right one to run.",
                Allocation = "Allocated to Thomas Okoro, the only supervisor with capacity, and the right one for a log study in any case.",
                EthicsRequirement = "Aggregated search logs with no session identifiers and no account linkage. No approval needed.",
                EthicsNotRequired = "Agreed. I confirmed with the library that the export carries no session identifiers.",
                EthicsHead = "Satisfied, on the strength of the library confirming the export contains no session identifiers.",
                EthicsFinal = "Closed as not required, with the library confirmation kept on the record.",
                PaperNotes = "Complete draft. The zero-result analysis in section 5 was added after the supervisor's reading of the first version.",
                PaperSupervisor = "The added zero-result analysis is the most useful part of the paper now. Ready for the committee.",
                CommitteeAppointment = "Two reviewers and an external who has published on catalogue search, which is a narrow field to find a reader in."
            },
            Votes =
            [
                new(DemoSeat.ReviewerTwo, true, "The log analysis is competent and the design recommendations follow from it rather than being bolted on."),
                new(DemoSeat.ReviewerThree, true, "Useful and readable. I would like to see the query taxonomy published alongside it."),
                new(DemoSeat.ExternalOne, false, "The sample is one catalogue at one institution and the recommendations are written as though they generalise. Narrow the claims and I would support it.")
            ]
        }
    ];

    /// <summary>
    /// The second Information Technology supervisor's students. Somebody has to be supervised by
    /// her: a demonstration set where one supervisor holds every publication cannot show a
    /// reallocation, a second opinion, or a supervisor who has stopped taking new work while
    /// keeping what she already has.
    /// </summary>
    public static DemoPublicationPlan[] ForMateoRossi =>
    [
        new()
        {
            Title = "Interpretability of student-facing risk scores",
            Abstract = "Whether the explanations shown alongside an at-risk flag are ones students can act on.",
            Stage = DemoStage.SupervisorAssigned,
            StartedDaysAgo = 21,
            LastActionDaysAgo = 7,
            Chosen = 1,
            AlternateSupervises = true,
            Proposals =
            [
                new("Fairness auditing of at-risk models",
                    "Whether an at-risk model performs equally across the groups it is applied to."),
                new("Interpretability of student-facing risk scores",
                    "Whether the explanations shown alongside an at-risk flag are ones students can act on."),
                new("Staff trust in predictive dashboards",
                    "What staff do with a prediction they do not believe.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Machine learning applied to students. This is squarely Priya's area rather than mine, but sending to both as usual.",
                PrimaryOffer = "I can supervise the dashboard study, but the first two need somebody who works on model fairness, which is not me.",
                AlternateOffer = "The interpretability question is exactly what I work on, and I would want the fairness audit folded into it as a second chapter.",
                Allocation = "Allocated to Priya Raman. Both of us agreed this needs her area rather than mine."
            }
        },

        new()
        {
            Title = "Bias auditing of automated marking tools",
            Abstract = "Whether automated marking agrees with human markers equally across student groups.",
            Stage = DemoStage.EthicsCompleted,
            StartedDaysAgo = 58,
            LastActionDaysAgo = 20,
            Chosen = 0,
            AlternateSupervises = true,
            Dispatch = DemoDispatch.AlternateOnly,
            Proposals =
            [
                new("Bias auditing of automated marking tools",
                    "Whether automated marking agrees with human markers equally across student groups."),
                new("Disagreement between automated and human marks",
                    "Where the two disagree most, and whether the disagreement is systematic."),
                new("Appeals against automated marks",
                    "Who appeals an automated mark, and what happens when they do.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Sent to Priya Raman alone: this is her area and the other supervisor has already said he is not the right reader for it.",
                AlternateOffer = "I will take the audit. It is the one of the three that produces a result the institution can act on.",
                Allocation = "Allocated to Priya Raman, the only supervisor this was sent to and the right one for it.",
                EthicsRequirement = "Student work and marks, linked to demographic attributes for the audit. Full documentation, and the demographic linkage needs its own justification.",
                EthicsDocuments = "The justification for the demographic linkage is the strongest part of the application. Complete.",
                EthicsCoordinator = "Checked against policy. The linkage is minimised and the retention period is short, which is what this needed.",
                EthicsHead = "The department supports this. An audit of our own marking tools is overdue and the design here is careful.",
                EthicsFinal = "Approved, reference AIS-ETH-2026-008, with the shorter retention period the Head of Department asked for."
            }
        },

        new()
        {
            Title = "Dataset documentation practice in applied machine learning",
            Abstract = "How often published models describe the data they were trained on well enough to reproduce.",
            Stage = DemoStage.Published,
            EthicsRequired = false,
            StartedDaysAgo = 198,
            LastActionDaysAgo = 150,
            Chosen = 2,
            AlternateSupervises = true,
            Year = 2026,
            Keywords = ["dataset documentation", "reproducibility", "machine learning"],
            Areas = ["Data Science"],
            Type = "Journal Article",
            Committee = [DemoSeat.ReviewerTwo, DemoSeat.ReviewerThree, DemoSeat.ExternalTwo],
            Proposals =
            [
                new("Model cards in practice",
                    "How many published models ship a model card, and what is on it."),
                new("Reproducibility of reported benchmark results",
                    "Whether reported benchmark numbers can be reproduced from what the paper provides."),
                new("Dataset documentation practice in applied machine learning",
                    "How often published models describe the data they were trained on well enough to reproduce.")
            ],
            Words = new DemoWords
            {
                Dispatch = "A survey of published artefacts. No participants. Priya is the obvious supervisor but sending to both.",
                PrimaryOffer = "I would only be a nominal supervisor here. Priya should take it.",
                AlternateOffer = "I will take the documentation study. It is the one where a negative result is still a contribution.",
                Allocation = "Allocated to Priya Raman, by agreement between both supervisors.",
                EthicsRequirement = "Published papers and their released artefacts. Nothing personal and nobody to consent.",
                EthicsNotRequired = "Agreed. This is a literature and artefact study end to end.",
                EthicsHead = "No concerns. A study of published papers needs no approval from us.",
                EthicsFinal = "Closed as not required. A study of the literature needs nothing from this committee.",
                PaperNotes = "Final draft. The sample grew from 120 to 184 papers after the second search, and the figures are recalculated throughout.",
                PaperSupervisor = "The larger sample strengthens it and the recalculation has been done carefully. Ready for the committee.",
                CommitteeAppointment = "A committee that between them have published on reproducibility, which is what this paper is really about.",
                PaperDecision = "Accepted. A clear result, carefully evidenced, and the committee was unanimous.",
                PublishDecision = "Yes. The whole point of the paper is that this material should be public."
            },
            Votes =
            [
                new(DemoSeat.ReviewerTwo, true, "184 papers is a real sample and the coding scheme is described well enough to be repeated."),
                new(DemoSeat.ReviewerThree, true, "The negative result is stated plainly without being turned into a complaint. Good work."),
                new(DemoSeat.ExternalTwo, true, "I have wanted this study to exist for three years. No objections.")
            ]
        }
    ];

    /// <summary>
    /// The Business department, which needs enough of its own work for its Head of Department and
    /// its Coordinator to have screens worth opening. A department with one publication in it
    /// cannot show that the Head of Department sees their own students and nobody else's.
    /// </summary>
    public static DemoPublicationPlan[] ForLucasFerreira =>
    [
        new()
        {
            Title = "Succession planning in owner-operated New Zealand firms",
            Abstract = "How firms without a designated successor plan, or avoid planning, for the owner's exit.",
            Stage = DemoStage.EthicsWithHeadOfDepartment,
            StartedDaysAgo = 15,
            LastActionDaysAgo = 6,
            Chosen = 0,
            Proposals =
            [
                new("Succession planning in owner-operated New Zealand firms",
                    "How firms without a designated successor plan, or avoid planning, for the owner's exit."),
                new("Family involvement and firm survival",
                    "Whether firms that employ family members outlast those that do not."),
                new("Exit intentions among owners over sixty",
                    "What owners approaching retirement say they intend to do with the business.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Interview work with business owners across all three. Both of you have the contacts for it.",
                PrimaryOffer = "The succession study is the one the sector actually asks us about. I will take it.",
                AlternateOffer = "I would take the family-involvement one, though it needs financial records the firms may not release.",
                Allocation = "Allocated to Aroha Bennett on the succession study.",
                EthicsRequirement = "Interviews with named owners about their own retirement plans and their firms' finances. Full documentation and firm-level anonymisation.",
                EthicsDocuments = "Anonymisation is described at firm and individual level. The commercial confidentiality clause is well drafted.",
                EthicsCoordinator = "Checked against policy. The commercial confidentiality wording is stronger than our template and I have kept a copy of it."
            }
        },

        new()
        {
            Title = "Cash flow forecasting in seasonal tourism businesses",
            Abstract = "What forecasting methods small tourism operators use, and how far ahead they trust them.",
            Stage = DemoStage.ProposalSelected,
            StartedDaysAgo = 7,
            LastActionDaysAgo = 2,
            Chosen = 1,
            Proposals =
            [
                new("Seasonal borrowing among small operators",
                    "How small tourism firms fund the off-season, and what it costs them."),
                new("Cash flow forecasting in seasonal tourism businesses",
                    "What forecasting methods small tourism operators use, and how far ahead they trust them."),
                new("Pricing responses to demand shocks",
                    "How quickly small operators change prices when demand moves, and what stops them.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Three finance-side proposals from an MBA student. The second is the most tractable in the time available.",
                PrimaryOffer = "The forecasting study is well scoped for a one-year project and I have operators who would take part.",
                AlternateOffer = "The pricing study is the more interesting economics, and I would supervise it if the student would rather go that way."
            }
        },

        new()
        {
            Title = "Board oversight of outsourced technology functions",
            Abstract = "How boards of small firms oversee technology work they have no in-house expertise to judge.",
            Stage = DemoStage.PaperAwaitingFinalDecision,
            EthicsRequired = false,
            StartedDaysAgo = 66,
            LastActionDaysAgo = 10,
            Chosen = 2,
            AlternateSupervises = true,
            Year = 2026,
            Keywords = ["corporate governance", "outsourcing", "board oversight"],
            Areas = ["Organisational Behaviour"],
            Type = "Thesis / Dissertation",
            Committee = [DemoSeat.ReviewerOne, DemoSeat.ReviewerTwo, DemoSeat.ExternalTwo],
            Proposals =
            [
                new("Technology spend in small firm annual reports",
                    "How technology expenditure is described where there is no requirement to break it out."),
                new("Director skills disclosures",
                    "What boards claim about their own technical competence, and how they word it."),
                new("Board oversight of outsourced technology functions",
                    "How boards of small firms oversee technology work they have no in-house expertise to judge.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Document analysis of published annual reports and governance statements. Marcus is the governance specialist of the two of you.",
                PrimaryOffer = "I can supervise the reporting study, but the governance framing of the third is Marcus's rather than mine.",
                AlternateOffer = "The oversight study is exactly my area and I have the disclosure dataset already assembled.",
                Allocation = "Allocated to Marcus Toledo, who already holds the disclosure dataset this depends on.",
                EthicsRequirement = "Published annual reports and governance statements. No participants and no confidential material.",
                EthicsNotRequired = "Agreed. Every source is a published filing.",
                EthicsHead = "The department agrees. Annual reports are public documents and no director is approached.",
                EthicsFinal = "Closed as not required. Published filings only, as the supervisor set out.",
                PaperNotes = "Complete draft covering 96 firms across four reporting years.",
                PaperSupervisor = "Four years of filings across 96 firms, coded consistently. The findings are modest and correctly stated. Ready for the committee.",
                CommitteeAppointment = "Two reviewers and an external from another business school, since the reviewers here both sit outside governance."
            },
            Votes =
            [
                new(DemoSeat.ReviewerOne, true, "Carefully coded and the inter-rater agreement is reported, which is more than most document studies manage."),
                new(DemoSeat.ReviewerTwo, true, "Modest claims, well supported. I have nothing substantive to raise."),
                new(DemoSeat.ExternalTwo, true, "A useful contribution to a literature dominated by large listed firms.")
            ]
        }
    ];

    /// <summary>The second Business student, so that department has a queue rather than an example.</summary>
    public static DemoPublicationPlan[] ForAmaraOkafor =>
    [
        new()
        {
            Title = "Remote work policy and retention in professional services",
            Abstract = "Whether stated remote-work policy predicts who stays, once pay is controlled for.",
            Stage = DemoStage.ProposalsWithSupervisors,
            Chosen = 0,
            StartedDaysAgo = 4,
            LastActionDaysAgo = 2,
            Proposals =
            [
                new("Remote work policy and retention in professional services",
                    "Whether stated remote-work policy predicts who stays, once pay is controlled for."),
                new("Return-to-office announcements and resignations",
                    "What happens to resignation rates in the quarter after a return-to-office announcement."),
                new("Hybrid arrangements and perceived fairness",
                    "Whether staff on different hybrid arrangements in the same team think the arrangement is fair.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Three proposals on remote work. The first two use published employment data; the third would need participants."
            }
        },

        new()
        {
            Title = "Non-financial reporting in privately held firms",
            Abstract = "What privately held firms disclose about sustainability when nothing obliges them to.",
            Stage = DemoStage.EthicsDocumentsWithCoordinator,
            StartedDaysAgo = 20,
            LastActionDaysAgo = 19,
            Chosen = 1,
            AlternateSupervises = true,
            Proposals =
            [
                new("Assurance of voluntary sustainability claims",
                    "How many voluntary sustainability claims are independently checked, and by whom."),
                new("Non-financial reporting in privately held firms",
                    "What privately held firms disclose about sustainability when nothing obliges them to."),
                new("Reporting frameworks chosen by small firms",
                    "Which reporting framework small firms adopt when several are available and none is required.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Reporting and assurance, all three. Marcus is the closer fit but sending to both.",
                PrimaryOffer = "I would supervise the framework-choice study, though I think it is the weakest of the three as written.",
                AlternateOffer = "The disclosure study is mine if the student wants it. It combines document analysis with interviews, which is the right method here.",
                Allocation = "Allocated to Marcus Toledo, who works on exactly this and rated it the strongest of the three.",
                EthicsRequirement = "Interviews with finance directors about their own firms' disclosures. Documentation required, with firm-level anonymisation.",
                EthicsDocuments = "Complete. The anonymisation holds even for the two firms distinctive enough to be guessed at."
            }
        },

        new()
        {
            Title = "Supplier diversity commitments and procurement outcomes",
            Abstract = "Whether firms that publish supplier diversity commitments buy differently from those that do not.",
            Stage = DemoStage.Published,
            EthicsRequired = false,
            StartedDaysAgo = 289,
            LastActionDaysAgo = 240,
            Chosen = 0,
            Year = 2025,
            Keywords = ["procurement", "supplier diversity", "corporate commitments"],
            Areas = ["Organisational Behaviour"],
            Type = "Thesis / Dissertation",
            Committee = [DemoSeat.ReviewerOne, DemoSeat.ReviewerThree, DemoSeat.ExternalOne],
            Proposals =
            [
                new("Supplier diversity commitments and procurement outcomes",
                    "Whether firms that publish supplier diversity commitments buy differently from those that do not."),
                new("Procurement thresholds and small supplier access",
                    "Whether the value threshold for a formal tender excludes the suppliers it is meant to protect."),
                new("Social procurement in local government",
                    "How councils weigh social outcomes against price, where they say they do.")
            ],
            Words = new DemoWords
            {
                Dispatch = "Procurement data, all published. The first is the one with a comparison group in it.",
                PrimaryOffer = "The first has a control group and the data is obtainable. I would take that one.",
                AlternateOffer = "The local government study is worth doing but the records are held in a form that would eat the whole year.",
                Allocation = "Allocated to Aroha Bennett, on the only one of the three whose data can be assembled inside a year.",
                EthicsRequirement = "Published procurement disclosures and supplier registers. No participants.",
                EthicsNotRequired = "Agreed, all of it is published under the disclosure rules.",
                EthicsHead = "No concerns. Procurement disclosures are published precisely so that they can be examined.",
                EthicsFinal = "Closed as not required, and the stage is open for the paper.",
                PaperNotes = "Final draft. The matched comparison group is the change since the last version the supervisor read.",
                PaperSupervisor = "The matched comparison is what makes this publishable rather than descriptive. Ready for the committee.",
                CommitteeAppointment = "Appointed across both departments, since the method is quantitative and the subject is procurement policy.",
                PaperDecision = "Accepted. The committee agreed, and the external's point about the matching variables is addressed in the final text.",
                PublishDecision = "Yes, publish. Two of the firms in the sample have already asked for a copy."
            },
            Votes =
            [
                new(DemoSeat.ReviewerOne, true, "The matching is done properly and the sensitivity analysis is reported rather than mentioned."),
                new(DemoSeat.ReviewerThree, true, "Readable and well evidenced. The policy implications follow from the findings."),
                new(DemoSeat.ExternalOne, true, "Sound. I would have matched on sector as well as size, but the authors justify their choice.")
            ]
        }
    ];
}
