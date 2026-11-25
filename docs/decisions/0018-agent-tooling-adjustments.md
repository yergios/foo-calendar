# 18. Agent tooling adjustments after review

- Status: Accepted (implementation in the fixes to [change spec 0005](../specs/0005-agent-tooling.md))
- Date: 2026-11-25
- Amends: [0017](0017-csp-and-budget-adjustments.md) (how a timing breach is judged), and
  the 2026-11-23 decision recorded in change spec 0005 that the Stop hook runs the full check
  every time a session stops

## Context

The review of change spec 0005 raised three questions that were the user's to answer:

- **The performance budget failed once in CI** with no app change: a cold load of 284 ms
  against a 201 ms budget, one failure in nine runs. Sixteen local runs never exceeded 1.3
  times the baseline. Build logs and artifacts are not readable without a token, so the CI
  distribution is unknown.
- **The privacy review triggers on listed files or on lines containing the word "note".**
  A leak written without that word, in a file not listed, never reaches the reviewer. The
  rules file and the review machinery were not listed either, so weakening them was not
  itself reviewed.
- **The Stop hook ran the full check, about a minute, on every stop,** including turns that
  changed nothing.

## Decision

- **A timing breach is retried once.** A DOM breach never is. Both attempts are kept in the
  results, and a regression that breaches twice fails. The cold-load figures are also printed
  to the CI job summary, so their distribution can be seen and the budget revisited.
- **The privacy trigger list grows:**
  - `src/Api/Dtos/AbsencesResponse.cs`, `src/Api/Services/PersonService.cs` and
    `docs/threat-model.md`;
  - the review machinery: the rules file, the reviewer's prompt, the review script and the
    commit hook. Changing any of them is itself reviewed.
- **The Stop hook skips the check when nothing has changed since the last green run.** That
  means the same HEAD and the same tracked, staged and untracked content. It reports the
  cached pass. Any change runs the full check.

## Alternatives considered

All offered on 2026-11-25 and declined:

- **Median of three runs:** about 22 s more on every CI run.
- **A higher cold-load floor:** blunts the one guard on startup cost.
- **Listing only the review machinery,** and keeping the app file list as it was.
- **Keeping the full check on every stop,** as first decided.

## Consequences

- A single noisy CI run no longer fails the budget; a real regression still does, at the cost
  of about 11 s on a breach.
- More commits go through the privacy reviewer, at a minute or two each.
- A turn that only answers a question ends at once. The guarantee is unchanged, because an
  unchanged tree has already passed.
