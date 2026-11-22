# Deployment

Running Foo Calendar for a team: what to configure, how to deploy the container, and what to know when it sits behind a proxy or needs watching.

## Configuring a deployment

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

What is *neither* is the product's own name and mark. Those are constants in `ProductInfo.cs` and `lib/productInfo.ts`, with no setting that changes them — your name goes in the header, the product's stays in the footer. See [TRADEMARK.md](../TRADEMARK.md).

## Deploying

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
