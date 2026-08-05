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
        var infoTech = await EnsureDepartmentAsync(db, "Information Technology", "IT", cancellationToken);
        var business = await EnsureDepartmentAsync(db, "Business", "BUS", cancellationToken);
        await EnsureResearchAreasAsync(db, cancellationToken);

        // ---------- People ----------

        var admin = await CreateAsync(userManager, "admin.test@ais.ac.nz", "Miriam", "Ashworth", RoleNames.Admin);

        var infoTechHead = await CreateAsync(userManager, "hod.test@ais.ac.nz", "Rangi", "Patel", RoleNames.HeadOfDepartment);
        var infoTechCoordinator = await CreateAsync(userManager, "coordinator.test@ais.ac.nz", "Elena", "Vasquez", RoleNames.Coordinator);
        var infoTechSupervisor = await CreateAsync(userManager, "supervisor.test@ais.ac.nz", "Thomas", "Okoro", RoleNames.Supervisor);
        var infoTechSupervisorTwo = await CreateAsync(userManager, "supervisor.second@ais.ac.nz", "Priya", "Raman", RoleNames.Supervisor);

        var businessHead = await CreateAsync(userManager, "hod.business@ais.ac.nz", "Grace", "Lindqvist", RoleNames.HeadOfDepartment);
        var businessCoordinator = await CreateAsync(userManager, "coordinator.business@ais.ac.nz", "Daniel", "Kaur", RoleNames.Coordinator);
        var businessSupervisor = await CreateAsync(userManager, "supervisor.business@ais.ac.nz", "Aroha", "Bennett", RoleNames.Supervisor);
        var businessSupervisorTwo = await CreateAsync(userManager, "supervisor.business.second@ais.ac.nz", "Marcus", "Toledo", RoleNames.Supervisor);

        // Three reviewers and two externals, against a default composition of two reviewers and one
        // external. Two of each would satisfy the rule and produce the same committee every time,
        // which is how the dataset ended up with one committee membership repeated across every
        // paper: enough people that committees can differ is the point, not enough to be legal.
        var reviewerOne = await CreateAsync(userManager, "reviewer.test@ais.ac.nz", "Sofia", "Marchetti", RoleNames.Reviewer);
        var reviewerTwo = await CreateAsync(userManager, "reviewer.second@ais.ac.nz", "Hemi", "Walker", RoleNames.Reviewer);
        var reviewerThree = await CreateAsync(userManager, "reviewer.third@ais.ac.nz", "Anika", "Sharma", RoleNames.Reviewer);
        var externalOne = await CreateAsync(userManager, "external.test@ais.ac.nz", "Jonathan", "Reyes", RoleNames.ExternalCommitteeMember);
        var externalTwo = await CreateAsync(userManager, "external.second@ais.ac.nz", "Ingrid", "Halvorsen", RoleNames.ExternalCommitteeMember);

        // Holds only the placeholder role every staff address starts with, so there is always an
        // account to try the Admin's "grant an operational role" flow on.
        await CreateAsync(userManager, "staff.test@ais.ac.nz", "Charlotte", "Nguyen", RoleNames.Staff);

        var studentOne = await CreateAsync(userManager, MarkerEmail, "Alex", "Moreau", RoleNames.Student);
        var studentTwo = await CreateAsync(userManager, "student.second@aisstudent.ac.nz", "Fatima", "Al-Rashid", RoleNames.Student);
        var studentThree = await CreateAsync(userManager, "student.third@aisstudent.ac.nz", "Noah", "Kingi", RoleNames.Student);
        var studentFour = await CreateAsync(userManager, "student.fourth@aisstudent.ac.nz", "Yuki", "Tanaka", RoleNames.Student);
        var studentFive = await CreateAsync(userManager, "student.fifth@aisstudent.ac.nz", "Mateo", "Rossi", RoleNames.Student);
        var studentBusiness = await CreateAsync(userManager, "student.business@aisstudent.ac.nz", "Lucas", "Ferreira", RoleNames.Student);
        var studentBusinessTwo = await CreateAsync(userManager, "student.business.second@aisstudent.ac.nz", "Amara", "Okafor", RoleNames.Student);

        db.HeadOfDepartmentProfiles.AddRange(
            new HeadOfDepartmentProfile { UserId = infoTechHead.Id, DepartmentId = infoTech.Id },
            new HeadOfDepartmentProfile { UserId = businessHead.Id, DepartmentId = business.Id });

        // One Coordinator per department, so the automatic allocation a student triggers by
        // starting a publication has exactly one answer and stays predictable to test against.
        db.CoordinatorProfiles.AddRange(
            new CoordinatorProfile { UserId = infoTechCoordinator.Id, DepartmentId = infoTech.Id },
            new CoordinatorProfile { UserId = businessCoordinator.Id, DepartmentId = business.Id });

        db.SupervisorProfiles.AddRange(
            new SupervisorProfile
            {
                UserId = infoTechSupervisor.Id,
                AreasOfExpertise = "Software engineering, human-computer interaction",
                ResearchInterests = "How teams adopt development practices, and why they abandon them"
            },
            new SupervisorProfile
            {
                UserId = infoTechSupervisorTwo.Id,
                AreasOfExpertise = "Data science, applied machine learning",
                ResearchInterests = "Fairness and interpretability in models used on people"
            },
            new SupervisorProfile
            {
                UserId = businessSupervisor.Id,
                AreasOfExpertise = "Organisational behaviour, small business strategy",
                ResearchInterests = "Decision-making in owner-operated firms"
            },
            new SupervisorProfile
            {
                UserId = businessSupervisorTwo.Id,
                AreasOfExpertise = "Accounting, corporate governance",
                ResearchInterests = "How small boards oversee what they cannot audit themselves"
            });

        // Which departments each of them is attached to. One supervisor and one reviewer sit in
        // both, because a demo set where everybody is in exactly one department never shows that
        // they can be in two, and a shape nothing exercises is a shape nobody notices is broken.
        db.DepartmentMemberships.AddRange(
            new DepartmentMembership { UserId = infoTechSupervisor.Id, DepartmentId = infoTech.Id },
            new DepartmentMembership { UserId = infoTechSupervisorTwo.Id, DepartmentId = infoTech.Id },
            new DepartmentMembership { UserId = infoTechSupervisorTwo.Id, DepartmentId = business.Id },
            new DepartmentMembership { UserId = businessSupervisor.Id, DepartmentId = business.Id },
            new DepartmentMembership { UserId = businessSupervisorTwo.Id, DepartmentId = business.Id },
            new DepartmentMembership { UserId = reviewerOne.Id, DepartmentId = infoTech.Id },
            new DepartmentMembership { UserId = reviewerTwo.Id, DepartmentId = infoTech.Id },
            new DepartmentMembership { UserId = reviewerTwo.Id, DepartmentId = business.Id },
            new DepartmentMembership { UserId = reviewerThree.Id, DepartmentId = infoTech.Id },
            new DepartmentMembership { UserId = reviewerThree.Id, DepartmentId = business.Id });

        db.CommitteeMemberProfiles.AddRange(
            new CommitteeMemberProfile { UserId = reviewerOne.Id, Type = CommitteeMemberRoleType.Reviewer, Affiliation = "Auckland Institute of Studies" },
            new CommitteeMemberProfile { UserId = reviewerTwo.Id, Type = CommitteeMemberRoleType.Reviewer, Affiliation = "Auckland Institute of Studies" },
            new CommitteeMemberProfile { UserId = reviewerThree.Id, Type = CommitteeMemberRoleType.Reviewer, Affiliation = "Auckland Institute of Studies" },
            new CommitteeMemberProfile { UserId = externalOne.Id, Type = CommitteeMemberRoleType.External, Affiliation = "University of Otago" },
            new CommitteeMemberProfile { UserId = externalTwo.Id, Type = CommitteeMemberRoleType.External, Affiliation = "Massey University" });

        db.StudentProfiles.AddRange(
            // Student IDs are AAAAMMXX: year of admission, month of admission, then the order that
            // student arrived in within that month. They are made to agree with the cohort beside
            // them, so a reader checking one against the other finds the same intake rather than
            // two facts that contradict each other.
            StudentProfileFor(studentOne, infoTech, "20260204", "MSc Information Technology", "2026 Semester 1"),
            StudentProfileFor(studentTwo, infoTech, "20260217", "MSc Information Technology", "2026 Semester 1"),
            StudentProfileFor(studentThree, infoTech, "20250731", "MSc Information Technology", "2025 Semester 2"),
            StudentProfileFor(studentFour, infoTech, "20250742", "MSc Information Technology", "2025 Semester 2"),
            StudentProfileFor(studentFive, infoTech, "20250718", "MSc Information Technology", "2025 Semester 2"),
            StudentProfileFor(studentBusiness, business, "20260209", "Master of Business Administration", "2026 Semester 1"),
            StudentProfileFor(studentBusinessTwo, business, "20250726", "Master of Business Administration", "2025 Semester 2"));

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
            services.GetRequiredService<ICommitteeService>(),
            services.GetRequiredService<ISystemSettingService>());

        // Everybody who can be put on a committee, by the seat a plan names them by. Shared between
        // the departments, which is what these people are: reviewers sit across the institution and
        // externals belong to another one entirely.
        var seats = new Dictionary<DemoSeat, Guid>
        {
            [DemoSeat.ReviewerOne] = reviewerOne.Id,
            [DemoSeat.ReviewerTwo] = reviewerTwo.Id,
            [DemoSeat.ReviewerThree] = reviewerThree.Id,
            [DemoSeat.ExternalOne] = externalOne.Id,
            [DemoSeat.ExternalTwo] = externalTwo.Id
        };

        var infoTechCast = new DemoCast(
            StudentId: Guid.Empty,
            CoordinatorId: infoTechCoordinator.Id,
            PrimarySupervisorId: infoTechSupervisor.Id,
            AlternateSupervisorId: infoTechSupervisorTwo.Id,
            HeadOfDepartmentId: infoTechHead.Id,
            AdminId: admin.Id,
            Seats: seats);

        var businessCast = infoTechCast with
        {
            CoordinatorId = businessCoordinator.Id,
            PrimarySupervisorId = businessSupervisor.Id,
            AlternateSupervisorId = businessSupervisorTwo.Id,
            HeadOfDepartmentId = businessHead.Id
        };

        (ApplicationUser Student, DemoCast Cast, DemoPublicationPlan[] Plans)[] work =
        [
            (studentOne, infoTechCast, DemoPlans.ForAlexMoreau),
            (studentTwo, infoTechCast, DemoPlans.ForFatimaAlRashid),
            (studentThree, infoTechCast, DemoPlans.ForNoahKingi),
            (studentFour, infoTechCast, DemoPlans.ForYukiTanaka),
            (studentFive, infoTechCast, DemoPlans.ForMateoRossi),
            (studentBusiness, businessCast, DemoPlans.ForLucasFerreira),
            (studentBusinessTwo, businessCast, DemoPlans.ForAmaraOkafor)
        ];

        foreach (var (student, template, plans) in work)
        {
            var cast = template with { StudentId = student.Id };

            foreach (var plan in plans)
            {
                await builder.BuildAsync(cast, plan, cancellationToken);
            }
        }

        // Two things a walk through the screens would otherwise find empty: the coordinator's saved
        // sets of supervisors, and somebody who has marked themselves unavailable. Both are small
        // and both are invisible until they exist.
        await SeedSupervisorGroupsAsync(db,
            infoTechCoordinator, [infoTechSupervisor, infoTechSupervisorTwo],
            businessCoordinator, [businessSupervisor, businessSupervisorTwo], cancellationToken);

        // One supervisor is not taking work on. The chooser on Send proposals leaves them out and
        // the administrator's screens still show them, which is the difference between this and an
        // account an administrator has disabled.
        infoTechSupervisorTwo.IsAvailable = false;
        await userManager.UpdateAsync(infoTechSupervisorTwo);

        logger.LogWarning(
            "Demonstration dataset created: {Accounts} accounts across {Publications} publications, every " +
            "account sharing one published password. This deployment must never hold real work.",
            await db.Users.CountAsync(cancellationToken),
            await db.PublicationContainers.CountAsync(cancellationToken));
    }

    /// <summary>
    /// A saved set per coordinator, so the chips on Send proposals are there to be used and the
    /// administrator's tidying screen has something to tidy. Named for a research area rather than
    /// for the people in it, which is how somebody would actually name one.
    /// </summary>
    private static async Task SeedSupervisorGroupsAsync(
        ApplicationDbContext db,
        ApplicationUser infoTechCoordinator, ApplicationUser[] infoTechSupervisors,
        ApplicationUser businessCoordinator, ApplicationUser[] businessSupervisors,
        CancellationToken cancellationToken)
    {
        if (await db.SupervisorGroups.AnyAsync(cancellationToken)) return;

        db.SupervisorGroups.AddRange(
            Group(infoTechCoordinator, "Software engineering", infoTechSupervisors),
            Group(infoTechCoordinator, "Everyone in Information Technology", infoTechSupervisors),
            Group(businessCoordinator, "Business research", businessSupervisors));

        await db.SaveChangesAsync(cancellationToken);

        static SupervisorGroup Group(ApplicationUser owner, string name, ApplicationUser[] members) => new()
        {
            OwnerId = owner.Id,
            Name = name,
            Members = [.. members.Select(m => new SupervisorGroupMember { SupervisorId = m.Id })]
        };
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
        else if (department.Name != name)
        {
            // Renamed in place rather than left as it was found. Seeding an existing database
            // otherwise kept whatever name it was first given, so a department renamed here stayed
            // wrong everywhere it was read. A code that changed as well leaves the old row behind,
            // which a database reset is what clears.
            department.Name = name;
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
    /// Only files kept on local disk, which is the only destination that can lose one this way: a
    /// bucket and a database row both outlive the container. The path is composed the way the local
    /// backend composes it, deliberately, because this is recovering that backend's own files and
    /// reaching for them is the one thing the storage interface has no business exposing.
    /// </summary>
    private static async Task RestoreMissingFilesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var environment = services.GetRequiredService<IWebHostEnvironment>();

        var settings = services.GetRequiredService<ISystemSettingsProvider>();
        var configuredRoot = await settings.GetStringAsync(SettingKeys.StorageLocalPath, cancellationToken);
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? services.GetRequiredService<IOptions<FileStorageSettings>>().Value.RootPath
            : configuredRoot.Trim();

        var storageRoot = Path.IsPathRooted(root) ? root : Path.Combine(environment.ContentRootPath, root);

        var files = await db.EthicsDocuments
            .Select(d => new { d.FilePath, Title = d.EthicsDocumentRequirement.Name })
            .Concat(db.PublicationVersions.Select(v => new { v.FilePath, Title = v.Publication.Title }))
            .ToListAsync(cancellationToken);

        var restored = 0;
        foreach (var file in files)
        {
            // Stored keys name the destination that wrote them. Anything not on local disk is
            // somebody else's to keep, and a key with no prefix predates configurable storage,
            // which means it is local.
            var separator = file.FilePath.IndexOf(':');
            var isLocal = separator < 0 || file.FilePath[..separator] == "local";
            if (!isLocal) continue;

            var path = separator < 0 ? file.FilePath : file.FilePath[(separator + 1)..];

            var fullPath = Path.Combine(storageRoot, path);
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
