# Foo Calendar

Internal tool for coordinating the team. Tracking vacations, appointments, and other absences on a shared, year-at-a-glance calendar with automatic public holiday syncing per region.

## Documentation

See [help.md](src/Web/src/docs/help.md) for usage documentation and FAQ.

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

The project is a single deployable unit: a .NET 10 Minimal API that serves both the backend and the Vue 3 frontend from the same process.

```
src/
├── Api/          .NET 10 Minimal API — people, absences, bank holidays, admin, auth
└── Web/          Vue 3 + TypeScript SPA — built into Api/wwwroot at publish time

tests/
└── UnitTests/    xUnit tests for the API's services, DTOs, and configuration
```

The frontend is compiled by Vite and placed in `src/Api/wwwroot`. The .NET app serves it as static files. In production there is one process, one port, one deployment artifact — the `Dockerfile` at the repository root builds exactly that, see [Deploying](#deploying).

**Key technologies**

- .NET 10 Minimal API with EF Core + SQLite
- Public holidays from a configurable source — the [Nager.Date](https://date.nager.at/) API by default, or a JSON file, or nothing at all — synced on a background job and on demand from the admin page, see [Where holidays come from](#where-holidays-come-from)
- Generic OIDC (works with Auth0, Okta, Keycloak, Azure AD, etc.) for authentication, with per-permission authorization on top — or no authentication at all, for a trusted internal network, see [Running without an identity provider](#running-without-an-identity-provider)
- Vue 3 + TypeScript + Pinia + Tailwind CSS for the frontend
- OpenAPI with a [Scalar](https://scalar.com/) reference UI, on in development and opt-in elsewhere, see [Browsing the API](#browsing-the-api)
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

1. At `src/Api/appsettings.json`, set values for `ConnectionStrings:FooCalendarDb` and the `Auth` section (`Authority`, `Audience`, `ClientId`, `Permissions`) — these come from whichever OIDC provider you use (Auth0, Okta, Keycloak, Azure AD, ...).
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

The API serves that configuration two ways. Whatever the app needs *before* anyone has signed in — how to sign in, your team's name, and the two links the error pages offer — is **inlined into the SPA shell** the API serves, so reading it costs no round trip and doesn't gate the mount. `GET /api/config/bootstrap` returns the same payload for the Vite dev server's SPA, which has no shell of ours to read it from. Everything that shapes the signed-in app is on `GET /api/config` instead.

| Setting | Purpose |
| ------------------------------ | ------------------------------------------------------------------------- |
| `Team:OrgName` | Your team's name, in the header. Empty falls back to the product's name. |
| `Team:SupportUrl` | "Contact support" links, including inside the help doc. Any scheme. |
| `Team:IssueUrl` | "Raise an issue" links on the error page and in the footer. |
| `Auth:Authority` | OIDC issuer. Used by the API to validate tokens and by the SPA to log in. |
| `Auth:ClientId` | The SPA's OIDC client id. Frontend-only, but required at startup. |
| `Auth:Audience` | Which API the access token is issued for. |
| `Auth:Scope` | Scopes the SPA requests. |
| `Auth:RedirectUri` | Where the IdP returns after login. Relative by default, so any hostname works. |
| `Auth:Permissions` | Claim values granting each permission, on both sides. Any one of them grants it. |
| `Auth:ClaimTypes:*` | Which claims carry identity and permissions, on both sides. |

Every one of these is overridable per environment without touching `appsettings.json`, using the standard .NET double-underscore environment variables — which is usually how you'd configure a container:

```bash
Team__OrgName="Acme Engineering" \
Team__SupportUrl="mailto:it@acme.example" \
Auth__Authority="https://acme.okta.com" \
Auth__ClientId="0oa1b2c3" \
Auth__Audience="https://leave.acme.example/api" \
Auth__ClaimTypes__Permissions__0="roles" \
ConnectionStrings__FooCalendarDb="Data Source=/var/lib/foo-calendar/app.db" \
dotnet run --project src/Api
```

The same applies to the rest of the `Team` section below, and to `Telemetry`.

To change the look rather than the wording, edit the design tokens at the top of `src/Web/src/styles/theme.css` — that one file drives the whole UI, and it documents the contrast rules to keep if you re-skin. Unlike the settings above, a theme change is compiled into the bundle, so it does mean a rebuild. That split is the rule, not an accident: anything Tailwind has to bake into the CSS is build-time, and everything a deployer might reasonably change without a Node toolchain is runtime.

What is *neither* is the product's own name and mark. Those are constants in `ProductInfo.cs` and `lib/productInfo.ts`, with no setting that changes them — your name goes in the header, the product's stays in the footer. See [TRADEMARK.md](TRADEMARK.md).

---

### Deploying

The `Dockerfile` builds the whole thing — Vite compiles the SPA, `dotnet publish` packages the API, and the result is one image running one process on one port. There is no separate web server to deploy or configure.

```bash
cp .env.example .env    # fill in at least the Auth__* values
docker compose up -d
```

That serves the app on `http://localhost:8080` and keeps the SQLite database in the `foo-calendar-data` volume. **That volume is the entire application state** — back it up and you have backed up everything.

To try the app before setting up an identity provider, run it in the Development environment, where authentication is off (`Auth:Provider=None`) and you are signed in as the `Auth:DevUser` identity, holding every permission:

```bash
docker build -t foo-calendar:1.0.0 .
docker run --rm -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Development foo-calendar:1.0.0
```

Nothing persists there — no volume is mounted, and the database goes away with the container. Uncommenting `ASPNETCORE_ENVIRONMENT=Development` in `.env` does the same through compose, keeping the volume. Outside Development the API refuses to start with `Auth:Provider=None` unless you also set `Auth:AllowUnauthenticatedDeployment=true`, so it can't be deployed unauthenticated by accident — see [Running without an identity provider](#running-without-an-identity-provider) if that is what you actually want.

If the container restarts in a loop, read the logs first: configuration is validated at startup, so a missing `Auth__Authority` or `Auth__ClientId` shows up there as a plain `must be configured` message rather than as a broken page later.

**Notes for other environments**

- The container listens on `8080` and runs as the non-root `app` user from the .NET base image.
- The image includes a `HEALTHCHECK` against `/healthchecks`. That endpoint is anonymous and runs every check, but reports only the overall status (`{"status":"Healthy"}`) — per-check detail, including exception messages, is at `/healthchecks/detail` and requires the `AuditRead` permission. Kubernetes and other platforms that probe over HTTP themselves don't need the `curl` that check relies on, and can drop that layer from the runtime stage.
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
`NOTICE.txt`), because the Nager API returns subdivision *codes* with no names attached. A
code missing from that table falls back to showing the raw code.

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


### Access levels

Signing in is enough to do everything a member of the team does. Five permissions gate the rest, each configured on its own and each enforced by a policy of the same name:

| Who | Can do |
| --------------------------- | ------------------------------------------------------------------------------------------ |
| Anyone signed in | View the calendar, manage their own profile and absences, add/edit/delete manual bank holidays |
| `PeopleManage` | Manage any person's profile, [import people in bulk](#importing-people-in-bulk) |
| `ReportsRead` | View team-wide absence totals |
| `HolidaysSync` | Trigger a bank-holiday sync by hand |
| `AuditRead` | Read the [audit log](#who-changed-what) and `/healthchecks/detail` |
| `AbsencesApprove` | Approve or reject pending absence requests, see [Absence approvals](#absence-approvals) |

Keeping the calendar's public holidays right is everyone's job, deliberately: a member can add, edit, and delete manual holidays without asking anyone. `HolidaysSync` is separate because it calls out to the holiday source and writes in bulk, not because a holiday is precious.

`AbsencesApprove` is the one exception to "every permission must be configured": it only does anything once `AbsenceApprovals:Enabled` turns the review workflow on, so a deployment that never asked for it is free to leave it unconfigured, see [Absence approvals](#absence-approvals).

`/admin` is a page of sections, each behind its own permission, so it shows whatever the signed-in user is allowed to see of it. Someone holding none of them never sees the nav item at all.

**Configuring who holds what.** `Auth:Permissions` maps each permission to the claim values that grant it. Values are lists because providers rarely hand you one tidy permission string:

```json
"Auth": {
  "Permissions": {
    "PeopleManage": [ "8f4c2f1a-0000-4c1e-9a3e-1c2d3e4f5a6b", "leave-admin" ],
    "ReportsRead": [ "leave-admin", "people-team" ],
    "HolidaysSync": [ "leave-admin", "office-coordinator" ],
    "AuditRead": [ "leave-admin" ]
  },
  "ClaimTypes": { "Permissions": [ "roles", "groups" ] }
}
```

- Every permission except `AbsencesApprove` must name at least one value or the app refuses to start; a name that isn't one of the five is rejected as a typo rather than silently granting nothing.
- Holding **any one** of a permission's values is enough, so a group id and a friendlier role name can coexist while you migrate between them.
- Nothing stops the same value appearing under all of them. That is how you configure one undivided admin role, and it is what the shipped `appsettings.json` does.
- `ClaimTypes:Permissions` names the claims to look in, searched in order. Entra ID, for instance, puts app roles in `roles` and group memberships in `groups`; listing both lets either grant a permission.
- Claim names are matched exactly (JWT claim names are case-sensitive), values case-insensitively — group ids are often GUIDs whose casing differs between a provider's dashboard and its tokens.

Grant the values to a user in your OIDC provider's dashboard. Locally, the dev bypass holds **every** value listed under `Auth:Permissions`, so the whole app is available with no identity provider at all. Set `Auth:DevUser:GrantAllPermissions` to `false` to try it as a plain member instead, or as whatever `Auth:DevUser:Permissions` lists.

The API's policies and the SPA's gating read the same map — served to the browser at startup — so they cannot disagree.

**Adding an endpoint.** Authorization is closed by default: a fallback policy requires an authenticated user, so a new route is protected without doing anything. A route needing a permission asks for it by name (`.RequireAuthorization(Permissions.PeopleManage)`), and the few genuinely public ones say so explicitly with `.AllowAnonymous()` — currently the SPA shell, `/api/config/bootstrap`, `/healthchecks`, and — only on a deployment with no identity provider — `POST`/`DELETE /api/identity`, which set and clear the claimed name and read nothing back.

**Adding a permission.** Add a constant to `Permissions.All` (`src/Api/Configuration/Permissions.cs`) and the matching name to `src/Web/src/types/permission.ts`. `Program.cs` registers a policy per entry, so nothing else needs touching — but every deployment must then configure it, since a permission nobody can hold fails startup rather than sitting there unreachable. Put it in `Permissions.Optional` instead if it gates a feature that is itself off by default (`AbsencesApprove` is the example): it still gets a policy and still has to be one of the recognized names, it just isn't required to be configured.

### Running without an identity provider

Setting up OIDC is the largest piece of work in deploying this, and for a team on a private
network it can be the only thing standing between them and a shared calendar. So it is
optional — deliberately, and with the trade stated plainly rather than buried.

```bash
Auth__Provider=None \
Auth__AllowUnauthenticatedDeployment=true \
dotnet run --project src/Api
```

Both settings are required outside Development. `Provider=None` on its own is also what a
half-finished deployment looks like, so the second flag is how you say you meant it.

**What you get.** Nobody signs in, and there is no sign-in screen. A visitor who hasn't
said who they are is resolved as an identity that owns no profile, which puts them in front
of the same "set up your profile" form a new hire on an OIDC deployment sees — with one
extra field, a username. Claiming that username *is* the sign-in: type one nobody has and
you get a new profile, type one that exists and you get that profile back, which is how
someone who cleared their cookie returns to their own. A standing banner says the
deployment has no sign-in, and doubles as the way to switch identity.

Because claiming a name is the whole interaction, the app never publishes a list of who you
could be — nothing anonymous reads any names back.

**What you give up.** Everything. Anyone who can reach the app can claim any identity,
including one holding every permission. There is no password, no token and nothing to
verify. Permission settings still apply — `Auth:DevUser:GrantAllPermissions=false` runs the
whole deployment as plain members — but they are not a security control here, since anyone
can simply claim a different name.

**When that is fine.** When reaching the app is already the access control: a private LAN,
a VPN, a Tailscale tailnet, a machine that is not routable from the internet. In that
setting this is roughly the security model of a whiteboard in the office, which is what a
team calendar largely is.

**When it is not.** Anything reachable from the internet, anything with contractors or
guests on the same network, anything where "who marked me absent" needs an answer you can
stand behind. Use OIDC. The app logs a `Critical` line naming the environment every time it
starts in this mode, so a deployment that drifted into it is visible in the logs as well as
on screen.

> A middle path worth knowing about: put the app behind a reverse proxy that authenticates
> for you — Authelia, oauth2-proxy, Cloudflare Access, Tailscale — and keep OIDC off. That
> gives real authentication without running an IdP yourself. The app doesn't read a proxy's
> identity header today; if that is the shape you want, say so.

### Who changed what

Rows carry `CreatedBy`/`UpdatedBy` for as long as they exist. The audit log covers the rest, and deletions in particular, where there is no row left to ask:

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "https://leave.acme.example/api/admin/audit?limit=50&offset=0"
```

```json
{
  "entries": [
    {
      "id": 412,
      "occurredAt": "2026-08-13T09:14:02.113",
      "actor": "jdoe",
      "action": "person.deleted",
      "target": "Jane D.",
      "details": "jdoe, ES-MD; 7 absence(s) deleted with them"
    }
  ],
  "total": 413
}
```

It records profile creates, updates, and deletes; bulk imports; deleted, approved, and rejected absences; every hand edit to a manual bank holiday; and hand-triggered syncs. It is append-only — nothing in the app updates or deletes an entry, and a person's entries deliberately outlive their profile. The weekly sync writes nothing: it would bury the actions people actually took, and its rows already say `<source>-sync`.

An absence's note is never recorded, and neither is a reviewer's reason for rejecting one — neither the log nor the admin page will show you either. The same applies to the actions themselves: an entry says an Other absence from the 2nd to the 6th was rejected, not why.

> Entries are deleted after `Retention:AuditEntryDays` (a year by default). That bound is also what keeps a deleted person's name from surviving in the log indefinitely — see [docs/data-protection.md](docs/data-protection.md). Set it to `0` to keep them forever.

> Overriding a list through environment variables is index-based: `Auth__ClaimTypes__Permissions__0=roles`, `__1=groups`, `Auth__Permissions__PeopleManage__0=leave-admin`. Indexes you don't set keep whatever `appsettings.json` had at that position, so replacing a longer list with a shorter one means blanking the leftovers (`Auth__Permissions__PeopleManage__1=`). Blank entries are dropped rather than matched.

### Absence approvals

Off by default: an absence is approved the instant it's created, same as always. Turn it on and every new absence waits for a review instead:

```bash
AbsenceApprovals__Enabled=true
Auth__Permissions__AbsencesApprove__0=leave-admin
```

- A pending absence still shows on the calendar, color-rendered like any other, with a `?` marking it unreviewed. A rejected one disappears from the calendar entirely; the only way back onto it is a new request.
- Whoever holds `AbsencesApprove` sees a "Absence approvals" section on `/admin`, listing every pending request — including its note, the one place someone other than its author is shown it, since deciding whether to approve needs the reason. Approving needs nothing further; rejecting takes an optional note explaining why.
- Editing a still-pending request just edits it. Editing one that was already approved sends it through review again, since what was approved was those specific dates, type, and note. A rejected request can't be edited at all — file a new one.
- The requester is told the outcome twice, independently: an [in-app notification](#getting-notified) always, and `absence.approved` / `absence.rejected` on the [webhook](#reacting-to-absences) if one is configured.

### Getting notified

Whoever a pending request belongs to finds out what happened to it from the bell icon in the header — no email or push setup required. It lists their own recent approvals and rejections (with the reviewer's note, when there is one), badges the unread count, and marks everything read once opened. `GET /api/notifications` and `POST /api/notifications/read` are what it calls; there's nothing to configure.

### Browsing the API

The API describes itself as OpenAPI at `/openapi/v1.json`, and serves [Scalar](https://scalar.com/) over it at `/scalar` — a reference you can read and call the API from, rather than a page of tables.

```bash
ApiDocs__Enabled=true       # off by default outside Development
ApiDocs__RoutePrefix=/scalar
```

It is **on in Development** and off everywhere else, because both routes are anonymous: a browser opening them carries no bearer token, so requiring one would serve a 401 to the only thing that ever opens them. Turning it on publishes the shape of the API — paths, parameters, schemas — to anyone who can reach the app. Never its contents: every route behind it still wants a token, and the fallback policy is unchanged. On an internal deployment whose users script against the API, that trade is usually worth making; think about it on one that faces further than that.

The document names a `Bearer` security scheme, so Scalar's auth panel takes an access token and sends it with every call you make from the page. Get one from your identity provider the way the SPA does, or from the browser devtools of a signed-in session.

Endpoints are grouped by tag — Absences, People, Bank holidays, Regions, Admin, Config, Notifications — which is what the sidebar is built from. A new endpoint inherits its group's tag from the `MapGroup` call, so there is nothing to remember per route.

> Under a sub-path (`ReverseProxy:PathBase`), Scalar resolves the document URL against the page's own path, so `/calendar/scalar` reads `/calendar/openapi/v1.json` without either being configured.

### Reacting to absences

The app can POST an event to a URL of your choosing whenever an absence changes, so it can feed whatever you already run — a Slack or Teams message, an email, a ticket, an HR export. There are deliberately no per-vendor connectors: there is one hook, and the glue on your side is usually a few lines.

```bash
Webhook__Url=https://hooks.example.internal/foo-calendar
Webhook__Secret=a-long-random-string
```

Five events are sent: `absence.created`, `absence.updated`, `absence.deleted`, `absence.approved` and `absence.rejected`. The last two only happen once [absence approvals](#absence-approvals) are turned on. Each one carries the absence in full, so a receiver rarely needs to call back:

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
    "status": "Pending",
    "note": null,
    "reviewNote": null
  }
}
```

For a deletion, `absence` describes the absence as it was. `status` is `Pending` on `absence.created` only when approvals are turned on (`Approved` otherwise, same as before); it's what lets a receiver tell a request that needs a look from one that's already settled.

`note` is free text somebody typed about their own absence, and free text is the one field whose contents nobody can predict. It is **never sent**, and there is no setting that sends it — the field is always in the payload, it simply always arrives `null`. `reviewNote`, a reviewer's reason for a rejection, is treated the same way for the same reason. Everything else here does go out, so point the hook somewhere you have thought about: a chat channel is usually a far wider audience than the people an absence was shared with.

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

So treat these as notifications, not as a ledger. Anything that must not miss a change should reconcile against `GET /api/absences` rather than counting on the hook. That endpoint returns everyone's absences, but a `note` only on the caller's own: it is the one field the calendar keeps between a person and the app.

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
