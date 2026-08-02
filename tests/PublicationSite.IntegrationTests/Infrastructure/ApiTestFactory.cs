using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PublicationSite.Api.Data;
using Testcontainers.MySql;
using Xunit;

namespace PublicationSite.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API host (Program.cs, full DI, real middleware pipeline) against a disposable
/// MySQL instance started via Testcontainers, so integration tests exercise the exact same wiring
/// as production instead of an in-memory substitute.
///
/// Overrides are applied via process environment variables rather than
/// WebApplicationFactory.ConfigureWebHost/ConfigureAppConfiguration: for a minimal-hosting
/// Program.cs (top-level `WebApplication.CreateBuilder`), that hook does not reliably win over
/// appsettings.*.json before Program.cs's own `builder.Configuration` reads run. Confirmed by this
/// project connecting to the local dev database instead of the Testcontainers instance until this
/// env-var approach was used. Environment variables are read by `WebApplication.CreateBuilder`
/// itself, at the point Program.cs actually executes, so they always win.
/// </summary>
public class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder()
        .WithImage("mysql:8.0.35")
        .WithDatabase("publication_site_test")
        .WithUsername("root")
        .WithPassword("test-password")
        .Build();

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();

        var connectionString = _mysql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connectionString);
        Environment.SetEnvironmentVariable("Jwt__Issuer", "IntegrationTests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "IntegrationTests.Client");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64)));
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "60");
        Environment.SetEnvironmentVariable("Frontend__BaseUrl", "http://localhost:3000");
        Environment.SetEnvironmentVariable("Mail__Host", "smtp.invalid"); // deliberately unreachable; SmtpEmailSender fails closed and logs, never throws
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:3000");


        // Migrate via a standalone DbContext BEFORE the host is built. Program.cs seeds roles as
        // part of its own startup (before app.Run()), and accessing `Services` below is what
        // triggers that startup, so the schema must already exist by then, not after.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 35)))
            .Options;
        await using (var migrationContext = new ApplicationDbContext(options))
        {
            await migrationContext.Database.MigrateAsync();
        }

        // Force the host to build now (WebApplicationFactory builds lazily on first use).
        _ = Services;

        // Then wait for the demonstration dataset before letting a test run.
        //
        // The host builds it in the background, which is right for a real deployment and wrong
        // here: these tests change system settings, and a build still under way against the old
        // ones assigns a committee that no longer matches the rules and gives up half finished.
        // The failure was only ever a line in the log, so it looked like flakiness in whichever
        // test happened to read that data next.
        //
        // Leaving the dataset switched on rather than skipping it: what decides whether anybody may
        // register is whether this deployment's data is disposable, and seeding the sample data is
        // what marks it so. Turned off, registration becomes invite-only and the auth tests have
        // nothing to register with.
        await Services.GetRequiredService<DemoDataSeedRunner>().Current;
    }

    public new async Task DisposeAsync()
    {
        await _mysql.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiTestFactory>
{
    public const string Name = "Api";
}
