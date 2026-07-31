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
using PublicationSite.Api.Common.Middleware;
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

// Replaces Identity's built-in validator rather than joining it — see the class for why.
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

// ---------- Reverse proxy (Render terminates TLS in front of the container) ----------
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The platform's edge is the only path into the container, and its proxy IP isn't
    // fixed/known in advance, so trust the forwarded headers regardless of source —
    // the standard pattern for PaaS deployments (Render, Heroku, Azure App Service, ...).
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
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AIS Research Publication Site API",
        Version = "v1",
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
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Applies any pending EF Core migrations on every startup (idempotent — only unapplied
    // ones run). Keeps deploys self-contained: no separate "run migrations" step needed
    // against a remote host with no shell access, e.g. on Render.
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    await DbSeeder.SeedRolesAsync(scope.ServiceProvider);
    await DbSeeder.SeedAdminAsync(scope.ServiceProvider, app.Configuration);

    if (app.Environment.IsDevelopment())
    {
        await DevelopmentDataSeeder.SeedTestUsersAsync(scope.ServiceProvider, app.Environment);
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
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestampUtc = DateTime.UtcNow }))
    .AllowAnonymous();

app.Run();

public partial class Program;
