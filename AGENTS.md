There's no need for you to verify visual changes, I will.

## Git

This repo works directly on `master`. This overrides the global "never commit on main/master"
rule and step 1 of the `/commit` command (branch creation) for this project only:

- Do not create a branch, for `/commit` or otherwise. Commit on `master` as if `direct` had
  been passed, unless I name a branch or ask for one.
- After committing, push `master` to its remote (`git push`), unless I say not to.
- Everything else about commits still applies: conventional commit subject, the
  `---` / `Co-authored by AI.` footer, no `Co-Authored-By` trailer.

One amendment to that footer, for this repository only: a commit that touches absence notes
carries a `Privacy-Review:` trailer on the line just above `Co-authored by AI.`, which still
ends the message (see [Privacy review](#privacy-review)):

```
docs(api): say who reads a note

Why the change was made.

---

Privacy-Review: passed sha256:4ad5352f53035eb2c9d44eb56d330b3b6c7a40b0f098c03adb2dae98badfdf71
Co-authored by AI.
```

## Definition of done

A change is done when `scripts/check.sh` passes: every check CI runs that does not need
Docker, the end-to-end suite and the performance budget included. It stops at the first
failure and names the step. A Stop hook runs it when a session stops and blocks the stop
when it fails; fix the cause, never the check. When nothing has changed since its last pass
(HEAD, staged, unstaged and untracked files), the hook reports that pass instead of running
it again. One check runs at a time per repository: a second waits, and a stop that timed out
waiting for another session's check is not something to fix.

## Commands

| What | Command |
| --- | --- |
| Everything, in CI's order | `scripts/check.sh` (`--list` prints the steps) |
| .NET build, 0 warnings | `dotnet build --configuration Release` |
| .NET tests | `dotnet test --configuration Release` (one test: `--filter "FullyQualifiedName~AbsenceServiceTests"`) |
| SPA checks | `pnpm --dir src/Web typecheck`, `lint:check`, `format:check`, `test` |
| Spec trace | `node scripts/check-spec-trace.mjs` |
| Script and hook tests | `node --test 'scripts/**/*.test.mjs'` |
| End-to-end flows | `node --test --test-timeout=120000 tools/e2e/flows.test.mjs`, against running instances ([tools/e2e/README.md](tools/e2e/README.md)) |
| Performance | `node tools/perf/harness.mjs 1000 --budget` against a production build ([tools/perf/README.md](tools/perf/README.md)); the `perf` skill |
| Seed a dev database | `scripts/seed.sh --people 100` (wipes it first) |
| Run the app | `dotnet run --project src/Api` and `pnpm --dir src/Web dev` ([README](README.md#quickstart-local-development)) |
| A new migration | the `migration` skill |
| A release | the `release` skill |

The browser checks need a `chrome-headless-shell` ([tools/perf/README.md](tools/perf/README.md#setup)).

## The code

[docs/architecture.md](docs/architecture.md) has the whole picture; the short map:

- `src/Api/`: the .NET 10 minimal API that also serves the SPA. `Program.cs` reads top to
  bottom in the order startup happens. `Endpoints/` are thin and delegate to `Services/`,
  which hold the domain rules; `Dtos/` carry request validation; `Configuration/` holds the
  typed options, each validated at startup; `Infrastructure/` the SPA shell, headers and
  exception mapping.
- `src/Web/`: the Vue 3 SPA, built into `src/Api/wwwroot`. `lib/` holds the logic worth
  unit testing (absence segments, who's off today, the browser cache); in-app docs, the
  privacy notice and the changelog are in `src/Web/src/docs/`.
- `tests/UnitTests`, `tests/IntegrationTests`: xUnit, tagged with the requirements they
  verify. `tools/e2e`, `tools/perf`: the browser checks. `tools/Seed`: dev data.
- `scripts/`: the check, the spec trace check, the privacy review and the hooks.

## Conventions

- **Comments say why**, never what: the intent, the constraint, the alternative that was
  rejected. No em dashes in comments.
- **Configuration is validated at startup**, before the database is touched, with a message
  naming the setting (CFG-1). A new setting gets a validation rule in the same change.
- **Authorization is closed by default**: every route needs a signed-in member unless
  listed in AUTH-3, and every privileged one names its permission
  ([0006](docs/decisions/0006-fail-closed-per-permission-authorization.md)).
- **Domain errors are thrown on purpose**: `InvalidInputException`, `NotOwnerException`,
  `NotFoundException` and `ConflictException` (`src/Api/Services/DomainException.cs`) map to
  400, 403, 404 and 409 with the rule's message. Anything else is a 500 that says nothing
  about itself (ERR-1, ERR-2).
- **Nullable reference types are on everywhere**; a suppression says why at its site
  (BLD-6). Every analyzer rule is on and warnings fail the build
  ([0013](docs/decisions/0013-strict-static-analysis.md)).
- Match the code around you before introducing a new pattern.

## How work is done here

- [SPEC.md](SPEC.md) describes the intended system as numbered requirements, each verified
  by a test, a CI step or review. A test names the IDs it verifies
  (`[Trait("Spec", "ABS-5")]`, or `[ABS-5]` in a Vitest or `node:test` name), and
  `scripts/check-spec-trace.mjs` keeps the two in step.
- [docs/specs/](docs/specs/README.md) holds change specs: what one change adds, changes or
  retires, the work in order, and when it is done. A build session works from a change spec,
  one commit per step, and its last commit updates `SPEC.md` and marks the spec Done.
- [docs/decisions/](docs/decisions/README.md) holds the decision records. Record a decision
  as an ADR when it is made, with the alternatives rejected, not afterwards.
- An independent reviewer checks the result of a build session: a fresh agent that reads
  the change against its spec and probes it by breaking code, to see whether a test notices.
- [docs/agentic-development.md](docs/agentic-development.md) tells how this has gone so far,
  with the cases where it caught something.

## Privacy review

An absence note reaches its author only: never anyone else, an audit entry, a log, a trace or
a metric, and it never leaves the process (ABS-9, AUD-2, PRIV-1,
[0007](docs/decisions/0007-no-special-category-data.md)). A change that could break that is
reviewed before it is committed:

- A staged diff **touches notes** when it changes a file listed in
  `.claude/privacy-paths.txt` or adds or removes a line under `src/`, `tests/` or `tools/`
  matching `\bnotes?\b`. The list includes itself and the review's own scripts and prompt.
  The rules are read as committed (HEAD's), never from the working tree, so an edit to them
  is judged by the rules before it.
- On `git commit`, a hook runs the `privacy-reviewer` subagent headlessly on that diff. A
  failed verdict blocks the commit with findings citing file and line: fix them and commit
  again. A pass is cached by the diff's hash, and the hook blocks once more to give you the
  exact `Privacy-Review: passed sha256:<hash>` line to put above `Co-authored by AI.`.
- Stage with `git add` in a command of its own, then commit what is staged: the review
  covers the staged diff, so `git add ... && git commit`, `git commit -a`, `-p` and
  `git commit <paths>` are refused when they could touch notes. Changing the staged diff
  after a review means another review.
- A merge commit is reviewed on what it adds beyond merging its parents automatically (its
  remerge diff): a plain `git merge` needs nothing; conclude a merge with conflicts, or with
  changes of your own, with `git commit`, which reviews it. `git rebase`, `git cherry-pick`
  and `git revert` of commits that touch notes are refused, because replaying a commit
  changes its diff and so its hash: use `--no-commit` (or cherry-pick one at a time) and then
  `git commit`. The hook finds git however it is run in a command (`cd`, `&&`, `env`,
  `bash -c`, aliases, `-C`, ...); git run from a script file or another program is seen only
  by CI.
- `node scripts/privacy-review.mjs hash` says whether the staged diff touches notes and
  prints its hash; `node scripts/privacy-review.mjs review` runs the reviewer on it by hand
  and prints the trailer. The subagent can also be asked to review any diff in a session.
- CI, and `scripts/check.sh` for the commits not pushed yet, fail a commit that touches notes
  without a trailer whose hash is its own diff's, merge commits included. That proves the
  trailer was written for exactly that diff, not that a review happened: anyone can compute a
  hash. The review itself is what the hook runs. Merge pull requests with a merge commit: a
  squash or rebase merge makes new commits no review covered.
