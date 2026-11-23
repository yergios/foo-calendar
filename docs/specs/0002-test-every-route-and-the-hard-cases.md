# 0002. Test every route and the hard cases

- Status: Ready to build
- Date: 2026-11-23
- Decided: on 2026-11-23, run roadmap step 4 as three change specs (this one, then
  [0003](0003-nullable-types-and-domain-errors.md), then
  [0004](0004-headers-telemetry-performance-e2e.md)); property-based tests without a new
  dependency
- Requirements covered: every entry left on `tests/spec-coverage-gaps.txt` for this spec,
  plus the AUD-1 hand-triggered sync clause; new tests for ABS-5, ABS-6, ABS-10, HOL-7,
  HOL-8, REG-1, PPL-10 and UI-2

## Goal

Empty the coverage gap list of everything that needs only tests, so that 0003 can change
types and error handling with every requirement guarded. Production code changes only where
a test exposes a defect against [SPEC.md](../../SPEC.md), or to make a rule testable (UI-5).

## Non-goals

- The entries on the gap list that belong to 0003 (ERR-1, ERR-2) and 0004 (SEC-*, OBS-*).
- New dependencies. Everything here is xUnit, Shouldly, NSubstitute, `Mvc.Testing` and
  Vitest, which the repo already has.

## Changes

### A. Every route's access, enumerated (AUTH-3, AUTH-6)

- One table-driven integration test reads every endpoint from the app's `EndpointDataSource`
  and classifies it: anonymous (the AUTH-3 list), member, or a named permission.
  - An endpoint missing from the table fails the test, so adding a route forces a decision.
  - Anonymous routes answer without credentials; every other one answers 401.
  - Each permission route answers 403 to a member holding every other permission, and
    not 403 to one holding it.
- `TestApp` gains a way to run as a member with a chosen set of permissions (Development
  with `Auth:DevUser:GrantAllPermissions=false` and `Auth:DevUser:Permissions`).

### B. Real bearer tokens (AUTH-1)

- Run the app as Production with `Auth:Provider=Oidc`. Replace the provider's metadata with a
  static `OpenIdConnectConfiguration` holding a test RSA key, configured on the JwtBearer
  options in `TestApp`, so no network is used.
- Mint tokens in tests with the token handler JwtBearer already brings in
  (`Microsoft.IdentityModel.JsonWebTokens`).
- Cases: a valid token answers 200. No token, an expired token, a wrong issuer, a wrong
  audience and a token signed by another key each answer 401.

### C. Endpoint behaviour the gap list names

| ID | Tests |
| --- | --- |
| PPL-1 | A member without a profile reads absences and the people list; creates their profile once, with names taken from the claims; a second create is 409 |
| PPL-5 | A `PeopleManage` holder creates, updates and deletes someone else's profile; a member without it gets 403 (covered by A) |
| PPL-8 | Sorting the paged people list by an absence-total column answers 403 without `ReportsRead` and sorts correctly with it |
| ABS-11 | A member reads their own totals; everyone's totals need `ReportsRead` |
| HOL-2 | The sync background service runs once at start and again after `SyncInterval` (use a short interval), covering this year and next; with `Holidays:Source=None` the hand-triggered sync answers 409 |
| HOL-11 | Hand-triggered sync for all regions, for one, and for an untracked one (400); the response lists synced and failed regions; one audit entry is written, which is also AUD-1's last untested clause |
| PRIV-3 | A sweep that throws is logged and the service keeps running and sweeps again on the next tick |
| CFG-5 | The served shell has the rewritten `<base href>` and the inlined config; the shell is `no-store`; a file under `/assets` is `immutable` |
| CFG-6 | A configuration value containing `</script>` is inlined escaped |
| CFG-7 | `security.txt` gives the security contact, falls back to the support URL, has an `Expires` a year ahead, and is 404 with neither |
| CFG-8 | `/healthchecks` returns only the status, anonymously; `/healthchecks/detail` needs `AuditRead` |
| CFG-9 | The OpenAPI document and the API reference exist only with `ApiDocs:Enabled`, which defaults to on in Development and off in Production |

### D. Concurrency (ABS-6, HOL-7)

- Use a temporary SQLite file shared by two `DbContext`s, each with its own connection, as
  in production.
- Race two overlapping absence creates for one person, and two colliding manual holidays,
  with `Task.WhenAll`, many times over.
- Assert that exactly one of each pair succeeds and the other is a conflict. The tests must
  fail when the serializable transaction is removed: check that once by hand and say so in
  the commit message.

### E. Property-based tests, no new dependency (ABS-5, ABS-10, HOL-8, REG-1, PPL-10, UI-2)

- A small seeded generator in each test project's support code (C#) and in a Vitest helper
  (TypeScript): random dates, ranges, absences and region codes from a fixed seed.
  - A failure prints the seed and the generated case.
  - An environment variable picks another seed, so CI is deterministic and a developer can
    explore.
- Properties, each checked against a brute-force model:
  - **ABS-5:** the overlap rule agrees with set intersection of the days covered.
  - **ABS-10:** working-day totals agree with counting day by day.
  - **HOL-8:** recurring occurrences appear exactly once per year touched, and on 29
    February only in leap years.
  - **REG-1:** the region pattern accepts exactly the generated valid codes and rejects
    mutations of them: lower case, whitespace, trailing newline, extra segments.
  - **PPL-10:** CSV written with quoting and parsed back is the same table.
  - **UI-2:** segments partition the year's days, and agree with painting day by day.

### F. "Who's off today" as a testable module (UI-5)

Move the rules out of `WhosOffTodayModal.vue` into `src/Web/src/lib/` without changing them:
who is off, ordering, and the first day back. Test them there with Vitest, including the
year-end and adjacent-absence cases fixed in `62eb83f`. The component becomes a thin view.

## Order of work

Each step is its own commit and leaves CI green; each removes its lines from the gap list.

1. A, with the `TestApp` permission support.
2. B.
3. C, a few rows per commit.
4. D.
5. E.
6. F.

## Done when

- `tests/spec-coverage-gaps.txt` holds only entries naming 0003 or 0004.
- `node scripts/check-spec-trace.mjs` passes. `dotnet build --no-incremental` has 0 warnings
  and `dotnet test` passes, as do the `src/Web` checks.
- Removing the serializable transaction from `AbsenceService.CreateAsync` makes the ABS-6
  test fail (checked by hand, stated in the commit).
- Adding a route with no entry in the access table makes test A fail (checked by hand).
- CI is green on `master`.

## Open questions

None.
