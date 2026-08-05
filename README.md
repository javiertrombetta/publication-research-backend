# Publication Research Backend

Backend API for the AIS **Research Publication Site**, a system supporting the full lifecycle of a student's
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
- **MailKit** for outgoing email (verification, password reset, invitations, workflow notifications)

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
  Common/                RoleNames, SettingKeys, ApiResponse envelope, exceptions, middleware, options
```

**Database schema:** see [docs/erd.md](docs/erd.md) for the full entity–relationship diagram (renders
natively on GitHub) and a table-by-table data dictionary. All 43 tables are documented there, checked
against `SHOW TABLES` on a live database rather than against the last time somebody remembered to look.

## Getting started

### 1. Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-started/get-docker/) (for local MySQL), or your own MySQL 8.x instance

### 2. Start MySQL

```bash
docker compose up -d
```

This starts MySQL 8.0 on `localhost:3306` (database `publication_site_dev`, root password `devpassword`,
**local dev only**, never used in any deployed environment).

### 3. Configure secrets

`appsettings.Development.json` already points at the local Docker MySQL instance and ships a **dev-only** JWT
signing key so the project runs out of the box. For any shared/deployed environment, override at minimum:

- `ConnectionStrings:Default`
- `Jwt:SigningKey`: generate your own: `openssl rand -base64 64`
- `Mail:*`: **not normally needed.** The mail server is an administrator setting, edited under System
  settings, and the stored value takes precedence over configuration. The seeded Admin arrives with its
  address already confirmed, so nothing has to be emailed before someone can sign in and set it up. These
  exist only as a fallback for a deployment whose first account arrives some other way.

  `Mail:Host` is deliberately blank by default, and a placeholder is worse than nothing: `SmtpEmailSender`
  reports "no mail server is configured" only while the host is empty, so a made-up host turns that clear
  message into a connection timeout and a stack trace.
- `AzureAd:TenantId` / `AzureAd:ClientId`: only needed once Microsoft Entra SSO is registered; the app runs
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
Reviewer, ExternalCommitteeMember, Student, Staff) on first run via `DbSeeder`.

### 5. Run the API

```bash
dotnet run
```

- Swagger UI: `http://localhost:5020/swagger` (always in Development; elsewhere opt-in via `Swagger:Enabled`)
- Health check: `GET /health`

### The API reference

Swagger is generated from the controllers, so every endpoint appears without anyone maintaining a
list. Two things make it worth reading, and both had to be switched on deliberately:

- **Response shapes.** Every action returns `IActionResult`, which tells Swashbuckle nothing about
  what comes back, so the reference described 124 endpoints and not one of their responses.
  `[ProducesResponseType]` on each action names the `ApiResponse<T>` it actually returns.
- **The prose already in the source.** `GenerateDocumentationFile` emits the XML that
  `IncludeXmlComments` feeds to Swagger; without it every `<summary>` written against an endpoint
  or a DTO stayed in the source and reached nobody.

Coverage is complete, and it is worth stating as numbers, because partial coverage is the state a
reference settles into: the endpoints somebody happened to explain are described, the rest are a
method name and a bare `Success`, and a reader cannot tell which they are looking at until they
have read it.

- **18 of 18 groups** carry a description, on the controller class.
- **165 of 165 operations** carry a summary.
- **671 responses** are declared, and none of them is left showing only its reason phrase.

Read off `/swagger/v1.1/swagger.json` rather than counted by hand, so the figures can be checked
against the running API instead of being believed.

The error responses are not guesswork. `ExceptionHandlingMiddleware` maps five exception types onto
five statuses, so the question "which can this endpoint answer with?" has an answer in the code:
whichever of them is reachable from the action. They were derived by walking the call graph across
the service implementations and taking the transitive closure, then checked against the running API.
401 without a token, 403 for the wrong role, 404 for an unknown id, 400 for a body that fails
validation, 422 for a step the workflow does not allow at that point.

Two things that check turned up. `POST /api/containers`, `/api/departments` and `/api/users` answer
**201** with a `Location` header and were declaring 200. And a `NotFoundException` is only
documented where the caller supplies the identifier, in the route or the query or the body. Reached
any other way it is an internal read-back failing on a record just written, or on the caller's own
account: a bug, not a contract, and documenting it tells a client to handle something it cannot
provoke.

