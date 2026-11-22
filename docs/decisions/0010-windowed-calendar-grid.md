# 10. Render the calendar as a windowed grid of absence segments

- Status: Accepted
- Date: 2026-09-02
- Source: commit `0320114`, [tools/perf](../../tools/perf/README.md)

## Context

The calendar was a `<table>` with one cell per person per day: about 36,500 cells at 100
people. Profiling with the tracing harness in `tools/perf` showed every interaction paid the
browser's style, layout, paint and hit-test passes over that whole DOM, so cost grew
linearly with the team. JavaScript was not the problem: search and sort logic measured under
a millisecond.

Longest main-thread block before the change: a search keystroke took 226 ms at 100 people
and 2.2 s at 1000; clearing the search 4.6 s at 1000; a hover crossing 170 ms at 1000.

## Decision

Replace the table with a windowed, layered grid:

- Only the rows in view are rendered.
- Each absence is one element per contiguous segment, not one per day
  (`lib/absenceSegments.ts`), positioned by transform.
- Today's column, the viewer's row and the hover highlight are single overlays moved by
  transform.
- Pointer events resolve by arithmetic over fixed geometry (`lib/gridGeometry.ts`), not by
  hit-testing elements.

## Alternatives considered

- **Keep the table and reduce re-rendering** (memoizing rows and cells, as `b10f996`,
  `18d13d7` and `d5426d9` had done). `0320114` measured JavaScript as already proportional
  to change: the cost was in the browser's passes over the DOM that existed, not in Vue
  re-rendering it, so this could not remove it.
- **A canvas or WebGL grid.** Rejected on 2026-09-01, before the windowed grid was built;
  the reasoning was not recorded. It stays rejected unless new profiling shows a new
  bottleneck.
- **Progressive rendering of the full grid.** Cut. The only reason recorded is that
  stale-while-revalidate already covers repeat visits ([0011](0011-client-side-stale-while-revalidate.md)).

## Consequences

- About 3,300 DOM nodes at any team size (47,616 before at 100 people, 455,611 at 1000).
- Longest block after: cold render 67-73 ms, search keystroke 23-31 ms, sort 14-17 ms,
  hover 1.3-1.7 ms, scroll tick 12-13 ms, at 100 and 1000 people.
- The grid's geometry is fixed (row height, column width), which is what makes coordinate
  lookup possible; a design that needs variable row heights would have to revisit this.
- Measure grid changes before and after with `node tools/perf/harness.mjs [100|500|1000]`.
  If it ever needs to go lower, the next step the harness README names is pooling row DOM
  rather than mounting and unmounting rows.
