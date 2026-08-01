using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PublicationSite.Api.Common;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Data;

/// <summary>
/// Production-safe startup seeding: the fixed role set, and (only when explicitly configured) the
/// single initial Admin account. Safe to call unconditionally on every startup in every
/// environment. Everything here is idempotent and no-ops when its configuration is absent.
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
    /// <c>Seed:AdminPassword</c>, if both are configured and no account with that email already
    /// exists. Intended for production/staging bootstrap: set those two values via environment
    /// variables or a secret store for the first deploy, then remove them. This never touches an
    /// existing account, so leaving them set afterwards is harmless but unnecessary. Never logs the
    /// password.
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
            logger.LogError(
                "Failed to seed the initial Admin account from Seed:AdminEmail/Seed:AdminPassword: {Errors}. Fix the configured values and redeploy; the app will retry on next startup.",
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, RoleNames.Admin);

        logger.LogWarning(
            "Seeded the initial Admin account ({Email}). Remove Seed:AdminEmail/Seed:AdminPassword from configuration now that it exists.",
            adminEmail);
    }

    /// <summary>
    /// Says so, loudly, when this deployment has come up with nobody who can administer it.
    ///
    /// It is an easy state to reach and a silent one to be in: point a service at an empty
    /// database with neither <c>Seed:AdminEmail</c> nor demonstration data, and the API starts
    /// perfectly, reports healthy and serves the public catalogue. The first anyone knows of it is
    /// a sign-in page that no credentials open, which looks like a broken login rather than an
    /// empty user table. One line in the startup log turns that into an instruction.
    ///
    /// Deliberately a log line and not a refusal to start. An API with no administrator is still
    /// serving every reader the catalogue, and crash-looping over a configuration value that can
    /// be supplied at any time would take that away to fix nothing.
    ///
    /// Enabled accounts only: an Admin that is disabled or awaiting confirmation is not a way in.
    /// </summary>
    public static async Task WarnIfNoAdministratorAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var administrators = await userManager.GetUsersInRoleAsync(RoleNames.Admin);
        if (administrators.Any(a => a.Status == UserStatus.Enabled))
        {
            return;
        }

        services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder)).LogError(
            "This deployment has no enabled Admin account, so nobody can sign in to administer it. " +
            "Set Seed:AdminEmail and Seed:AdminPassword and deploy again. The account is created on the " +
            "next startup, and neither value ever overwrites an existing account, so this is safe to do " +
            "at any point. Clear them once you have signed in.");
    }
}