One inconsistency is documented rather than fixed, because fixing it would break callers. A 400 has
two shapes: FluentValidation's automatic validation returns `ValidationProblemDetails`, with the
failure per field, while a `ValidationAppException` returns the usual envelope. The frontend's
`ApiClientBase` already branches on both, and that is what per-field errors on a form depend on, so
the declared type per endpoint is whichever one that endpoint actually produces.

`/health` has no class and no attributes to carry any of this, being a minimal endpoint. Its
summary is `.WithSummary(...)`; its group description and its 200 are filled in by
`Common/Swagger/HealthTagDescriptionFilter.cs`.

The version is `Common/ApiVersion.cs`, currently **v1.1**, and the document is served under it at
`/swagger/v1.1/swagger.json`. It is deliberately not in the route. Every path stays `api/…`, which
is what the frontend, the Postman collection and the team's saved requests are written against.
Raise the minor part when endpoints are added or described, the major part when something already
published changes shape. The Swagger UI is pointed at the endpoint explicitly, since its default
only ever looks for `/swagger/v1/swagger.json`.

A note for anyone documenting a positional record: the `///` block goes **above the record**, as
`<param name="...">`. Written inside the parameter list it looks right, compiles, and is silently
discarded. The compiler says so as CS1587, which is invisible until the documentation file is on.

There is also a [Postman collection](docs/postman) covering every endpoint, with the request bodies
filled in and a note on each about why it behaves the way it does.

### 6. Seeding

Three deployments exist and each wants a different amount of data in it. `Data/DbSeeder.cs` covers what
every one of them needs; `Data/DemoDataSeeder.cs` covers the sample dataset, and only runs where it is
asked for.

| Deployment | `ASPNETCORE_ENVIRONMENT` | `Seed:DemoData` | What ends up in the database |
| --- | --- | --- | --- |
| A developer's machine | `Development` | unset (defaults on) | Roles, and the full demonstration dataset |
| The shared instance the team tests against | `Production` | `true` | Roles, the configured Admin, and the full demonstration dataset |
| Production | `Production` | unset | Roles and the configured Admin. Nothing else |

The default is closed outside development, so forgetting the setting costs a deployment its sample data
rather than publishing a known password on a live site.

**The production Admin** (a no-op unless configured, so it is safe everywhere): set these two before the
first deploy, then remove them.

```bash
export Seed__AdminEmail="you@ais.ac.nz"
export Seed__AdminPassword="SomeStrongPassword123!"
```

It creates the account once and never touches an existing one, logging a warning to confirm. That account
then configures everything else and invites everyone else.

Because it is checked on every startup rather than only the first, this is also the way back in if a
deployment ends up with . Set the two values, deploy, sign in, then clear them. And it says so
itself: a deployment that comes up with no enabled Admin account and no demonstration data logs an error at
startup naming that remedy, rather than leaving the first person to find out at a sign-in page that no
credentials open.

#### The demonstration dataset

Twenty-two accounts across two departments, and thirty publications parked at every point in the three
pipelines where somebody has to act, so each role signs in to find work of its own waiting, at every
decision the system asks anyone to make, without having to walk a publication through ten prior steps to
reach the eleventh.

Each publication carries its own proposals and its own words: what the coordinator wrote when sending it
out, what each supervisor replied, what the ethics readers said about that particular study, and how each
committee member voted. None of those sentences is shared with another publication. That matters for
judging the system rather than only demonstrating it: a screen full of one repeated sentence cannot show
whether a search works, and a column of identical dates sorts the same both ways, so the control that
orders by it looks broken. Each publication is also dated back by its own amount, so the set spans two
academic years, some queues have genuinely been waiting longer than others, and one ethics review is far
enough past the institution's window to be overdue.

Every account uses the password `DevTest123!`.

Password hashing is deliberately cheaper on a deployment that seeds this data. Identity's default
of 100,000 PBKDF2 iterations is what makes a stolen hash impractical to crack; it also costs about
two seconds of sign-in on the fraction of a CPU a free hosting tier gives you. There is no secret
here for it to protect, the password being on this page, so it drops to 10,000 wherever
`Seed:DemoData` is on, and stays at the full strength everywhere else. The count lives inside each
hash, so it applies to accounts created afterwards; existing ones need recreating for it to take
effect.

