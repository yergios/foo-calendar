---
name: perf
description: Measure the calendar's main-thread cost at 100 and 1000 people against a production build and compare it with tools/perf/baseline.json. Use after a change to the calendar grid, its data loading or its header controls, or when asked how fast the calendar is.
---

# Measure the calendar's performance

The harness and what its columns mean are described in `tools/perf/README.md`; read it first.
PERF-1 and PERF-2 in `SPEC.md` are the budgets.

## Guards

- **Never change `tools/perf/baseline.json` or the README's baseline table** unless the
  user asks for a new baseline in so many words. A red budget is fixed in the code, never by
  moving the baseline. When asked, follow the README's "Refreshing the baseline" procedure
  exactly, and commit the baseline in a commit of its own whose message says why.
- Measure a production build, never `pnpm dev`: the dev server's numbers mean nothing here.
- Timings are noisy. Run each size at least three times and report the typical `longest`,
  not the best. A single slow run is not a regression; a DOM count over budget always is.
  `--budget` already measures a timing breach a second time and fails only when both runs
  breach; read both attempts in the results file before calling it noise or a regression.

## Steps

1. Build and serve the commit to measure:
   ```
   pnpm --dir src/Web build
   dotnet run --project src/Api
   ```
   The API caches the shell, so restart it after every build. It listens on
   `http://localhost:5055/`; set `APP_URL` if it is elsewhere.
2. Seed real data for the 100-person run: `scripts/seed.sh --people 100` (it wipes the
   database first; say so before running it if the database might hold anything the user
   wants).
3. Run, three times each:
   ```
   node tools/perf/harness.mjs 100
   node tools/perf/harness.mjs 1000 --budget
   ```
   The 1000-person run answers with synthetic payloads, so it needs no seeding.
4. Compare each interaction's `longest` with `tools/perf/baseline.json` (1000 people) and
   with the README's table (both sizes), and the DOM node count with the 5,000 budget.
5. Report a table: interaction, baseline, typical `longest` at 100 and at 1000, and the
   DOM size, and name anything that moved by more than noise. For a regression, the trace
   (`TRACE_OUT`, default `/tmp/foo-calendar-trace-<people>.json`) loads in DevTools'
   Performance panel and shows where the time went.
6. Stop the API you started.
