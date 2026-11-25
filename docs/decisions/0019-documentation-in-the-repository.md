# 19. Documentation lives in the repository; the app serves only the deployment's notices

- Status: Accepted
- Date: 2026-11-25

## Context

The documentation was in four places. The README carried a developer guide. `docs/` held
the operator and design documents. The app served six pages at `/docs`: a user guide, an
FAQ, installation, a privacy notice, terms of use and the changelog. Two agent skills held
the only written procedure for adding a migration and cutting a release.

The same topics were written more than once. The in-app installation page repeated
`docs/deployment.md` and had already started to differ from it. The in-app user guide said
holidays sync "once a week from Nager.Date", but the holiday source is configurable. A new
engineer had no single page that listed the tools for running, seeding, testing and
measuring the app.

Two of the in-app pages are different from the others. The privacy notice and the terms are
filled in with the deployment's own name and support link. The deployer is the data
controller and has to show its users that notice. PRIV-4 holds the notice to the
specification.

## Decision

- **The repository holds the documentation.** The user guide (with the FAQ),
  installation (merged into `docs/deployment.md`), the developer guide
  (`docs/development.md`) and the changelog (`CHANGELOG.md` at the root) each live in one
  file. The README is the index.
- **The app serves only the privacy notice and the terms,** at `/privacy` and `/terms`,
  because only the deployment can fill them in. `/docs` is gone: links to it, and to its
  tabs, now reach the not-found page.
- **The app's footer links to the repository** for the user guide, the deployment guide and
  the changelog. `REPOSITORY_URL` is set to the upstream repository.
- **Skills point to the developer guide** for a procedure, and keep only what is specific to
  an agent: the guards and when to ask.

## Alternatives considered

Offered on 2026-11-25:

- **Keep every page in the app.** Rejected: the operator documents would exist twice, and
  they had already drifted apart.
- **Remove every page from the app,** including privacy and terms. Rejected: a repository
  page cannot name the deployment's operator or link its support channel, the operator has
  to show its users the notice, and PRIV-4 relies on the in-app notice.
- **Keep the user guide and the FAQ in the app.** Declined: they are end-user help that
  changes with the product, not with the deployment, so one copy in the repository is
  enough.
- **Redirect `/docs` to the notices,** so links already in use keep arriving. Rejected after
  review: only the privacy and terms tabs had somewhere to go. `/docs` on its own, and the
  user guide, FAQ, installation and changelog tabs, would have opened the privacy notice,
  which is a wrong page rather than a missing one.

## Consequences

- A member who opens Documentation leaves the app for the repository. A fork's footer still
  points upstream unless the fork changes `REPOSITORY_URL`.
- The footer's Documentation, Install and version links open `master`, not the deployed
  version. Bundled pages always matched the build they shipped in; these show the latest
  behaviour, which a deployment running an older release may not have. Linking the
  `v<version>` tag instead would fix that once every release is reliably tagged.
- The `marked` dependency stays, since privacy and terms are still Markdown.
- One file per topic. A change to how the app behaves updates its one page, and the README
  index makes that page easy to find.
