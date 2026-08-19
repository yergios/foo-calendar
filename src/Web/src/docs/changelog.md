# Changelog

---

## Unreleased

### Added

- One permission per job instead of a single admin role: `PeopleManage`, `ReportsRead`, `HolidaysSync`, and `AuditRead`, each configured on its own under `Auth:Permissions` and enforced by a policy of the same name. The admin page shows only the sections you hold, and the nav item is hidden from someone who holds none of them.
- An audit log, readable at `/admin` with `AuditRead` or from `GET /api/admin/audit`. It records profile creates/updates/deletes, bulk imports, deleted absences, every hand edit to a manual bank holiday, and hand-triggered syncs — with who did it and when. Append-only, and a person's entries outlive their profile.
- `Auth:DevUser:GrantAllPermissions` (default on) holds every configured permission in local development, so the dev bypass never drifts as permissions are added. Set it false to browse as a plain member.
- The API describes itself as OpenAPI at `/openapi/v1.json` and serves a [Scalar](https://scalar.com/) reference over it at `/scalar`, with a bearer scheme so calls can be made from the page. On in development, `ApiDocs:Enabled` elsewhere — both routes are anonymous, so it is off by default.
- `Webhook:IncludeAbsenceNote` opts absence notes back into webhook payloads.
- Regions are now tracked automatically: picking one on your profile starts syncing that region's public holidays, for the current year and the next.
- The profile page offers every country and region the holiday source knows about, rather than a fixed list. Both fields are searchable, by name or by code, and ignore accents.
- Region and country names come from a bundled CLDR name table, so a region no longer has to be named by hand to be readable.
- The admin sync panel can re-run a single region, which is what a region reported as failed needs.
- A new `Appointment` absence type, and an `Other` absence may now optionally be time-based too: a single day with a start and end time instead of a whole-day date range. Any number of time-based absences can share a day; a whole-day absence still takes the day exclusively. Every displayed time is labeled with the team's timezone.
- A "Who's off today" button on the home page lists everyone out today: approved personal absences plus anyone whose home region has a bank holiday, without listing a person twice for both.
- Arrow buttons alongside the calendar grid page it horizontally by about a viewport's width; holding one down ramps into a continuous scroll instead of repeated clicking.

### Changed

- **Breaking:** `Auth:AdminPermissions` is replaced by the map `Auth:Permissions`, which names the claim values granting each permission. Listing the same value under all four reproduces the single admin role it replaced. The app refuses to start on the old key, and on a permission left with no values.
- An absence's **note is now visible only to its author**. The team still sees the dates and the type; nobody else sees the note, on the calendar, on the admin page, or in the audit log.
- **Breaking:** webhook payloads no longer carry the absence note by default. The `note` field is still there, it arrives `null` — set `Webhook:IncludeAbsenceNote` to restore it.
- Manually added bank holidays stay everyone's to maintain, deliberately. `HolidaysSync` gates only the hand-triggered sync, which calls out to the holiday source and writes in bulk.
- `/healthchecks/detail` now requires `AuditRead` rather than the admin permission.
- **Breaking:** `Team:HomeRegion` is now required. It is the team's own locality — always synced, never removed.
- **Breaking:** `Team:HomeTimeZone` is now required, an IANA timezone id (e.g. `Europe/Zurich`). Every time-based absence's start/end time is wall-clock time in this one zone, for every viewer.
- The calendar's region filter and the manual bank-holiday form now offer only the regions the team occupies. Adding a holiday for anywhere else is rejected.
- `POST /api/admin/bank-holidays/sync` reports `syncedRegions`/`failedRegions` instead of countries, and takes an optional `region`.

### Removed

- When the last person in a region leaves or is deleted, that region's bank holidays are deleted, **including manually added ones**. A country's nationwide holidays go only once nobody is left in any of its regions. The home region is exempt.
