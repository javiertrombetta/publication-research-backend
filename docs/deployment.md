# Deployment

How code gets from a push to `main` onto a running instance on Render.

```text
git push origin main
        │
        ▼
GitHub Actions (.github/workflows/ci-cd.yml)
  1. test          dotnet build + dotnet test (unit + integration, both projects)
  2. build-and-push  docker build → push to Docker Hub (main branch only, after tests pass)
        │
        ▼
Docker Hub: javiertrombetta/publication-research-backend:latest
        │
        ▼
Render pulls the image and runs it (render.yaml blueprint)
```

Render never builds anything from source — it only ever pulls the image GitHub Actions already
built and pushed. That split keeps the two concerns separate: GitHub owns "is this code good and
does it build", Render owns "keep a container of it running and reachable".

## Image tags

Every push to `main` produces `javiertrombetta/publication-research-backend:latest` plus a tag for
the short commit SHA (e.g. `:5708bec`) — this is what Render always runs.

To cut a versioned release, push a `v<major>.<minor>.<patch>` git tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

This additionally publishes `:1.0.0`, `:1.0`, and `:1` — useful for pinning a deployment to a
specific version instead of always tracking `latest`. Tagging a commit doesn't change what `latest`
points to; that always follows `main`.

## One-time setup

### 1. Push this repo to GitHub

The remote is already configured (`origin` → `github.com/javiertrombetta/publication-research-backend`).
Once you're happy with what's staged:

```bash
git add -A
git commit -m "Add Docker, CI/CD, and Render deployment"
git push origin main
```

### 2. Create a Docker Hub access token

Docker Hub → your avatar → **Account Settings → Personal access tokens → Generate new token**.
Give it **Read & Write** scope. Copy it immediately — Docker Hub won't show it again.

Do **not** use your Docker Hub password in CI. The token is scoped and revocable; your password
isn't.

### 3. Add the token to GitHub Actions secrets

Repo → **Settings → Secrets and variables → Actions → New repository secret**. Add two:

| Name | Value |
| --- | --- |
| `DOCKERHUB_USERNAME` | `javiertrombetta` |
| `DOCKERHUB_TOKEN` | the access token from step 2 |

Or via the `gh` CLI, from your own terminal (never paste the token into chat with an AI assistant
or anywhere else it might get logged):

```bash
gh secret set DOCKERHUB_USERNAME --body "javiertrombetta"
```

```bash
gh secret set DOCKERHUB_TOKEN
```

The second form prompts you to paste the token interactively instead of putting it on the command
line, where it could end up in your shell history.

### 4. Confirm the pipeline runs

After pushing, check **GitHub → Actions** tab. The `test` job runs on every push and PR; the
`build-and-push` job additionally runs (and only runs) on pushes to `main`, once `test` has
passed. First run takes a few minutes — Docker layer caching (`cache-from/to: type=gha`) makes
subsequent runs faster.

### 5. Get a MySQL instance Render can reach

**Render does not offer managed MySQL** (only Postgres, Key Value, and Disks). Pick one:

- **Managed MySQL from a third party** — PlanetScale, Aiven, or Railway all have a MySQL offering
  with a free/cheap tier and give you a connection string directly. Fastest path to a working demo.
- **Self-hosted on Render** — a second Render service (`type: pserv`, private service, not exposed
  to the internet) running `mysql:8.0.35` with a **paid Render Disk** attached for persistence.
  More setup, but keeps everything on one platform.
- **Anything else you already have** — a VM, AWS RDS, etc. Render just needs outbound network
  access to it, which is unrestricted by default.

Whatever you pick, you need a connection string in this shape for the next step:

```text
Server=<host>;Port=3306;Database=publication_site;User=<user>;Password=<password>;TreatTinyAsBoolean=true;
```

Apply the schema once, from your machine, against that host:

```bash
cd src/PublicationSite.Api
dotnet ef database update --connection "Server=<host>;Port=3306;Database=publication_site;User=<user>;Password=<password>;"
```

(The app also auto-applies pending migrations on every startup — see `Program.cs` — so this manual
step is a safety net / first-run convenience, not strictly required. It **does not** seed anything
by itself.)

### 6. Deploy on Render via the Blueprint

