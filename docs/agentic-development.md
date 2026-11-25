# Developing with agents

How this repository is built with coding agents, told from its own history. Every claim below
cites the commit or document it comes from; `git show <hash>` has the detail. The rules an
agent follows are in [AGENTS.md](../AGENTS.md); this page is the why and the record.

## The loop

1. **A roadmap** of large steps. It is not a file in the repository; commits name its steps:
   the step 1 review (merged in `fc39c62`) covered build hygiene and CI, the step 2 review
   (`dd6aaf9`) the decision records, architecture and threat model, step 4 became change specs
   0002 to 0004 (`d19733d`), and step 5 is change spec 0005, this tooling (`67e6b55`).
2. **[SPEC.md](../SPEC.md)** says what the system must do as numbered requirements, each
   verified by a test, a CI step or review (`5a3ac31`, 86 requirements at the start). A test
   names the IDs it verifies, and `scripts/check-spec-trace.mjs` fails CI when a `[test]`
   requirement has no test and is not on the gap list, or a test cites an ID that does not
   exist (`4c35656`).
3. **A change spec** in [docs/specs](specs/README.md) turns one roadmap step into work: the
   requirements it adds or changes, the changes in order, and what "done" means. It is
   written before the code, from an interview with the user that records what they decided
   and when (each spec's "Decided" lines).
4. **A build session** works from the change spec, one commit per step, and says which step
   each commit is: "Spec 0001 step 4 (A, API side), per ADR 0014" (`32c3234`), "Spec 0001
   step 7 (H, TRC-1, TRC-2)" (`4c35656`). Its last commit updates `SPEC.md` and marks the spec
   Done.
5. **An independent review** reads the result against the spec and the code, and probes it
   by breaking code to see whether a test notices. Its findings become fix commits, an
   amended spec or a new decision record: the 0004 review produced
   [ADR 0017](decisions/0017-csp-and-budget-adjustments.md) (`1e889d1`).
6. **Hooks** make the parts that must never be skipped automatic: the full check when a
   session stops, and a privacy review of every commit that touches absence notes (change
   spec 0005, adjusted by [ADR 0018](decisions/0018-agent-tooling-adjustments.md)).

## What the process caught

### Reasons nobody had recorded

The step 2 review compared every decision record with the commits it cited, and removed the
rationale no commit, comment or note supported (`e52495b`): why the admin role was split
(`ce05498` has no message), a case against canvas rendering written after the fact, with a
claim the measurements contradicted ("every interaction within a frame", while search took 23
to 31 ms), and more. Dates and sources were corrected to the commits that made each decision.
The step 1 review before it had found the same kind of thing in the build configuration:
`.editorconfig` suppression reasons that did not match the code, and a claim that naming
rules were enforced in the build when the command-line build ignores them, which it checked
with a probe file (`e4657ba`, merged with that review's fixes in `fc39c62`).

It also removed one alternative that had been real: declining the dotnet/runtime style was a
choice made in a session and written down nowhere the reviewer could see, and it had to be
restored by hand (`d6eab30`). Hence the rule in AGENTS.md: record a decision as an ADR when it
is made.

### Tests that found production defects

- **`MapInboundClaims`.** The first tests with real signed bearer tokens found that JwtBearer
  renamed `sub` to `ClaimTypes.NameIdentifier`, so with the default username claim a signed-in
  member had no username and a profile was stored without one (AUTH-1, PPL-1). Claims now keep
  their issued names (`477ea16`). The tests fail with the mapping turned back on, and the
  changelog tells deployments that had worked around the old mapping what to change
  (`85a1828`).
- **The encoder escaping.** The CFG-6 test found that the inlined bootstrap configuration was
  serialized with `UnsafeRelaxedJsonEscaping`, so a configuration value holding `</script>`
  ended the script element early. The shell now uses the default encoder, and ADR 0004's
  consequence that said otherwise was corrected (`7187d9b`).

### The access table and the holiday source

`RouteAccessTests` reads every endpoint from the app's route table and fails on a route its
access table does not classify (`abc4828`). But it only ever started the app with
`Holidays:Source=None`, so a route mapped only when a holiday source is configured would never
have been seen. Each access test now runs under `None` and under the shipped default, `Nager`,
and takes the union of both route sets; a probe route mapped only with a source makes it fail
naming the route (`6da7d05`). Tightening the same tests from "not 403" to "below 500" turned up
three 500s, one of which, `POST /api/people` with no body, was left for change spec 0003 to
close with nullable reference types.

### Mutation probes

A test that passes whatever the code does verifies nothing, so tests are checked by breaking
the code they cover and reverting. Recorded probes:

- Four property models were made independent of the code they check, each confirmed by a
  probe: a `year % 4` leap rule, every region's holidays counted for a person, the overlap
  check not excluding the absence being updated, and the wrong letter on a stacked day. Before,
  the leap-year model called the same `DateTime.IsLeapYear` as production, and its queries met
  no century year (`55a6852`).
- Removing the 500 scrub left the ERR-2 tests green, because the default writer omits the
  detail on its own; a test handler that leaks the exception now pins it (`7870d5e`).
- Mapping an extra route with no access-table entry fails the route test (`abc4828`).
- The performance budget had been measuring nothing in two places: the region filter
  baseline timed a click on "All", which with no filter set changes nothing (`5291f63`), and
  the synthetic absences were fixed to 2026, so from 2027 every budget would have passed on an
  empty grid. A budget run now fails when it measured nothing (`8706e79`).
- The `[SEC-3]` summary test passed when run alone, without any flow having run; it now
  passes only when every flow ran and passed, and the shell's refusal of injected inline
  script is checked in the browser (`f8128d8`).
- The spec trace counted commented-out and skipped tests as coverage until its reader was
  rewritten with its own tests (`7fc36c9`).

### This tooling, built the same way

Building change spec 0005 found two things the spec's own commands would have missed:

- Git did not read the `Privacy-Review:` trailer in this repository's commit messages: it
  takes the footer's `---` line for the start of a patch and reads no trailers after it. The
  fixture tests failed, and `--no-divider` fixed it (`d4a24a4`).
- The reviewer subagent, restricted to its read tools, answered in prose and gave no verdict,
  because the schema answer goes through a tool of its own, `StructuredOutput`, which then
  has to be listed (`46c2a2c`).

Its independent review then probed the hooks the way the earlier reviews probed tests: 41
ways of running a commit fed to the commit hook, evil merges, old-base branches, repository
git settings and a prompt injection fed to the reviewer, a headless session trying to escape
the allowlist. The fixes are in [ADR 0018](decisions/0018-agent-tooling-adjustments.md) and
the commits after `01ad000`.

### A spec that asked for too few tests

Change spec 0006 listed UI-11's tests as four cases of the label function, and the build
wrote exactly those (`0ce891f`). The independent review's mutation probes showed what they
missed: dropping the `%SUPPORT_LABEL%` fill in `legal.ts`, swapping label and URL, or
reverting a placeholder in `privacy.md` or `terms.md` left every test green. So did putting
the old `mailto:support@example.com` back into `appsettings.json` (CFG-2's "no value ships"),
because the test host gave every start outside Development a contact of its own. No test
caught it because none was asked for: the spec named how to verify a helper, not the
requirement, and the builder verified what it was told. The fix tests the notices as members
read them and a Production start on only what ships (`fc52621`, `88213df`).

## Hooks and the privacy review

Advice in AGENTS.md is followed most of the time; the two things that must hold every time
are enforced by hooks in the shared [`.claude/settings.json`](../.claude/settings.json):

- **The Stop hook** runs `scripts/check.sh` (every check CI runs that does not need Docker,
  the end-to-end suite and the performance budget included) when a session stops, unless
  nothing has changed since its last pass, and blocks the stop with the failing step and the
  end of its output. A check that still fails after Claude Code's cap of continuations ends
  the turn reporting the failure, not a success.
- **The commit hook** runs the `privacy-reviewer` subagent on every diff about to be
  committed that touches absence notes, as `.claude/privacy-paths.txt` defines it at HEAD,
  and lets the commit through only with a passed verdict for that exact diff, recorded in the
  message as `Privacy-Review: passed sha256:<diff hash>`. It reads every git command in a
  Bash call, however it is run, and refuses the ones that would commit something it has not
  reviewed (a rebase or cherry-pick of note commits, `git commit -a`).
- **What CI adds.** CI recomputes each pushed or pull-request commit's diff hash, merge
  commits included, and fails a commit that touches notes without a matching trailer. That
  catches a commit made outside Claude Code, or by a script the hook cannot see, that skipped
  the review without writing a trailer. It cannot tell a reviewed trailer from one computed
  by hand: `privacy-review.mjs` is public, and the hash only proves the trailer names that
  exact diff. Outside Claude Code the review is followed by convention (`privacy-review.mjs
  review`), and a pull request is read by a person before it is merged.

The note is singled out because it is the one free-text field, the one place the app's
promise to store no health data can leak ([ADR 0007](decisions/0007-no-special-category-data.md)),
and its rules (author only, never in an audit entry, a log, a trace or a metric) are easy to
break in a change that is about something else.

## Limits

- A review only sees what is written down. Decisions made in a session and not recorded can
  be removed as unsupported (`d6eab30`).
- The timing budget runs on shared CI machines: a cold load once measured 284 ms against a
  201 ms budget on a commit that changed no application code (`46c2a2c`'s run). A timing
  breach is now measured a second time before it fails, and every run's figures go to the CI
  job summary ([ADR 0018](decisions/0018-agent-tooling-adjustments.md)); the DOM budget is
  deterministic.
- The privacy reviewer is a model reading a diff. It narrows what reaches `master` unreviewed;
  it does not replace the tests that pin ABS-9, AUD-2 and the projection.
