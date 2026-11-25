# Deployment

Running Foo Calendar for a team: what to configure, how to deploy the container, and what to know when it sits behind a proxy or needs watching.

## Quickstart

Foo Calendar is one container running one process on one port, with its state in one SQLite
file. With Docker and Docker Compose:

1. Clone the repository and copy `.env.example` to `.env`. Every setting is described there.
2. Fill in what has no workable default:
   - `Team__HomeRegion` and `Team__HomeTimeZone`: which public holidays always sync, and
     the time zone every absence's times are shown in ([Regions and holidays](regions-and-holidays.md)).
   - `Auth__Authority`, `Auth__Audience` and `Auth__ClientId`: your OIDC provider (Auth0,
     Okta, Keycloak, Entra ID and similar all work).
   - `Auth__Permissions__*`: the claim values that grant each admin permission
     ([Access control](access-control.md)).
   - `Team__SupportUrl`, `Team__IssueUrl` and, optionally, `Team__SecurityContact`: where
     your users, bug reports and security researchers reach you. They fill in the footer's
     Contact link, the in-app privacy notice and terms, the error page's "Raise an issue"
     link and `/.well-known/security.txt`.
3. `docker compose up -d`. The app is on `http://localhost:8080`, and the
   `foo-calendar-data` volume holds everything it stores: back that volume up.

