# 0005. Agent tooling: one check, enforced hooks, automatic privacy review

- Status: Done (2026-11-24)
- As built, two flags the commands below lack: git reads the trailer only with
  `--no-divider` as well, since it otherwise takes the footer's `---` for the start of a
  patch; and the reviewer's `tools` list includes `StructuredOutput`, without which a
  headless run answers in prose and gives no verdict. The check measured 57 s; the Stop
  hook stops it after 120 s and has 180 s.
- Date: 2026-11-24
- Decided by the user on 2026-11-23:
  - the Stop hook runs the full check every time a session stops;
  - the privacy reviewer runs automatically on every diff that touches absence notes;
  - the review is recorded as a git trailer on the line just above `Co-authored by AI.`;
  - CI verifies those trailers on pull requests as well as pushes.
- Requirements added: AGT-1 to AGT-9 (see [SPEC.md](../../SPEC.md))

## Goal

Make the way this repository was built repeatable by the next agent, and enforced rather
than advised:

- one command that says whether the tree is shippable, which a Stop hook runs;
- the privacy rules checked by a dedicated reviewer on every change that could break them;
- the instructions, permissions and recurring procedures every session needs, in the
  repository;
- a written account of the workflow, using this project's own history as the example.

## Non-goals

- Changing any application behaviour.
- New dependencies. Hooks and scripts are Bash or Node built-ins; the reviewer is Claude Code
  run headlessly.

## Changes

### A. `scripts/check.sh`, the one check (AGT-1)

Runs, in this order, every check CI runs that does not need Docker, stopping at the first
failure and naming it:

1. .NET: restore (the NuGet audit), Release build with 0 warnings, tests.
2. Version agreement between `Api.csproj` and `package.json`.
3. SPA: frozen install, typecheck, lint, format check, tests, `pnpm audit --audit-level high`.
4. The spec trace check and its own tests.
5. The browser checks: build the SPA, then start the local instances the end-to-end suite and
   the performance budget need, the stub OIDC provider included (see `tools/e2e/README.md`).
   Run both suites, then stop every process it started, even on failure. With no
   `chrome-headless-shell`, this step fails and says how to get one.

Each step prints its name and duration. `scripts/check.sh --list` prints the steps.
`AGENTS.md` and the README's developer guide name it as the definition of done.

### B. The Stop hook (AGT-2)

- A Stop hook in the shared `.claude/settings.json` runs `scripts/check.sh` every time a
  session stops. Its timeout is long enough for the full run (measure it, then set about
  twice that).
- On failure it blocks the stop and feeds back the failing step's name and the last lines
  of its output.
- It honours Claude Code's loop protection, documented at
  https://code.claude.com/docs/en/hooks: `stop_hook_active`, and the cap after which Claude
  Code ends the turn anyway. A run that keeps failing after that ends with the failure
  reported, not with a success.
- `scripts/hooks/` holds the hook scripts. A Node `node:test` suite drives them with recorded
  hook-input JSON and a stub `check.sh`, so their behaviour is tested without running the
  real check.

### C. The privacy reviewer (AGT-3, AGT-4, AGT-6)

- **The subagent,** `.claude/agents/privacy-reviewer.md`, has read-only tools. Its prompt
  holds the rules it checks a diff against, citing them: ABS-9, AUD-2, PRIV-1, ADR 0007, and
  the threat model's note rows. In short: a note reaches its author only, on every read path;
  never an audit entry, a log, a trace or a metric; never leaves the process. It answers with
  a structured verdict (`passed` or `failed`, with findings citing file and line). It can be
  run by hand as well.
- **What "touches notes" means:** a staged diff touches notes when it changes a file listed
  in `.claude/privacy-paths.txt`, or when any added or removed line under `src/`, `tests/` or
  `tools/` matches the note pattern that file defines.
  - The starting list: the absence entity, its DTO and projection, `AbsenceService`, the
    audit log, the SPA's absence types, API module and browser cache,
    `AddAbsenceModal.vue`, `docs/data-protection.md` and the in-app `privacy.md`.
  - The pattern: `\bnotes?\b`, case-insensitive.
- **The diff hash (AGT-4):** SHA-256 of one diff command, shared by the hook and CI in
  `scripts/privacy-review.mjs`:
  - for a staged diff, `git diff --cached --no-color --no-ext-diff --full-index --binary`;
  - for a commit, the same options against its first parent.
  - A test proves the two give the same hash for the same change.
- **The commit hook (AGT-3):** a PreToolUse hook on Bash commands running `git commit`.
  1. If the staged diff touches notes, it hashes the diff and looks for a cached verdict
     under `.git/privacy-reviews/<hash>`.
  2. With none, it runs the reviewer headlessly, with hooks disabled for that run so it
     cannot recurse, and only read tools:
     `claude -p --agent privacy-reviewer --json-schema <verdict schema> --settings '{"disableAllHooks":true}' --allowedTools Read,Grep,Glob`.
     Check the exact flags against `claude --help`. The diff goes in on stdin.
  3. On `failed`, it blocks the commit and returns the findings.
  4. On `passed`, it caches the verdict. If the commit message lacks
     `Privacy-Review: passed sha256:<hash>` on the line above `Co-authored by AI.`, it
     blocks once and says exactly which line to add.
  5. A commit whose trailer matches a cached `passed` verdict for the staged diff goes
     through.
  6. A commit that does not touch notes is never delayed.
