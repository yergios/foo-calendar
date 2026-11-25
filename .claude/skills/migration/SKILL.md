---
name: migration
description: Add an EF Core migration to the API the way this repository does - named in its style, checked for pending model changes, its SQL read for data loss, and covered by a migration test. Use whenever an entity, its configuration in AppDbContext, or the database schema changes.
---

# Add a database migration

The procedure, from naming the migration to its test, is
[A database migration](../../../docs/development.md#a-database-migration) in the developer
guide. Read it and follow it; this skill adds what is specific to an agent.

## Guards

- Never edit a migration that is already on `master`: add a new one.
- Never delete data silently. When the SQL drops a table or a column, narrows a type, or
  deletes rows, say so to the user before committing, and test exactly what survives.
- A migration that touches the absence note, or the audit log, is a note-touching change:
  the commit goes through the privacy review (see AGENTS.md).
- Without the `dotnet-ef` global tool, ask before
  `dotnet tool install --global dotnet-ef` at the EF Core version in
  `Directory.Packages.props`: it installs outside the tree.

## Report

Tell the user the migration's name, which rows or values its SQL does not keep (or that it
keeps everything), and the test that pins it. Then commit as AGENTS.md says.
