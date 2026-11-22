# 0001. Remove approvals and close the review gaps

- Status: Ready to build
- Date: 2026-11-22
- Decided: [ADR 0014](../decisions/0014-remove-absence-approvals.md) (remove approvals); on
  2026-11-22 also: add sign-out, cap read ranges at 366 days, pin the SDK feature band
- Requirements added or changed: AUTH-5, AUTH-8, ABS-7, ABS-8, ABS-9, HOL-10, REG-1, PRIV-2,
  PRIV-4, CFG-1, CFG-3, CFG-4, UI-9, UI-10, BLD-2, TRC-1, TRC-2 (see [SPEC.md](../../SPEC.md))

## Goal

Make the code match [SPEC.md](../../SPEC.md): remove the approval workflow and everything that
exists only for it, close the gaps the 2026-11-22 review of steps 1 and 2 left open, and make
every requirement traceable to its tests.

## Non-goals

- Nullable reference types, typed domain errors, integration tests of every route, security
  headers. They are the next step of the roadmap, not this one; the requirements that need
  them stay on the coverage gap list (change H).
- Any new feature.

## Changes

### A. Remove the approval workflow (ABS-7, ABS-9, AUTH-5, CFG-3, UI-10, PRIV-2, AUD-1)

API:

- `Absence.Status`, `ReviewedBy`, `ReviewedAt`, `ReviewNote`, and `AbsenceStatus` go. Every
  absence is in effect when created, and every read returns all absences in range.
- `AbsenceApprovalOptions`, `Permissions.AbsencesApprove` and `Permissions.Optional` go.
  `Permissions.All` and `Permissions.Known` become one list again.
- The pending, approve and reject endpoints, `GetPendingAsync`, `ApproveAsync`, `RejectAsync`
  and `ReviewAsync` go, with the audit actions `absence.approved` and `absence.rejected`.
  Existing audit entries with those actions stay: the log is append-only.
- `Notification`, `NotificationType`, `INotificationService`, `NotificationService`,
  `NotificationsEndpoints` and `Retention:NotificationDays` go.
- The note is projected for its author only, with no reviewer exception (ABS-9).
- Startup refuses `AbsenceApprovals:Enabled=true` and any value under
  `Auth:Permissions:AbsencesApprove`, saying the feature was removed (CFG-3). The second
  already fails as an unknown permission; the message must say why.

Migration, one EF migration:

1. Delete absences whose status is Rejected. The calendar already hid them.
2. Drop the status and review columns. Pending absences stay, as ordinary absences.
3. Drop the `Notifications` table.

SPA:

- Remove the approvals admin section and route, the notification bell with its store and API
  module, the `?` pending marker in `lib/absenceSegments.ts`, status filters (including the
  "approved only" filter in "Who's off today"), and approval wording in the add/edit modal.
- `AbsenceDto` loses its status and review fields; bump every cache key version that holds
  absences (UI-8).

Tests: delete the approval and notification tests; keep everything they covered that still
applies (overlap, ownership, audit, retention of audit entries and absences).

### B. Sign-out (AUTH-8, UI-9)

- A sign-out control in the header, on every page.
- It removes the OIDC session through `oidc-client-ts` (`removeUser`, then `signoutRedirect`
  when the provider advertises an end-session endpoint, otherwise a local sign-out to the
  sign-in page), and deletes every key under the app's storage prefix in both
  `localStorage` and `sessionStorage`.
- `auth_redirect` in `sessionStorage` (`router/index.ts`) moves under the app's prefix; it
  is the only key outside it today.
- New setting `Auth:PostLogoutRedirectUri`, relative by default (`/`), resolved like
  `Auth:RedirectUri` and served in the bootstrap configuration. Deployers must register it
  with their provider; `docs/deployment.md` says so.
- In Development with `Auth:Provider=None`, sign-out clears storage and reloads.

### C. Cap read ranges (ABS-8, HOL-10)

`GET /api/absences` and `GET /api/bank-holidays/catalog` answer 400 with a problem detail
when `to < from` or when `from..to` covers more than 366 days. The SPA asks for at most a
year (366 days in a leap year) or 121 days, so no client change is needed.

### D. Validate before migrating (CFG-1, CFG-4)

Run the options validation that `ValidateOnStart` registers before `MigrateAsync`, so a
misconfiguration stops the process with the database untouched. A test starts the app with
an invalid setting against an empty database file and asserts that no tables were created.

### E. Exact region codes (REG-1)

