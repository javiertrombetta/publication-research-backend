# Publication Research Backend

Backend API for the AIS **Research Publication Site** — a system supporting the full lifecycle of a student's
research paper: proposal submission, supervisor assignment, ethics approval, review, committee evaluation, and
optional publication to a public catalogue.

This is a Web API only (JSON). It is consumed by a separate frontend application.

## Tech stack

- **.NET 10** / ASP.NET Core Web API
- **Entity Framework Core 9** + **Pomelo.EntityFrameworkCore.MySql** (MySQL 8.0)
- **ASP.NET Core Identity** for user/role management, **JWT Bearer** for API auth (+ optional Microsoft Entra ID
  SSO exchange)
- **Serilog** (console + rolling file)
- **FluentValidation**
- **Swashbuckle** (Swagger / OpenAPI)
- **MailKit** for outgoing email (verification, password reset, workflow notifications)

## Project layout

```text
src/PublicationSite.Api/
  Entities/            Domain model (EF Core entities)
  Enums/                Status/type enums
  Data/                 ApplicationDbContext, EF Core Fluent API configurations, migrations, DbSeeder
  DTOs/                 Request/response models + FluentValidation validators, grouped by feature
  Services/
    Interfaces/         Service contracts
    Implementations/    Business logic (one service per feature area)
  Controllers/          Thin controllers; auth via [Authorize(Roles = ...)]
  Common/                RoleNames, ApiResponse envelope, exceptions, middleware, strongly-typed options
```

Architecture is intentionally a single project organised by folder ("layered simple"), not a multi-project
Clean Architecture split — appropriate for a 3-person team and this project's size.

**Database schema:** see [docs/erd.md](docs/erd.md) for the full entity–relationship diagram (renders
natively on GitHub) and a table-by-table data dictionary for all 38 tables.

## Getting started

### 1. Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (for local MySQL) — or your own MySQL 8.x instance

### 2. Start MySQL

```bash
docker compose up -d
```

This starts MySQL 8.0 on `localhost:3306` (database `publication_site_dev`, root password `devpassword` —
**local dev only**, never used in any deployed environment).

### 3. Configure secrets

`appsettings.Development.json` already points at the local Docker MySQL instance and ships a **dev-only** JWT
signing key so the project runs out of the box. For any shared/deployed environment, override at minimum:

- `ConnectionStrings:Default`
- `Jwt:SigningKey` — generate your own: `openssl rand -base64 64`
- `Mail:*` — real SMTP credentials (until then, emails fail silently and are logged — the workflow itself is
  unaffected since every notification is also stored in-app)
- `AzureAd:TenantId` / `AzureAd:ClientId` — only needed once Microsoft Entra SSO is registered; the app runs
  fine without it, the SSO exchange endpoint simply stays unavailable until configured

Prefer [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) or environment
variables over committing real values to any `appsettings.*.json`.

### 4. Apply database migrations

```bash
cd src/PublicationSite.Api
dotnet tool install --global dotnet-ef   # first time only
dotnet ef database update
```

This creates the schema and seeds the eight fixed roles (Admin, HeadOfDepartment, Coordinator, Supervisor,
InternalCommitteeMember, ExternalCommitteeMember, Student, Staff) on first run via `DbSeeder`.

### 5. Run the API

```bash
dotnet run
```

- Swagger UI: `http://localhost:5289/swagger` (Development only)
- Health check: `GET /health`

### 6. Seeding accounts

`DbSeeder`/`DevelopmentDataSeeder` (in `Data/`) run automatically on every startup, both idempotent:

