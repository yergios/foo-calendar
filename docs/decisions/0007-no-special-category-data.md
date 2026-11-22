# 7. Store no special-category data, and keep notes inside the app

- Status: Accepted, amended by [0014](0014-remove-absence-approvals.md): notes become author-only, with no approver exception
- Date: 2026-09-02
- Source: commit `2f0400c`, [data protection](../data-protection.md)

## Context

Absence types are visible to the whole team. A `Sick` type therefore told everyone who was
ill, which is health data under GDPR Article 9: a materially higher bar than anything else
the app stores, in a tool that is otherwise about dates. The free-text note is the one field
whose contents nobody can predict.

## Decision

- **No `Sick` type.** Illness is recorded as `Other`, which says someone is away without
  saying why. The enum is `Vacation`, `Other`, `Appointment`.
- **Notes never leave the app.** A note is shown to its author, and to approvers while a
  request is pending, since deciding needs the reason. It is projected away in the database
  query for everyone else, never written to the audit log, and never sent anywhere. The
  webhook's `IncludeAbsenceNote` toggle was deleted rather than defaulted off, and the
  webhook itself later went ([0008](0008-in-app-notifications-only.md)).
- **Data that has served its purpose expires**: audit entries after 365 days and
  notifications after 90 by default, swept daily. Absences are kept by default, because
  looking back over previous years is what a leave calendar is for.
- **Erasure is explicit**: deleting a person cascades to their absences and notifications,
  declared in `AppDbContext` rather than inherited from an EF default.

## Alternatives considered

- **Keep `Sick`, with configurable visibility.** Rejected in favour of removing the type
  outright, so that the app stores no special-category data by design. Hiding the type
  from colleagues would not have achieved that.

## Consequences

- Removing `Sick` renumbered the enum, so databases from before 2026-09-02 had to be
  recreated rather than migrated.
- The guarantee depends on one thing the app cannot enforce: people not typing health
  details into a note. The data-protection document tells deployers to tell their teams.
- The audit log deliberately outlives the people it names; the retention bound is what
  keeps it from becoming a permanent record of everyone who ever worked there.
