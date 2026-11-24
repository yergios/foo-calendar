---
name: release
description: Cut a release of Foo Calendar - bump the version in Api.csproj and package.json together, date the changelog's Unreleased section, run the full check and commit. Use when asked to release, cut or version a new release.
---

# Release

## Guards

- The version lives in two places that CI ties together (BLD-5): `<Version>` in
  `src/Api/Api.csproj` and `"version"` in `src/Web/package.json`. Change both, to the same
  value, in the same commit.
- Ask which version, unless the user gave it. Suggest one by semantic versioning from the
  Unreleased section: a breaking change to configuration or the API is a major bump, an
  addition a minor one, fixes alone a patch.
- **Never tag, and never push a tag, without asking first**, even after the commit is pushed.
  A tag is what deployments pull.
- Do not release from a tree that has changes unrelated to the release; stop and say so.

## Steps

1. `git status` must be clean, on `master`, up to date with its remote.
2. Set the new version in `src/Api/Api.csproj` and `src/Web/package.json`.
3. In `src/Web/src/docs/changelog.md`, rename `## Unreleased` to
   `## <version> (<today, YYYY-MM-DD>)` and put a new, empty `## Unreleased` above it. Do
   not rewrite the entries; fix only what is plainly wrong, and say what you changed.
4. Run `scripts/check.sh`. It must pass; a failure is fixed in its own commit first.
5. Commit, with the repository's footer:
   ```
   chore(release): <version>

   ---

   Co-authored by AI.
   ```
   and push `master` as AGENTS.md says.
6. Ask whether to tag. On a yes: `git tag --annotate v<version> --message "Foo Calendar <version>"`,
   then ask again before `git push origin v<version>`.
