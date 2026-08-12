# Foo Calendar

Internal tool for coordinating the team. Tracking vacations, sick leaves, and other absences on a shared, year-at-a-glance calendar with automatic public holiday syncing per region.

## Documentation

See [help.md](src/Web/src/docs/help.md) for usage documentation and FAQ.

## Changelog

See [changelog.md](src/Web/src/docs/changelog.md) for version history.

## License

Foo Calendar is source-available under the [Elastic License 2.0](LICENSE.txt) (`Elastic-2.0`). In plain terms:

- **You may** read the source, run it, self-host it, and modify it — including inside a for-profit company, to coordinate that company's own teams.
- **You may** redistribute it and your modifications, as long as you pass along these terms and mark clearly what you changed.
- **You may not** offer it to third parties as a hosted or managed service that exposes a substantial part of its functionality.
- **You may not** strip or obscure the licensing and copyright notices.

The full text is in [LICENSE.txt](LICENSE.txt) and it is the only thing that governs; the summary above is not legal advice.

---

## Developer guide

### Architecture overview

The project is a single deployable unit: a .NET 10 Minimal API that serves both the backend and the Vue 3 frontend from the same process.

```
src/
├── Api/          .NET 10 Minimal API — people, absences, bank holidays, admin, auth
└── Web/          Vue 3 + TypeScript SPA — built into Api/wwwroot at publish time

tests/
└── UnitTests/    xUnit test project (scaffold — no tests written yet)
```

