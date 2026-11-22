# 13. Every analyzer rule on, warnings fail the build

- Status: Accepted
- Date: 2026-11-22
- Source: `Directory.Build.props`, `.editorconfig`, `.github/workflows/ci.yml`

## Context

`AnalysisMode=All` and the Sonar analyzer were already enabled, but warnings did not fail
the build, so they accumulated. The suppressions meant to handle the inapplicable ones sat
under an `.editorconfig` section that matched no file, a seed tool had stopped compiling
without anyone noticing, and a dependency carried a high-severity advisory.

## Decision

- **`TreatWarningsAsErrors`** for every project. A new warning is fixed, or suppressed with
  its reason, in the commit that introduces it.
- **NuGet audit findings are errors too**, so a published advisory against any dependency,
  transitive ones included, breaks the build until it is upgraded or pinned past.
- **Suppressions carry reasons.** Rules that do not apply to an application assembly are
  turned off in `.editorconfig`, each with a comment saying why. A rule that is right in
  general but wrong at one site is suppressed there, with a scoped `#pragma` and its reason.
- **`.editorconfig` is the .NET SDK's own template** (`dotnet new editorconfig`), kept as
  generated, with project choices in one overrides section at the end.
- **CI runs all of it** on every push to `master` and every pull request, together with
  the SPA's typecheck, lint (zero warnings), format check, tests and `pnpm audit`, and a
  smoke test of the container image.

## Alternatives considered

- **The template's own style preferences** where the code differs: explicit types instead
  of `var`, and `_camelCase` private fields. Overridden rather than adopted, so the rules
  describe the code as it is written (`99817b0`, and the reasons in `.editorconfig`'s
  overrides section).
- **Leaving warnings as warnings.** That is how they built up.

## Consequences

- A clean build means something, and CI can be the definition of done for a change.
- A newly published advisory can turn a previously green commit red without any code
  change. That is intended: it is the signal to upgrade.
- Moderate npm advisories in lint tooling, which often have no fix within the ranges
  upstream allows, are reported but do not fail CI; high and critical ones do.