| Role | Accounts |
| --- | --- |
| Admin | `admin.test@ais.ac.nz` |
| Head of Department | `hod.test@ais.ac.nz` (Information Technology), `hod.business@ais.ac.nz` (Business) |
| Coordinator | `coordinator.test@ais.ac.nz` (Information Technology), `coordinator.business@ais.ac.nz` (Business) |
| Supervisor | `supervisor.test@ais.ac.nz`, `supervisor.second@ais.ac.nz`, `supervisor.business@ais.ac.nz`, `supervisor.business.second@ais.ac.nz` |
| Reviewer | `reviewer.test@ais.ac.nz`, `reviewer.second@ais.ac.nz`, `reviewer.third@ais.ac.nz` |
| External committee member | `external.test@ais.ac.nz`, `external.second@ais.ac.nz` |
| Student | `student.test@aisstudent.ac.nz`, `student.second@…`, `student.third@…`, `student.fourth@…`, `student.fifth@…`, `student.business@…`, `student.business.second@…` |
| Staff (no operational role yet) | `staff.test@ais.ac.nz` |

Three Reviewers and two externals, against a default composition of two Reviewers and one external. Two of
each would satisfy the rule and then produce the same three names on every committee; the extra Reviewer is
what lets committees differ from one another. Two departments, so a Head of Department can be seen to have
sight of their own students and not everyone's, and enough work in each that both have a queue rather than
an example. Two Supervisors per department, so a Coordinator allocating a proposal has a real choice rather
than a single option to rubber-stamp, and both of them supervise something: one of them has also stopped
taking new work while keeping what she already had, which is a state nothing else in the data shows.

`student.test@aisstudent.ac.nz` carries the stages a student acts on, from an empty publication through to
one in the public catalogue, so one account demonstrates the whole route. The other students carry the
stages where somebody else acts, which is what puts a queue in front of every other role.

The dataset is built by calling the same service methods the API calls, rather than by writing rows. What
state a publication is in is spread across its container, proposals, ethics approval, paper, versions,
committee, activity history and notifications, and all of those have to agree; going through the services
means the sample data can only ever be in a state the application itself can produce. It is written in one
transaction, so an interrupted run leaves nothing behind and the next start rebuilds it.

It runs in the background once the server is listening, not before. Against a hosted database it takes long
enough that doing it inline would hold the health check open past the point where a platform gives up on
the deploy. `GET /api/dev/demo-data` (Admin) reports whether it has finished.

### Resetting a shared deployment (frontend team use)

`POST /api/dev/reset-database` (Admin-only) wipes and recreates the whole schema, then reseeds the roles,
the configured Admin and, where the deployment asks for it, the demonstration dataset. It returns as soon
as signing in is possible again and finishes the dataset in the background.

It is a no-op (403) unless **both** `DevTools:EnableDatabaseReset` and `Seed:DemoData` are set. The second
is not redundant: this deletes everything and asks nothing first, and whether a database is disposable is
exactly what seeding demonstration data into it declares. A deployment that seeds none is holding either
real work or nothing worth wiping, so no administrator's token can erase it even if the reset switch was
left on by mistake. Logging in again is required afterwards; tokens issued before a reset stop working.

## Runtime settings

Most of what an institution would want to change is data, not configuration. `SystemSettings` is a key/value
table read through `ISystemSettingsProvider` (cached in memory, invalidated on write) and edited through
`/api/settings`, grouped so each group can be validated as a whole. `Common/SettingKeys.cs` is the canonical
list. Nothing outside the provider spells a key as a literal, because a typo in a reader silently yields the
default rather than failing.

| Group | Covers |
| --- | --- |
| `committees` | Required internal/external members and minimum approvals |
| `ethics-documents` | Which documents the ethics stage asks students for |
| `deadlines` | Expected days for supervisor response, ethics review and committee review, and how far ahead of each a reminder goes out |
| `uploads` | Maximum file size and permitted extensions |
| `passwords` | Length, character classes, expiry, lockout threshold and duration |
| `access` | Registration mode, single sign-on, invitation validity, token lifetimes |
| `notifications` | SMTP server and the master email switch |
| `institution` | Name, email domains, contact addresses, privacy policy, rows per page |

