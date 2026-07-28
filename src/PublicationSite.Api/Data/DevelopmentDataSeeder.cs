using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using PublicationSite.Api.Common;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Data;

/// <summary>
/// Creates one ready-to-use, already-enabled account per role for local manual testing.
/// <b>Development environment only</b> — every account uses the same publicly-known
/// password, which would be a real vulnerability anywhere else. The caller in
/// <c>Program.cs</c> already gates this behind <see cref="IHostEnvironment.IsDevelopment"/>;
/// this class re-checks it itself so an accidental call from elsewhere fails loudly instead
/// of quietly seeding known credentials.
/// </summary>
public static class DevelopmentDataSeeder
{
    public const string TestUserPassword = "DevTest123!";
    private const string MarkerEmail = "student.test@aisstudent.ac.nz";

    /// <param name="allowOutsideDevelopment">
    /// Set only by <see cref="Controllers.DevToolsController"/>, itself gated behind
    /// <c>DevTools:EnableDatabaseReset</c>, so these known-password accounts can be
    /// (re)seeded on a shared frontend-testing deployment without loosening the guard
    /// for every other caller.
    /// </param>
    public static async Task SeedTestUsersAsync(
        IServiceProvider services, IHostEnvironment environment, bool allowOutsideDevelopment = false)
    {
        if (!environment.IsDevelopment() && !allowOutsideDevelopment)
        {
            throw new InvalidOperationException(
                $"{nameof(DevelopmentDataSeeder)} must only run in the Development environment.");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();

        // Idempotency guard: if the marker account exists, assume the whole batch already does.
        if (await userManager.FindByEmailAsync(MarkerEmail) is not null)
        {
            return;
        }

        var department = await db.Departments.FirstOrDefaultAsync(d => d.Code == "TEST");
        if (department is null)
        {
            department = new Department { Name = "Test Department", Code = "TEST" };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
        }

        var admin = await CreateUserAsync(userManager, "admin.test@ais.ac.nz", "Test", "Admin", RoleNames.Admin);
        var headOfDepartment = await CreateUserAsync(userManager, "hod.test@ais.ac.nz", "Test", "HeadOfDepartment", RoleNames.HeadOfDepartment);
        var coordinator = await CreateUserAsync(userManager, "coordinator.test@ais.ac.nz", "Test", "Coordinator", RoleNames.Coordinator);
        var supervisor = await CreateUserAsync(userManager, "supervisor.test@ais.ac.nz", "Test", "Supervisor", RoleNames.Supervisor);
        var internalMember = await CreateUserAsync(userManager, "internal.test@ais.ac.nz", "Test", "InternalCommitteeMember", RoleNames.InternalCommitteeMember);
        var externalMember = await CreateUserAsync(userManager, "external.test@ais.ac.nz", "Test", "ExternalCommitteeMember", RoleNames.ExternalCommitteeMember);
        var student = await CreateUserAsync(userManager, MarkerEmail, "Test", "Student", RoleNames.Student);
        await CreateUserAsync(userManager, "staff.test@ais.ac.nz", "Test", "Staff", RoleNames.Staff);

        db.HeadOfDepartmentProfiles.Add(new HeadOfDepartmentProfile { UserId = headOfDepartment.Id, DepartmentId = department.Id });
        db.CoordinatorProfiles.Add(new CoordinatorProfile { UserId = coordinator.Id, DepartmentId = department.Id });
        db.SupervisorProfiles.Add(new SupervisorProfile { UserId = supervisor.Id, DepartmentId = department.Id });
        db.CommitteeMemberProfiles.Add(new CommitteeMemberProfile { UserId = internalMember.Id, Type = CommitteeMemberRoleType.Internal });
        db.CommitteeMemberProfiles.Add(new CommitteeMemberProfile { UserId = externalMember.Id, Type = CommitteeMemberRoleType.External });
        db.StudentProfiles.Add(new StudentProfile
        {
            UserId = student.Id,
            DepartmentId = department.Id,
            StudentIdNumber = "TEST-0001",
            Programme = "MSc Computer Science",
            Cohort = "2026"
        });
        // admin.test has no profile — the Admin role doesn't need one, matching production Admins.

        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> CreateUserAsync(
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
            AuthProvider = AuthProvider.Local
        };

        var result = await userManager.CreateAsync(user, TestUserPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed test user '{email}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, role);
        return user;
    }
}
