# Foo Calendar

Internal tool for coordinating the team. Tracking vacations, sick leaves, and other absences on a shared, year-at-a-glance calendar with automatic public holiday syncing per region.

## Documentation

See [help.md](src/Web/src/docs/help.md) for usage documentation and FAQ.

## Changelog

See [changelog.md](src/Web/src/docs/changelog.md) for version history.

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

The frontend is compiled by Vite during CI and placed in `src/Api/wwwroot`. The .NET app serves it as static files. In production there is one process, one port, one deployment artifact.

**Key technologies**

- .NET 10 Minimal API with EF Core + SQLite
- [Nager.Date](https://date.nager.at/) public holiday API, synced automatically on a weekly background job (and on demand from the admin page)
- Generic OIDC (works with Auth0, Okta, Keycloak, Azure AD, etc.) for authentication and permission-based admin access — or a zero-setup dev-only bypass, see below
- Vue 3 + TypeScript + Pinia + Tailwind CSS for the frontend
- OpenTelemetry / Azure Monitor for observability

---

### Quickstart: Local development

**Prerequisites**

- .NET 10 SDK
- Node.js 20.19+ (or 22.12+)
- pnpm (`npm install -g pnpm`)

**Setup**

> I strongly suggest using dotnet user secrets for anything sensitive (OIDC credentials, connection strings), rather than editing `appsettings.json` directly.

Out of the box, `src/Api/appsettings.Development.json` sets `Auth:Provider` to `None` and the frontend's `src/Web/.env.development` sets `VITE_AUTH_MODE=dev` — together these fully bypass SSO in local development, so you can run the app with no OIDC/IdP tenant at all. Everything below is only needed once you point at a real provider (staging/production, or if you want to exercise the real auth path locally):

1. At `src/Api/appsettings.json`, set values for `ConnectionStrings:FooCalendarDb` and the `Auth` section (`Authority`, `Audience`, `AdminPermission`) — `Authority`/`Audience` come from whichever OIDC provider you use (Auth0, Okta, Keycloak, Azure AD, ...).
2. `Nager:BaseUrl` defaults to the public Nager.Date API (`https://date.nager.at/api/v3/`) and normally doesn't need changing.
3. At `src/Web/.env.development`, set the `VITE_OIDC_*` values for your OIDC provider (and remove/unset `VITE_AUTH_MODE`). Set `VITE_BYPASS_ADMIN_PERMISSION=true` if you want to see the admin UI locally without an admin-permissioned account — this flag is only ever read in development.

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

Grant the admin permission to a user in your OIDC provider's dashboard. Locally, set `VITE_BYPASS_ADMIN_PERMISSION=true` in `src/Web/.env.development` instead of needing an actual admin-permissioned account — see [Quickstart](#quickstart-local-development) above.

### Debugging

Azure Application Insights / OpenTelemetry are configured for this project (`src/Api/Configuration/TelemetryOptions.cs`).

You'll also find, in the web app itself, page `/admin`, where an admin can inspect and manage the full list of people and re-trigger the bank-holiday sync.
