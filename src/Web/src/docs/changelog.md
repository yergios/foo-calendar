# Changelog

---

## Unreleased

### Added

- Per-job permissions instead of a single admin role: `PeopleManage`, `ReportsRead`, `HolidaysSync`, and `AuditRead`, each configured under `Auth:Permissions` (the claim values that grant it) and enforced by a policy of the same name. The app refuses to start if a permission is left with no values.
- The admin area is split into its own page per section — People, Bank holidays, Absence approvals, Audit log — reachable from a left sidebar, each gated by its own permission(s). The sidebar, and the nav item leading to it, shows only what you hold; someone who holds none of them never reaches `/admin` at all.
- An audit log, readable at `/admin` with `AuditRead` or from `GET /api/admin/audit`. It records profile creates/updates/deletes, bulk imports, deleted absences, every hand edit to a manual bank holiday, and hand-triggered syncs — with who did it and when. Append-only, and a person's entries outlive their profile. `/healthchecks/detail` also requires `AuditRead`.
- `Auth:DevUser:GrantAllPermissions` (default on) holds every configured permission in local development, so the dev bypass never drifts as permissions are added. Set it false to browse as a plain member.
- The API describes itself as OpenAPI at `/openapi/v1.json` and serves a [Scalar](https://scalar.com/) reference over it at `/scalar`, with a bearer scheme so calls can be made from the page. On by default in development, `ApiDocs:Enabled` elsewhere — both routes are anonymous, so it is off by default.
- An absence's note is visible only to its author. The team sees the dates and the type; nobody else sees the note, on the calendar, on the admin page, or in the audit log.
- Webhook payloads don't carry the absence note by default — the `note` field is still there, it just arrives `null`. Set `Webhook:IncludeAbsenceNote` to include it.
- Regions are tracked automatically: picking one on your profile starts syncing that region's public holidays, for the current year and the next. `Team:HomeRegion`, the team's own locality, is required and always synced, never removed even once nobody's home city matches it.
- When the last person in a region leaves or is deleted, that region's bank holidays are deleted too, including manually added ones; a country's nationwide holidays go only once nobody is left in any of its regions, and the home region is exempt. The calendar's region filter and the manual bank-holiday form offer only the regions the team occupies — adding a holiday for anywhere else is rejected.
- The profile page offers every country and region the holiday source knows about, rather than a fixed list. Both fields are searchable, by name or by code, and ignore accents.
- Region and country names come from a bundled CLDR name table, so a region no longer has to be named by hand to be readable.
- The admin sync panel (`HolidaysSync`) can re-run a single region, which is what a region reported as failed needs; `POST /api/admin/bank-holidays/sync` reports `syncedRegions`/`failedRegions` and takes an optional `region` for this. `HolidaysSync` gates only this hand-triggered sync, which calls out to the holiday source and writes in bulk — manually added bank holidays stay everyone's to maintain.
- A new `Appointment` absence type, and an `Other` absence may now optionally be time-based too: a single day with a start and end time instead of a whole-day date range. Any number of time-based absences can share a day; a whole-day absence still takes the day exclusively. `Team:HomeTimeZone`, an IANA timezone id (e.g. `Europe/Zurich`), is required — every time-based absence's start/end time is wall-clock time in this one zone, for every viewer, and every displayed time is labeled with it.
- A "Who's off today" button on the home page lists everyone out today: approved personal absences plus anyone whose home region has a bank holiday, without listing a person twice for both.
- Arrow buttons alongside the calendar grid page it horizontally by about a viewport's width; holding one down ramps into a continuous scroll instead of repeated clicking.
