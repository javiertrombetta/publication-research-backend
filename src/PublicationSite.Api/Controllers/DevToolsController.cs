using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Data;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// Endpoints for shared testing deployments only. Every action here is a no-op (403) unless
/// <c>DevTools:EnableDatabaseReset</c> is explicitly set — see appsettings.json / render.yaml.
/// </summary>
[ApiController]
[Route("api/dev")]
[Authorize(Roles = RoleNames.Admin)]
public class DevToolsController(
    ApplicationDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment) : ControllerBase
{
    /// <summary>
    /// Drops and recreates the entire schema, then reseeds the eight fixed roles, the
    /// configured Admin (<c>Seed:AdminEmail</c>/<c>Seed:AdminPassword</c>), and one
    /// ready-to-use account per role (see <see cref="DevelopmentDataSeeder"/>) — everything
    /// gets wiped, including every account's login. Log in again afterwards; any token issued
    /// before the reset stops working.
    /// </summary>
    [HttpPost("reset-database")]
    public async Task<IActionResult> ResetDatabase()
    {
        if (!bool.TryParse(configuration["DevTools:EnableDatabaseReset"], out var enabled) || !enabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("Database reset is disabled on this deployment. Set DevTools:EnableDatabaseReset=true to enable it."));
        }

        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        await DbSeeder.SeedRolesAsync(HttpContext.RequestServices);
        await DbSeeder.SeedAdminAsync(HttpContext.RequestServices, configuration);
        await DevelopmentDataSeeder.SeedTestUsersAsync(HttpContext.RequestServices, environment, allowOutsideDevelopment: true);

        return Ok(ApiResponse.Ok(
            "Database reset. Schema recreated; roles, the configured Admin, and one test account per role were reseeded. Log in again — previous tokens are no longer valid."));
    }
}
