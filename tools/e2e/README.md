# End-to-end tests

`flows.test.mjs` runs the app's main flows in a real browser: onboarding, adding, editing
and deleting an absence from the calendar, what a member with and without permissions is
offered, signing out, the API reference, and the shell refusing an injected inline script.
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

## Run

1. A `chrome-headless-shell`, as for the [performance harness](../perf/README.md#setup).
   CI pins the version, and the SHA-256 of its zip, in `.github/workflows/ci.yml`, which
   says how to move both to a new release.
2. Two instances of the production build in Development, with holidays off: one as it
   starts, one whose dev identity holds no permission. The container is what CI tests:

   ```
   docker build -t foo-calendar:local .
   docker run -d --rm -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Development \
     -e Holidays__Source=None foo-calendar:local
   docker run -d --rm -p 8081:8080 -e ASPNETCORE_ENVIRONMENT=Development \
     -e Holidays__Source=None -e Auth__DevUser__GrantAllPermissions=false foo-calendar:local
   ```

3. `node --test --test-timeout=120000 tools/e2e/flows.test.mjs`. The timeout is per flow,
   as in CI, so a flow that hangs fails rather than holding the run.

`APP_URL` and `APP_URL_NO_PERMISSIONS` override `http://localhost:8080/` and
`http://localhost:8081/`. The flows run in order and build on each other (onboarding creates
the profile the calendar flows use); a rerun against the same database starts over by
deleting that profile first.

A failing flow leaves a screenshot, and the console, violations and exceptions of that
flow alone, in `E2E_OUT` (default `$TMPDIR/foo-calendar-e2e`), which CI uploads as an artifact.
