# 12. Elastic License 2.0, unmodified, with attribution carried by trademark

- Status: Accepted
- Date: 2026-09-02
- Source: commit `2f0400c`, [LICENSE.txt](../../LICENSE.txt), [TRADEMARK.md](../../TRADEMARK.md),
  [NOTICE.txt](../../NOTICE.txt)

## Context

The app is meant to be self-hosted by other teams, including inside for-profit companies,
while not being resold as a hosted service, and while keeping its name and attribution
visible.

## Decision

- The license is the stock Elastic License 2.0 (`Elastic-2.0`), not modified in any way.
- Attribution is carried outside the license: `TRADEMARK.md` spells out what a fork may and
  may not do with the name and mark, `NOTICE.txt` ties the footer to ELv2's notices clause,
  and the footer's attribution has no setting that removes it.
- The product name and mark are constants; `Branding:AppName` was deleted. A deployment
  names itself with `Team:OrgName`, which appears in the header.

## Alternatives considered

- **ELv2 with a bespoke attribution rider.** Rejected: a modified license sends every
  adopter's legal team back to a review that the stock text would have passed.
- **CPAL-1.0**, an OSI license with a UI-attribution exhibit. Rejected: its copyleft and
  network-use clause are heavier than wanted.

## Consequences

- Adopters read a license their lawyers may already know.
- A footer cannot be made technically non-removable in source-available software.
  "Non-removable" means no configuration removes it; removing it in a fork is a notices
  violation under ELv2.
- Colors, fonts and shape stay the deployer's to change ([0004](0004-runtime-configuration.md)).
