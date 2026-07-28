using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PublicationSite.Api.Common;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;

namespace PublicationSite.IntegrationTests.Infrastructure;

/// <summary>
/// Bootstraps accounts directly via UserManager/DbContext (bypassing HTTP + real email
/// delivery) so tests can set up realistic fixtures — an Admin, a Department, a Coordinator,
/// a Supervisor — without re-testing registration/verification on every single test.
/// </summary>
public static class TestSeeder
{
    public static async Task<Department> CreateDepartmentAsync(ApiTestFactory factory, string? name = null, string? code = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var department = new Department { Name = name ?? $"Dept-{Guid.NewGuid():N}", Code = code ?? Guid.NewGuid().ToString("N")[..8] };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department;
    }

    public static Task<ApplicationUser> CreateEnabledUserAsync(ApiTestFactory factory, string role, string? email = null, Guid? departmentId = null) =>
        CreateUserInternalAsync(factory, role, email, departmentId, confirmedAndEnabled: true);

    public static async Task ConfirmEmailAsync(ApiTestFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException($"User '{email}' not found.");
        user.Status = UserStatus.Enabled;
        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);
    }

    public static async Task<Guid> GetUserIdAsync(ApiTestFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    private static async Task<ApplicationUser> CreateUserInternalAsync(
        ApiTestFactory factory, string role, string? email, Guid? departmentId, bool confirmedAndEnabled)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var resolvedEmail = email ?? $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@ais.ac.nz";
        var user = new ApplicationUser
        {
            Email = resolvedEmail,
            UserName = resolvedEmail,
            FirstName = role,
            LastName = "TestUser",
            Status = confirmedAndEnabled ? UserStatus.Enabled : UserStatus.Pending,
            EmailConfirmed = confirmedAndEnabled
        };

        var result = await userManager.CreateAsync(user, "SuperSecret123!");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(user, role);

        var resolvedDepartmentId = departmentId ?? (await EnsureAnyDepartmentAsync(db)).Id;

        switch (role)
        {
            case RoleNames.Coordinator:
                db.CoordinatorProfiles.Add(new CoordinatorProfile { UserId = user.Id, DepartmentId = resolvedDepartmentId });
                break;
            case RoleNames.Supervisor:
                db.SupervisorProfiles.Add(new SupervisorProfile { UserId = user.Id, DepartmentId = resolvedDepartmentId });
                break;
            case RoleNames.HeadOfDepartment:
                db.HeadOfDepartmentProfiles.Add(new HeadOfDepartmentProfile { UserId = user.Id, DepartmentId = resolvedDepartmentId });
                break;
            case RoleNames.InternalCommitteeMember:
                db.CommitteeMemberProfiles.Add(new CommitteeMemberProfile { UserId = user.Id, Type = CommitteeMemberRoleType.Internal });
                break;
            case RoleNames.ExternalCommitteeMember:
                db.CommitteeMemberProfiles.Add(new CommitteeMemberProfile { UserId = user.Id, Type = CommitteeMemberRoleType.External });
                break;
        }

        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Department> EnsureAnyDepartmentAsync(ApplicationDbContext db)
    {
        var existing = await db.Departments.FirstOrDefaultAsync();
        if (existing is not null)
        {
            return existing;
        }

        var department = new Department { Name = "Default Department", Code = "DEF" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department;
    }
}
