# 1. Record architecture decisions

- Status: Accepted
- Date: 2026-11-22

## Context

Most of this project's design choices were made with alternatives on the table that were
then rejected, and the reasons lived in commit messages, code comments and private notes.
A reader of the code sees the outcome but not what was weighed, so a settled question looks
open and gets re-argued.

## Decision

Significant decisions are recorded here as Architecture Decision Records, in the format
Michael Nygard described in "Documenting Architecture Decisions" (2011): one numbered file
per decision with its context, the decision, the alternatives considered, and the
consequences.

- A decision is significant if reversing it would change the deployment shape, the data
  the app holds, its security posture, or a measured performance property.
- Records are dated by when the decision was made, which for the first batch is earlier
  than when they were written down. Each cites the commit or file it comes from.
- A record is not edited to reflect a later change of mind. A new record supersedes it, and
  the old one's status says so.
- Where the original reasoning for rejecting an alternative was not written down, the
  record says only what the measurements show, not a reason reconstructed after the fact.

## Consequences

- [The index](README.md) lists every decision and its status.
- Proposing to undo one of these means writing the record that supersedes it, which forces
  the reasons to be stated against the ones already here.
