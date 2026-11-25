---
name: migration
description: Add an EF Core migration to the API the way this repository does - named in its style, checked for pending model changes, its SQL read for data loss, and covered by a migration test. Use whenever an entity, its configuration in AppDbContext, or the database schema changes.
---

# Add a database migration

Migrations live in `src/Api/Migrations/` and are applied at startup (CFG-4), so a migration
that loses data loses it on every deployment the moment it upgrades.

## Guards

- Never edit a migration that is already on `master`: add a new one.
- Never delete data silently. When the SQL drops a table or a column, narrows a type, or
  deletes rows, say so to the user before committing, and test exactly what survives.
- A migration that touches the absence note, or the audit log, is a note-touching change:
  the commit goes through the privacy review (see AGENTS.md).

`dotnet ef` is the `dotnet-ef` global tool; the repository has no tool manifest. Without it,
`dotnet tool install --global dotnet-ef`, at the EF Core version in
`Directory.Packages.props`, after asking: it installs outside the tree.

## Steps

1. Change the entity in `src/Api/Data/Models/` and, if needed, `AppDbContext`.
2. Name the migration in the existing style: PascalCase, a verb and what it changes, such
   as `AddAuditEntries`, `RemoveAbsenceApprovals`, `UpdatePersonFirstAndLastName`. Then:
   ```
   dotnet ef migrations add <Name> --project src/Api
   ```
   The tool stamps the timestamp prefix; keep it.
3. Check that the model and the migrations agree; this must report no pending changes:
   ```
   dotnet ef migrations has-pending-model-changes --project src/Api
   ```
4. Read the SQL it will run, from the migration before it to the new one:
   ```
   dotnet ef migrations script <PreviousMigration> <Name> --project src/Api
   ```
   Look for `DROP`, a table rebuilt by SQLite (`CREATE TABLE "ef_temp_..."` then
   `INSERT INTO ... SELECT`), and `DELETE`. For each, name which rows or values do not
   survive.
5. Add a test in `tests/UnitTests/Data/` in the shape of
   `RemoveAbsenceApprovalsMigrationTests`: an in-memory SQLite database migrated to the
   previous migration, rows inserted as that release stored them (every optional column
   filled in at least once), then migrated up, asserting what is kept, what is gone and the
   columns that remain. Tag it with the requirement it protects (`[Trait("Spec", "...")]`).
6. Run `scripts/check.sh`.