The frontend is compiled by Vite and placed in `src/Api/wwwroot`. The .NET app serves it as static files. In production there is one process, one port, one deployment artifact — the `Dockerfile` at the repository root builds exactly that, see [Deploying](#deploying).

**Key technologies**

- .NET 10 Minimal API with EF Core + SQLite
- [Nager.Date](https://date.nager.at/) public holiday API, synced automatically on a weekly background job (and on demand from the admin page)
- Generic OIDC (works with Auth0, Okta, Keycloak, Azure AD, etc.) for authentication and permission-based admin access — or a zero-setup dev-only bypass, see below
- Vue 3 + TypeScript + Pinia + Tailwind CSS for the frontend
- OpenTelemetry (traces, metrics, and logs over OTLP) for observability — vendor-neutral, see [Observability](#observability)

---

### Quickstart: Local development

**Prerequisites**

- .NET 10 SDK
- Node.js 20.19+ (or 22.12+)
- pnpm (`npm install -g pnpm`)

**Setup**

> I strongly suggest using dotnet user secrets for anything sensitive (OIDC credentials, connection strings), rather than editing `appsettings.json` directly.

Out of the box, `src/Api/appsettings.Development.json` sets `Auth:Provider` to `None`, which fully bypasses SSO in local development, so you can run the app with no OIDC/IdP tenant at all. Everything below is only needed once you point at a real provider (staging/production, or if you want to exercise the real auth path locally):

1. At `src/Api/appsettings.json`, set values for `ConnectionStrings:FooCalendarDb` and the `Auth` section (`Authority`, `Audience`, `ClientId`, `AdminPermission`) — these come from whichever OIDC provider you use (Auth0, Okta, Keycloak, Azure AD, ...).
2. `Nager:BaseUrl` defaults to the public Nager.Date API (`https://date.nager.at/api/v3/`) and normally doesn't need changing.

The frontend needs no setup of its own: it reads branding and OIDC settings from the API at startup — see [Configuring a deployment](#configuring-a-deployment).

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

---

### Configuring a deployment

Everything that differs between one company's deployment and another's is API configuration, read at startup. **The frontend bundle contains no environment-specific values**, so a single build artifact can be deployed anywhere: rebranding it or pointing it at a different identity provider never requires rebuilding the SPA.

At startup the frontend fetches `GET /api/config/bootstrap` (the one anonymous endpoint, since it's what tells the app how to sign in) and configures itself from the response:

| Setting | Purpose |
| ------------------------------ | ------------------------------------------------------------------------- |
| `Branding:AppName` | Name in the header, the footer, and the browser tab title. |
| `Branding:SupportUrl` | "Contact support" links, including inside the help doc. Any scheme. |
| `Branding:IssueUrl` | "Raise an issue" links on the error page and in the footer. |
| `Auth:Authority` | OIDC issuer. Used by the API to validate tokens and by the SPA to log in. |
| `Auth:ClientId` | The SPA's OIDC client id. Frontend-only, but required at startup. |
| `Auth:Audience` | Which API the access token is issued for. |
| `Auth:Scope` | Scopes the SPA requests. |
| `Auth:RedirectUri` | Where the IdP returns after login. Relative by default, so any hostname works. |
| `Auth:AdminPermission` | Permission value gating `/admin`, on both sides. |
| `Auth:ClaimTypes:*` | Which claims carry identity and permissions, on both sides. |

Every one of these is overridable per environment without touching `appsettings.json`, using the standard .NET double-underscore environment variables — which is usually how you'd configure a container:

```bash
Branding__AppName="Acme Leave" \
Branding__SupportUrl="mailto:it@acme.example" \
Auth__Authority="https://acme.okta.com" \
Auth__ClientId="0oa1b2c3" \
Auth__Audience="https://leave.acme.example/api" \
Auth__ClaimTypes__Permissions="roles" \
ConnectionStrings__FooCalendarDb="Data Source=/var/lib/foo-calendar/app.db" \
dotnet run --project src/Api
```

The same applies to the `RegionCatalog` and `PersonColorPalette` sections below, and to `Telemetry`.

To change the look rather than the wording, edit the design tokens at the top of `src/Web/src/styles/theme.css` — that one file drives the whole UI, and it documents the contrast rules to keep if you re-skin. Unlike the settings above, a theme change is compiled into the bundle, so it does mean a rebuild.

---

### Deploying

The `Dockerfile` builds the whole thing — Vite compiles the SPA, `dotnet publish` packages the API, and the result is one image running one process on one port. There is no separate web server to deploy or configure.

```bash
cp .env.example .env    # fill in at least the Auth__* values
docker compose up -d
```

That serves the app on `http://localhost:8080` and keeps the SQLite database in the `foo-calendar-data` volume. **That volume is the entire application state** — back it up and you have backed up everything.

To try the app before setting up an identity provider, run it in the Development environment, where SSO is bypassed entirely (`Auth:Provider=None`) and you are signed in as the `Auth:DevUser` identity, admin included:

```bash
docker build -t foo-calendar:1.0.0 .
docker run --rm -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Development foo-calendar:1.0.0
```

Nothing persists there — no volume is mounted, and the database goes away with the container. Uncommenting `ASPNETCORE_ENVIRONMENT=Development` in `.env` does the same through compose, keeping the volume. Either way the API refuses to start with `Auth:Provider=None` outside Development, so this can't be deployed by accident.

If the container restarts in a loop, read the logs first: configuration is validated at startup, so a missing `Auth__Authority` or `Auth__ClientId` shows up there as a plain `must be configured` message rather than as a broken page later.

**Notes for other environments**

- The container listens on `8080` and runs as the non-root `app` user from the .NET base image.
- The image includes a `HEALTHCHECK` against `/healthchecks`, which is anonymous and reports per-check detail as JSON. Kubernetes and other platforms that probe over HTTP themselves don't need the `curl` that check relies on, and can drop that layer from the runtime stage.
- A **named volume** picks up the right ownership automatically. A **bind mount** does not: `chown 1654 /your/host/path` first, or migrations will fail on first start with a read-only database error.
- Configure everything through environment variables, as `.env.example` shows. Nothing needs to be baked into the image, which is why the same image works for every deployment.
- Migrations are applied automatically at startup, so upgrading is a matter of pulling a newer image and restarting. This is also why more than one replica is not yet safe — see [Scaling](#scaling).

#### Behind a reverse proxy

Nothing here is needed when the app owns its own hostname and terminates its own TLS. Both settings are off by default.

**Serving from a sub-path.** To host the app at `https://intra.example.com/calendar` rather than at the root of a hostname:

```bash
ReverseProxy__PathBase=/calendar
```

The frontend build is not involved. `index.html` carries a `<base href>` that the API rewrites at startup, and every asset URL, router route and API call the app emits is relative to it — which is why the same image serves `/` and `/calendar` with no rebuild.

Set it whether or not your proxy strips the prefix before forwarding. `UsePathBase` only rewrites requests that actually arrive carrying it, so both arrangements work:

```nginx
# Prefix forwarded intact
location /calendar/ {
    proxy_pass http://foo-calendar:8080/calendar/;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

The container's own health probe calls `/healthchecks` without the prefix, which keeps working either way.

**TLS terminated elsewhere.** So the app sees the scheme, host and client address the original caller used rather than the proxy's:

```bash
ReverseProxy__TrustForwardedHeaders=true
```

This is off by default on purpose. With nothing in front of the app, any client can set `X-Forwarded-For` and forge its own address in your logs and traces. Turn it on only when the app is genuinely behind a proxy, and restrict which peers may send those headers if the app is reachable any other way:

```bash
ReverseProxy__KnownProxies__0=10.0.0.7
ReverseProxy__KnownNetworks__0=10.0.0.0/8
```

Leaving both empty accepts forwarded headers from any peer, which is the normal container setup where the proxy is the only route in.

#### Scaling

The app is designed to run as a single instance, and two things assume it:

- **Migrations run on startup.** Several replicas starting at once would race each other applying the same migration.
- **The bank-holiday sync is in-process.** Every replica would run its own weekly sync against Nager.

SQLite is not the blocker here; both would need addressing before a second replica, whatever the database. For the size of team this tool is for, one instance is the intended shape.

---

### Adding a new region or country

No code changes are required — the region catalog is entirely config-driven.

Edit the `RegionCatalog` array in `src/Api/appsettings.json`:

```json
{
  "Code": "ES",
  "Name": "Spain",
  "Regions": [
    { "Code": "ES-MD", "Label": "Madrid" },
    { "Code": "ES-CT", "Label": "Catalonia" }
  ]
}
```

- `Code` follows ISO 3166-2 (the country code alone for a nationwide entry, `COUNTRY-SUBDIVISION` for a specific region).
- This one list drives three things: the home-city dropdown on a person's profile, the region filter on the calendar, and which regions get synced from Nager (both on the weekly background job and via `POST /api/admin/bank-holidays/sync`).

### Adding an assignable color

Edit the `PersonColorPalette` array in `src/Api/appsettings.json` — each entry must be a 6-digit hex color (`#RRGGBB`). This is the palette offered when someone picks their calendar color. A color already claimed by someone else shows as taken until they change it or delete their profile.

---

### Access levels

Most of the app is available to anyone signed in. A single elevated permission gates the admin area:

| Who                                                                          | Can do                                                                                                                  |
| ---------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Anyone signed in                                                             | View the calendar, manage their own profile and absences, add/edit/delete bank holidays                                 |
| Holder of the `Auth:AdminPermission` permission (`src/Api/appsettings.json`) | Everything above, plus `/admin`: manage any person's profile, trigger a bank-holiday sync, view team-wide absence stats |

Grant the admin permission to a user in your OIDC provider's dashboard. Locally, the dev bypass grants whatever is listed in `Auth:DevUser:Permissions` (`src/Api/appsettings.Development.json`), which already includes the admin permission — so the admin area is reachable without an admin-permissioned account.

Which claim carries permissions varies by provider; set `Auth:ClaimTypes:Permissions` to match (`permissions`, `roles`, `groups`, ...). Both the API's authorization policy and the frontend's admin nav read that one setting.

### Observability

The app emits traces, metrics, and logs via OpenTelemetry and pushes all three over OTLP. It is not tied to any vendor: point it at whatever you already run.

**Nothing is exported until you set an endpoint.** With none configured the instrumentation stays inert, so the default deployment is silent rather than retrying a collector that isn't there.

The standard `OTEL_*` environment variables are read as-is, which is what most platforms and sidecars already inject:

| Variable | Purpose |
| ---------------------------- | ------------------------------------------------------------------------- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Collector base URL. Setting it is what turns exporting on. |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (default, port 4317) or `http/protobuf` (port 4318). |
| `OTEL_EXPORTER_OTLP_HEADERS` | Comma-separated headers, e.g. `api-key=…` for a hosted backend. |
| `OTEL_SERVICE_NAME` | Reported as `service.name`. Defaults to the application name. |
| `OTEL_RESOURCE_ATTRIBUTES` | Extra resource attributes, e.g. `deployment.environment=staging`. |

```bash
# Everything to a local collector over gRPC
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 dotnet run --project src/Api

# A hosted backend over HTTP, authenticated
OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp.example.com \
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf \
OTEL_EXPORTER_OTLP_HEADERS=api-key=secret \
OTEL_SERVICE_NAME=foo-calendar \
dotnet run --project src/Api
```

The `Telemetry` section in `src/Api/appsettings.json` (`OtlpEndpoint`, `OtlpProtocol`, `ServiceName`) mirrors the first four and **takes precedence** when a value is non-empty; leave the keys blank to let the environment decide. See `src/Api/Configuration/TelemetryOptions.cs`.

Prometheus users should scrape an OpenTelemetry Collector placed in front of the app rather than the app itself — the app only pushes OTLP and exposes no `/metrics` endpoint.

### Debugging

You'll also find, in the web app itself, page `/admin`, where an admin can inspect and manage the full list of people and re-trigger the bank-holiday sync.
