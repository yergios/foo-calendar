# 0006. The operator's contact is required

- Status: Done
- Date: 2026-11-25
- Requirements added or changed: CFG-2 (`Team:SupportUrl` required outside Development),
  UI-11 (new) (see [SPEC.md](../../SPEC.md))
- Decision: [0020](../decisions/0020-operator-contact-required.md)

## Goal

Every deployment's privacy notice and terms name a contact that the operator chose, shown
the way a member would write it down. Today `appsettings.json` ships
`mailto:support@example.com`, so a deployment that never sets `Team:SupportUrl` publishes a
contact nobody reads, and one that clears it gets empty links. Both notices show a
`mailto:` contact with its scheme.

## Non-goals

- `Team:IssueUrl` and its shipped `your-org` placeholder. It feeds only the error page.
- Checking that the contact reaches anyone, or restricting its scheme.
- Changing the wording of the notices beyond the link text.

## Changes

### A. `Team:SupportUrl` is required outside Development (CFG-2)

- Validate in `Program.cs`, with the other `TeamOptions` rules: outside Development the
  value must be present (`Team:SupportUrl must be configured, ...`) and an absolute URI
  (`Uri.TryCreate(..., UriKind.Absolute, ...)`, any scheme). In Development an empty value
  is accepted; a non-empty one must still be absolute.
- `src/Api/appsettings.json`: `"SupportUrl": ""`. `src/Api/appsettings.Development.json`:
  a local value, e.g. `mailto:support@example.local`, so the notices render in a local run
  and in the browser checks.
- `.env.example`: `Team__SupportUrl=` left empty, its comment saying it is required.
- `TeamOptions.SupportUrl`'s summary says it is required outside Development, and why (the
  notices name it as the controller's contact).
- Tests in `tests/IntegrationTests/StartupTests.cs`, tagged CFG-1 and CFG-2:
  - missing outside Development stops startup naming the setting;
  - a relative value (`support`) stops startup naming it, in Production and in Development;
  - `mailto:` and `https:` values start in Production;
  - empty starts in Development.
- Existing tests that start the app in Production now need a `Team:SupportUrl`: give them
  one, rather than loosening the rule.

### B. A `mailto:` contact shows as its address (UI-11)

- `src/Web/src/lib/legal.ts`: a `%SUPPORT_LABEL%` placeholder, the contact as a member reads
  it. For `mailto:` that is the address alone, without the scheme or any `?subject=...`
  query. For any other scheme it is the URL unchanged.
- `src/Web/src/legal/privacy.md` and `terms.md`: every `[%SUPPORT_URL%](%SUPPORT_URL%)`
  becomes `[%SUPPORT_LABEL%](%SUPPORT_URL%)`. Nothing else in them changes.
- Vitest cases, named with `[UI-11]`, for the label: `mailto:a@b.example`,
  `mailto:a@b.example?subject=Hi`, an `https:` URL, and empty.

### C. Documentation

- `docs/deployment.md`: the `Team:SupportUrl` row says it is required outside Development
  and is the contact the notices name. The quickstart already asks for it.
- `CHANGELOG.md`, under Unreleased, Changed: `Team:SupportUrl` is required outside
  Development and no longer ships a placeholder, so an upgrade that relied on the
  placeholder stops at startup until it is set; a `mailto:` contact shows as its address.

## Order of work

Each step is its own commit and leaves `scripts/check.sh` passing.

1. A, with its tests, removing nothing from the gap list (CFG-2 already has tests).
2. B, with its tests, removing UI-11 from `tests/spec-coverage-gaps.txt`. `privacy.md` is a
   privacy path, so this commit goes through the privacy review.
3. C, which also sets this spec's status to Done, its row in `docs/specs/README.md` to
   Done, and `SPEC.md`'s pending list back to none.

## Done when

- The app started outside Development with `Team:SupportUrl` empty, or relative, stops with
  a message naming it, and starts with a `mailto:` or `https:` value.
- `git grep -n "example.com" -- src/Api/appsettings.json .env.example` finds no support
  contact.
- `git grep -n "\[%SUPPORT_URL%\]" -- src/Web/src/legal` finds nothing.
- `node scripts/check-spec-trace.mjs` passes with UI-11 off the gap list, and
  `scripts/check.sh` passes.
- CI is green on `master`.

## Open questions

None.
