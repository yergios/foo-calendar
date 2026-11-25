# Developer guide

Everything you need to work on Foo Calendar: setting it up, running it, filling it with
data, debugging it, checking a change, and the changes that have a set procedure. How the
system is put together is in [Architecture](architecture.md). What it must do is in
[SPEC.md](../SPEC.md).

## Prerequisites

- .NET 10 SDK, feature band 10.0.1xx. `global.json` pins it, and any patch of that band
  works.
- Node.js 22, as CI uses. The SPA alone builds on 20.19+, but the browser checks and the
  performance harness need 22.
- pnpm (`npm install -g pnpm`).
- For the browser checks only: a `chrome-headless-shell`, see
  [tools/perf/README.md](../tools/perf/README.md#setup). `scripts/check.sh` says how to get
  the release CI pins when it is missing.

## Running it locally

```bash
dotnet run --project src/Api        # API on http://localhost:5055
pnpm --dir src/Web install
pnpm --dir src/Web dev              # SPA on http://localhost:5173
```

Open `http://localhost:5173`. The Vite dev server proxies `/api/*` to the API; change the
target with `VITE_API_TARGET_URL` in `src/Web/.env.development`. On first run the API
creates the SQLite database, `src/Api/foo-calendar.db`, and applies the migrations.

No identity provider is needed. `src/Api/appsettings.Development.json` sets
`Auth:Provider` to `None`, which skips sign-in, and you are the `Auth:DevUser` identity
holding every permission. The API refuses to start that way outside Development. To
exercise real sign-in locally, set `Auth:Provider` to `Oidc` and your provider's `Auth`
values ([Deployment](deployment.md#configuring-a-deployment)). Keep credentials in user
secrets, not in `appsettings.json`:

```bash
dotnet user-secrets set "Auth:Authority" "https://your-tenant.example.com" --project src/Api
```

The SPA has no configuration of its own: the API serves it the sign-in settings, the team's
name and the permissions at startup.

## Data for development

```bash
scripts/seed.sh                     # 50 people over 10 regions
scripts/seed.sh --people 100 --regions 20
scripts/seed.sh --help
```

The seed **empties the database first**, then creates people across regions with 50 or
more absences each, so the same options always give the same data. To start from nothing
instead, delete `src/Api/foo-calendar.db`.

## Debugging

- **The API reference.** In Development the API serves its OpenAPI document and a
  [Scalar](https://scalar.com/) reference at `http://localhost:5055/scalar`. You can call
  the API from that page ([Browsing the API](administration.md#browsing-the-api)).
- **As a plain member.** The dev identity holds every permission. To see what a member
  without one sees, run
  `Auth__DevUser__GrantAllPermissions=false dotnet run --project src/Api`. To hold only some
  permissions, also list their claim values, as in
  `Auth__DevUser__Permissions__0=AuditRead` ([Access control](access-control.md)).
- **A startup failure.** Configuration is checked before anything else, and an invalid
  value stops the API with a message naming the setting (CFG-1). Read the first error in
  the log.
- **Health.** `/healthchecks/detail` shows each check with its error. The dev identity can
  read it, since it holds `AuditRead`.
- **Traces, metrics and logs.** Point the API at any OTLP collector, as in
  `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 dotnet run --project src/Api`. The
  spans and metrics are listed in [Observability](deployment.md#observability).
- **Stale data on screen.** The SPA paints from its `localStorage` cache before it
  revalidates ([0011](decisions/0011-client-side-stale-while-revalidate.md)). **Sign out**
  clears everything the app stored in the browser.
- **The production build.** `pnpm --dir src/Web build` writes the SPA into
  `src/Api/wwwroot`, and the API serves it at `http://localhost:5055` under the real
  Content-Security-Policy. The API caches the page, so restart it after every build. A
  policy violation shows in the browser console.

## Commands

| What | Command |
| --- | --- |
| Everything, in CI's order | `scripts/check.sh` (`--list` prints the steps) |
| .NET build, 0 warnings | `dotnet build --configuration Release` |
| .NET tests | `dotnet test --configuration Release` (one test: `--filter "FullyQualifiedName~AbsenceServiceTests"`) |
| SPA checks | `pnpm --dir src/Web typecheck`, `lint:check`, `format:check`, `test` |
| Spec trace | `node scripts/check-spec-trace.mjs` |
| Script and hook tests | `node --test 'scripts/**/*.test.mjs'` |
| End-to-end flows | `node --test --test-timeout=120000 tools/e2e/flows.test.mjs`, against running instances ([tools/e2e](../tools/e2e/README.md)) |
| Performance | `node tools/perf/harness.mjs 1000 --budget` against a production build ([tools/perf](../tools/perf/README.md)) |
| Seed a dev database | `scripts/seed.sh --people 100` (wipes it first) |
| Run the app | `dotnet run --project src/Api` and `pnpm --dir src/Web dev` ([above](#running-it-locally)) |
| A new migration | [A database migration](#a-database-migration) |
| A release | [Cutting a release](#cutting-a-release) |

## Checking a change

```bash
scripts/check.sh
```

A change is done when this passes. It runs every check CI runs that does not need Docker,
in CI's order, and stops at the first failure, naming it. That covers:

- the .NET restore (with its NuGet audit), the Release build with zero warnings, and the
  tests;
- the version agreement between the API and the SPA;
- the SPA's typecheck, lint, format check, tests and dependency audit;
- the spec trace check, the scripts' own tests, and the privacy review trailers of the
  commits not pushed yet;
- the browser checks. These build the SPA, start five local instances of the production
  build and the stub OIDC provider, and run the end-to-end suite and the performance budget
  against them.

Every process it starts is stopped when it ends, pass or fail. One check runs at a time per
repository, and a second waits for the first. Without network it works from the package
caches and skips the two dependency audits, saying so; CI runs them. The instances listen
on ports 18080 to 18084 and the stub on 19000 and 19001, so the check can run beside a
container you started by hand on 8080.

Every push to `master` and every pull request runs `.github/workflows/ci.yml`. None of its
steps is advisory:

| Job | What fails it |
| --- | --- |
| API | A build warning (every analyzer on, warnings as errors, NuGet audit included), a unit or integration test, or `Api.csproj` and `package.json` disagreeing on the version |
| Web | Typecheck, lint warnings, formatting, a Vitest test, or a high or critical `pnpm audit` advisory |
| Spec traceability | A `[test]` requirement no test references and the gap list does not name, or a reference to an ID that is missing or retired (`scripts/check-spec-trace.mjs`); a test of the scripts or hooks failing; a pushed or pull-request commit, merge commits included, that touches absence notes without the `Privacy-Review` trailer of its own diff (`scripts/privacy-review.mjs verify`) |
| Container image and browser checks | The image not building, starting, migrating an empty database and serving the shell; an end-to-end flow failing or causing a Content-Security-Policy violation it does not expect, signing in through a stub OIDC provider included (`tools/e2e`); the calendar at 1000 people exceeding 5,000 DOM nodes after an interaction, or an interaction's longest block exceeding the larger of three times its baseline and 16 ms twice in a row, or the run not measuring every interaction against a grid with absences drawn (`tools/perf`) |

The browser checks run on the repository's own DevTools driver, `tools/browser/driver.mjs`,
against a pinned `chrome-headless-shell`
([0016](decisions/0016-own-devtools-driver-for-browser-checks.md)). A failing flow uploads
its screenshot, console and violations. The performance results are uploaded either way.

## Tests

- **.NET.** `tests/UnitTests` covers services, DTOs, configuration and migrations.
  `tests/IntegrationTests` runs the API in memory and covers every route. Both use xUnit.
- **SPA.** Vitest, beside the code in `src/Web/src`, for the logic in `lib/` and the
  stores.
- **End to end.** `tools/e2e` runs the main flows in a real browser against the container,
  signing in through a stub OIDC provider. Its [README](../tools/e2e/README.md) covers the
  instances it needs and how to run it by hand.
- **Performance.** `tools/perf` measures the calendar's main-thread cost at 100 and 1000
  people and holds it to a baseline. Its [README](../tools/perf/README.md) explains each
  column, the budget and how to refresh the baseline.

Every test names the requirements it verifies, as `[Trait("Spec", "ABS-5")]` in xUnit or
`[ABS-5]` in a Vitest or `node:test` name. `scripts/check-spec-trace.mjs` fails when a
`[test]` requirement in SPEC.md has no test, or when a test names an ID that does not exist
(TRC-1, TRC-2). A requirement with a known gap is listed in `tests/spec-coverage-gaps.txt`.

## Common changes

### A setting

Add it to its options class in `src/Api/Configuration/` with a validation rule in the same
change. An invalid value must stop startup with a message naming the setting (CFG-1). Then
document it in `.env.example` and in the guide that covers its area.

### An endpoint

Authorization is closed by default: a fallback policy requires a signed-in user, so a new
route is protected without doing anything. A route that needs a permission asks for it by
name (`.RequireAuthorization(Permissions.PeopleManage)`). The few public routes say so with
`.AllowAnonymous()`, and AUTH-3 lists every one of them
([0006](decisions/0006-fail-closed-per-permission-authorization.md)). The
endpoint stays thin and delegates to a service in `src/Api/Services/`. A rule it enforces
throws one of the domain errors in `src/Api/Services/DomainException.cs`, which map to 400,
403, 404 and 409.

### A permission

Add a constant to `Permissions.All` (`src/Api/Configuration/Permissions.cs`) and the
matching name to `src/Web/src/types/permission.ts`. `Program.cs` registers a policy for each
entry, so nothing else needs touching. But every deployment must then configure it, since
the app refuses to start when a permission names no claim value. Say so in the changelog.

### A database migration

Migrations live in `src/Api/Migrations/` and are applied at startup (CFG-4). A migration that
loses data loses it on every deployment the moment it upgrades. Never edit one that is
already on `master`; add a new one. `dotnet ef` is the `dotnet-ef` global tool, at the EF
Core version in `Directory.Packages.props`.

1. Change the entity in `src/Api/Data/Models/` and, if needed, `AppDbContext`.
2. Name the migration in the existing style: PascalCase, a verb and what it changes, such
   as `AddAuditEntries`, `RemoveAbsenceApprovals`, `UpdatePersonFirstAndLastName`. Then run
   `dotnet ef migrations add <Name> --project src/Api` and keep the timestamp prefix it
   stamps.
3. Check that the model and the migrations agree. This must report no pending changes:
   `dotnet ef migrations has-pending-model-changes --project src/Api`.
4. Read the SQL it will run:
   `dotnet ef migrations script <PreviousMigration> <Name> --project src/Api`. Look for
   `DROP`, a table SQLite rebuilds (`CREATE TABLE "ef_temp_..."` then
   `INSERT INTO ... SELECT`), and `DELETE`, and name which rows or values do not survive.
5. Add a test in `tests/UnitTests/Data/` in the shape of
   `RemoveAbsenceApprovalsMigrationTests`. Migrate an in-memory SQLite database to the
   previous migration, insert rows as that release stored them (every optional column filled
   in at least once), then migrate up. Assert what is kept, what is gone and the columns
   that remain. Tag the test with the requirement it protects.
6. Run `scripts/check.sh`.

A migration that touches the absence note or the audit log goes through the privacy review
when it is committed ([AGENTS.md](../AGENTS.md#privacy-review)).

### Cutting a release

The version lives in two places that CI ties together (BLD-5): `<Version>` in
`src/Api/Api.csproj` and `"version"` in `src/Web/package.json`. Pick it by semantic
versioning from the changelog's Unreleased section. A breaking change to configuration or
the API is a major bump, an addition a minor one, and fixes alone a patch.

1. Start from a clean `master`, up to date with its remote.
2. Set the new version in `src/Api/Api.csproj` and `src/Web/package.json`.
3. In `CHANGELOG.md`, rename `## Unreleased` to `## <version> (<YYYY-MM-DD>)` and put a new,
   empty `## Unreleased` above it.
4. Run `scripts/check.sh`.
5. Commit as `chore(release): <version>` and push.
6. Tag it, `git tag --annotate v<version> --message "Foo Calendar <version>"`, and push the
   tag. A tag is what deployments pull, so push it only when the release is meant to ship.

## How changes are made

This repository is built mostly with coding agents, and the process is written down for
people and agents alike. [AGENTS.md](../AGENTS.md) holds the rules: the definition of done,
the conventions, how much process each kind of change needs, and the privacy review of any
change that touches absence notes. [Developing with agents](agentic-development.md) explains
why it works this way and what it has caught. A change to what the system does starts as a
change spec in [docs/specs](specs/README.md). A choice between real alternatives is
recorded as a [decision record](decisions/README.md).
