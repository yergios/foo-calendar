# 4. Deployment settings at runtime, the theme at build time, the name nowhere

- Status: Accepted
- Date: 2026-08-12, extended 2026-09-02
- Source: commits `ca4dca1` (runtime config), `2f0400c` (inlined bootstrap, branding split)

## Context

Branding, OIDC settings and the admin permission used to be inlined into the SPA bundle by
Vite. Changing any of them meant rebuilding the frontend, so every deployment was in effect
a fork, and one distributable image was impossible.

## Decision

Three categories, decided by who may change a value and whether a compiler is needed:

- **Deployment settings are runtime API configuration.** OIDC settings, permissions, the
  team's name, support links, holiday source, retention: all read at startup from
  `appsettings.json` or `__` environment variables. The bundle contains no
  environment-specific value.
- **The theme is build time.** Colors, fonts and shape live in `theme.css`, because Tailwind
  bakes them into the CSS.
- **The product's name and mark are constants** (`ProductInfo.cs`, `lib/productInfo.ts`),
  with no setting that reaches them (see [0012](0012-elastic-license-unmodified.md)).

What the SPA needs before sign-in is inlined by the API into the shell it serves, as
`window.__bootstrap`, in the same startup pass that rewrites `<base href>` for sub-path
deployments. `GET /api/config/bootstrap` serves the same payload for the Vite dev server.

## Alternatives considered

- **Fetching the bootstrap config before mount** (the first version, `ca4dca1`). It put a
  serial network round trip in front of first paint. Replaced by inlining in `2f0400c`.

## Consequences

- One image serves any deployment, at the root or under a sub-path, with any IdP.
- The inlined payload is serialized with the API's own JSON options, whose default encoder
  escapes `<`, `>` and `&`. That escaping is what makes inlining safe; `SpaShell.cs` says
  so, so nobody "tidies" it into relaxed escaping.
- The shell is served `no-store`, since a stale copy would point at a retired IdP.
- Settings are validated at startup (`ValidateOnStart`), so a misconfiguration stops the
  process with a message instead of breaking a page later.
