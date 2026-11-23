# 17. Adjust the CSP for real providers, and floor the timing budget at a frame

- Status: Accepted (implementation in the fixes to [change spec 0004](../specs/0004-headers-telemetry-performance-e2e.md))
- Date: 2026-11-23
- Amends: [0015](0015-strict-content-security-policy.md), [0016](0016-own-devtools-driver-for-browser-checks.md)

## Context

The review of change spec 0004 found that the policy as specified (SEC-1) and the timing
budget (PERF-2) had four problems:

- **Providers on a second host.** Only `Auth:Authority`'s origin is allowed. A provider that
  serves its authorize and token endpoints from another host cannot complete sign-in. AWS
  Cognito is the common case. Entra ID and Auth0 custom domains serve everything from the
  issuer's host.
- **Silent renewal in a frame cannot work.** The shell is served with
  `frame-ancestors 'none'`, so the provider's redirect back to `/callback` cannot render
  inside the app's own hidden frame. `/callback` never handled the silent flow either.
  Renewal in practice goes through refresh tokens, since `offline_access` is requested by
  default.
- **The API reference allowed `'unsafe-eval'`** because a library inside Scalar probes
  whether `Function()` works on every load. Scalar works without it, and the page shares the
  app's origin and storage.
- **The hover interaction's timing budget** was 3 x 1.7 ms, which left the thinnest margin
  of any interaction against CI noise.

## Decision

- **An optional setting lists extra provider origins.** Each must be an absolute `https`
  origin, checked at startup, and is added to the shell's `connect-src`. Origins are not
  read from the provider's discovery document: that would make startup depend on the network
  and let a remote document shape the policy.
- **No framed silent renewal.** The provider leaves `frame-src`, `frame-ancestors 'none'`
  stays, and renewal relies on refresh tokens. A provider that issues none needs the user to
  sign in again when the token expires.
- **No `'unsafe-eval'` on the API reference.** Its end-to-end check tolerates exactly the one
  known violation, matched by directive, blocked URI and file.
- **The timing budget has a one-frame floor:** an interaction fails when its longest block
  exceeds the larger of three times its baseline and 16 ms.

## Alternatives considered

All offered on 2026-11-23 and declined:

- **Origins from the discovery document,** or leaving second-host providers unsupported.
- **Making framed silent renewal work** with `frame-ancestors 'self'` and a silent-callback
  handler. That means more code and a weaker framing stance on the app's own origin, for a
  path that browsers blocking third-party cookies already break.
- **Keeping `'unsafe-eval'`** to avoid an exception list.
- **Keeping a strict 3x budget** and accepting occasional reruns.

## Consequences

- A deployment on a provider like Cognito sets the extra origins; everyone else sets nothing.
- Deployers need a provider and client configured to issue refresh tokens to the SPA, or
  their users sign in again when an access token expires.
- The API reference's policy is strict; a new violation there fails CI.
- A timing regression smaller than a frame on a sub-frame interaction goes unnoticed, which
  is the cost of not flaking. The 170 ms full-grid hover would still fail by far.
