# Architecture decision records

Why the system is shaped the way it is, including what was considered and rejected. The
format and conventions are in [0001](0001-record-architecture-decisions.md).

| # | Decision | Date | Status |
| --- | --- | --- | --- |
| [1](0001-record-architecture-decisions.md) | Record architecture decisions | 2026-11-22 | Accepted |
| [2](0002-one-deployable-unit.md) | One process serves both the API and the SPA | 2026-08-11 | Accepted |
| [3](0003-sqlite-single-instance.md) | SQLite, and exactly one instance | 2026-08-11 | Accepted |
| [4](0004-runtime-configuration.md) | Deployment settings at runtime, the theme at build time, the name nowhere | 2026-08-12 | Accepted |
| [5](0005-oidc-only-authentication.md) | Authentication only through an OIDC provider | 2026-09-03 | Accepted |
| [6](0006-fail-closed-per-permission-authorization.md) | Authorization is closed by default and granted per permission | 2026-08-15 | Accepted, amended by 14 |
| [7](0007-no-special-category-data.md) | Store no special-category data, and keep notes inside the app | 2026-09-02 | Accepted, amended by 14 |
| [8](0008-in-app-notifications-only.md) | Notifications stay inside the app: no email, no webhooks | 2026-09-03 | Accepted, amended by 14 |
| [9](0009-regions-follow-the-team.md) | Tracked regions follow where people are | 2026-08-13 | Accepted |
| [10](0010-windowed-calendar-grid.md) | Render the calendar as a windowed grid of absence segments | 2026-09-02 | Accepted |
| [11](0011-client-side-stale-while-revalidate.md) | Cache in the client with stale-while-revalidate | 2026-09-02 | Accepted |
| [12](0012-elastic-license-unmodified.md) | Elastic License 2.0, unmodified, with attribution carried by trademark | 2026-08-12 | Accepted |
| [13](0013-strict-static-analysis.md) | Every analyzer rule on, warnings fail the build | 2026-11-22 | Accepted |
| [14](0014-remove-absence-approvals.md) | Remove absence approvals | 2026-11-22 | Accepted |
| [15](0015-strict-content-security-policy.md) | A strict Content-Security-Policy, with the bootstrap script hashed at startup | 2026-11-23 | Accepted |
| [16](0016-own-devtools-driver-for-browser-checks.md) | Browser checks run on the repo's own DevTools driver | 2026-11-23 | Accepted |

To add one, copy the shape of an existing record, take the next number, and add a row here.
