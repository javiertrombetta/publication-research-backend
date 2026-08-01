using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Data;

/// <summary>
/// Fills an empty database with a worked example of the whole system: an account for every role,
/// and a publication parked at each point in the three pipelines where somebody has to act.
///
/// <b>Never for a deployment holding real work.</b> Every account here shares one published
/// password, which is the entire point on a machine the team is testing against and a serious
/// vulnerability anywhere else. <see cref="IsEnabled"/> is what decides, and it is deliberately
/// closed by default outside development. See the remarks there.
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoPassword = "DevTest123!";

    /// <summary>
    /// If this account exists the dataset has already been built, so the whole thing is skipped.
    /// </summary>
    private const string MarkerEmail = "student.test@aisstudent.ac.nz";

    /// <summary>
    /// Whether this deployment wants the demonstration dataset.
    ///
    /// Three deployments exist and only two of them do. A developer's machine wants it without
    /// being asked, which is what the environment covers. The shared instance the team tests
    /// against also wants it, but runs as Production. It has a real hostname, a hosted database and
    /// TLS in front of it, and pretending otherwise to get the data would also switch on developer
    /// exception pages and the local connection string. So it asks for the data explicitly, with
    /// <c>Seed:DemoData</c>.
    ///
    /// Production sets nothing and gets nothing: no flag, no accounts. That is the safe direction
    /// for the mistake to fall. Forgetting the setting costs a deployment its sample data, where
    /// the opposite would publish a known password on the live site.
    /// </summary>
    /// <remarks>
    /// Parsed leniently on purpose. Binding it as a bool throws on anything that is not "true" or
    /// "false", and this is read while the application is being built, so "yes" in an environment
    /// variable took the whole service down and kept it down, restart after restart. A typo should
    /// not be able to do that. Anything unrecognised falls back to the environment's own answer,
    /// which is off outside development, so the failure still lands on the safe side.
    /// </remarks>
    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        bool.TryParse(configuration["Seed:DemoData"], out var enabled)
            ? enabled
            : environment.IsDevelopment();

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DemoDataSeeder));
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();

        if (await userManager.FindByEmailAsync(MarkerEmail) is not null)
        {
            // The rows are there, but on a host with an ephemeral disk the uploads that go with
            // them may not be. See RestoreMissingFilesAsync.
            await RestoreMissingFilesAsync(services, cancellationToken);
            return;
        }

        logger.LogInformation("Building the demonstration dataset. This runs once, on an empty database.");

        // All of it or none of it. The marker account is created early on, so a run that died part-
        // way, whether a failed startup or a container killed mid-deploy, would leave a half-built
        // dataset that every later start mistook for a finished one and skipped. Committing once at
        // the end means an interrupted run leaves nothing behind and the next start rebuilds from
        // scratch. (Files already written to disk are the exception, and are harmless: they are
        // orphaned bytes nothing points at.)
        //
        // Routed through the execution strategy because the context retries on transient failures,
        // and a transaction opened outside one would silently lose that protection. A retry would
        // re-enter this delegate with the previous attempt's entities still tracked, so it is not
        // expected to succeed, but the rollback leaves a clean database, and the next start
        // rebuilds properly, which is the outcome that matters.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await BuildAsync(services, db, userManager, logger, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static async Task BuildAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var computing = await EnsureDepartmentAsync(db, "Computing and Information Technology", "CIT", cancellationToken);
        var business = await EnsureDepartmentAsync(db, "Business and Management", "BUS", cancellationToken);
        await EnsureResearchAreasAsync(db, cancellationToken);

        // ---------- People ----------

        var admin = await CreateAsync(userManager, "admin.test@ais.ac.nz", "Miriam", "Ashworth", RoleNames.Admin);

        var computingHead = await CreateAsync(userManager, "hod.test@ais.ac.nz", "Rangi", "Patel", RoleNames.HeadOfDepartment);
        var computingCoordinator = await CreateAsync(userManager, "coordinator.test@ais.ac.nz", "Elena", "Vasquez", RoleNames.Coordinator);
        var computingSupervisor = await CreateAsync(userManager, "supervisor.test@ais.ac.nz", "Thomas", "Okoro", RoleNames.Supervisor);
        var computingSupervisorTwo = await CreateAsync(userManager, "supervisor.second@ais.ac.nz", "Priya", "Raman", RoleNames.Supervisor);

        var businessHead = await CreateAsync(userManager, "hod.business@ais.ac.nz", "Grace", "Lindqvist", RoleNames.HeadOfDepartment);
        var businessCoordinator = await CreateAsync(userManager, "coordinator.business@ais.ac.nz", "Daniel", "Kaur", RoleNames.Coordinator);
        var businessSupervisor = await CreateAsync(userManager, "supervisor.business@ais.ac.nz", "Aroha", "Bennett", RoleNames.Supervisor);
        var businessSupervisorTwo = await CreateAsync(userManager, "supervisor.business.second@ais.ac.nz", "Marcus", "Toledo", RoleNames.Supervisor);

        // Two of each kind, because the default composition asks for two internal members and one
        // external. A single one of each would make the standard committee impossible to build,
        // and whoever tried would meet a rule they could not satisfy rather than a working system.
        var internalOne = await CreateAsync(userManager, "internal.test@ais.ac.nz", "Sofia", "Marchetti", RoleNames.InternalCommitteeMember);
        var internalTwo = await CreateAsync(userManager, "internal.second@ais.ac.nz", "Hemi", "Walker", RoleNames.InternalCommitteeMember);
        var externalOne = await CreateAsync(userManager, "external.test@ais.ac.nz", "Jonathan", "Reyes", RoleNames.ExternalCommitteeMember);
        var externalTwo = await CreateAsync(userManager, "external.second@ais.ac.nz", "Ingrid", "Halvorsen", RoleNames.ExternalCommitteeMember);

        // Holds only the placeholder role every staff address starts with, so there is always an
        // account to try the Admin's "grant an operational role" flow on.
        await CreateAsync(userManager, "staff.test@ais.ac.nz", "Charlotte", "Nguyen", RoleNames.Staff);

        var studentOne = await CreateAsync(userManager, MarkerEmail, "Alex", "Moreau", RoleNames.Student);
        var studentTwo = await CreateAsync(userManager, "student.second@aisstudent.ac.nz", "Fatima", "Al-Rashid", RoleNames.Student);
        var studentThree = await CreateAsync(userManager, "student.third@aisstudent.ac.nz", "Noah", "Kingi", RoleNames.Student);
        var studentFour = await CreateAsync(userManager, "student.fourth@aisstudent.ac.nz", "Yuki", "Tanaka", RoleNames.Student);
        var studentBusiness = await CreateAsync(userManager, "student.business@aisstudent.ac.nz", "Lucas", "Ferreira", RoleNames.Student);

        db.HeadOfDepartmentProfiles.AddRange(
            new HeadOfDepartmentProfile { UserId = computingHead.Id, DepartmentId = computing.Id },
            new HeadOfDepartmentProfile { UserId = businessHead.Id, DepartmentId = business.Id });

        // One Coordinator per department, so the automatic allocation a student triggers by
        // starting a publication has exactly one answer and stays predictable to test against.
        db.CoordinatorProfiles.AddRange(
            new CoordinatorProfile { UserId = computingCoordinator.Id, DepartmentId = computing.Id },
            new CoordinatorProfile { UserId = businessCoordinator.Id, DepartmentId = business.Id });

        db.SupervisorProfiles.AddRange(
            new SupervisorProfile
            {
                UserId = computingSupervisor.Id, DepartmentId = computing.Id,
                AreasOfExpertise = "Software engineering, human-computer interaction",
                ResearchInterests = "How teams adopt development practices, and why they abandon them"
            },
            new SupervisorProfile
            {
                UserId = computingSupervisorTwo.Id, DepartmentId = computing.Id,
                AreasOfExpertise = "Data science, applied machine learning",
                ResearchInterests = "Fairness and interpretability in models used on people"
            },
            new SupervisorProfile
            {
                UserId = businessSupervisor.Id, DepartmentId = business.Id,
                AreasOfExpertise = "Organisational behaviour, small business strategy",
                ResearchInterests = "Decision-making in owner-operated firms"
            },
            new SupervisorProfile
            {
                UserId = businessSupervisorTwo.Id, DepartmentId = business.Id,
                AreasOfExpertise = "Accounting, corporate governance",
                ResearchInterests = "How small boards oversee what they cannot audit themselves"
            });

        db.CommitteeMemberProfiles.AddRange(
            new CommitteeMemberProfile { UserId = internalOne.Id, Type = CommitteeMemberRoleType.Internal, Affiliation = "Auckland Institute of Studies" },
            new CommitteeMemberProfile { UserId = internalTwo.Id, Type = CommitteeMemberRoleType.Internal, Affiliation = "Auckland Institute of Studies" },
            new CommitteeMemberProfile { UserId = externalOne.Id, Type = CommitteeMemberRoleType.External, Affiliation = "University of Otago" },
            new CommitteeMemberProfile { UserId = externalTwo.Id, Type = CommitteeMemberRoleType.External, Affiliation = "Massey University" });

        db.StudentProfiles.AddRange(
            StudentProfileFor(studentOne, computing, "AIS-2026-0184", "MSc Information Technology", "2026 Semester 1"),
            StudentProfileFor(studentTwo, computing, "AIS-2026-0207", "MSc Information Technology", "2026 Semester 1"),
            StudentProfileFor(studentThree, computing, "AIS-2025-0912", "MSc Information Technology", "2025 Semester 2"),
            StudentProfileFor(studentFour, computing, "AIS-2025-0877", "MSc Information Technology", "2025 Semester 2"),
            StudentProfileFor(studentBusiness, business, "AIS-2026-0341", "Master of Business Administration", "2026 Semester 1"));

        // admin.test holds no profile, matching a real Admin: the role is an administrative
        // capability rather than a place in a department.
        await db.SaveChangesAsync(cancellationToken);

        // ---------- Publications ----------

        var builder = new DemoPipelineBuilder(
            db,
            services.GetRequiredService<IContainerService>(),
            services.GetRequiredService<IProposalService>(),
            services.GetRequiredService<IEthicsService>(),
            services.GetRequiredService<IPublicationService>(),
            services.GetRequiredService<ICommitteeService>());

        var computingCast = new DemoCast(
            StudentId: Guid.Empty,
            CoordinatorId: computingCoordinator.Id,
            PrimarySupervisorId: computingSupervisor.Id,
            AlternateSupervisorId: computingSupervisorTwo.Id,
            HeadOfDepartmentId: computingHead.Id,
            AdminId: admin.Id,
            CommitteeMemberIds: [internalOne.Id, internalTwo.Id, externalOne.Id]);

        var businessCast = computingCast with
        {
            CoordinatorId = businessCoordinator.Id,
            PrimarySupervisorId = businessSupervisor.Id,
            AlternateSupervisorId = businessSupervisorTwo.Id,
            HeadOfDepartmentId = businessHead.Id,
            CommitteeMemberIds = [internalOne.Id, internalTwo.Id, externalTwo.Id]
        };

        foreach (var (student, plans) in PlansByStudent(studentOne, studentTwo, studentThree, studentFour, studentBusiness))
        {
            var cast = (student == studentBusiness ? businessCast : computingCast) with { StudentId = student.Id };

            foreach (var plan in plans)
            {
                await builder.BuildAsync(cast, plan, cancellationToken);
            }
        }

        logger.LogWarning(
            "Demonstration dataset created: {Accounts} accounts across {Publications} publications, every " +
            "account sharing one published password. This deployment must never hold real work.",
            await db.Users.CountAsync(cancellationToken),
            await db.PublicationContainers.CountAsync(cancellationToken));
    }

    /// <summary>
    /// Which publication sits where. Every stage appears at least once, so each role signs in to
    /// find something of theirs waiting, and the first student carries the stages a student acts
    /// on so one account can be walked from an empty publication to a published paper.
    /// </summary>
    private static IEnumerable<(ApplicationUser Student, DemoPublicationPlan[] Plans)> PlansByStudent(
        ApplicationUser one, ApplicationUser two, ApplicationUser three, ApplicationUser four, ApplicationUser business)
    {
        yield return (one,
        [
            new("Automated accessibility testing in continuous integration",
                "Whether accessibility checks running on every build change what developers fix, and when.",
                DemoStage.ProposalsDrafted),

            new("Pair programming and defect density in student projects",
                "A comparison of defect rates between paired and solo work across one teaching semester.",
                DemoStage.SupervisorAssigned),

            new("Interview study of code review practice in small teams",
                "What reviewers in teams of fewer than ten people actually look for, in their own words.",
                DemoStage.EthicsDocumentsRequested),

            new("Latency perception in progressive web applications",
                "How long an interface can take to respond before people report it as slow.",
                DemoStage.EthicsCompleted, EthicsRequired: false),

            new("Onboarding documentation and time to first contribution",
                "Measuring how documentation quality affects how quickly new contributors ship something.",
                DemoStage.PaperAccepted, EthicsRequired: false),

            new("Static analysis adoption in New Zealand software teams",
                "A survey of which static analysis tools are adopted, which are abandoned, and why.",
                DemoStage.Published, Keywords: ["static analysis", "software quality", "developer practice"], Year: 2026)
        ]);

        yield return (two,
        [
            new("Energy cost of client-side rendering on low-end devices",
                "Measuring battery consumption of comparable interfaces rendered on the client and on the server.",
                DemoStage.ProposalsSubmitted),

            new("Test flakiness and developer trust in build pipelines",
                "Whether intermittent test failures change how teams respond to a red build.",
                DemoStage.ProposalsWithSupervisors),

            new("Data minimisation in student information systems",
                "An audit of what personal data teaching systems collect against what they demonstrably use.",
                DemoStage.ProposalSelected),

            new("Retrieval practice in introductory programming courses",
                "A controlled comparison of retrieval practice against re-reading in a first programming paper.",
                DemoStage.Published, Keywords: ["computing education", "retrieval practice", "assessment"], Year: 2025)
        ]);

        yield return (three,
        [
            new("Screen reader compatibility of learning management systems",
                "An evaluation of three widely deployed platforms against WCAG 2.2 success criteria.",
                DemoStage.EthicsDeclared),

            new("Open data reuse in institutional research repositories",
                "How often deposited datasets are cited, and by whom, across five years of deposits.",
                DemoStage.EthicsNotRequiredAwaitingCoordinator, EthicsRequired: false),

            new("Wellbeing and workload in postgraduate research cohorts",
                "A mixed-methods study of reported workload against supervision arrangements.",
                DemoStage.EthicsDocumentsUploaded),

            new("Peer feedback quality in online group assessment",
                "Whether structured prompts improve the specificity of feedback students give one another.",
                DemoStage.EthicsDocumentsWithCoordinator),

            new("Digital literacy on entry to postgraduate study",
                "Establishing a baseline of incoming digital skills and where the gaps cluster.",
                DemoStage.EthicsWithHeadOfDepartment),

            new("Attendance patterns and outcomes in blended delivery",
                "Relating attendance in blended courses to final outcomes, controlling for prior attainment.",
                DemoStage.EthicsAwaitingFinalDecision)
        ]);

        yield return (four,
        [
            new("Version control practice among first-year students",
                "What students do with version control when nobody is grading how they use it.",
                DemoStage.PaperWithSupervisor, EthicsRequired: false),

            new("Continuous deployment in regulated environments",
                "How teams under audit requirements reconcile them with deploying several times a day.",
                DemoStage.PaperAwaitingCommittee, EthicsRequired: false),

            new("Technical debt reporting and its effect on planning",
                "Whether making technical debt visible in planning changes what teams schedule.",
                DemoStage.CommitteeReviewing, EthicsRequired: false),

            new("Search behaviour in institutional publication catalogues",
                "Log analysis of how readers actually search a research catalogue, against how it is designed.",
                DemoStage.PaperAwaitingFinalDecision, EthicsRequired: false)
        ]);

        // In the other department, so the Head of Department's view can be seen to be limited to
        // their own students rather than to everyone's.
        yield return (business,
        [
            new("Succession planning in owner-operated New Zealand firms",
                "How firms without a designated successor plan, or avoid planning, for the owner's exit.",
                DemoStage.EthicsWithHeadOfDepartment)
        ]);
    }

    private static StudentProfile StudentProfileFor(
        ApplicationUser user, Department department, string idNumber, string programme, string cohort) =>
        new()
        {
            UserId = user.Id,
            DepartmentId = department.Id,
            StudentIdNumber = idNumber,
            Programme = programme,
            Cohort = cohort
        };

    private static async Task<Department> EnsureDepartmentAsync(
        ApplicationDbContext db, string name, string code, CancellationToken cancellationToken)
    {
        var department = await db.Departments.FirstOrDefaultAsync(d => d.Code == code, cancellationToken);
        if (department is null)
        {
            department = new Department { Name = name, Code = code };
            db.Departments.Add(department);
            await db.SaveChangesAsync(cancellationToken);
        }

        return department;
    }

    private static async Task EnsureResearchAreasAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        string[] names =
        [
            "Software Engineering",
            "Human-Computer Interaction",
            "Data Science",
            "Information Security",
            "Computing Education",
            "Organisational Behaviour"
        ];

        var existing = await db.ResearchAreas.Select(a => a.Name).ToListAsync(cancellationToken);
        var missing = names.Except(existing).Select(name => new ResearchArea { Name = name }).ToList();

        if (missing.Count > 0)
        {
            db.ResearchAreas.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<ApplicationUser> CreateAsync(
        UserManager<ApplicationUser> userManager, string email, string firstName, string lastName, string role)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Status = UserStatus.Enabled,
            EmailConfirmed = true,
            AuthProvider = AuthProvider.Local,
            PasswordChangedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, DemoPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the demonstration account '{email}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    /// <summary>
    /// Rewrites any demonstration upload whose file is no longer on disk.
    ///
    /// The database outlives the container but the container's filesystem does not: a redeploy of
    /// the shared testing instance leaves every ethics document and every paper version pointing
    /// at a file that is gone, and the first reviewer to open one meets a 404 in a system that is
    /// otherwise working. Replacing them costs nothing and keeps that environment usable.
    ///
    /// Only for local storage, which is the only backend that can lose a file this way. The path
    /// is composed the same way <c>LocalFileStorageService</c> composes it, deliberately: it is
    /// recovering that service's own files, and reaching for them is the one thing the storage
    /// interface has no business exposing.
    /// </summary>
    private static async Task RestoreMissingFilesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (services.GetRequiredService<IFileStorageService>() is not Services.Implementations.LocalFileStorageService)
        {
            return;
        }

        var db = services.GetRequiredService<ApplicationDbContext>();
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var storageRoot = Path.Combine(
            environment.ContentRootPath,
            services.GetRequiredService<IOptions<FileStorageSettings>>().Value.RootPath);

        var files = await db.EthicsDocuments
            .Select(d => new { d.FilePath, Title = d.EthicsDocumentRequirement.Name })
            .Concat(db.PublicationVersions.Select(v => new { v.FilePath, Title = v.Publication.Title }))
            .ToListAsync(cancellationToken);

        var restored = 0;
        foreach (var file in files)
        {
            var fullPath = Path.Combine(storageRoot, file.FilePath);
            if (File.Exists(fullPath)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath,
                DemoDocuments.Pdf(file.Title, "Replaced after the container's filesystem was reset."), cancellationToken);
            restored++;
        }

        if (restored > 0)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DemoDataSeeder))
                .LogInformation("Replaced {Count} demonstration upload(s) missing from disk after a redeploy.", restored);
        }
    }
}
