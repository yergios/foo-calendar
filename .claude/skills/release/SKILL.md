---
name: release
description: Cut a release of Foo Calendar - bump the version in Api.csproj and package.json together, date the changelog's Unreleased section, run the full check and commit. Use when asked to release, cut or version a new release.
---

# Release

The procedure is [Cutting a release](../../../docs/development.md#cutting-a-release) in the
developer guide. Read it and follow it; this skill adds what is specific to an agent.

## Guards

- Ask which version, unless the user gave it. Suggest one by the guide's rule.
- Do not release from a tree that has changes unrelated to the release; stop and say so.
- In `CHANGELOG.md`, do not rewrite the entries. Fix only what is plainly wrong, and say
  what you changed.
- A failing `scripts/check.sh` is fixed in its own commit first.
- Commit with the repository's footer and push `master` as AGENTS.md says:
  ```
  chore(release): <version>

  ---

  Co-authored by AI.
  ```
- **Never tag, and never push a tag, without asking first**, even after the commit is
  pushed. A tag is what deployments pull. On a yes, create the tag as the guide says, then
  ask again before `git push origin v<version>`.
