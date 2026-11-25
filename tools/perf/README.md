# Calendar interaction benchmark

`harness.mjs` drives a headless Chromium against the running app over the DevTools
protocol, performs the calendar's interactions (typing a search, clearing it, each sort
mode, hovering across columns and rows, wheel scrolling, toggling a region filter,
switching year) and reports what the renderer's main thread did for each one. No
dependencies beyond Node 22+: it runs on the DevTools driver in `tools/browser`, which the
[end-to-end tests](../e2e/README.md) share.

## Setup

1. A `chrome-headless-shell` binary. Download the current stable one from
   https://googlechromelabs.github.io/chrome-for-testing/ (linux64
   `chrome-headless-shell`), unzip it, and either place it at
   `~/.cache/chrome-headless-shell/chrome-headless-shell-linux64/chrome-headless-shell`
   or point `CHROME_HEADLESS_SHELL` at it.
2. A production build served by the API: `pnpm build` in `src/Web`, then
   `dotnet run --project src/Api` (Development bypasses SSO). The API caches the SPA
   shell, so restart it after every build. `APP_URL` overrides `http://localhost:5055/`.
3. Real data: `scripts/seed.sh --people 100`.

## Run

```
node tools/perf/harness.mjs          # 100 people: whatever the API actually serves
node tools/perf/harness.mjs 500      # 500 people: /api/people and /api/absences answered
node tools/perf/harness.mjs 1000     # with synthetic payloads of the seed's shape
```

The synthetic absences are generated for whichever year the page asks for, so the
calendar has something to draw whatever the date: the year it opens on, and the one the
year switch moves to. The harness finds the controls it drives by their `data-perf`
attributes (`sort`, `sort-<mode>`, `filter`, `region`, `year-previous`, `year-next`), not
by their tooltips or labels; keep those attributes when reworking the header.

Each run prints one line per interaction:

- `busy`: main-thread time spent in that window (union of every traced event), in ms.
- `longest`: the longest contiguous busy run. This is the lag you feel; under 16 ms it
  never costs a frame.
- `blocks>16`: how many contiguous busy runs exceeded a frame.
- `script`/`style`/`layout`/`prepaint`/`paint`: the traced phases. Vue's own work runs in
  microtasks and is not counted under `script`; `busy` minus the other columns is a fair
  estimate of it.
- `wall`: the window's duration, most of it deliberate waiting between steps.

The full trace is written to `/tmp/foo-calendar-trace-<people>.json` (`TRACE_OUT` to
change it) and loads in DevTools' Performance panel. The numbers, and each interaction's DOM
size once it is done, go to `/tmp/foo-calendar-perf-<people>.json` (`RESULT_OUT`).

Numbers are main-thread only (headless software rasterization, so raster and GPU time
are excluded), which is exactly what blocks input.

## Budget

```
node tools/perf/harness.mjs 1000 --budget
```

fails, after printing the usual report, when at 1000 people

- the DOM holds more than 5,000 nodes after any interaction
  ([PERF-1](../../SPEC.md#perf-performance)), or
- an interaction's longest block exceeds its budget: the larger of three times its baseline
  and 16 ms, one frame ([PERF-2](../../SPEC.md#perf-performance),
  [0017](../../docs/decisions/0017-csp-and-budget-adjustments.md)).

It also fails when the run did not measure what it claims to: an expected interaction
missing from the trace, no absence segment drawn after the cold load or after switching
to the previous year, the hover overlays not shown after the hover sweep, or the region
filter not turning on and off. A control the harness cannot find stops the run with an
error naming its `data-perf` hook. Without `--budget`, these are printed as warnings.

A run whose only failures are timing breaches is measured once more, in a fresh browser,
and the budget fails only when that second run fails too
([0018](../../docs/decisions/0018-agent-tooling-adjustments.md)). A DOM breach, or a run that
did not measure what it claims, fails at once. The results file keeps every attempt under
`attempts`, with the last one's numbers and failures at the top; the second run's trace goes
beside the first, with `.retry` before its extension.

CI runs it against the container on every push, with the `chrome-headless-shell` version
pinned in `.github/workflows/ci.yml`, and keeps the results file as an artifact either way.
It also writes every interaction's longest block, per attempt, to the job's summary page,
so the spread of CI timings can be read without downloading anything.
The baselines it reads are in [`baseline.json`](baseline.json): the "after, 1000 people"
column of the table below. Each interaction is held to the baseline whose key its name
starts with (every keystroke to `search-type`, every sort mode to `sort`, every hover sweep
to `hover`); the year switch has no baseline yet and is held to the DOM budget only.

The DOM budget is deterministic. A timing failure can be CI noise, since CI machines are
slower and busier than the one below; the second measurement tells noise from a regression,
which breaches both. The threefold
slack is there to absorb that while still catching work that grows with the team. The
16 ms floor keeps interactions with sub-millisecond baselines, such as hovering, from
failing on noise alone: a block shorter than a frame never costs one, and the 170 ms a
full-grid hover used to take would still fail by far.

### Refreshing the baseline

Only on purpose, in a commit of its own that says why, never to make a red run green:

1. Build and serve the commit to measure as in Setup, on a quiet machine.
2. Run `node tools/perf/harness.mjs 1000` a few times and take each interaction's typical
   `longest`, not its best.
3. Update both the table below (with the machine, browser and date) and `baseline.json`,
   which must stay the table's "after, 1000 people" column.

## Baseline (Ryzen 7 5800X, chrome-headless-shell 152, 2026-09-02)

Longest main-thread block in ms, before and after the windowed grid rewrite.

| interaction                 | before 100 | before 1000 | after 100 | after 1000 |
| --------------------------- | ---------: | ----------: | --------: | ---------: |
| cold render (after data)    |        369 |       3,322 |        73 |         67 |
| search keystroke            |        226 |       2,156 |        23 |         31 |
| clear search                |        458 |       4,559 |        17 |         16 |
| sort                        |        369 |       3,112 |        14 |         17 |
| region filter               |        448 |       4,845 |       n/a |       21 \* |
| hover, one column crossing  |         28 |         170 |       1.3 |        1.7 |
| scroll, one wheel tick      |         44 |         459 |        12 |         13 |
| DOM nodes                   |     47,616 |     455,611 |     3,366 |      3,304 |

\* Re-measured on 2026-11-24, same machine and browser, as the typical `longest` of four
runs (20.2 to 21.3 ms per toggle). The 6 ms first recorded here measured a click on the
filter's "All" box, which with no filter set changes nothing; the harness now picks a
region and checks that the filter turned on and off. The 100-person figure was not
re-measured.

`baseline.json` holds the "after 1000" column; it is what the budget above reads.

The "before" grid rendered one `<td>` per person per day, so style, layout, paint and
hit testing all scaled with the team. The grid now renders only the rows in view and only
absence segments inside them, so everything scales with the viewport instead.

What remains in the largest blocks (search keystroke, sort) is creating the segment
elements of rows entering the window: the seed's 52 absences per person is about 60
segments per row, and ~20 rows enter on a sort. Real teams are sparser. If it ever needs
to go lower, the next step is pooling row DOM (key rows and segments by slot and patch
them in place) rather than mounting and unmounting; nothing else in the grid scales.
