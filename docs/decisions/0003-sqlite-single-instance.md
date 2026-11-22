# 3. SQLite, and exactly one instance

- Status: Accepted
- Date: 2026-08-12
- Source: [Scaling](../deployment.md#scaling), `Program.cs` startup tasks

## Context

The app holds a team's people, absences, holidays, an audit log and notifications: small,
relational, and written a few times a day per person. Whoever deploys it should not need to
run or back up a database server.

## Decision

EF Core over SQLite, in a single file on the data volume. The app runs as one instance.
Migrations are applied at startup, and the bank-holiday sync and retention sweep run as
in-process background services.

## Consequences

- Backing up the volume backs up everything; there is nothing else to restore.
- Two things assume a single instance and would need changing before a second replica:
  startup migrations (replicas would race to apply the same one) and the in-process
  holiday sync (every replica would run its own). Both are documented in
  [Scaling](../deployment.md#scaling).
- SQLite is not what blocks scaling out: the two points above would need addressing with
  any database. Data access goes through EF Core, so a different database is a provider
  and migrations change if a deployment ever needs one.
- SQLite takes a database-wide write lock, which the absence overlap check relies on:
  a `Serializable` transaction serializes concurrent creates (`AbsenceService`).
- Tests run against real in-memory SQLite rather than EF's InMemory provider, so unique
  indexes and query translation behave as in production (`SqliteTestBase`).