`ValidationPatterns.RegionCode` ends in `\z` instead of `$`, so `"ES\n"` is rejected. The
same pattern is used by the request attributes and the generated regex.

### F. Pin the SDK (BLD-2)

- `global.json` pins the 10.0.1xx feature band (`"version": "10.0.100"`,
  `"rollForward": "latestPatch"`).
- CI's `setup-dotnet` reads `global-json-file: global.json`.
- The Dockerfile's SDK stage uses an image whose SDK satisfies `global.json`; the build
  fails rather than silently using another band.

### G. Small fixes found by the review

- `PeopleEndpoints.GetPaged`: remove "sick" from the comment about sortable stats columns.
- Re-indent `src/Api/Api.csproj`, `tests/UnitTests/UnitTests.csproj` and
  `tools/Seed/Seed.csproj` with two spaces, as `.editorconfig` asks; add the missing final
  newlines to `Directory.Packages.props`, `UnitTests.csproj` and `Data/regions.json`.

### H. Traceability (TRC-1, TRC-2)

- Tag tests: `[Trait("Spec", "<ID>")]` in xUnit, `[<ID>]` in Vitest test names.
- `scripts/check-spec-trace.mjs`, dependency-free Node: parses requirement IDs and their
  verification tags from `SPEC.md`, collects references from `tests/**/*.cs` and
  `src/Web/src/**/*.spec.ts`, and fails on:
  - a reference to an ID that does not exist or is retired;
  - a `[test]` requirement with no reference that is not on the gap list;
  - an ID on the gap list that now has a reference, so the list can only shrink.
- `tests/spec-coverage-gaps.txt` lists the `[test]` requirements not yet covered, one per
  line with the reason. After this change it may only hold requirements this change does not
  touch, and each must say what is missing (most need the integration tests of the next
  step).
- A CI step runs the script.

### I. Documentation (PRIV-4)

- In-app `privacy.md` and `documentation.md`: notes are visible to their author only;
  approvals, pending requests and the bell are gone; the browser cache and sign-out are
  described.
- `docs/data-protection.md`: remove the Notification section and its retention row; add the
  OTLP collector to "where data leaves" and the browser cache to what is stored; describe
  sign-out.
- `docs/administration.md`: remove "Absence approvals" and "Getting notified".
- `docs/access-control.md`: remove `AbsencesApprove` and the optional-permission paragraph.
- `docs/deployment.md`: add `Team:HomeTimeZone`, `Team:SecurityContact` and
  `Auth:PostLogoutRedirectUri` to the settings table.
- `docs/architecture.md`: replace the absence state diagram with the overlap rules.
- `docs/threat-model.md`: sign-out and the range cap close two gaps; the review race goes
  with the reviews.
- `.env.example` and `appsettings.json`: drop `AbsenceApprovals` and `NotificationDays`, add
  `PostLogoutRedirectUri`.
- Changelog: a Removed section for approvals and notifications, and Added entries for
  sign-out and the range cap.

## Order of work

Each step leaves the build, the tests and CI green, and is its own commit.

1. F (SDK pin), E, G: small, independent.
2. D, then the CFG-3 checks.
3. C.
4. A, API side and migration, with its tests.
5. A, SPA side.
6. B.
7. H: tag existing tests while touching them in 4 to 6, then the script, the gap list and the
   CI step.
8. I, then update `SPEC.md`'s pending list and this spec's status to Done.

## Done when

- `dotnet build --no-incremental` reports 0 warnings and `dotnet test` passes.
- `pnpm typecheck`, `pnpm lint:check`, `pnpm format:check` and `pnpm test` pass in `src/Web`.
- `node scripts/check-spec-trace.mjs` passes, and every requirement this spec lists is
  referenced by a test, except the `[review]` and `[ci]` ones.
- A migration test applies the new migration to a database at the previous migration holding
  one Pending, one Approved and one Rejected absence, and finds two absences left and no
  `Notifications` table.
- Starting with `AbsenceApprovals:Enabled=true` fails with the removal message (test).
- `GET /api/absences` with a 367-day range answers 400 (test).
- `git grep -i -E "approv|pending|notification" -- src tests` finds nothing but the
  migration history and the audit actions kept for old entries.
- CI is green on `master`, including the container smoke test.
- Signing out in the running app returns to the sign-in page, and `localStorage` holds no key
  under the app's prefix afterwards (checked by hand).

## Open questions

None blocking. Recorded choices that can be revisited:

- Sign-out removes interface preferences (column width, row order) too. On a shared computer
  they reveal who used it, and they are cheap to set again.
