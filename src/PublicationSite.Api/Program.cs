using System.Reflection;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Middleware;
using PublicationSite.Api.Common.Swagger;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Most PaaS targets (Render included) assign the listen port at runtime via PORT rather
// than a fixed value baked into config. Only override Kestrel's URLs when it's actually
// set, so local dev keeps using launchSettings.json / appsettings as before.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// ---------- Serilog ----------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ---------- Options ----------
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection(MailSettings.SectionName));
builder.Services.Configure<FileStorageSettings>(builder.Configuration.GetSection(FileStorageSettings.SectionName));
builder.Services.Configure<FrontendSettings>(builder.Configuration.GetSection(FrontendSettings.SectionName));

// ---------- Database ----------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 35)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(3)));

// ---------- Identity ----------
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        // Deliberately wide open here. IdentityOptions are bound once at start-up, so they
        // cannot follow a value an administrator changes at runtime; ConfigurablePasswordValidator
        // below is the authority on password rules and reads them fresh on every check. Leaving
        // real limits here as well would only produce a second, contradictory error message.
        options.Password.RequiredLength = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
        // Identity's lockout is switched off here for the same reason as its password rules:
        // these options are bound once at start-up. IAccountLockoutService owns lockout instead,
        // reading the administrator's threshold on every attempt and covering the
        // change-password path as well as sign-in.
        options.Lockout.AllowedForNewUsers = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ---------- Password hashing cost ---------- Identity hashes with PBKDF2 at 100,000 iterations,
// which is deliberately expensive: it is what makes a stolen hash impractical to crack. On a full-
// sized machine it costs about 60ms. On the fraction of a CPU a free hosting tier provides it costs
// closer to two seconds, and that is the whole of what people experience as a slow sign-in.
//
// Lowered only where the demonstration dataset lives. That flag is a deployment stating that its
// data is disposable and that every one of its accounts shares a password published in the README.
// There is no secret there for 100,000 iterations to protect, and the cost buys nothing but a wait
// for a team trying to test. Production sets no flag and keeps the full strength, which is the
// direction the mistake has to fall.
//
// This only governs hashes made from here on. Identity stores the iteration count inside each hash
// and verifies with the count it finds there, and it does not re-hash on a successful sign-in, so
// accounts created before this change stay as slow as they were. The demonstration accounts have to
// be recreated for it to take effect: POST /api/dev/reset-database.
if (DemoDataSeeder.IsEnabled(builder.Configuration, builder.Environment))
{
    builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 10_000);
}

// Replaces Identity's built-in validator rather than joining it. See the class for why.
builder.Services.RemoveAll<IPasswordValidator<ApplicationUser>>();
builder.Services.AddScoped<IPasswordValidator<ApplicationUser>, ConfigurablePasswordValidator>();
builder.Services.AddScoped<IAccountLockoutService, AccountLockoutService>();

// ---------- Authentication (JWT) ----------
var authenticationBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