- **Reading the trailer:** git parses it with
  `git -c trailer.privacy-review.key=Privacy-Review interpret-trailers --parse`. That
  configuration is what lets git read a trailer that shares its paragraph with
  `Co-authored by AI.`.

### D. CI verifies the trailers (AGT-5)

- A step in the trace job (with `fetch-depth: 0`) runs `scripts/privacy-review.mjs verify`
  over:
  - on `push`, `github.event.before..github.sha`, falling back to the commit alone when
    `before` is all zeros;
  - on `pull_request`, `pull_request.base.sha..pull_request.head.sha`, the PR's own commits,
    never GitHub's synthetic merge commit.
- Merge commits are skipped; their changes were reviewed in the commits they merge.
- Every other commit that touches notes must carry a trailer whose hash equals the hash of
  its own diff. Otherwise the step fails, naming the commit and what is missing or
  mismatched, and saying that squash merging produces commits no review covered.
- Commits before this change is merged are exempt: the script starts from the commit that
  introduced it.
- A `node:test` suite builds fixture repositories in a temporary directory and covers:
  - a reviewed commit passing;
  - a missing trailer failing;
  - a trailer copied onto a different diff failing;
  - a commit not touching notes passing without a trailer;
  - a merge commit skipped;
  - the push and pull-request ranges.

### E. Shared Claude Code settings (AGT-8)

- `.claude/settings.json`, committed, holds the hooks from B and C and a permission
  allowlist of commands that only read or check:
  - `git status`, `git diff` and `git log`;
  - `dotnet build`, `dotnet test` and `dotnet ef migrations has-pending-model-changes`;
  - the `pnpm` check scripts;
  - `node scripts/...` and `scripts/check.sh`.
- Nothing that writes outside the working tree, pushes, or deletes is pre-allowed.
- `.claude/settings.local.json` stays personal and ignored.

### F. `AGENTS.md`, rewritten (AGT-7)

Keep the user's existing rules word for word: visual checks, and committing and pushing on
`master`. Add:

- **Commands:** `scripts/check.sh`, each suite on its own, and the seed and perf tools.
- **A short map of the code**, pointing to `docs/architecture.md`.
- **Conventions:** comments say why, startup validation, closed-by-default authorization,
  domain errors, nullable types.
- **The workflow:** `SPEC.md` describes the intended system, `docs/specs` holds change specs,
  `docs/decisions` holds ADRs. Record a decision as an ADR when it is made. A build session
  works from a change spec; an independent reviewer checks the result.
- **The privacy workflow,** and the amendment to the commit footer: for this repository a
  `Privacy-Review:` trailer may sit on the line just above `Co-authored by AI.`, which still
  ends the message.

`CLAUDE.md` keeps importing it.

### G. Project skills (AGT-9)

Under `.claude/skills/`, each a `SKILL.md` that states its steps and its guards:

- **`perf`:** run the harness at 100 and 1000 people against a production build and compare
  with `tools/perf/baseline.json`. Never refresh the baseline unless asked, and then follow
  the README's procedure in a commit of its own.
- **`release`:**
  - bump the version in `Api.csproj` and `package.json` together;
  - move the changelog's Unreleased section under the new version with today's date;
  - run `scripts/check.sh`;
  - commit, then ask before tagging or pushing a tag.
- **`migration`:**
  - add an EF migration with the existing naming style;
  - check `has-pending-model-changes`;
  - read the generated SQL (`dotnet ef migrations script`) for data loss;
  - add a migration test in the shape of `RemoveAbsenceApprovalsMigrationTests`.

### H. `docs/agentic-development.md`

How this repository is developed with agents, from its own history:

- the roadmap;
- `SPEC.md` and the change specs;
- fresh build agents working from a spec;
- independent reviewers that probe by breaking code;
- fixes;
- hooks and the privacy review.

Include the concrete cases where the process caught something, citing commits, for example:

- reviewers removing reasons nobody had recorded;
- tests exposing `MapInboundClaims` and the encoder escaping;
- the access table catching a route that only exists with a holiday source configured;
- the reviewers' mutation probes.

Every claim is checked against `git log` and the documents; nothing is invented. Link it
from the README's guide table.

## Owner actions

These are not code, and the user does them:

- In the GitHub repository settings, disable squash merging, so a squash commit never
  replaces reviewed commits.
- Accept the workspace trust dialog in Claude Code for this repository, so the shared hooks
  run.

## Order of work

Each step is its own commit and leaves CI green.

1. A.
2. C: the diff hash, the touch detection and the reviewer subagent, with tests.
3. D, with its fixture tests and the CI step.
4. B and the commit hook from C, wired in `.claude/settings.json` (E), with the hook tests.
5. F and G.
6. H, then `SPEC.md`'s pending list and this spec's status.

## Done when

- `scripts/check.sh` passes on a clean checkout with `chrome-headless-shell` present, and
  fails naming the step when any check is broken (checked by hand once, stated in the
  commit).
- In a real Claude Code session in this repository, all checked by hand and stated in the
  final commit:
  - stopping runs the check;
  - a note-touching commit triggers the reviewer, and passes only with the right trailer;
  - a commit not touching notes is not delayed.
- The hook, trailer and touch-detection test suites pass in CI.
- `node scripts/check-spec-trace.mjs` passes with AGT requirements traced; 0 warnings; every
  suite passes; CI is green on `master`.

## Open questions

None. The Stop hook runs the full check every time, as decided, even when nothing changed
since the last green run. Whether to skip a run for a tree identical to the last passing one
can be revisited if the wait becomes a problem.
