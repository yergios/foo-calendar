# 14. Remove absence approvals

- Status: Accepted (implementation tracked in [change spec 0001](../specs/0001-remove-approvals-and-close-review-gaps.md))
- Date: 2026-11-22
- Amends: [0006](0006-fail-closed-per-permission-authorization.md) (drops `AbsencesApprove`),
  [0007](0007-no-special-category-data.md) (notes become author-only without exception),
  [0008](0008-in-app-notifications-only.md) (the in-app notifications go too)

## Context

An opt-in approval workflow was added on 2026-08-16 (`feab1aa`): with
`AbsenceApprovals:Enabled`, a new absence waited as `Pending` until a holder of
`AbsencesApprove` approved or rejected it, and an approved absence went back to `Pending`
when edited. Everything else attached to it:

- the in-app notifications, whose only two kinds are "approved" and "rejected", and the
  "Waiting on you" list for approvers, with their table, endpoints, bell and retention setting;
- the approvals admin page, the review fields on `Absence`, and the `?` marker on the grid;
- `AbsencesApprove`, the only optional permission, which `Permissions.Optional` exists for;
- the one exception to note privacy: approvers read the notes of pending requests, which the
  in-app privacy notice ("nobody but you can ever read it") did not say.

The feature was off by default. The product is positioned as a calendar for coordinating a
team spread across countries.

## Decision

Remove the approval workflow and everything that exists only for it. An absence is in effect
from the moment its owner adds it, and its note is visible to its author only.

Settings that asked for the feature stop the app at startup with a message saying it was
removed (`AbsenceApprovals:Enabled=true`, `Auth:Permissions:AbsencesApprove`), rather than
being ignored, so a deployment relying on reviews finds out at once.

Existing data on upgrade: pending absences stay on the calendar as ordinary absences, as they
already appeared there; rejected ones, which the calendar no longer showed, are deleted.

## Alternatives considered

- **Keep it, opt-in.** Offered with removal on 2026-11-22 and declined. Keeping it meant
  specifying and maintaining the three-state lifecycle and correcting the privacy notice to
  admit approvers.

## Consequences

- Leave approval is out of scope ([SPEC.md](../../SPEC.md), Non-goals). A team that needs it
  needs an HR system next to this.
- One table, one permission, one admin page, the notification bell and two settings fewer;
  the "optional permission" concept goes with the only permission that used it.
- Notes are visible to their author only, on every path, which makes the privacy notice true
  as written.
- The race-safe review update (a conditional `UPDATE ... WHERE Status = Pending`) goes with
  the feature. The overlap and collision checks keep the same serializable-transaction
  approach.
- Nothing in the app tells a person anything any more; there is nothing to tell them.