// Configured via the options pattern (rather than a one-off snapshot read of IConfiguration)
// so JwtBearerOptions always resolves the exact same JwtSettings that ITokenService signs
// with, however/whenever the DI container ends up building each of them.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
    {
        var jwtSettings = jwtOptions.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtSettings.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// Second scheme, "AzureAd": validates tokens issued by Microsoft Entra ID for this API's
// App Registration. The frontend acquires that token via MSAL and calls
// POST /api/auth/azure-sso/exchange, which swaps it for our own app JWT. Only registered
// when a tenant is actually configured, so local dev works without real Azure credentials.
var azureAdTenantId = builder.Configuration["AzureAd:TenantId"];
if (!string.IsNullOrWhiteSpace(azureAdTenantId))
{
    authenticationBuilder.AddMicrosoftIdentityWebApi(
        builder.Configuration,
        configSectionName: "AzureAd",
        jwtBearerScheme: "AzureAd");
}

builder.Services.AddAuthorization();

// ---------- HTTP context / current user ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IContainerAccessService, ContainerAccessService>();
builder.Services.AddScoped<IContainerService, ContainerService>();
builder.Services.AddScoped<IProposalService, ProposalService>();
builder.Services.AddScoped<ISupervisorGroupService, SupervisorGroupService>();

// Closes proposal rounds whose answer-by date has passed. Without it that date is a note in the
// database that nothing ever reads.
builder.Services.AddHostedService<ExpiredProposalRoundService>();
builder.Services.AddScoped<IEthicsService, EthicsService>();
builder.Services.AddScoped<IPublicationService, PublicationService>();
builder.Services.AddScoped<ICommitteeService, CommitteeService>();
builder.Services.AddScoped<ICatalogueService, CatalogueService>();
builder.Services.AddScoped<INotificationQueryService, NotificationQueryService>();
builder.Services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
// Settings are read on paths as hot as signing in, so they are cached in memory rather than
// queried per use; the provider drops the cache whenever the service writes.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ISystemSettingsProvider, SystemSettingsProvider>();
builder.Services.AddScoped<ISystemSettingService, SystemSettingService>();
builder.Services.AddScoped<IEthicsDocumentRequirementService, EthicsDocumentRequirementService>();
builder.Services.AddScoped<IUserProfileFactory, UserProfileFactory>();
builder.Services.AddScoped<IInvitationService, InvitationService>();

// ---------- Demonstration dataset (development and the shared testing deployment only) ----------
if (DemoDataSeeder.IsEnabled(builder.Configuration, builder.Environment))
{
    builder.Services.AddSingleton<DemoDataSeedRunner>();
    builder.Services.AddHostedService(services => services.GetRequiredService<DemoDataSeedRunner>());
}

// ---------- Reverse proxy (Render terminates TLS in front of the container) ----------
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The platform's edge is the only path into the container, and its proxy IP isn't fixed/known
    // in advance, so trust the forwarded headers , the standard pattern for PaaS deployments
    // (Render, Heroku, Azure App Service, ...).
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---------- CORS ----------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ---------- Validation ----------
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

// ---------- Controllers ----------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Safety net: DTOs should never carry EF navigation cycles, but this prevents a
        // 500 (rather than silently misleading JSON) if one slips through in the future.
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// ---------- Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(ApiVersion.Current, new OpenApiInfo
    {
        Title = "AIS Research Publication Site API",
        Version = ApiVersion.Current,
        Description = "Backend API for the AIS Research Publication Site."
    });

    var jwtScheme = new OpenApiSecurityScheme
    {
        Scheme = "bearer",
        BearerFormat = "JWT",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Description = "Enter the JWT access token."
    };
    options.AddSecurityDefinition("Bearer", jwtScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, [] }
    });

    // The summaries written against each action and DTO, which is where the reasoning behind an
    // endpoint already lives: why a decision needs a comment, which figures a committee is judged
    // by, what a status actually covers. Swagger showed none of it until the documentation file was
    // switched on in the csproj alongside this.
    var documentation = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(documentation))
    {
        options.IncludeXmlComments(documentation, includeControllerXmlComments: true);
    }

    // Every other group in the reference is a controller, and takes its description from the
    // controller's own summary. /health is a minimal endpoint with no class to hang one on, so
    // its group would be the only heading in the document with nothing under it but a route.
    options.DocumentFilter<HealthTagDescriptionFilter>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Applies any pending EF Core migrations on every startup (idempotent: only unapplied ones
    // run). Keeps deploys self-contained: no separate "run migrations" step needed against a remote
    // host with no shell access, e.g. on Render.
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    await DbSeeder.SeedRolesAsync(scope.ServiceProvider);
    await DbSeeder.SeedAdminAsync(scope.ServiceProvider, app.Configuration);

    // The demonstration dataset is not seeded here. It is slow enough against a hosted database to
    // hold up the health check, so DemoDataSeedRunner starts it once the server is listening, and
    // it is registered only where it is wanted (see DemoDataSeeder.IsEnabled).
    //
    // Which is also why this check is skipped where that dataset is coming: it brings an Admin with
    // it, and complaining before it has finished would be an alarm about nothing.
    if (!DemoDataSeeder.IsEnabled(app.Configuration, app.Environment))
    {
        await DbSeeder.WarnIfNoAdministratorAsync(scope.ServiceProvider);
    }
}

// ---------- Middleware pipeline ----------
app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

// Swagger is always on in Development; elsewhere it's opt-in via Swagger:Enabled
// (e.g. to explore a freshly deployed environment) rather than exposed by default.
var swaggerEnabled = app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false);
if (swaggerEnabled)
{
    app.UseSwagger();

    // The document is named after the version, so the spec moves with it. Naming the endpoint
    // explicitly matters: the default UI only ever looks for /swagger/v1/swagger.json, and would
    // show an empty page the moment the version stopped being "v1".
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint($"/swagger/{ApiVersion.Current}/swagger.json",
            $"AIS Research Publication Site API {ApiVersion.Current}"));
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
// A minimal endpoint rather than a controller action, so it carries its description as metadata
// instead of an XML comment. It answers as soon as the server is listening and touches nothing
// else. A health check that queried the database would report the API down whenever the database
// was merely slow, and the host would restart a process that was working.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestampUtc = DateTime.UtcNow }))
    .AllowAnonymous()
    .WithTags("Health")
    .WithSummary("Says that the API is up, for the host that decides whether to keep it running.");
// Its 200 is described in HealthTagDescriptionFilter, alongside the group: a minimal endpoint has
// no [ProducesResponseType] and no XML comment to carry either of them.

app.Run();

public partial class Program;
