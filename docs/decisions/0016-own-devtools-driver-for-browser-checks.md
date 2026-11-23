# 16. Browser checks run on the repo's own DevTools driver

- Status: Accepted (implementation in [change spec 0004](../specs/0004-headers-telemetry-performance-e2e.md))
- Date: 2026-11-23

## Context

Two things were checked only by hand: whether the main flows work in a real browser, and
whether the calendar stays fast as the team grows ([0010](0010-windowed-calendar-grid.md)).
`tools/perf/harness.mjs` already drives `chrome-headless-shell` over the DevTools protocol
with no dependencies, and it is how the grid's numbers were measured.

## Decision

- **End-to-end tests** are a small dependency-free suite in `tools/e2e/` on a DevTools
  driver shared with the perf harness. They run in CI against the production container and
  fail on any Content-Security-Policy violation ([0015](0015-strict-content-security-policy.md)).
  Test names carry the requirement IDs they verify, like the other suites.
- **Performance** fails CI on two budgets at 1000 people:
  - a DOM-node budget, which is deterministic and catches a return to rendering per cell;
  - each interaction's longest main-thread block staying within three times its recorded
    baseline, loose enough for CI noise and tight enough to catch work that grows with the
    team.

## Alternatives considered

- **Playwright.** Offered on 2026-11-23 and declined: the standard tool, with auto-waiting,
  traces and a runner, but one more dependency tree and a browser download to keep patched,
  for a handful of flows the existing driver can run.
- **No end-to-end tests.** Declined: sign-out, onboarding and the CSP can only be proven in
  a browser.
- **A DOM budget only.** Declined: never flaky, but blind to a regression that keeps the DOM
  small and gets slower.
- **No performance check in CI.** Declined: ADR 0010's numbers would only be as good as the
  last time someone remembered to run the harness.

## Consequences

- The driver is code this repo maintains: waiting, timeouts and failure artifacts are ours to
  get right.
- Refreshing the performance baseline is a deliberate commit to `tools/perf/baseline.json`,
  with the measurement behind it.
- A timing failure in CI can still be noise; a rerun distinguishes noise from a regression,
  and the DOM budget never needs one.
