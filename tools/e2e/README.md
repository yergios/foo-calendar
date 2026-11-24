# End-to-end tests

`flows.test.mjs` runs the app's main flows in a real browser: onboarding, adding, editing
and deleting an absence from the calendar, what a member with and without permissions is
offered, signing out, the API reference, the shell refusing an injected inline script, and
signing in and out through an OIDC provider on another origin.
Every flow fails on any uncaught exception and on any Content-Security-Policy violation it
does not name as expected; a flow that expects one matches it by what was refused and
where, so it cannot hide another
([0015](../../docs/decisions/0015-strict-content-security-policy.md)). The `[SEC-3]` test
at the end passes only when every flow ran and passed, so it fails when run on its own.
Dependency-free:
`node:test` on the DevTools driver in `tools/browser`, shared with the performance harness
([0016](../../docs/decisions/0016-own-devtools-driver-for-browser-checks.md)).

Each test's name carries the requirement IDs it verifies in brackets, like the Vitest tests,
and `scripts/check-spec-trace.mjs` reads them (TRC-2).

## The stub provider

`oidc-stub.mjs` is a minimal OIDC provider the suite starts itself, on ports 9000 and 9001
(`OIDC_STUB_PORT`, `OIDC_STUB_TOKEN_PORT`), on the loopback addresses only: a discovery
document, an authorize endpoint that redirects straight back with a code for one fixed
member, a token endpoint that checks the code and its PKCE verifier and returns RS256-signed
id and access tokens and a refresh token, the key set it signs with, and an end-session
endpoint that records the call. It has two tenants: `http://localhost:9000`, with everything
on that origin, and `http://localhost:9000/split`, whose token endpoint is on
`http://localhost:9001`, as AWS Cognito's is on its hosted domain. Its signing key is kept
in `$TMPDIR` between runs, because the app caches a provider's keys.

With it, the suite signs in through a real flow and checks that the app's API calls carry
the provider's token and are accepted (AUTH-1), that signing out goes through the
end-session endpoint with the id token as hint (AUTH-8), and that the shell's policy allows
exactly what sign-in needs: with no violation on the provider's own origin, a refused token
request on the second origin unless `Auth:ExtraOrigins` names it, and none when it does
(SEC-1, SEC-3).

The instances that use it run in Development with `Auth:Provider=Oidc`. The stub's origin is
`http`, which `Auth:Authority` accepts only in Development (CFG-2); that and the bearer
handler fetching keys over `http` are all Development changes for them. The dev identity is
off, and tokens are validated as they are in production.

## Run

1. A `chrome-headless-shell`, as for the [performance harness](../perf/README.md#setup).
   CI pins the version, and the SHA-256 of its zip, in `.github/workflows/ci.yml`, which
   says how to move both to a new release.
2. Five instances of the production build in Development, with holidays off: one as it
   starts, one whose dev identity holds no permission, and three that sign in through the
   stub provider. The container is what CI tests. The last three share the host's network,
   so they reach the stub at the same `localhost` URL the browser does, which is also its
   issuer:

   ```
   docker build -t foo-calendar:local .
   dev="-e ASPNETCORE_ENVIRONMENT=Development -e Holidays__Source=None"
   oidc="$dev -e Auth__Provider=Oidc -e Auth__Audience=foo-calendar-e2e -e Auth__ClientId=foo-calendar-e2e-spa"
   docker run -d --rm -p 8080:8080 $dev foo-calendar:local
   docker run -d --rm -p 8081:8080 $dev -e Auth__DevUser__GrantAllPermissions=false foo-calendar:local
   docker run -d --rm --network host -e ASPNETCORE_HTTP_PORTS=8082 $oidc \
     -e Auth__Authority=http://localhost:9000 foo-calendar:local
   docker run -d --rm --network host -e ASPNETCORE_HTTP_PORTS=8083 $oidc \
     -e Auth__Authority=http://localhost:9000/split foo-calendar:local
   docker run -d --rm --network host -e ASPNETCORE_HTTP_PORTS=8084 $oidc \
     -e Auth__Authority=http://localhost:9000/split -e Auth__ExtraOrigins__0=http://localhost:9001 \
     foo-calendar:local
   ```

3. `node --test --test-timeout=120000 tools/e2e/flows.test.mjs`. The timeout is per flow,
   as in CI, so a flow that hangs fails rather than holding the run.

`APP_URL`, `APP_URL_NO_PERMISSIONS`, `APP_URL_OIDC`, `APP_URL_OIDC_SPLIT` and
`APP_URL_OIDC_SPLIT_EXTRA_ORIGIN` override `http://localhost:8080/` to
`http://localhost:8084/`. The flows run in order and build on each other (onboarding creates
the profile the calendar flows use); a rerun against the same database starts over by
deleting that profile first.

A failing flow leaves a screenshot, and the console, violations and exceptions of that
flow alone, in `E2E_OUT` (default `$TMPDIR/foo-calendar-e2e`), which CI uploads as an artifact.
