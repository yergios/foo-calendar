# 15. A strict Content-Security-Policy, with the bootstrap script hashed at startup

- Status: Accepted (implementation in [change spec 0004](../specs/0004-headers-telemetry-performance-e2e.md))
- Date: 2026-11-23

## Context

The [threat model](../threat-model.md) listed "no security headers" as an accepted gap, and
made it worse by pairing it with tokens held in `sessionStorage`: script injected into the
page could read them. Vue escapes interpolated data, and the one `v-html` renders the bundled
docs, so injection is unlikely; nothing stops it once it happens.

The shell the API serves holds exactly one inline script, the bootstrap configuration that
`SpaShell` writes at startup ([0004](0004-runtime-configuration.md)). Everything else is
`/assets`, under the app's own origin.

## Decision

Serve the shell with a strict policy:

- scripts only from `'self'` plus the SHA-256 of the inlined bootstrap script, computed at
  startup from the same string that is inlined;
- connections and frames only to `'self'` and the identity provider's origin;
- no plugins, `base-uri` and `form-action` restricted, and `frame-ancestors 'none'`.

Every response also carries `X-Content-Type-Options: nosniff` and a `Referrer-Policy` that
keeps paths and queries from leaving the origin. Inline styles may be allowed only if the
SPA proves to need them, with the reason in the code; scripts are never relaxed.

## Alternatives considered

- **Basic headers only** (nosniff, frame-ancestors, Referrer-Policy, no CSP). Offered on
  2026-11-23 and declined: it leaves the token theft path the threat model names fully open.
- **A per-request nonce.** The shell is one string prepared at startup and served
  `no-store`; a hash fits that with no per-request work, and a nonce would mean rewriting
  the shell per response.

## Consequences

- A configuration change that alters the bootstrap payload changes the hash with it, because
  both come from one string at startup.
- A reverse proxy must pass the header through unchanged.
- The end-to-end suite fails on any violation, so a dependency or component that starts
  needing inline script is caught in CI rather than in a browser console.
- The API reference, a third-party UI, gets a separate policy so it cannot loosen the
  shell's.