1. Render dashboard → **New → Blueprint** → connect this GitHub repo. Render reads `render.yaml`
   and proposes the `publication-research-backend` web service.
2. Approve it. Render creates the service pointed at
   `docker.io/javiertrombetta/publication-research-backend:latest` but it will fail to boot until
   you fill in the required environment variables (anything marked `sync: false` in `render.yaml`
   has no value yet).
3. Service → **Environment**, fill in at minimum:

   | Key | Value |
   | --- | --- |
   | `ConnectionStrings__Default` | from step 5 |
   | `Jwt__SigningKey` | `openssl rand -base64 64` — generate a real one, don't reuse the dev key from `appsettings.Development.json` |
   | `Frontend__BaseUrl` | your frontend's URL (or `http://localhost:3000` until you have one) |
   | `Cors__AllowedOrigins__0` | same as above — must match exactly for the frontend to be able to call this API |
   | `Mail__*` | **leave these out.** The mail server is an administrator setting edited under System settings, the stored value wins over configuration, and the seeded Admin needs no email to sign in. Setting them to placeholders is worse than omitting them: the "no mail server is configured" warning only appears while the host is empty, so a made-up host produces a connection timeout instead. Until an administrator configures one, emails are skipped and logged, and the workflow still works because every notification is stored in-app as well |
   | `Seed__AdminEmail`, `Seed__AdminPassword` | your real Admin login — created once on first boot, never overwritten afterwards |
   | `Seed__DemoData` | `"true"` on the shared instance the team tests against, so it comes up with an account for every role and a publication at every pipeline stage. **Remove it for production** — every demonstration account shares one published password, and the default outside development is off |

4. **Manual Deploy → Deploy latest commit** (or just wait — Render also polls the image tag
   periodically). First boot runs pending EF Core migrations and seeds the roles and the Admin
   account automatically. With `Seed__DemoData` set, the sample dataset is then built in the
   background — the service reports healthy straight away and the data appears over the following
   minutes, since building it inline would hold the health check open past the point where Render
   gives up on the deploy. `GET /api/dev/demo-data` (Admin) says when it has finished.

### 7. Verify

```bash
curl https://<your-service>.onrender.com/health
```

Should return `{"status":"healthy",...}`. If `Swagger__Enabled` is `true` (it is, by default in
`render.yaml`), `/swagger` is also browsable — useful for a first smoke test, consider turning it
off once a real frontend exists.

## Resetting the database

While the team is testing against this deployment, `POST /api/dev/reset-database` (Admin-only)
wipes and recreates the schema, then reseeds the roles, the configured Admin and — where
`Seed__DemoData` is set — the whole demonstration dataset (`DevTest123!` for every account; see
the README). It returns as soon as signing in is possible again and finishes the dataset in the
background. It's controlled by `DevTools__EnableDatabaseReset` in `render.yaml`, currently
`"true"`.

**Turn it off (`"false"`, or delete the key) the moment this deployment holds real user data** —
there is no confirmation step, any Admin token can trigger it, and it deletes everything.

It also requires `Seed__DemoData`. That is belt and braces rather than duplication: whether a
database is disposable is exactly what seeding demonstration data into it declares, so a deployment
that seeds none cannot be wiped even if this switch was left on by mistake.

## Known limitations of this setup

- **Uploaded files don't survive a redeploy.** `IFileStorageService` writes to local disk inside
  the container (`/app/App_Data/uploads`), and Render's default filesystem is ephemeral — a new
  deploy starts from a fresh image with nothing written. The demonstration dataset works around
  this for its own uploads only: on every start it replaces any sample document whose file has
  gone, so a redeploy doesn't leave reviewers clicking through to a 404. Anything a real person
  uploaded is simply lost. For anything beyond a demo, either attach
  a paid [Render Disk](https://render.com/docs/disks) at that path, or implement `IFileStorageService`
  against Azure Blob Storage (the interface was designed for exactly this swap — see
  `Services/Implementations/LocalFileStorageService.cs`).
- **Render's free plan spins down after 15 minutes of inactivity** and takes ~30–60s to cold-start
  the next request. Fine for a demo/capstone; upgrade the `plan` in `render.yaml` before this is
  anyone's production system.
- **No managed MySQL on Render** — see step 5 above. This is the one piece of infrastructure this
  repo can't fully automate for you.
