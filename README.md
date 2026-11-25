# Foo Calendar

[![CI](https://github.com/yergios/foo-calendar/actions/workflows/ci.yml/badge.svg)](https://github.com/yergios/foo-calendar/actions/workflows/ci.yml)

A self-hostable calendar for coordinating international teams. Tracking vacations, appointments, and other absences on a shared, year-at-a-glance calendar with automatic public holiday syncing per region.

## Documentation

Usage documentation, FAQ, privacy notice, and terms of use are served in-app from the
footer's Documentation link and rendered from [`src/Web/src/docs`](src/Web/src/docs/).

How it is built, and why:

| Guide | Covers |
| --- | --- |
| [Specification](SPEC.md) | What the system must do, as numbered requirements traced to tests; pending changes in [docs/specs](docs/specs/README.md) |
| [Architecture](docs/architecture.md) | The system, startup, the request pipeline, the absence lifecycle, the frontend |
| [Decision records](docs/decisions/README.md) | Every significant design decision, with what was considered and rejected |
| [Threat model](docs/threat-model.md) | Trust boundaries, what is exposed, mitigations, and the risks accepted |
| [Developing with agents](docs/agentic-development.md) | How the repository is built with coding agents: specs, reviews, hooks, the privacy review, and what they caught |

Running and operating it:

| Guide | Covers |
| --- | --- |
| [Deployment](docs/deployment.md) | Configuration, the container image, reverse proxies, scaling, observability |
| [Regions and holidays](docs/regions-and-holidays.md) | How tracked regions follow the team, and the holiday sources |
| [Access control](docs/access-control.md) | Permissions, granting them through your identity provider, adding new ones |
| [Administration](docs/administration.md) | Bulk import, the audit log, the API reference |
| [Data protection](docs/data-protection.md) | Every piece of personal data stored, where data leaves, what stays your responsibility |

## Changelog

See [changelog.md](src/Web/src/docs/changelog.md) for version history.

## License

Foo Calendar is source-available under the [Elastic License 2.0](LICENSE.txt) (`Elastic-2.0`). In plain terms:

- **You may** read the source, run it, self-host it, and modify it — including inside a for-profit company, to coordinate that company's own teams.
- **You may** redistribute it and your modifications, as long as you pass along these terms and mark clearly what you changed.
- **You may** re-skin it to match your brand: colors, fonts and shape live in one file, and your team's own name goes in the header via `Team:OrgName`.
- **You may not** offer it to third parties as a hosted or managed service that exposes a substantial part of its functionality. Running it for your own staff is not this, and your company being profitable changes nothing.
- **You may not** strip or obscure the licensing and copyright notices, which includes the product name, mark and copyright line in the app's footer.
- **You may not** rename the product or call your fork "Foo Calendar". The license grants no rights in the name or the mark.

See [TRADEMARK.md](TRADEMARK.md) for what that means in practice, spelled out. The full text is in [LICENSE.txt](LICENSE.txt) and it is the only thing that governs; the summary above is not legal advice.

Handling personal data with this? Read [docs/data-protection.md](docs/data-protection.md) before you deploy it: it lists everything the app stores, everywhere data leaves, and what stays your responsibility as the controller.

---

## Developer guide

### Architecture overview

A short version follows; [docs/architecture.md](docs/architecture.md) has the full picture
with diagrams.

The project is a single deployable unit: a .NET 10 Minimal API that serves both the backend and the Vue 3 frontend from the same process.

```
src/
├── Api/          .NET 10 Minimal API — people, absences, bank holidays, admin, auth
└── Web/          Vue 3 + TypeScript SPA — built into Api/wwwroot at publish time

tests/
└── UnitTests/    xUnit tests for the API's services, DTOs, and configuration
```

The frontend is compiled by Vite and placed in `src/Api/wwwroot`. The .NET app serves it as static files. In production there is one process, one port, one deployment artifact — the `Dockerfile` at the repository root builds exactly that, see [Deploying](docs/deployment.md#deploying).

**Key technologies**

- .NET 10 Minimal API with EF Core + SQLite
- Public holidays from a configurable source — the [Nager.Date](https://date.nager.at/) API by default, or a JSON file, or nothing at all — synced on a background job and on demand from the admin page, see [Where holidays come from](docs/regions-and-holidays.md#where-holidays-come-from)
- Generic OIDC (works with Auth0, Okta, Keycloak, Azure AD, etc.) for authentication, with per-permission authorization on top — or a zero-setup dev-only bypass, see below
- Vue 3 + TypeScript + Pinia + Tailwind CSS for the frontend
- OpenAPI with a [Scalar](https://scalar.com/) reference UI, on in development and opt-in elsewhere, see [Browsing the API](docs/administration.md#browsing-the-api)
- OpenTelemetry (traces, metrics, and logs over OTLP) for observability — vendor-neutral, see [Observability](docs/deployment.md#observability)

---

### Quickstart: Local development

**Prerequisites**

- .NET 10 SDK, feature band 10.0.1xx (`global.json` pins it; any patch of that band works)
- Node.js 20.19+ (or 22.12+)
- pnpm (`npm install -g pnpm`)

**Setup**

> I strongly suggest using dotnet user secrets for anything sensitive (OIDC credentials, connection strings), rather than editing `appsettings.json` directly.

Out of the box, `src/Api/appsettings.Development.json` sets `Auth:Provider` to `None`, which fully bypasses SSO in local development, so you can run the app with no OIDC/IdP tenant at all. Everything below is only needed once you point at a real provider (staging/production, or if you want to exercise the real auth path locally):

1. At `src/Api/appsettings.json`, set values for `ConnectionStrings:FooCalendarDb` and the `Auth` section (`Authority`, `Audience`, `ClientId`, `Permissions`) — these come from whichever OIDC provider you use (Auth0, Okta, Keycloak, Azure AD, ...).
2. Holidays are pulled from the public Nager.Date API by default, which needs no setup. See [Where holidays come from](docs/regions-and-holidays.md#where-holidays-come-from) to point that elsewhere.

The frontend needs no setup of its own: it reads branding and OIDC settings from the API at startup — see [Configuring a deployment](docs/deployment.md#configuring-a-deployment).

**Run the API**

```bash
cd src/Api
dotnet run
```

The API listens on `http://localhost:5055` by default. On first run it creates the SQLite database and applies EF Core migrations automatically.

**Run the frontend**

```bash
cd src/Web
pnpm install
pnpm dev
```

The Vite dev server runs on `http://localhost:5173` and proxies `/api/*` requests to the .NET API. Configure the target port via `VITE_API_TARGET_URL` in `src/Web/.env.development`.

### Checking a change

```bash
scripts/check.sh
```

A change is done when this passes. It runs every check CI runs that does not need Docker, in
CI's order, and stops at the first failure, naming it: the .NET restore (with its NuGet
audit), the Release build with zero warnings and the tests; the version agreement between
the API and the SPA; the SPA's typecheck, lint, format check, tests and dependency audit;
the spec trace check, the scripts' own tests and the privacy review trailers of the commits
not pushed yet; and the browser checks, which build the
SPA, start five local instances of the production build and the stub OIDC provider, and run
the [end-to-end suite](tools/e2e/README.md) and the [performance budget](tools/perf/README.md#budget)
against them. Every process it starts is stopped when it ends, whether it passed or not.
`scripts/check.sh --list` prints the steps. One check runs at a time per repository; a
second waits for the first. Without network it works from the package caches and skips the
two dependency audits, saying so; CI runs them.

The browser checks need a `chrome-headless-shell`; without one, that step fails and says
how to get the release CI pins. The instances listen on ports 18080 to 18084 and the stub on
19000 and 19001, so the check can run beside a hand-started container on 8080.
