# 8. Notifications stay inside the app: no email, no webhooks

- Status: Accepted
- Date: 2026-09-03
- Source: commit `63c2976`

## Context

The app had grown two outbound integrations: SMTP email (per-person preferences and a weekly
digest) and an outbound webhook for absence events. Both were opt-in, and no deployment had
configured either. The in-app notification bell already told a requester the outcome of
their request unconditionally. The one real gap was that approvers had no signal that
something was waiting on them.

## Decision

Both were removed, along with `Person.Email`, whose only documented purpose was delivery.
Approvers instead see everything pending under "Waiting on you" in the bell, badged there
and on the approvals link. It is a live query over pending absences rather than a stored
event, so it cannot go stale and clears itself the moment a request is resolved anywhere.

## Alternatives considered

- **Keep email, opt-in.** An SMTP relay, templates, delivery preferences and a digest
  scheduler, for a channel nobody had turned on, holding an address field that existed
  only to serve it.
- **Keep the webhook.** Rejected for the same recorded reason: an opt-in integration
  surface nobody had configured.

## Consequences

- Apart from telemetry to an OTLP collector, when the deployer configures one, nothing
  leaves the app except holiday-source requests carrying country codes and the sign-in
  traffic to the identity provider. Those two are the rows of the
  [data protection](../data-protection.md) "where data leaves" table.
- No SMTP credentials to configure or protect, and two fewer dependencies (MailKit,
  MimeKit).
- People only learn of an outcome when they next open the app. Accepted for a tool that is
  opened to plan time off, not watched.