Before you put personal data in it, read [Data protection](data-protection.md), and fill in
the governing-law section of the in-app terms (`src/Web/src/legal/terms.md`). To try the app
without an identity provider first, see [Deploying](#deploying).

## Configuring a deployment

Everything that differs between one company's deployment and another's is API configuration, read at startup. **The frontend bundle contains no environment-specific values**, so a single build artifact can be deployed anywhere: rebranding it or pointing it at a different identity provider never requires rebuilding the SPA.

The API serves that configuration two ways. Whatever the app needs *before* anyone has signed in — how to sign in, your team's name, and the two links the error pages offer — is **inlined into the SPA shell** the API serves, so reading it costs no round trip and doesn't gate the mount. `GET /api/config/bootstrap` returns the same payload for the Vite dev server's SPA, which has no shell of ours to read it from. Everything that shapes the signed-in app is on `GET /api/config` instead.

| Setting | Purpose |
| ------------------------------ | ------------------------------------------------------------------------- |
| `Team:OrgName` | Your team's name, in the header. Empty falls back to the product's name. |
| `Team:SupportUrl` | Required outside Development, with nothing shipped in its place. The contact the in-app privacy notice and terms name as the operator's, and the footer's Contact link. An absolute URL in any scheme; a `mailto:` contact shows in the notices as its address. |
| `Team:IssueUrl` | The "Raise an issue" link on the error page. |
| `Team:SecurityContact` | Served as `Contact` in `/.well-known/security.txt`. Optional: falls back to `Team:SupportUrl`. |
| `Team:HomeTimeZone` | Required. IANA timezone id, e.g. `Europe/Zurich`. Every time-based absence's times are wall-clock time in this one zone, for every viewer, and are labeled with it. |
| `Auth:Authority` | OIDC issuer. Used by the API to validate tokens and by the SPA to log in. An absolute `https` URL; `http` is accepted only in Development. |
| `Auth:ExtraOrigins` | Optional list. Origins besides `Auth:Authority`'s that the SPA fetches from to sign in, for a provider whose token endpoint is on another host. See [Content-Security-Policy and the identity provider](#content-security-policy-and-the-identity-provider). |
| `Auth:ClientId` | The SPA's OIDC client id. Frontend-only, but required at startup. |
| `Auth:Audience` | Which API the access token is issued for. |
| `Auth:Scope` | Scopes the SPA requests. Keep `offline_access` (the default includes it) where your provider needs it to issue refresh tokens, which is how a session is renewed. |
| `Auth:RedirectUri` | Where the IdP returns after login. Relative by default, so any hostname works. |
| `Auth:PostLogoutRedirectUri` | Where the IdP returns after sign-out. `/` by default, resolved like `Auth:RedirectUri`. Register it with your provider as an allowed post-logout redirect URI, or it will refuse to send the browser back. |
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

The same applies to the rest of the `Team` section (`Team:HomeRegion` is covered in [Regions and holidays](regions-and-holidays.md)), and to `Telemetry`, see [Observability](#observability).

To change the look rather than the wording, edit the design tokens at the top of `src/Web/src/styles/theme.css` — that one file drives the whole UI, and it documents the contrast rules to keep if you re-skin. Unlike the settings above, a theme change is compiled into the bundle, so it does mean a rebuild. That split is the rule, not an accident: anything Tailwind has to bake into the CSS is build-time, and everything a deployer might reasonably change without a Node toolchain is runtime.

What is *neither* is the product's own name and mark. Those are constants in `ProductInfo.cs` and `lib/productInfo.ts`, with no setting that changes them — your name goes in the header, the product's stays in the footer. See [TRADEMARK.md](../TRADEMARK.md).

## Deploying

The `Dockerfile` builds the whole thing — Vite compiles the SPA, `dotnet publish` packages the API, and the result is one image running one process on one port. There is no separate web server to deploy or configure. `compose.yaml` runs that image, as in the [Quickstart](#quickstart), and keeps the SQLite database in the `foo-calendar-data` volume. **That volume is the entire application state** — back it up and you have backed up everything.

To try the app before setting up an identity provider, run it in the Development environment, where authentication is off (`Auth:Provider=None`) and you are signed in as the `Auth:DevUser` identity, holding every permission:

```bash
docker build -t foo-calendar:1.0.0 .
docker run --rm -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Development foo-calendar:1.0.0
```

Nothing persists there — no volume is mounted, and the database goes away with the container. Uncommenting `ASPNETCORE_ENVIRONMENT=Development` in `.env` does the same through compose, keeping the volume. Either way the API refuses to start with `Auth:Provider=None` outside Development, so this can't be deployed by accident.

If the container restarts in a loop, read the logs first: configuration is validated at startup, so a missing `Auth__Authority` or `Auth__ClientId` shows up there as a plain `must be configured` message rather than as a broken page later.

**Notes for other environments**

- The container listens on `8080` and runs as the non-root `app` user from the .NET base image.
- The image includes a `HEALTHCHECK` against `/healthchecks`. That endpoint is anonymous and runs every check, but reports only the overall status (`{"status":"Healthy"}`) — per-check detail, including exception messages, is at `/healthchecks/detail` and requires the `AuditRead` permission. Kubernetes and other platforms that probe over HTTP themselves don't need the `curl` that check relies on, and can drop that layer from the runtime stage.
- A **named volume** picks up the right ownership automatically. A **bind mount** does not: `chown 1654 /your/host/path` first, or migrations will fail on first start with a read-only database error.
- Configure everything through environment variables, as `.env.example` shows. Nothing needs to be baked into the image, which is why the same image works for every deployment.
- Migrations are applied automatically at startup, so upgrading is a matter of pulling a newer image and restarting. This is also why more than one replica is not yet safe — see [Scaling](#scaling).

### Behind a reverse proxy

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

**Leave the app's security headers alone.** The app sets its own `Content-Security-Policy` on the page it serves, `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer` on every response ([0015](decisions/0015-strict-content-security-policy.md)). A proxy must pass them through unchanged: one that strips the policy, or replaces it with a site-wide one of its own, either drops the protection or breaks the app. The policy allows the page's one inline script by a hash computed at startup from this deployment's configuration, so no static policy written at the proxy can match it. Adding headers the app does not set, such as HSTS, is the proxy's job and is fine.

### Content-Security-Policy and the identity provider

The page may connect to its own origin, to **the origin of `Auth:Authority`**, and to any origin listed in **`Auth:ExtraOrigins`**; it may submit forms only to its own origin and `Auth:Authority`'s ([0017](decisions/0017-csp-and-budget-adjustments.md)). What the sign-in library fetches must be on one of those: the discovery document and the token endpoint, which also serves refresh. Signing in and out are navigations to the provider, which the policy does not restrict. Most providers serve everything from the issuer's own host (Keycloak, Entra ID, Okta, Auth0 including custom domains, Authentik), and need nothing more.

A provider whose discovery document puts the token endpoint on another host needs that host's origin in `Auth:ExtraOrigins`; without it the browser refuses the request, and signing in fails with a `connect-src` violation in the console. **AWS Cognito** is the common case: the issuer is the user pool, and the token endpoint is on the pool's hosted domain:

```bash
Auth__Authority=https://cognito-idp.eu-west-1.amazonaws.com/eu-west-1_AbCdEfGhI
Auth__ExtraOrigins__0=https://your-domain.auth.eu-west-1.amazoncognito.com
```

Each entry is an origin only: `https`, a host and an optional port, with no path, query or fragment (`http` is accepted only in Development, as for `Auth:Authority`). Anything else stops the app at startup with a message naming the setting. The origins are deliberately not read from the discovery document, so the policy never depends on the network or on what a remote document says. A host outside ASCII is written into the policy in its punycode form.

**Sessions are renewed with refresh tokens, never in a hidden frame.** The page is served with `frame-ancestors 'none'`, so a renewal frame could never load back into it, and the policy allows no frames to the provider. When an access token expires, the app asks the token endpoint for a new one with the refresh token the provider issued at sign-in. Configure the provider and the SPA's client to issue refresh tokens to a browser client (often called refresh token rotation, or the `offline_access` scope, which `Auth:Scope` requests by default). Without one, a member is sent back through sign-in when their access token expires, and returns to the page they were on.

The API reference, when `ApiDocs:Enabled`, is served with a policy of its own, looser where Scalar needs it (inline styles) and on that page only. It allows no `eval`: Scalar's bundle checks for it on every load, is refused, and works without it.

### Scaling

The app is designed to run as a single instance, and two things assume it:

- **Migrations run on startup.** Several replicas starting at once would race each other applying the same migration.
- **The bank-holiday sync is in-process.** Every replica would run its own sync against the holiday source.

SQLite is not the blocker here; both would need addressing before a second replica, whatever the database. For the size of team this tool is for, one instance is the intended shape.

## Observability

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

### The app's own spans and metrics

Besides the ASP.NET Core, HTTP client and runtime instrumentation, the app traces and measures the two jobs that run in the background, where no request would show them failing. Both come from the activity source and meter named `FooCalendar`. Their attributes carry codes, counts and durations only, never a username, a name or a note.

| Span | Attributes |
| --- | --- |
| `holiday_sync`, one per run, i.e. one year (the scheduled sync runs two, this year and next; a hand-triggered sync and a new region on a profile run their own) | `foo_calendar.holiday_sync.year`, `.region_count`, `.failed_country_count`; `.outcome` `cancelled` on a run stopped by shutdown or by its caller going away |
| `holiday_sync.country`, a child of the run per country asked | `foo_calendar.holiday_sync.country`, `.outcome` (`synced`, `failed` or `cancelled`); a failed one has an error status and `error.type`, the exception's type. A cancelled one is not an error and not a failed country |
| `retention_sweep`, one per sweep | `foo_calendar.retention.audit_entries_deleted`, `.absences_deleted`; a failed sweep has an error status and `error.type` instead |

| Metric | Type | Unit | Attributes |
| --- | --- | --- | --- |
| `foo_calendar.holiday_sync.runs` | counter, completed runs only | `{run}` | |
| `foo_calendar.holiday_sync.failed_countries` | counter | `{country}` | `foo_calendar.holiday_sync.country` |
| `foo_calendar.holiday_sync.duration` | histogram of completed runs, buckets from 0.05 s to 300 s | `s` | |
| `foo_calendar.retention.deleted_rows` | counter, recorded every sweep, zero included | `{row}` | `foo_calendar.retention.table` (`audit_entries` or `absences`) |
| `foo_calendar.retention.failed_sweeps` | counter | `{sweep}` | |

Worth an alert: `failed_countries` rising (the holiday source is down or refusing a country; the log has the exception), `runs` not rising for longer than `Holidays:SyncInterval` (the sync stopped running), and `failed_sweeps` rising (data is being kept past its retention period; the log has the exception).