Three of these needed the code that reads them to change, because ASP.NET Core binds the equivalent options
once at startup and so cannot follow a value edited at runtime:

- **Password rules**: Identity's built-in validator reads `IdentityOptions`. Those are set to the loosest
  configuration the system will ever allow, and `ConfigurablePasswordValidator` (which replaces the built-in
  one) is the authority, reading the rules fresh on every check.
- **Lockout**: same problem, so Identity's lockout is switched off and `IAccountLockoutService` owns it.
  That also lets it cover changing a password, not just signing in: someone at a borrowed unlocked laptop
  attacks the change-password form, where the sign-in page's protection would never be consulted.
- **Token lifetimes**: read per issue rather than from the startup snapshot. The issuer, audience and signing
  key deliberately stay in configuration: changing one from a web form would invalidate every token in
  circulation, including the caller's own.

### Settings that must not apply retroactively

Two settings describe what is asked of a piece of research, so changing them cannot be allowed to change the
rules for work already under way:

- **Committee composition** is copied onto a `PublicationContainer` when it is created, and committee
  assignment validates against that snapshot rather than against the current setting.
- **Ethics documents** are copied onto an `EthicsApproval` when documentation is first requested, via
  `EthicsApprovalRequirements`. Without it, adding a fourth required form would silently reopen every ethics
  stage already completed under a list of three.

Containers and approvals that predate the snapshots have none, and fall back to whatever is configured now,
which is the only figure anyone ever agreed for them.

## How people get accounts

`access.registration-mode` is either `Open` or `InviteOnly`. Its default is not a constant: it comes from the
hosting environment, so an unconfigured system is open in Development and invite-only anywhere else. Open
registration is **refused** outside a development environment rather than merely discouraged. It would hand
out accounts to anyone who guessed the email domain.

In production, staff and students are expected to arrive through Microsoft Entra ID. The token plumbing is
already in place and activates when `AzureAd:TenantId` is configured; `access.azure-sso-enabled` records
whether the institution intends to use it, and the API reports separately whether a tenant actually exists so
the interface can say that switching it on would currently do nothing.

External committee members are outside the institution by definition. They have no institutional address, so
no email domain could say what they are, and they are always invited and always sign in with a password.

**Invitations** (`/api/invitations`) let an administrator invite any address to any role, choosing the role as
they send it. The role comes from the invitation and never from the acceptance request. Otherwise accepting
one would be a way to award yourself whatever role you liked. Only a SHA-256 hash of the token is stored, so
the token exists solely in the email that was sent and a leaked database cannot be used to accept anyone's
invitation. Accepting, re-sending or withdrawing all invalidate the current token.

## Business rules encoded in the domain

- Email domain decides the auto-assigned role at registration: the student domain → Student, the staff domain →
  Staff (an Admin must then grant the actual operational role). Both domains are settings, and the API refuses
  to let them , because the same address cannot mean both.
- New accounts start `Pending` and only become `Enabled` after email verification, regardless of provider
  (local or Azure SSO). Accounts created by accepting an invitation are enabled immediately: the invitation
  reached that address and was answered, which is the same proof a verification email would give.
- The publication process is three sequential pipelines per `PublicationContainer`: Research Proposals → Ethics
  Approval → Research Paper. `PublicationContainer.CurrentPipeline` tracks progress; a paper cannot be
  submitted until the Ethics status is `Verified` or `NotRequired`.
- Every decision that changes a `PublicationContainer` requires a comment, stored in its `ActivityHistory`
  (visible to everyone with access to that container), enforced at the service layer, not just validation.
- `AuditLogEntry` is a separate, append-only, system-wide trail; foreign keys to `Users` are `RESTRICT` so a
  user cannot be deleted out from under their own audit history.
- Access to a `PublicationContainer` (and everything under it, proposals and ethics docs and paper versions) is
  gated by `IContainerAccessService`, not just role membership: Admin, the owning Student, the assigned
  Coordinator/Supervisor, the Head of Department of the student's department, and assigned Committee members.
