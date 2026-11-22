# 6. Authorization is closed by default and granted per permission

- Status: Accepted, amended by [0014](0014-remove-absence-approvals.md): `AbsencesApprove` and optional permissions are removed
- Date: 2026-08-15
- Source: commits `a2f0bc8` (several claims and values), `ce05498` (per-permission
  policies), `feab1aa` (`AbsencesApprove`), `Program.cs` (Auth section),
  `Configuration/Permissions.cs`

## Context

The first version had one admin role. `a2f0bc8` made it accept several claim types and
values, because identity providers express membership in different claims (Entra ID puts
app roles in `roles` and group memberships in `groups`) and formats (names, GUIDs).
`ce05498` then split the role into four permissions, each configured on its own; that
commit has no message, and the reason for the split was not recorded beyond what the code
and README say per permission. `AbsencesApprove` followed the next day with the approval
flow (`feab1aa`).

## Decision

- **A fallback policy requires an authenticated user** for every endpoint. A new route is
  protected without anyone remembering to protect it; the few public ones opt out with
  `.AllowAnonymous()` (the SPA shell, `/api/config/bootstrap`, `/healthchecks`,
  `/.well-known/security.txt`, and the API reference when enabled).
- **One policy per permission**: `PeopleManage`, `ReportsRead`, `HolidaysSync`, `AuditRead`,
  `AbsencesApprove`. `Auth:Permissions` maps each to the claim values that grant it, from
  any of the claim types in `Auth:ClaimTypes:Permissions`. Any one value is enough.
- **Startup fails closed**: every permission must list at least one value, except optional
  ones gating a feature that is itself off by default (`AbsencesApprove`). A permission name
  that doesn't exist is rejected as a typo rather than granting nothing.
- **The SPA reads the same map** from the bootstrap config, so the interface and the API
  cannot disagree. The interface only decides what to offer; the API is the gate.

## Alternatives considered

- **A single admin role** (`Auth:AdminPermission`, the original). Replaced. `Program.cs`
  still detects the old setting shape and stops with the exact replacement to use, rather
  than silently binding nothing and leaving everything ungated.
- **`RequireClaim` per policy.** It pairs one claim type with its values; this needs any of
  several types with any of several values, so it is a `RequireAssertion` instead.

## Consequences

- Adding a permission is a deliberate deployment change: every deployment must configure
  it, or startup says which one is missing.
- Maintaining manual bank holidays is deliberately *not* gated: keeping the calendar right
  is everyone's job. `HolidaysSync` gates only the hand-triggered sync, which calls out to
  the holiday source and writes in bulk.
- Permission claim names match exactly, values case-insensitively, because group GUIDs
  differ in case between provider dashboards and tokens.
