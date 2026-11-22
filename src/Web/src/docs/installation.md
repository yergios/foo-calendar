# Installation

%PRODUCT_NAME% is a single deployable unit — one container, one process, one port — meant to be self-hosted by whoever wants to run it for their own team.

### Requirements

- Docker and Docker Compose, or, to build from source: .NET 10 SDK, Node.js 20.19+ (or 22.12+), and pnpm.

### Quickstart with Docker Compose

1. Clone [%REPOSITORY_URL%](%REPOSITORY_URL%).
2. Copy `.env.example` to `.env` and fill it in — at minimum `Team__HomeRegion` and `Team__HomeTimeZone`, plus your OIDC provider's `Auth__Authority`, `Auth__Audience`, and `Auth__ClientId`. To try it out first without an identity provider, set `ASPNETCORE_ENVIRONMENT=Development` instead — that bypasses sign-in entirely, and the app refuses to start that way outside Development, so it can't end up running like that by accident.
3. `docker compose up -d`. The container listens on port 8080 by default.

### What actually needs configuring

- **`Team__HomeRegion`** / **`Team__HomeTimeZone`** — required, no default. Which public holidays sync automatically, and the timezone every absence's time is shown in.
- **`Auth__Authority` / `Audience` / `ClientId`** — your OIDC provider (Auth0, Okta, Keycloak, Azure AD, and similar all work). The app won't start without these outside Development.
- **`Auth__Permissions__*`** — which claim values from your provider grant each admin permission (managing people, reading reports, syncing holidays, and so on).
- **`Team__SupportUrl`**, **`IssueUrl`**, and **`SecurityContact`** — where people, bug reports, and security researchers reach whoever runs this deployment. These also fill in this page's own Contact link, the Privacy notice, and `/.well-known/security.txt`.

Everything else has a workable default. `.env.example` in the repository documents every setting, including holiday sources, retention windows, and running behind a reverse proxy on a sub-path.

### Data

SQLite, on a single volume (`foo-calendar-data` in `compose.yaml`). Back that volume up and the whole application is backed up.

### Branding

The header shows `Team__OrgName`; colors, fonts, and shape are yours to change in `src/Web/src/styles/theme.css` before building. The product's own name and mark are fixed — see `TRADEMARK.md` in the repository for what's yours to change and what isn't.

---

Full configuration reference, source, and license: [%REPOSITORY_URL%](%REPOSITORY_URL%).