- **Deleting an account is anonymise-and-lock, not a row delete.** Every foreign key pointing at a user is
  `RESTRICT`, so a real delete would either be refused or would have to detach published research from its
  author. `DELETE /api/users/{id}` requires a reason, writes the audit entry *before* stripping anything
  (recording the former address), then replaces the identifying fields, disables the account and locks it out
  permanently. The person can no longer sign in and is no longer identifiable; what they did stays
  attributable.
- **Deadlines mark work as overdue; they never block it.** A deadline that stopped a supervisor responding
  late would only strand the student waiting on them.

## Known scope decisions

- Admin "act on behalf of" is wired through the primary workflow endpoints (proposals, ethics, paper
  submission/publish) via the `OnBehalfOfUserId` audit trail, not duplicated as a fully parallel endpoint
  surface for every single Admin permission listed in the requirements. That would have meant doubling the
  controller surface for marginal value.
- File storage is local disk (`IFileStorageService` / `LocalFileStorageService`) behind an interface so it can
  be swapped for Azure Blob Storage later without touching callers.
- Deadlines are stored and validated, and the API exposes them, but nothing yet acts on them: reminders and
  escalation need a background scheduler that does not exist here.
- Publication categories have a table and no endpoints. Nothing consumes them.

## Deployment

Docker + GitHub Actions build and push an image to Docker Hub on every push to `main`; Render pulls
it from there. Full walkthrough, required environment variables, and known limitations (ephemeral
disk, no managed MySQL on Render): [docs/deployment.md](docs/deployment.md).

```bash
docker compose up -d          # mysql + the containerised API itself, for a local smoke test
```

### Postman

[docs/postman/](docs/postman/) has a ready-to-import collection (one folder per controller) generated from the
live OpenAPI spec, plus an environment pointing at the deployed Render instance. Import both, run
**Auth > Login**, and the access/refresh tokens are saved into collection variables automatically, and every
other request already inherits Bearer auth from them. It covers all 165 endpoints and is checked against
the live spec; if you add one, add it here too or regenerate from `/swagger/v1.1/swagger.json`.

## Testing

```text
tests/PublicationSite.UnitTests/          xUnit + Moq + FluentAssertions + EF Core Sqlite in-memory
tests/PublicationSite.IntegrationTests/   xUnit + WebApplicationFactory + Testcontainers (real MySQL in Docker)
```

Run everything:

```bash
dotnet test
```

226 tests: 219 unit, 7 integration.

- **Unit tests** exercise the service layer directly against a fresh SQLite in-memory database per test
  (relational/FK-enforcing, unlike the EF Core InMemory provider), with `UserManager`/`SignInManager` mocked via
  Moq (they're built specifically to support this). No Docker required.
- **Integration tests** boot the real `Program.cs` (full DI, real middleware, real JWT auth) against a
  disposable MySQL container (Testcontainers), so they need **Docker running**. `FullPublicationJourneyTests`
  walks one student's paper through all three pipelines end-to-end over real HTTP.

Run only one project: `dotnet test tests/PublicationSite.UnitTests` or `.../PublicationSite.IntegrationTests`.

Writing the integration suite surfaced three real bugs that no amount of unit testing (with mocked
dependencies) would have caught, all now fixed:

1. JWT signing key mismatch. `JwtBearerOptions` read `JwtSettings` via a one-off `IConfiguration` snapshot at
   startup while `TokenService` read it via `IOptions<JwtSettings>`; under certain hosting scenarios these
   diverged, so tokens the app issued failed its own validation. Fixed by having `JwtBearerOptions` resolve the
   same `IOptions<JwtSettings>` instead (see `Program.cs`).
2. MySQL's `DATETIME` silently truncates to whole-second precision, which made EF Core's optimistic-concurrency
   check fail on nearly any `UPDATE` (the in-memory value had sub-second precision, the round-tripped one
   didn't). Fixed with a global `datetime(6)` convention in `ApplicationDbContext.ConfigureConventions`.
3. Newly created `Keyword` entities reached only through the `Publication.Keywords` navigation (never explicitly
   `Add()`-ed) were tracked as `Modified` instead of `Added`, because `Keyword.Id` already has a non-default
   value from its property initialiser. EF Core's heuristic for entities reached only via fixup assumes a
   non-default key means "already exists". Fixed by explicitly calling `db.Keywords.Add(...)` for new keywords
   in `PublicationService.UpdateMetadataAsync`.
