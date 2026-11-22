# 9. Tracked regions follow where people are

- Status: Accepted
- Date: 2026-08-13
- Source: commit `14b62b1`, [Regions and holidays](../regions-and-holidays.md),
  `Services/TeamRegionService.cs`

## Context

Public holidays differ by country and by subdivision (ISO 3166-2). The calendar used to read
a hand-maintained `RegionCatalog` from `appsettings.json`, and that one list drove the
profile dropdown, the region filter and which holidays were synced. Every new hire from a
new place meant a configuration change and a redeploy.

## Decision

The set of tracked regions is derived from people's profiles. The only configured region is
`Team:HomeRegion`, which is always synced and never pruned.

- Picking a region on a profile syncs its own and its country's holidays immediately, for
  this year and next. A failure there is logged and left to the scheduled sync; saving a
  profile never fails because a holiday source is down.
- When the last person leaves a region, its holidays are deleted, manual entries included.
  A country's nationwide holidays go only once nobody is left in any of its regions.
- The weekly sync and the startup sync cover wherever the team is at that moment.
- Region names come from a bundled CLDR table (`Data/regions.json`), because the holiday
  source returns subdivision codes without names.

## Alternatives considered

- **The configured catalog** (the original). Replaced, for the reason above.

## Consequences

- No configuration or redeploy when the team spreads somewhere new.
- Deleting the last person in a region deletes hand-added holidays there. This is
  documented as a warning; the home region is exempt.
- The prune queries inline the "a country matches its own subdivisions" rule instead of
  calling `RegionCode.BelongsTo`, so EF can translate it to SQL.
