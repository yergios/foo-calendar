# 2. One process serves both the API and the SPA

- Status: Accepted
- Date: 2026-08-12
- Source: commit `45031df` (container image and compose deployment)

## Context

The app has a .NET API and a Vue SPA. The usual split deploys them separately: a static
host or CDN for the SPA, the API behind it, and CORS or a reverse proxy to join them. The
people running this are a team self-hosting a tool for themselves, not an operations group.

## Decision

The SPA is compiled by Vite into the API's `wwwroot` and served by the same ASP.NET Core
process. The `Dockerfile` builds both into one image that runs one process on one port as
the non-root `app` user. The database lives on a mounted volume, so the volume is the whole
application state.

## Consequences

- Same origin, so no CORS, and the API can rewrite the SPA's shell at startup (see
  [0004](0004-runtime-configuration.md)).
- Deploying is pulling an image and restarting; backing up is copying a volume.
- Hashed assets are served `immutable` by the static file middleware, and the shell
  `no-store`, so caching is controlled in one place (`Program.cs`, `SpaShell.cs`).
- The SPA cannot be scaled or cached separately from the API. Accepted at this scale; see
  [0003](0003-sqlite-single-instance.md).
