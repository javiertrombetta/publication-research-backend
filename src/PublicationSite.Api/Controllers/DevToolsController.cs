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
    IHostEnvironment environment,
    IServiceProvider services) : ControllerBase
{
    /// <summary>
    /// Drops and recreates the entire schema, then reseeds the eight fixed roles, the configured
    /// Admin (<c>Seed:AdminEmail</c>/<c>Seed:AdminPassword</c>) and, where this deployment asks
    /// for it, the demonstration dataset (see <see cref="DemoDataSeeder"/>) — everything gets
    /// wiped, including every account's login. Log in again afterwards; any token issued before
    /// the reset stops working.
    ///
    /// Returns as soon as the schema and the accounts needed to sign in exist. The demonstration
    /// publications are built in the background over the following minute or so, because doing it
    /// inline would hold this request open long enough for a proxy in front to give up on it.
    /// Poll <c>GET api/dev/demo-data</c> to see when it has finished.
    /// </summary>
    [HttpPost("reset-database")]
    public async Task<IActionResult> ResetDatabase()
    {
        if (!bool.TryParse(configuration["DevTools:EnableDatabaseReset"], out var enabled) || !enabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("Database reset is disabled on this deployment. Set DevTools:EnableDatabaseReset=true to enable it."));
        }

        // Both flags, not either. This deletes everything, and the question it really turns on is
        // whether the data here is disposable — which is the question Seed:DemoData already
        // answers. A deployment that seeds no sample data is one holding either real work or
        // nothing worth wiping, so an administrator's token should not be able to erase it even
        // if somebody left the reset switch on by mistake.
        if (!DemoDataSeeder.IsEnabled(configuration, environment))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail(
                    "Database reset is only available where the demonstration dataset is, because that is what " +
                    "marks a deployment's data as disposable. This one does not seed it (Seed:DemoData is off), " +
                    "so its database is treated as holding real work."));
        }

        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        await DbSeeder.SeedRolesAsync(HttpContext.RequestServices);
        await DbSeeder.SeedAdminAsync(HttpContext.RequestServices, configuration);

        // Registered only where the dataset is wanted, so its absence is the answer for a
        // deployment that carries none, rather than something to work around here.
        var demoData = services.GetService<DemoDataSeedRunner>();
        demoData?.Trigger();

        var demoNote = demoData is null
            ? "This deployment seeds no demonstration data, so the database now holds only the roles and the configured Admin."
            : "The demonstration dataset is being rebuilt in the background and will be complete shortly.";

        return Ok(ApiResponse.Ok(
            $"Database reset. Schema recreated, and the roles and configured Admin reseeded. {demoNote} " +
            "Log in again — previous tokens are no longer valid."));
    }

    /// <summary>
    /// Whether this deployment carries demonstration data, and how much of it is there. Answers
    /// the question the reset endpoint leaves open: the rebuild finishes after the response, so
    /// something has to be able to say when.
    /// </summary>
    [HttpGet("demo-data")]
    public async Task<IActionResult> DemoDataStatus(CancellationToken cancellationToken)
    {
        return Ok(ApiResponse<object>.Ok(new
        {
            enabled = DemoDataSeeder.IsEnabled(configuration, environment),
            accounts = await db.Users.CountAsync(cancellationToken),
            publications = await db.PublicationContainers.CountAsync(cancellationToken),
            published = await db.Publications.CountAsync(p => p.IsPublished, cancellationToken)
        }));
    }
}
