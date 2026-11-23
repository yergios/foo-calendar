# 0004. Headers, telemetry, performance and end-to-end checks

- Status: Done
- Date: 2026-11-23
- Decided: [ADR 0015](../decisions/0015-strict-content-security-policy.md) (strict CSP),
  [ADR 0016](../decisions/0016-own-devtools-driver-for-browser-checks.md) (end-to-end and
  performance checks on the repo's own DevTools driver, with a DOM budget and loose timing
  budget)
- Requirements added or changed: SEC-1 to SEC-4, OBS-1 to OBS-3, PERF-1, PERF-2; UI-4 and
  UI-10 move from `[review]` to `[test]`; TRC-2 also reads end-to-end tests (see
  [SPEC.md](../../SPEC.md))

## Goal

- Close the threat model's "no security headers" gap.
- Make the background jobs observable.
- Turn the two things checked by hand today into CI checks: the grid's performance, and the
  main flows working in a real browser.

## Non-goals

- New dependencies. The checks reuse the DevTools-protocol code in `tools/perf`. Telemetry
  tests use `System.Diagnostics`' `ActivityListener` and `MeterListener`, not an exporter
  package.
- HSTS and TLS. TLS is terminated in front of the app; HSTS is that layer's job, as the
  threat model's scope says.

## Changes

### A. Response headers (SEC-1, SEC-2, SEC-4)

- Middleware adds to every response:
  - `X-Content-Type-Options: nosniff`;
  - a `Referrer-Policy` that sends no path or query cross-origin, for example `no-referrer`
    or `strict-origin-when-cross-origin`.
- The SPA shell gets its Content-Security-Policy, built once at startup in
  `SpaShell.Prepare`:
  - `script-src` is `'self'` plus the SHA-256 of the inlined bootstrap script, so the policy
    and the shell cannot disagree;
  - `connect-src` and `frame-src` are `'self'` plus `Auth:Authority`'s origin, which silent
    sign-in and the token endpoint need;
  - `default-src 'self'`, `object-src 'none'`, `base-uri 'self'`, and `form-action` for the
    app and the provider;
  - `frame-ancestors 'none'`;
  - `img-src 'self' data:` if the favicon or the theme needs it.
- Styles: start from `style-src 'self'`. If the end-to-end run (D) shows Vue or a component
  needs inline styles, allow `'unsafe-inline'` for styles only, with the reason in a comment.
  Scripts are never relaxed.
- The API reference (`/scalar`, when enabled) gets a policy of its own allowing what Scalar
  needs, stated in the code; it must not weaken the shell's.
- Integration tests cover:
  - the shell's header, including that the hash matches the inlined script for a given
    config;
  - nosniff and Referrer-Policy on the shell, an API route and a static file.

### B. Telemetry for the background jobs (OBS-1, OBS-2, OBS-3)

- One `ActivitySource` and one `Meter` for the app, registered with OpenTelemetry in
  `Program.cs` alongside the existing instrumentation.
- Holiday sync:
  - a span per run (year, number of regions) and a child span per country (country code,
    outcome);
  - counters of runs and failed countries, and a duration histogram.
- Retention sweep: a span per run, and a counter of deleted rows tagged by table.
- Attributes carry codes, counts and durations only, never a username, a name or a note
  (OBS-3).
- Integration tests listen with `ActivityListener` and `MeterListener` and assert the spans,
  attributes and measurements for a successful sync, a failed country, and a sweep that
  deletes rows.

### C. One DevTools driver, two uses

- Move the DevTools-protocol client out of `tools/perf/harness.mjs` into a shared module
  (`tools/browser/` or similar): launching `chrome-headless-shell`, the websocket session,
  evaluate, navigation and waiting, console and exception capture.
- The harness keeps its behaviour and output.
- The driver captures CSP violations (`securitypolicyviolation` events and console reports)
  for the end-to-end suite to fail on.

### D. End-to-end suite (UI-4, UI-10, AUTH-8, UI-9, SEC-3, SEC-4)

- `tools/e2e/`, dependency-free Node on the shared driver. Each test's name carries its
  requirement IDs in brackets, the same convention as Vitest's (TRC-2), and
  `scripts/check-spec-trace.mjs` also reads these files.
- Flows, against the production container in Development (dev identity, holidays off):
  - **Onboarding:** the first visit creates a profile.
  - **UI-4:** add an absence by clicking an empty day in your own row, edit it, delete it.
  - **AUTH-8, UI-9:** sign out, and afterwards the app's storage prefix holds no key.
  - **UI-10:** with every permission the admin entry is shown. Against a second instance
    with `Auth:DevUser:GrantAllPermissions=false` and no permissions it is not, and
    `/admin` routes to the 403 page.
  - **SEC-3:** no CSP violation in any flow.
  - **SEC-4:** with `ApiDocs:Enabled`, the API reference loads with no violation under its
    own policy.
- On failure, save a screenshot, the console and the violations as CI artifacts.
- In `SPEC.md`, change UI-4 and UI-10 to `[test]` in the commit that adds their flows.

### E. Performance budget in CI (PERF-1, PERF-2)

- `tools/perf/baseline.json` holds the "after, 1000 people" column of `tools/perf/README.md`
  (longest block per interaction, and DOM nodes). The README table stays, and says the JSON
  is what CI reads.
- `harness.mjs` gains a machine-readable result and a budget mode, which fails when:
  - the DOM exceeds 5,000 nodes after any interaction (PERF-1);
  - any interaction's longest block exceeds three times its baseline (PERF-2).
- CI runs it at 1000 synthetic people against the container. It uses a `chrome-headless-shell`
  pinned to a version from Chrome for Testing, downloaded and cached in the workflow. The
  harness results are uploaded as an artifact either way.

### F. Documentation

- `docs/threat-model.md`: the "no security headers" gap is closed; describe the policy and
  what remains (styles, if relaxed).
- `docs/deployment.md` and `docs/architecture.md`:
  - a reverse proxy must not strip or override the app's CSP;
  - the new spans and metrics, and their names;
  - the CI checks.
- `tools/perf/README.md`: the budget, and how to refresh the baseline deliberately.

## Order of work

Each step is its own commit and leaves CI green; gap-list entries go in the commits that add
their tests.

1. A, with `style-src 'self'` provisionally.
2. B.
3. C, keeping the harness's output identical.
4. D, adjusting A's style policy if the suite proves it necessary.
5. E.
6. F, then `SPEC.md`'s pending list and this spec's status.

## Done when

- `curl -sI` on the shell shows the CSP with the bootstrap hash, nosniff and the
  Referrer-Policy; the integration tests assert the same.
- The end-to-end suite passes in CI with zero CSP violations, and fails when the bootstrap
  hash is wrong (checked by hand once, stated in the commit).
- The budget check passes in CI, and fails when the grid renders every row (checked by hand
  once, for example by disabling the windowing, and stated in the commit).
- Telemetry tests pass, and `docs/architecture.md` lists the spans and metrics.
- `tests/spec-coverage-gaps.txt` is empty of every entry naming 0004; `node
  scripts/check-spec-trace.mjs` passes; 0 build warnings; every test suite passes; CI is
  green on `master`.

## Open questions

None.
