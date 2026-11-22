# 5. Authentication only through an OIDC provider

- Status: Accepted
- Date: 2026-09-03
- Source: commits `2f0400c` (unauthenticated mode added), `ace9864` (removed)

## Context

Standing up an identity provider can be the only thing between a small team and a shared
calendar. On 2026-09-02 the app gained an opt-in unauthenticated deployment mode
(`Auth:AllowUnauthenticatedDeployment`): nobody signed in, visitors said who they were, the
answer went in a cookie, and a permanent banner said so.

## Decision

Removed the next day. Outside Development the app requires a generic OIDC provider
(Auth0, Okta, Keycloak, Entra ID and others), validates its JWTs, and stores no credentials
of its own. `Auth:Provider=None` is again only a development bypass that authenticates every
request as the fixed `Auth:DevUser`; the API refuses to start with it in any other
environment and logs a warning whenever it is on.

The reasoning, from `ace9864`: accepting whoever a visitor says they are only looks free.
Followed through, the app ends up owing people a password, a reset flow, a lockout policy
and somewhere safe to keep the hashes, which is an identity provider written here, badly.

## Alternatives considered

- **Self-asserted identity in a cookie.** Shipped and withdrawn, as above.
- **Local accounts with passwords.** The same conclusion, reached directly.
- **Identity from a trusted reverse proxy** (Authelia, oauth2-proxy, Tailscale headers).
  Deferred, not rejected. It is the known path if a deployment wants real identity without
  running an OIDC provider, and would arrive as its own record.

## Consequences

- Every production deployment needs an OIDC provider. The trial path without one is the
  Development environment, which `docker run -e ASPNETCORE_ENVIRONMENT=Development` gives.
- The app holds no secrets of its users' and has no credential store to breach.
- The SPA uses the authorization code flow (`oidc-client-ts`) and sends a bearer token on
  every API call. No auth cookies, so cross-site request forgery has nothing to ride on.
