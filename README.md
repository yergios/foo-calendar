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
- Public holidays from a configurable source — the [Nager.Date](https://date.nager.at/) API by default, or a JSON file, or nothing at all — synced on a background job and on demand from the admin page, see [Where holidays come from](#where-holidays-come-from)
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

1. At `src/Api/appsettings.json`, set values for `ConnectionStrings:FooCalendarDb` and the `Auth` section (`Authority`, `Audience`, `ClientId`, `AdminPermissions`) — these come from whichever OIDC provider you use (Auth0, Okta, Keycloak, Azure AD, ...).
2. Holidays are pulled from the public Nager.Date API by default, which needs no setup. See [Where holidays come from](#where-holidays-come-from) to point that elsewhere.

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
| `Auth:AdminPermissions` | Claim values gating `/admin`, on both sides. Any one of them grants it. |
| `Auth:ClaimTypes:*` | Which claims carry identity and permissions, on both sides. |

Every one of these is overridable per environment without touching `appsettings.json`, using the standard .NET double-underscore environment variables — which is usually how you'd configure a container:

```bash
Branding__AppName="Acme Leave" \
Branding__SupportUrl="mailto:it@acme.example" \
Auth__Authority="https://acme.okta.com" \
Auth__ClientId="0oa1b2c3" \
Auth__Audience="https://leave.acme.example/api" \
Auth__ClaimTypes__Permissions__0="roles" \
ConnectionStrings__FooCalendarDb="Data Source=/var/lib/foo-calendar/app.db" \
dotnet run --project src/Api
```

The same applies to the `Team`, `RegionCatalog` and `PersonColorPalette` sections below, and to `Telemetry`.

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
- The image includes a `HEALTHCHECK` against `/healthchecks`. That endpoint is anonymous and runs every check, but reports only the overall status (`{"status":"Healthy"}`) — per-check detail, including exception messages, is at `/healthchecks/detail` and requires the admin permission. Kubernetes and other platforms that probe over HTTP themselves don't need the `curl` that check relies on, and can drop that layer from the runtime stage.
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
- **The bank-holiday sync is in-process.** Every replica would run its own sync against the holiday source.

SQLite is not the blocker here; both would need addressing before a second replica, whatever the database. For the size of team this tool is for, one instance is the intended shape.

---

### Which regions the calendar tracks

Nothing to configure, and no code changes: the set of tracked regions is **derived from
where people say they are**. Whoever sets `ES-MD` on their profile is what makes Madrid a
region this calendar knows about.

The only configured region is the team's own:

```json
"Team": { "HomeRegion": "CH-ZH" }
```

`Team:HomeRegion` is required, ISO 3166-2, and a bare country code (`"CH"`) is valid if the
team doesn't care about subdivisions. It is the one region synced whether or not anyone has
picked it, and the one region never dropped when the last person in it leaves.

Everything else follows from that:

| Event | What happens |
| --- | --- |
| Someone picks a region on their profile | Its country's and its own holidays are synced immediately, for this year and next |
| Someone changes region | The new one is synced; the old one is dropped if they were the last person in it |
| The last person in a region leaves or is deleted | That region's holidays are **deleted, manual entries included** |
| The last person in a country leaves | The country's nationwide holidays go too — but only once nobody is left in *any* of its regions |
| Weekly, and on startup | Everywhere the team currently is gets re-synced, current year and next |

> [!WARNING]
> Deleting a person deletes the bank holidays of any region they were the last occupant of,
> and that includes hand-added ones. `Team:HomeRegion` is exempt; nothing else is.

**Picking a region.** The profile page offers everything the holiday source knows about —
all 204 countries for Nager, and the subdivisions of whichever one is selected. The calendar's
region filter and the manual-holiday form offer only the regions the team occupies, and the
API rejects a manual holiday for anywhere else.

**Region names** come from a bundled ISO 3166-1/3166-2 name table
(`src/Api/Data/regions.json`, derived from [CLDR](https://cldr.unicode.org/), see
`NOTICE.txt`), because the Nager API returns subdivision *codes* with no names attached. To
override a name — CLDR calls `ES-MD` "Madrid Autonomous Community" — or to name a code the
table is missing, use the optional `RegionCatalog` section:

```json
"RegionCatalog": [
  { "Code": "ES", "Name": "Spain", "Regions": [{ "Code": "ES-MD", "Label": "Madrid" }] }
]
```

It only overrides display names now; leaving it out entirely is fine. Anything with no entry
in either place falls back to showing the raw code.

### Where holidays come from

Public holidays are filled in by a configurable source. Whichever one is in use, the app owns the result: rows are stored locally, admins can override any of them by hand, and **a manual entry is never overwritten by a later sync**.

```json
"Holidays": {
  "Source": "Nager",
  "SyncInterval": "7.00:00:00",
  "Nager": { "BaseUrl": "https://date.nager.at/api/v3/" },
  "File": { "Path": "" }
}
```

| `Source` | Behaviour |
| -------- | ------------------------------------------------------------------------------------- |
| `Nager` | Default. Pulls from the [Nager.Date](https://date.nager.at/) API. Needs outbound internet. |
| `File` | Reads a JSON file. No egress, and it can express closures no public API knows about. |
| `None` | No automatic holidays at all. Whatever admins add by hand is all there is. |

Every region the team occupies is synced on startup, then every `SyncInterval` (a `d.hh:mm:ss` span), for the current year and the next. An admin can also trigger it from `/admin`, which with `Source=None` refuses with a `409` rather than pretending to have synced.

A source is asked for one country at a time, so a failed fetch takes every region of that country with it, and all of them come back in `failedRegions`. Re-running just those is what the region selector on the admin sync panel is for — `POST /api/admin/bank-holidays/sync?year=2026&region=ES-MD` — rather than re-fetching every other country to get at one.

**Behind a proxy?** No configuration here: `HttpClient` reads the standard `HTTP_PROXY`, `HTTPS_PROXY` and `NO_PROXY` environment variables by itself.

#### The file source

Point it at a JSON file, typically mounted in:

```bash
Holidays__Source=File
Holidays__File__Path=/etc/foo-calendar/holidays.json
```

```json
[
  { "region": "CH", "date": "2026-08-01", "name": "Swiss National Day" },
  { "region": "CH-ZH", "date": "2026-04-13", "name": "Sechselauten" },
  { "region": "CH", "date": "2020-12-24", "name": "Office closed", "recurringAnnually": true }
]
```

- `region` is an ISO 3166-2 code. A country code applies nationwide; a subdivision code applies to that region only. Entries for regions nobody on the team is in are ignored on sync.
- `recurringAnnually` ignores the year in `date` and applies its month/day to every year synced, which is what company closures usually are. (Feb 29 only lands on leap years.)
- The file is re-read on every sync, so edits take effect on the next one without a restart. A missing or malformed file is reported as a failed country on the admin page and logged — it never silently syncs nothing.

Rows created this way are labelled `File` in the API and the UI, and stamped `file-sync` in the audit columns, so it stays visible which system put a holiday there.

### Adding an assignable color

Edit the `PersonColorPalette` array in `src/Api/appsettings.json` — each entry must be a 6-digit hex color (`#RRGGBB`). This is the palette offered when someone picks their calendar color. A color already claimed by someone else shows as taken until they change it or delete their profile.

---

### Importing people in bulk

Onboarding a whole team one person at a time gets old around the tenth. `POST /api/admin/people/import` takes a CSV body and creates a person per row:

```bash
curl -X POST https://leave.acme.example/api/admin/people/import \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: text/csv" \
  --data-binary @people.csv
```

```csv
username,firstName,lastName,email,homeCity
jdoe,Jane,Doe,jane@acme.example,ES-MD
asmith,Alan,Smith,alan@acme.example,CH-ZH
```

**Only `username` is required**, and it has to match whatever `Auth:ClaimTypes:UserName` will carry for that person, since that is what links a sign-in to their record. Everything else is optional:

| Column | Notes |
| ------------- | ---------------------------------------------------------------------------- |
| `username` | Required. Must match the sign-in identity. |
| `firstName`, `lastName` | Up to 100 characters each. |
| `displayName` | Defaults to "First Last", or the username when neither is given. |
| `email` | Up to 255 characters. |
| `homeCity` | ISO 3166-2, e.g. `ES-MD` or just `ES`. Blank is fine. Importing someone into a region new to the team pulls its holidays in. |
| `color` | Assigned from `PersonColorPalette` when blank, which is the usual case. |

Header names are matched loosely — case, spaces and underscores are ignored, so `Home City`, `home_city` and `homeCity` are the same column — and column order doesn't matter. Quoted fields, embedded commas and a UTF-8 BOM are all handled, so a spreadsheet export works as-is.

**Existing usernames are skipped, never updated.** That makes re-running the same file safe: fix what was reported and run it again, and the people who already made it through come back as skipped rather than duplicated. Editing an existing person stays a job for `/admin`.

Valid rows are imported even when others fail, and the response says exactly what happened, by line number:

```json
{
  "created": ["jdoe", "asmith"],
  "skipped": [{ "row": 5, "username": "ok", "reason": "Already imported, or already exists in the app." }],
  "failed": [{ "row": 6, "username": "broken", "reason": "The field HomeCity must match the regular expression ..." }]
}
```

> **Palette size caps your headcount.** Each person's calendar color has to be unique, so the app holds at most as many people as `PersonColorPalette` has colors — 32 as shipped. An import that runs out says so, per row. Add more colors before importing a larger team, see [Adding an assignable color](#adding-an-assignable-color).

### Access levels

Most of the app is available to anyone signed in. A single elevated permission gates the admin area:

| Who                                                                          | Can do                                                                                                                  |
| ---------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Anyone signed in                                                             | View the calendar, manage their own profile and absences, add/edit/delete bank holidays                                 |
| Holder of any `Auth:AdminPermissions` value (`src/Api/appsettings.json`) | Everything above, plus `/admin`: manage any person's profile, [import people in bulk](#importing-people-in-bulk), trigger a bank-holiday sync, view team-wide absence stats |

Grant the admin permission to a user in your OIDC provider's dashboard. Locally, the dev bypass grants whatever is listed in `Auth:DevUser:Permissions` (`src/Api/appsettings.Development.json`), which already includes the admin permission — so the admin area is reachable without an admin-permissioned account.

**Matching what your provider actually emits.** Both of these are lists, because providers rarely hand you one tidy permission string:

```json
"Auth": {
  "AdminPermissions": [ "8f4c2f1a-0000-4c1e-9a3e-1c2d3e4f5a6b", "leave-admin" ],
  "ClaimTypes": { "Permissions": [ "roles", "groups" ] }
}
```

- `ClaimTypes:Permissions` names the claims to look in, searched in order. Entra ID, for instance, puts app roles in `roles` and group memberships in `groups`; listing both lets either grant admin.
- `AdminPermissions` lists the values that count. Holding **any one** is enough, so a group id and a friendlier role name can coexist while you migrate between them.
- Claim names are matched exactly (JWT claim names are case-sensitive), values case-insensitively — group ids are often GUIDs whose casing differs between a provider's dashboard and its tokens.

The API's authorization policy and the frontend's admin nav read the same two settings, so they cannot disagree.

**Adding an endpoint.** Authorization is closed by default: a fallback policy requires an authenticated user, so a new route is protected without doing anything. Admin-only routes ask for the `AuthOptions.AdminPolicy` policy (`.RequireAuthorization(AuthOptions.AdminPolicy)`), and the few genuinely public ones say so explicitly with `.AllowAnonymous()` — currently the SPA shell, `/api/config/bootstrap`, and `/healthchecks`.

> Overriding a list through environment variables is index-based: `Auth__ClaimTypes__Permissions__0=roles`, `__1=groups`. Indexes you don't set keep whatever `appsettings.json` had at that position, so replacing a longer list with a shorter one means blanking the leftovers (`Auth__AdminPermissions__1=`). Blank entries are dropped rather than matched.

### Reacting to absences

The app can POST an event to a URL of your choosing whenever an absence changes, so it can feed whatever you already run — a Slack or Teams message, an email, a ticket, an HR export. There are deliberately no per-vendor connectors: there is one hook, and the glue on your side is usually a few lines.

```bash
Webhook__Url=https://hooks.example.internal/foo-calendar
Webhook__Secret=a-long-random-string
```

Three events are sent: `absence.created`, `absence.updated` and `absence.deleted`. Each one carries the absence in full, so a receiver rarely needs to call back:

```json
{
  "id": "f3428de8-3643-4eee-81d7-632ed1f1c695",
  "event": "absence.created",
  "occurredAt": "2026-05-01T09:00:00.000Z",
  "actor": "jdoe",
  "absence": {
    "id": 7,
    "personId": 3,
    "personName": "Jane Doe",
    "startDate": "2026-05-04",
    "endDate": "2026-05-08",
    "type": "Vacation",
    "note": "Trip"
  }
}
```

For a deletion, `absence` describes the absence as it was.

**Headers**

| Header | Purpose |
| ------------------------- | ------------------------------------------------------------------ |
| `X-FooCalendar-Event` | The event name, for routing without parsing the body. |
| `X-FooCalendar-Delivery` | Unique per delivery, and repeated in `id`, for deduplication. |
| `X-FooCalendar-Signature` | `sha256=<hex>`, HMAC-SHA256 of the raw body keyed with `Webhook:Secret`. Absent when no secret is set. |

Verify the signature over the **raw bytes you received**, before parsing:

```python
expected = "sha256=" + hmac.new(secret, raw_body, hashlib.sha256).hexdigest()
if not hmac.compare_digest(expected, request.headers["X-FooCalendar-Signature"]):
    abort(401)
```

Without a secret, anyone who learns the URL can post convincing events at it. Set one.

**Delivery is best effort, on purpose.** Events are queued in memory and delivered by a background loop, so a receiver that is slow, down or misconfigured can never slow down or fail the request that produced the event. The consequences are worth being explicit about:

- A failed delivery is retried `Webhook:MaxAttempts` times (3 by default, backing off 2s then 4s) and then dropped with an error in the log.
- A restart drops anything still queued, and a receiver that stays down long enough will have events dropped from a full queue.
- Events are delivered in order, one at a time.

So treat these as notifications, not as a ledger. Anything that must not miss a change should reconcile against `GET /api/absences` rather than counting on the hook.

Note that `note` is free text a person typed, and it is included in the payload. If that is sensitive in your organisation, terminate the webhook somewhere you control rather than pointing it at a third-party service.

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
