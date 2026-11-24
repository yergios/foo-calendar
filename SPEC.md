# Foo Calendar specification

What the system must do, stated so each requirement can be checked. This document describes
the **intended** behaviour. Where the code does not match it yet, the difference is listed in
a change spec under [`docs/specs/`](docs/specs/), and that change spec is the work to do.

- **Why** each behaviour is the way it is lives in the [decision records](docs/decisions/README.md).
  This document states the rule and links the record.
- **How** it is built lives in [architecture](docs/architecture.md). This document avoids
  naming files except where a file is itself the contract.

## How to read this

- Every requirement has an ID: an area prefix and a number, e.g. `ABS-4`. IDs are never
  reused. A removed requirement keeps its ID, marked *Retired*.
- "Must" is a requirement. Anything else is explanation.
- Each requirement ends with how it is verified:
  - `[test]`: at least one automated test references the ID (see [TRC](#trc-traceability)).
  - `[ci]`: a CI step fails when it does not hold.
  - `[review]`: checked by reading the code or the running app, because a test would not
    add confidence (e.g. visual rendering).

## Purpose

A self-hostable, year-at-a-glance calendar for a team spread across countries: who is away
when, and which public holidays apply to whom. It coordinates; it does not administer leave.

## Non-goals

- **No approval workflow.** An absence is on the calendar the moment its owner adds it.
  Entitlement, approval and payroll belong to an HR system
  ([0014](docs/decisions/0014-remove-absence-approvals.md)).
- **No outbound messaging**: no email, webhooks or push ([0008](docs/decisions/0008-in-app-notifications-only.md)).
- **No health data** and no field designed to hold any ([0007](docs/decisions/0007-no-special-category-data.md)).
- **No credentials of its own.** Identity comes from an OIDC provider ([0005](docs/decisions/0005-oidc-only-authentication.md)).
- **One instance.** No replication, no horizontal scaling ([0003](docs/decisions/0003-sqlite-single-instance.md)).
- **Not a public service.** It serves the members of one team.

## Glossary

| Term | Meaning |
| --- | --- |
| Member | Anyone the identity provider authenticates for this app |
| Person | A member's profile: username, names, and optionally a home region |
| Region | An ISO 3166-2 code: a country (`ES`) or a subdivision (`ES-MD`) |
| Home region | `Team:HomeRegion`, the team's own region |
| Tracked region | The home region plus every region some person has set |
| Absence | A person's time away: whole-day over a date range, or time-based within one day |
| Manual holiday | A bank holiday a member added by hand |
| Synced holiday | A bank holiday written by the holiday sync from the configured source |
| Permission | One of `PeopleManage`, `ReportsRead`, `HolidaysSync`, `AuditRead` |

---

## AUTH: Authentication and authorization

- **AUTH-1** Outside the Development environment, every request to a protected route must
  carry a bearer token issued by `Auth:Authority` for `Auth:Audience`; anything else is
  answered 401. `[test]`
- **AUTH-2** `Auth:Provider=None` must authenticate every request as `Auth:DevUser`, and the
  app must refuse to start with it in any environment other than Development. While it is
  active, startup must log a warning naming the identity and its permissions. `[test]`
- **AUTH-3** Every route must require an authenticated member unless it is one of exactly
  these: the SPA shell and static assets, `GET /api/config/bootstrap`, `GET /healthchecks`,
  `GET /.well-known/security.txt`, and, only when `ApiDocs:Enabled`, the OpenAPI document and
  the API reference UI. `[test]`
- **AUTH-4** A permission must be granted when any claim whose type is listed in
  `Auth:ClaimTypes:Permissions` carries any value listed for it under `Auth:Permissions`.
  Claim types match exactly; values match case-insensitively. `[test]`
- **AUTH-5** The app must refuse to start when any permission has no claim value configured,
  or when `Auth:Permissions` names a permission that does not exist. The message must name
  what to configure. `[test]`
- **AUTH-6** Each permission-gated route must answer 403 to a member without the
  permission, regardless of what the SPA shows. `[test]`
- **AUTH-7** The SPA must decide what to offer from the same permission map the API
  enforces, read from the bootstrap configuration. `[review]`
- **AUTH-8** A signed-in member must be able to sign out from the app header, on the pages
  that show it (the calendar, profile and admin pages). Signing out must end the OIDC session
  with the identity provider and remove everything the app stored in the browser (see
  [UI-9](#ui-calendar-and-browser)). `[test]`
- **AUTH-9** A signed-in request whose token lacks the claim `Auth:ClaimTypes:UserName`
  names must be answered 500 on every route that needs to know who the member is (their
  profile, their absences, anything that records who acted), and the logged error must name
  that claim type and the setting. It is the deployment's misconfiguration, not the
  caller's ([ERR-2](#err-errors)). Routes that need no member answer as usual. `[test]`

## SEC: Response headers

- **SEC-1** The SPA shell must be served with a Content-Security-Policy that allows scripts
  only from the app's own origin plus the inlined bootstrap script by its SHA-256 hash,
  computed at startup; connections only to the app's own origin, `Auth:Authority`'s origin
  and the origins listed in `Auth:ExtraOrigins`; frames to the app's own origin only, never
  to the identity provider; no plugins; `base-uri` limited to the app and `form-action` to
  the app and `Auth:Authority`'s origin; and `frame-ancestors 'none'`
  ([0017](docs/decisions/0017-csp-and-budget-adjustments.md)). Each `Auth:ExtraOrigins`
  entry must be an absolute `https` origin (scheme, host, optional port; no path, query or
  fragment; `http` only in Development, as for `Auth:Authority`), checked at startup like
  every setting ([CFG-1](#cfg-configuration-and-startup)). Hosts appear in the policy in
  their ASCII (punycode) form. The shell is served only with this policy: `index.html` is
  never served as a file. Any relaxation, such as inline styles, must carry its reason next
  to it in the code. `[test]`
- **SEC-2** Every response must carry `X-Content-Type-Options: nosniff` and a
  `Referrer-Policy` that sends no path or query to other origins. `[test]`
- **SEC-3** The SPA must run under that policy without a single violation in the end-to-end
  flows. `[test]`
- **SEC-4** The API reference, when enabled, must work under a policy of its own, stated in
  the code, which allows no `eval`. Its end-to-end check tolerates exactly one violation,
  the `eval` probe in Scalar's bundle, matched by directive, blocked URI and file. `[test]`

## PPL: People

- **PPL-1** A member without a profile must be able to read the calendar, and must be able
  to create their own profile once. The username comes from the `Auth:ClaimTypes:UserName`
  claim, first and last names from their claims; display name and home region from the
  request. `[test]`
- **PPL-2** Usernames must be unique; creating a second profile for a username is a
  conflict (409). `[test]`
- **PPL-3** Display, first and last names must be at most 100 characters. A home region, when
  given, must be a valid region code ([REG-1](#reg-regions)). `[test]`
- **PPL-4** A member must be able to update their own display name and home region, and to
  delete their own profile. `[test]`
- **PPL-5** A holder of `PeopleManage` must be able to create, update and delete any profile.
  A profile created this way must name a username; a missing or blank one is a 400. `[test]`
- **PPL-6** Deleting a person must delete their absences with them, and must record one audit
  entry stating how many absences went. `[test]`
- **PPL-7** Every member must be able to list all people. The paged list must support search
  by display name (falling back to username), filtering by regions where a country matches
  its own subdivisions, and sorting; page size must be clamped to 1..200, default 25. `[test]`
- **PPL-8** Sorting the people list by an absence-total column must require `ReportsRead`.
  `[test]`
- **PPL-9** A search term must match literally: `%`, `_` and the escape character in it are
  not wildcards. `[test]`

### PPL: Bulk import

- **PPL-10** `POST /api/admin/people/import` (`PeopleManage`) must accept CSV whose first row
  is a header containing a `username` column. Header matching ignores case, spaces and
  underscores, and column order. Quoted fields, embedded commas and a UTF-8 BOM must be
  handled. `[test]`
- **PPL-11** Each row must be validated by the same rules as a single create (PPL-3). A row
  with no username or invalid fields is reported as failed with its line number; a username
  that already exists, in the app or earlier in the file, is reported as skipped and never
  updated. Valid rows are imported even when others fail. `[test]`
- **PPL-12** A missing display name must default to "First Last", or to the username when
  neither name is given. `[test]`
- **PPL-13** An import must record one audit entry with the created, skipped and failed
  counts, and must sync each newly occupied region once, not once per row. `[test]`

## ABS: Absences

- **ABS-1** An absence must be one of `Vacation`, `Appointment`, `Other`. There must be no
  type that names a health reason ([0007](docs/decisions/0007-no-special-category-data.md)). `[test]`
- **ABS-2** A whole-day absence spans `StartDate..EndDate` inclusive, with
  `EndDate >= StartDate`. A time-based absence is a single day with `StartTime < EndTime`.
  `Appointment` is always time-based; `Other` is time-based exactly when a time is supplied;
  `Vacation` is never time-based. Anything else is a 400. `[test]`
- **ABS-3** A note must be at most 500 characters. `[test]`
- **ABS-4** A member must be able to create, update and delete only their own absences;
  acting on someone else's is a 403, and a missing absence is a 404. `[test]`
- **ABS-5** Within one person, a whole-day absence must not share any day with another
  absence of theirs. Any number of time-based absences may share a day with each other.
  Ranges that touch on one day overlap. A violation is a 409. `[test]`
- **ABS-6** The overlap check and the write must be atomic: two concurrent requests must not
  both succeed in creating overlapping absences. `[test]`
- **ABS-7** Every absence is in effect from the moment it is created; there is no pending,
  approved or rejected state. `[test]`
- **ABS-8** `GET /api/absences?from&to` must return every absence overlapping the inclusive
  range, ordered by start date then id. It must answer 400 when `to < from` or when the range
  spans more than 366 days. `[test]`
- **ABS-9** An absence's note must be returned only to its author, on every read path. For
  anyone else the note is absent from the response, not hidden by the client. `[test]`
- **ABS-10** An absence total for a person and year must count whole-day absences only,
  clamped to the year, and only days that are weekdays and not one of that person's own bank
  holidays ([HOL-9](#hol-bank-holidays)). Totals are reported per type for `Vacation` and
  `Other`. `[test]`
- **ABS-11** A member must be able to read their own totals; reading everyone's must require
  `ReportsRead`. A year outside 1900..2999, here or on the people list, is a 400. `[test]`
- **ABS-12** Creating, updating and deleting an absence must each record an audit entry with
  the type and dates, never the note. `[test]`

## HOL: Bank holidays

- **HOL-1** `Holidays:Source` must be one of `Nager`, `File`, `None`. `Nager` requires a base
  URL, `File` requires a path; startup must refuse anything else. `[test]`
- **HOL-2** With a source configured, the sync must run at startup and then every
  `Holidays:SyncInterval` (default 7 days), covering every tracked region for the current
  year and the next. With `None`, no sync runs and a hand-triggered sync answers 409. `[test]`
- **HOL-3** A source must be asked once per country. Its nationwide holidays and the
  holidays of tracked subdivisions are stored; holidays of untracked subdivisions are
  dropped. A failed country must be reported with all its tracked regions, and must not stop
  other countries syncing. `[test]`
- **HOL-4** A sync must never overwrite or delete a manual holiday. A synced holiday that
  already exists is refreshed in name and source label; duplicates within one response are
  stored once. Synced rows are attributed to `<source>-sync`. `[test]`
- **HOL-5** The file source must re-read its file on every sync. `recurringAnnually` entries
  apply their month and day to every synced year. A missing or malformed file is a failed
  sync, never a silent empty one. `[test]`
- **HOL-6** Any member must be able to add, edit and delete manual holidays, only for a
  tracked region or the country of one; anywhere else is a 400. Synced holidays cannot be
  edited or deleted (409). Holiday names must be at most 200 characters. `[test]`
- **HOL-7** Two holidays in one region must not fall on the same day, where a recurring
  holiday occupies its month and day in every year. A collision is a 409, checked atomically
  with the write. `[test]`
- **HOL-8** A recurring holiday must appear on its month and day in every year a query
  touches; 29 February appears only in leap years. `[test]`
- **HOL-9** A person's own bank holidays are their country's nationwide holidays plus their
  exact subdivision's holidays, never a sibling subdivision's. A person with no home region
  has none. The API and the SPA must apply the same rule. `[test]`
- **HOL-10** `GET /api/bank-holidays/catalog?from&to` must return the holidays of every
  tracked region and their countries in the range. It must answer 400 when `to < from` or when
  the range spans more than 366 days. `[test]`
- **HOL-11** A holder of `HolidaysSync` must be able to trigger a sync for a year, for every
  tracked region or for one named tracked region; an untracked region, or a year outside
  1900..2999, is a 400. The response lists synced and failed regions, and the action is
  audited. `[test]`
- **HOL-12** Adding, editing and deleting a manual holiday must each record an audit entry.
  `[test]`

## REG: Regions

- **REG-1** A region code must match `^[A-Z]{2}(-[A-Z0-9]{1,3})?\z` exactly: no surrounding
  whitespace and no trailing newline. `[test]`
- **REG-2** The tracked regions must be the home region plus every distinct home region set
  on a profile. No other configuration adds or removes regions
  ([0009](docs/decisions/0009-regions-follow-the-team.md)). `[test]`
- **REG-3** When a profile's region becomes one not previously tracked, that region and its
  country must be synced for this year and next. A sync failure there must be logged and must
  not fail the profile change. `[test]`
- **REG-4** When the last person leaves a region (by update or deletion), that region's
  holidays must be deleted, manual ones included. A country's nationwide holidays must be
  deleted only when nobody is left in the country or any of its subdivisions. The home
  region and its country are never pruned. `[test]`
- **REG-5** `GET /api/regions/countries` and `GET /api/regions/{country}` must list what the
  holiday source knows, labelled from the bundled CLDR name table, falling back to the code
  when a name is missing. The country code must be validated before it reaches the source.
  `[test]`

## AUD: Audit log

- **AUD-1** The audit log must record, with the acting username, time, action, target and a
  description: profile create, update (only when something changed) and delete; imports;
  absence create, update and delete; manual holiday add, edit and delete; hand-triggered
  syncs. `[test]`
- **AUD-2** No audit entry may contain an absence note. `[test]`
- **AUD-3** The app must offer no way to edit or delete an audit entry other than the
  retention sweep. Entries must survive the deletion of the people they name. `[test]`
- **AUD-4** Reading the log must require `AuditRead`, support search, action and time
  filters and sorting, and clamp the page size to 1..200, default 50. `[test]`
- **AUD-5** Changes the system makes on its own (scheduled syncs, region pruning, the
  retention sweep, cascaded deletes) are not audited; they are logged to the application log
  instead. `[review]`

## PRIV: Data protection and retention

- **PRIV-1** Data must leave the process only to the identity provider (sign-in), the
  holiday source (country codes only), and, when configured, an OTLP collector (traces,
  metrics and logs). No absence note may appear in any log, trace or audit entry. `[review]`
- **PRIV-2** Audit entries older than `Retention:AuditEntryDays` (default 365) and absences
  that ended more than `Retention:AbsenceDays` ago (default 0, meaning never) must be
  deleted by a sweep that runs at startup and then every `Retention:SweepInterval` (default
  1 day). `0` keeps a table forever. `[test]`
- **PRIV-3** The sweep must not stop the app when it fails; the next run retries. `[test]`
- **PRIV-4** The in-app privacy notice, `docs/data-protection.md` and the threat model must
  describe what is stored, shown, cached and sent exactly as this specification does.
  `[review]`

## CFG: Configuration and startup

- **CFG-1** All configuration must be validated before the database is migrated: an invalid
  configuration must stop the process with a message naming the setting, and must leave the
  database untouched. `[test]`
- **CFG-2** Required settings: `Team:HomeRegion` (a region code), `Team:HomeTimeZone` (an
  IANA id), `ConnectionStrings:FooCalendarDb`, and outside Development `Auth:Authority`,
  `Auth:Audience` and `Auth:ClientId`. `Auth:Authority` must be an absolute `https` URL;
  `http` is accepted only in Development. `[test]`
- **CFG-3** Settings of features that no longer exist must stop the app when they ask for
  that feature, rather than being silently ignored: `AbsenceApprovals:Enabled` with any value
  but `false`, and any value under `Auth:Permissions:AbsencesApprove`. The message must say
  the feature was removed. `[test]`
- **CFG-4** Pending migrations must be applied at startup, after validation. `[test]`
- **CFG-5** The SPA shell must have its `<base href>` set to `ReverseProxy:PathBase` and
  carry the bootstrap configuration inline; startup must fail if the shell has no
  `<base href>` while a path base is set. The shell is served `no-store`; files under
  `/assets` are served `immutable`. `[test]`
- **CFG-6** The inlined bootstrap configuration must be JSON with `<`, `>` and `&` escaped,
  so no configuration value can end the script element. `[test]`
- **CFG-7** `/.well-known/security.txt` must give `Team:SecurityContact`, falling back to
  `Team:SupportUrl`, with an `Expires` one year ahead; with neither configured it is a 404.
  `[test]`
- **CFG-8** `/healthchecks` must report only the overall status; per-check detail must
  require `AuditRead`. `[test]`
- **CFG-9** The OpenAPI document and API reference must be served only when
  `ApiDocs:Enabled`, which defaults to on in Development and off elsewhere. `[test]`
- **CFG-10** Forwarded headers must be ignored unless `ReverseProxy:TrustForwardedHeaders`
  is set. `[review]`

## ERR: Errors

- **ERR-1** A request that breaks a domain rule must be answered with problem details: 400
  for invalid input, 403 for acting on what the caller does not own, 404 for what does not
  exist, 409 for a conflict with what is stored, each with the rule's own message. Only
  errors the domain raises on purpose map to these. `[test]`
- **ERR-2** Any other failure must be answered 500 with problem details that carry no
  exception message, type or stack, and must be logged with the exception. Development
  answers the same way; the exception is in the log there too. `[test]`

## UI: Calendar and browser

- **UI-1** The calendar must show one row per person and one column per day of the selected
  year, render only the rows in view, and keep the DOM size independent of team size
  ([0010](docs/decisions/0010-windowed-calendar-grid.md)). `[review]`
- **UI-2** An absence must be drawn as runs of days with identical content. A run is painted
  except on weekends and on that person's own bank holidays (HOL-9). A painted run shows its
  type's letter once. Absences are clamped to the displayed year. `[test]`
- **UI-3** Hovering a cell must list every absence on it, its own or not, with time ranges
  for time-based ones labelled with `Team:HomeTimeZone`. `[test]`
- **UI-4** A member must be able to add an absence from any empty day in their own row, and
  edit or delete their own absences from the calendar. `[test]`
- **UI-5** "Who's off today" must list everyone with an absence covering today or a bank
  holiday of their own today, each person once, time-based absences first, and for whole-day
  absences the first later working day they are back, skipping their own holidays and
  absences that follow directly. `[test]`
- **UI-6** Every displayed time must be labelled with the time zone it is in. `[review]`
- **UI-7** Repeat visits must paint from the browser cache before any request completes,
  then revalidate, re-rendering nothing when the data is unchanged
  ([0011](docs/decisions/0011-client-side-stale-while-revalidate.md)). `[test]`
- **UI-8** A change to a cached shape must change its cache key version, and older versions
  must be swept. `[test]`
- **UI-9** Everything the app itself stores in `localStorage` or `sessionStorage`, user
  interface preferences and the OIDC library's session and sign-in state included, must live
  under the app's key prefix, so sign-out (AUTH-8) can remove all of it and nothing
  belonging to another app on the same origin. Once sign-out starts, the app must write
  nothing there and fetch no token until it has left the page. `[test]`
- **UI-10** Admin sections must be reachable only with their permission: People
  (`PeopleManage` or `ReportsRead`), Bank holidays (`HolidaysSync`), Audit log
  (`AuditRead`). A member with none of them sees no admin entry point. `[test]`

## OBS: Telemetry

- **OBS-1** The holiday sync must emit a trace span per run and per country, recording the
  year, the country and the outcome, and metrics for runs, failed countries and duration,
  the duration bucketed in seconds. A run cancelled by shutdown or by its caller must be
  recorded as cancelled, not as a failed country or an error. `[test]`
- **OBS-2** The retention sweep must emit a trace span per run and a counter of deleted rows
  per table; a failed sweep must mark its span as an error with the exception's type, and
  be counted. `[test]`
- **OBS-3** No span, metric or log attribute the app adds may carry a username, a name or an
  absence note; region and country codes, counts and durations are fine. `[review]`

## PERF: Performance

- **PERF-1** With 1000 people, the calendar's DOM must stay under 5,000 nodes through every
  measured interaction ([0010](docs/decisions/0010-windowed-calendar-grid.md)). `[ci]`
- **PERF-2** With 1000 people, no measured interaction's longest main-thread block may
  exceed the larger of three times its baseline and 16 ms (one frame). The baselines are in
  `tools/perf/baseline.json`, the "after, 1000 people" column of `tools/perf/README.md`.
  The slack absorbs CI noise, and the floor keeps sub-frame interactions from failing on
  noise alone; the check exists to catch a return to work that grows with the team
  ([0017](docs/decisions/0017-csp-and-budget-adjustments.md)). A run that skipped an
  interaction or drew no absences fails rather than passing. `[ci]`

## BLD: Build and quality

- **BLD-1** The .NET build must have zero warnings with every analyzer rule enabled and
  warnings treated as errors, including NuGet audit findings
  ([0013](docs/decisions/0013-strict-static-analysis.md)). `[ci]`
- **BLD-2** The .NET SDK must be pinned by `global.json` to one feature band with patch
  roll-forward, and CI must use that file. `[ci]`
- **BLD-3** The SPA must pass typecheck, lint with zero warnings, format check and its unit
  tests; `pnpm audit` must report no high or critical advisory. `[ci]`
- **BLD-4** The container image must build, start against an empty database, report healthy
  and serve the shell. `[ci]`
- **BLD-5** `Api.csproj` and `package.json` must carry the same version. `[ci]`
- **BLD-6** Nullable reference types must be enabled in every C# project, with no
  suppressions of nullable warnings except at a site that says why. `[ci]`

## AGT: Agent tooling

- **AGT-1** `scripts/check.sh` must run every check CI runs that does not need Docker, the
  end-to-end suite and the performance budget included, stop at the first failure and name
  it. It is the definition of done for a change. `[review]`
- **AGT-2** A Stop hook must run `scripts/check.sh` every time a Claude Code session stops in
  this repository, and block the stop with the failing step when it fails. `[test]`
- **AGT-3** A commit whose staged diff touches absence notes (as `.claude/privacy-paths.txt`
  defines) must not be created by Claude Code until the privacy reviewer has passed that
  exact diff and the message carries `Privacy-Review: passed sha256:<diff hash>` on the line
  just above `Co-authored by AI.`. A commit that does not touch notes is not delayed. `[test]`
- **AGT-4** The diff hash is the SHA-256 of one diff command with fixed options, and is the
  same for a staged diff and for the commit made from it. `[test]`
- **AGT-5** CI must fail when a non-merge commit in a push or a pull request touches notes
  without a trailer whose hash equals its own diff's. `[test]`
- **AGT-6** The privacy reviewer checks a diff against ABS-9, AUD-2, PRIV-1 and ADR 0007 and
  answers with a structured verdict citing file and line. `[review]`
- **AGT-7** `AGENTS.md` must give an agent the commands, the code map, the conventions, the
  spec and decision workflow, and the privacy workflow with its commit trailer. `[review]`
- **AGT-8** Shared Claude Code settings must hold the hooks and pre-allow only commands that
  read or check; nothing that pushes, deletes or writes outside the tree. `[review]`
- **AGT-9** Project skills must exist for performance measurement, releases and database
  migrations, each stating its steps and guards. `[review]`

## TRC: Traceability

- **TRC-1** Every requirement verified by test must be referenced by at least one test, and
  every ID a test references must exist in this document and not be retired. A CI step must
  fail otherwise. `[ci]`
- **TRC-2** A .NET test references a requirement with `[Trait("Spec", "ABS-5")]`; a Vitest
  test puts the ID in brackets in its name, e.g. `it("[UI-2] clamps to the year", ...)`, and
  so does a `node:test` test in `tools/e2e` or under `scripts`. A test may reference several. A commented-out or
  skipped test references nothing. `[ci]`

`scripts/check-spec-trace.mjs` is the CI step. A `[test]` requirement whose test does not
exist yet is listed, with what is missing, in `tests/spec-coverage-gaps.txt`; the step also
fails when a listed requirement gains a reference, so that list only shrinks.

---

## Changing this specification

1. Write the change as a numbered spec in [`docs/specs/`](docs/specs/) (the `/spec` command
   interviews for one): what changes, which IDs it adds, changes or retires, and how it will
   be verified.
2. Update this document in the same commit that makes the code match, so it always describes
   what `master` does once pending change specs are done.
3. A decision with alternatives worth remembering also gets a decision record.

Pending change specs, to be built in this order:

- [0005: Agent tooling](docs/specs/0005-agent-tooling.md)