**Production Admin** (safe in every environment — it's a no-op unless configured): set these two before the
first deploy, then remove them:

```bash
export Seed__AdminEmail="you@ais.ac.nz"
export Seed__AdminPassword="SomeStrongPassword123!"
```

It only ever creates the account once (never touches an existing one), and logs a warning confirming it did.

**Local test users** (one enabled account per role, **Development environment only** — this is hard-guarded in
code, not just by config, since every account below shares the same password): created automatically the first
time you `dotnet run` in Development, no setup needed.

| Role | Email | Password |
| --- | --- | --- |
| Admin | `admin.test@ais.ac.nz` | `DevTest123!` |
| HeadOfDepartment | `hod.test@ais.ac.nz` | `DevTest123!` |
| Coordinator | `coordinator.test@ais.ac.nz` | `DevTest123!` |
| Supervisor | `supervisor.test@ais.ac.nz` | `DevTest123!` |
| InternalCommitteeMember | `internal.test@ais.ac.nz` | `DevTest123!` |
| ExternalCommitteeMember | `external.test@ais.ac.nz` | `DevTest123!` |
| Student | `student.test@aisstudent.ac.nz` | `DevTest123!` |
| Staff (no operational role yet) | `staff.test@ais.ac.nz` | `DevTest123!` |

Coordinator/Supervisor/HeadOfDepartment/Student all share one `Test Department` (code `TEST`), so the
Coordinator auto-assignment logic works out of the box when the test Student creates a Publication Container.

From there, use the Admin endpoints (`/api/users`, `/api/departments`) to create any additional real accounts.

### Resetting a shared deployment (frontend team use)

`POST /api/dev/reset-database` (Admin-only) wipes and recreates the whole schema, then reseeds roles, the
configured Admin, and the one-account-per-role test users above — useful while a frontend team is building
against a shared deployment and wants a clean slate. It's a no-op (403) unless `DevTools:EnableDatabaseReset`
is set, which is **only** appropriate on a deployment holding no real user data — see the warning in
[render.yaml](render.yaml). Logging in again is required afterwards; tokens issued before a reset stop working.

## Business rules encoded in the domain

- Email domain decides the auto-assigned role at registration: `@aisstudent.ac.nz` → Student, `@ais.ac.nz` →
  Staff (an Admin must then grant the actual operational role).
- New accounts start `Pending` and only become `Enabled` after email verification, regardless of provider
  (local or Azure SSO).
- The publication process is three sequential pipelines per `PublicationContainer`: Research Proposals → Ethics
  Approval → Research Paper. `PublicationContainer.CurrentPipeline` tracks progress; a paper cannot be
  submitted until the Ethics status is `Verified` or `NotRequired`.
- Every decision that changes a `PublicationContainer` requires a comment, stored in its `ActivityHistory`
  (visible to everyone with access to that container) — enforced at the service layer, not just validation.
- `AuditLogEntry` is a separate, append-only, system-wide trail; foreign keys to `Users` are `RESTRICT` so a
  user cannot be deleted out from under their own audit history.
- Access to a `PublicationContainer` (and everything under it — proposals, ethics docs, paper versions) is
  gated by `IContainerAccessService`, not just role membership: Admin, the owning Student, the assigned
  Coordinator/Supervisor, the Head of Department of the student's department, and assigned Committee members.

## Known scope decisions

- Admin "act on behalf of" is wired through the primary workflow endpoints (proposals, ethics, paper
  submission/publish) via the `OnBehalfOfUserId` audit trail, not duplicated as a fully parallel endpoint
  surface for every single Admin permission listed in the requirements — that would have meant doubling the
  controller surface for marginal value.
- File storage is local disk (`IFileStorageService` / `LocalFileStorageService`) behind an interface so it can
  be swapped for Azure Blob Storage later without touching callers.

## Deployment

Docker + GitHub Actions build and push an image to Docker Hub on every push to `main`; Render pulls
it from there. Full walkthrough, required environment variables, and known limitations (ephemeral
disk, no managed MySQL on Render): [docs/deployment.md](docs/deployment.md).

```bash
docker compose up -d          # mysql + the containerized API itself, for a local smoke test
```

### Postman

[docs/postman/](docs/postman/) has a ready-to-import collection (86 requests, one folder per
controller) generated from the live OpenAPI spec, plus an environment pointing at the deployed
Render instance. Import both, run **Auth > Login**, and the access/refresh tokens are saved into
collection variables automatically — every other request already inherits Bearer auth from them.

## Testing

```text
tests/PublicationSite.UnitTests/          xUnit + Moq + FluentAssertions + EF Core Sqlite in-memory
tests/PublicationSite.IntegrationTests/   xUnit + WebApplicationFactory + Testcontainers (real MySQL in Docker)
```

Run everything:

```bash
dotnet test
```

- **Unit tests** exercise the service layer directly against a fresh SQLite in-memory database per test
  (relational/FK-enforcing, unlike the EF Core InMemory provider), with `UserManager`/`SignInManager` mocked via
  Moq (they're built specifically to support this). No Docker required.
- **Integration tests** boot the real `Program.cs` — full DI, real middleware, real JWT auth — against a
  disposable MySQL container (Testcontainers), so they need **Docker running**. `FullPublicationJourneyTests`
  walks one student's paper through all three pipelines end-to-end over real HTTP.

Run only one project: `dotnet test tests/PublicationSite.UnitTests` or `.../PublicationSite.IntegrationTests`.

Writing the integration suite surfaced three real bugs that no amount of unit testing (with mocked
dependencies) would have caught, all now fixed:

1. JWT signing key mismatch — `JwtBearerOptions` read `JwtSettings` via a one-off `IConfiguration` snapshot at
   startup while `TokenService` read it via `IOptions<JwtSettings>`; under certain hosting scenarios these
   diverged, so tokens the app issued failed its own validation. Fixed by having `JwtBearerOptions` resolve the
   same `IOptions<JwtSettings>` instead (see `Program.cs`).
2. MySQL's `DATETIME` silently truncates to whole-second precision, which made EF Core's optimistic-concurrency
   check fail on nearly any `UPDATE` (the in-memory value had sub-second precision, the round-tripped one
   didn't). Fixed with a global `datetime(6)` convention in `ApplicationDbContext.ConfigureConventions`.
3. Newly created `Keyword` entities reached only through the `Publication.Keywords` navigation (never explicitly
   `Add()`-ed) were tracked as `Modified` instead of `Added`, because `Keyword.Id` already has a non-default
   value from its property initializer — EF Core's heuristic for entities reached only via fixup assumes a
   non-default key means "already exists". Fixed by explicitly calling `db.Keywords.Add(...)` for new keywords
   in `PublicationService.UpdateMetadataAsync`.
