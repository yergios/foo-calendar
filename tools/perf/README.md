# Calendar interaction benchmark

`harness.mjs` drives a headless Chromium against the running app over the DevTools
protocol, performs the calendar's interactions (typing a search, clearing it, each sort
mode, hovering across columns and rows, wheel scrolling, toggling a region filter,
switching year) and reports what the renderer's main thread did for each one. No
dependencies beyond Node 22+.

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
change it) and loads in DevTools' Performance panel.

Numbers are main-thread only (headless software rasterization, so raster and GPU time
are excluded), which is exactly what blocks input.

## Baseline (Ryzen 7 5800X, chrome-headless-shell 152, 2026-09-02)

Longest main-thread block in ms, before and after the windowed grid rewrite.

| interaction                 | before 100 | before 1000 | after 100 | after 1000 |
| --------------------------- | ---------: | ----------: | --------: | ---------: |
| cold render (after data)    |        369 |       3,322 |        73 |         67 |
| search keystroke            |        226 |       2,156 |        23 |         31 |
| clear search                |        458 |       4,559 |        17 |         16 |
| sort                        |        369 |       3,112 |        14 |         17 |
| region filter               |        448 |       4,845 |         6 |          6 |
| hover, one column crossing  |         28 |         170 |       1.3 |        1.7 |
| scroll, one wheel tick      |         44 |         459 |        12 |         13 |
| DOM nodes                   |     47,616 |     455,611 |     3,366 |      3,304 |

The "before" grid rendered one `<td>` per person per day, so style, layout, paint and
hit testing all scaled with the team. The grid now renders only the rows in view and only
absence segments inside them, so everything scales with the viewport instead.

What remains in the largest blocks (search keystroke, sort) is creating the segment
elements of rows entering the window: the seed's 52 absences per person is about 60
segments per row, and ~20 rows enter on a sort. Real teams are sparser. If it ever needs
to go lower, the next step is pooling row DOM (key rows and segments by slot and patch
them in place) rather than mounting and unmounting; nothing else in the grid scales.
