# Changelog

---

## Unreleased

### Added

- Regions are now tracked automatically: picking one on your profile starts syncing that region's public holidays, for the current year and the next.
- The profile page offers every country and region the holiday source knows about, rather than a fixed list. Both fields are searchable, by name or by code, and ignore accents.
- Region and country names come from a bundled CLDR name table, so a region no longer has to be named by hand to be readable.
- The admin sync panel can re-run a single region, which is what a region reported as failed needs.

### Changed

- **Breaking:** `Team:HomeRegion` is now required. It is the team's own locality — always synced, never removed.
- `RegionCatalog` is no longer what decides which regions exist. It is now an optional set of display-name overrides, and may be left out entirely.
- The calendar's region filter and the manual bank-holiday form now offer only the regions the team occupies. Adding a holiday for anywhere else is rejected.
- `POST /api/admin/bank-holidays/sync` reports `syncedRegions`/`failedRegions` instead of countries, and takes an optional `region`.

### Removed

- When the last person in a region leaves or is deleted, that region's bank holidays are deleted, **including manually added ones**. A country's nationwide holidays go only once nobody is left in any of its regions. The home region is exempt.

---

## v1.0.0 — 2026-07-28

### Added

- Initial release.
