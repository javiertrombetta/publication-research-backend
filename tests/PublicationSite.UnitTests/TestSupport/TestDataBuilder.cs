using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;

namespace PublicationSite.UnitTests.TestSupport;

/// <summary>Small convenience factory for entities commonly needed across service tests.</summary>
public static class TestDataBuilder
{
    private static int _counter;

    private static string Next(string prefix) => $"{prefix}{System.Threading.Interlocked.Increment(ref _counter)}";

    public static Department Department(ApplicationDbContext db, string? name = null, string? code = null)
    {
        var department = new Api.Entities.Department
        {
            Name = name ?? Next("Department"),
            Code = code ?? Next("D")
        };
        db.Departments.Add(department);
        db.SaveChanges();
        return department;
    }

    public static ApplicationUser User(ApplicationDbContext db, string? email = null, UserStatus status = UserStatus.Enabled)
    {
        var user = new ApplicationUser
        {
            Email = email ?? $"{Next("user")}@example.com",
            UserName = email ?? $"{Next("user")}@example.com",
            FirstName = "Test",
            LastName = "User",
            Status = status,
            EmailConfirmed = true
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public static StudentProfile StudentProfile(ApplicationDbContext db, ApplicationUser user, Department department)
    {
        var profile = new Api.Entities.StudentProfile
        {
            UserId = user.Id,
            DepartmentId = department.Id,
            StudentIdNumber = Next("S"),
            Programme = "MSc Computer Science",
            Cohort = "2026"
        };
        db.StudentProfiles.Add(profile);
        db.SaveChanges();
        return profile;
    }

    public static CoordinatorProfile CoordinatorProfile(ApplicationDbContext db, ApplicationUser user, Department department, bool isAvailable = true)
    {
        var profile = new Api.Entities.CoordinatorProfile
        {
            UserId = user.Id,
            DepartmentId = department.Id,
            IsAvailableForAssignment = isAvailable
        };
        db.CoordinatorProfiles.Add(profile);
        db.SaveChanges();
        return profile;
    }

    public static SupervisorProfile SupervisorProfile(ApplicationDbContext db, ApplicationUser user, Department department)
    {
        var profile = new Api.Entities.SupervisorProfile
        {
            UserId = user.Id,
            DepartmentId = department.Id
        };
        db.SupervisorProfiles.Add(profile);
        db.SaveChanges();
        return profile;
    }

    public static HeadOfDepartmentProfile HeadOfDepartmentProfile(ApplicationDbContext db, ApplicationUser user, Department department)
    {
        var profile = new Api.Entities.HeadOfDepartmentProfile
        {
            UserId = user.Id,
            DepartmentId = department.Id
        };
        db.HeadOfDepartmentProfiles.Add(profile);
        db.SaveChanges();
        return profile;
    }

    public static CommitteeMemberProfile CommitteeMemberProfile(ApplicationDbContext db, ApplicationUser user, CommitteeMemberRoleType type = CommitteeMemberRoleType.Internal)
    {
        var profile = new Api.Entities.CommitteeMemberProfile
        {
            UserId = user.Id,
            Type = type
        };
        db.CommitteeMemberProfiles.Add(profile);
        db.SaveChanges();
        return profile;
    }

    public static PublicationContainer Container(
        ApplicationDbContext db, ApplicationUser student, ApplicationUser coordinator,
        ApplicationUser? supervisor = null, PipelineStage stage = PipelineStage.ResearchProposals,
        ContainerStatus status = ContainerStatus.InProgress)
    {
        var container = new Api.Entities.PublicationContainer
        {
            StudentId = student.Id,
            CoordinatorId = coordinator.Id,
            AssignedSupervisorId = supervisor?.Id,
            CurrentPipeline = stage,
            Status = status
        };
        db.PublicationContainers.Add(container);
        db.SaveChanges();
        return container;
    }

    public static ResearchProposal Proposal(
        ApplicationDbContext db, PublicationContainer container,
        string? title = null, ProposalStatus status = ProposalStatus.Draft)
    {
        var proposal = new ResearchProposal
        {
            PublicationContainerId = container.Id,
            Title = title ?? $"Proposal {Guid.NewGuid():N}",
            Abstract = "Abstract.",
            Status = status
        };
        db.ResearchProposals.Add(proposal);
        db.SaveChanges();
        return proposal;
    }
}
