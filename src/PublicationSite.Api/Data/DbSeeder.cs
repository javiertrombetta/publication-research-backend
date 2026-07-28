using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PublicationSite.Api.Common;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Data;

/// <summary>
/// Production-safe startup seeding: the fixed role set, and (only when explicitly
/// configured) the single initial Admin account. Safe to call unconditionally on every
/// startup in every environment — everything here is idempotent and no-ops when its
/// configuration is absent.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var roleName in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole(roleName));
            }
        }
    }

    /// <summary>
    /// Creates the one initial Admin account from <c>Seed:AdminEmail</c> /
    /// <c>Seed:AdminPassword</c>, if both are configured and no account with that email
    /// already exists. Intended for production/staging bootstrap: set those two values via
    /// environment variables or a secret store for the first deploy, then remove them —
    /// this never touches an existing account, so leaving them set afterwards is harmless
    /// but unnecessary. Never logs the password.
    /// </summary>
    public static async Task SeedAdminAsync(IServiceProvider services, IConfiguration configuration)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));

        var adminEmail = configuration["Seed:AdminEmail"];
        var adminPassword = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByEmailAsync(adminEmail) is not null)
        {
            logger.LogInformation("Seed:AdminEmail is configured but an account with that email already exists; skipping.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "Admin",
            LastName = "Account",
            Status = UserStatus.Enabled,
            EmailConfirmed = true,
            AuthProvider = AuthProvider.Local
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed the initial Admin account: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(admin, RoleNames.Admin);

        logger.LogWarning(
            "Seeded the initial Admin account ({Email}). Remove Seed:AdminEmail/Seed:AdminPassword from configuration now that it exists.",
            adminEmail);
    }
}
