// Drives a headless Chromium over the DevTools protocol against the running app, traces
// the renderer main thread, and reports what each calendar interaction costs. See
// README.md for setup and how to read the numbers.
//
//   node tools/perf/harness.mjs [people] [--budget]
//
//   people = 100 (real data) | 500 | 1000 (synthetic)
//   --budget: fail unless the run is within PERF-1 and PERF-2 against baseline.json, and
//             measured everything it should have. A run whose only failures are timing
//             breaches is measured once more, and fails only if the second run breaches too.
//
// Interactions are bracketed with console.timeStamp('start:<name>') / ('end:<name>')
// markers in the page; the report is what the main thread did between them.
import { appendFileSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { launch, settle, withTimeout } from "../browser/driver.mjs";

const args = process.argv.slice(2);
const PEOPLE = Number(args.find((a) => !a.startsWith("--")) ?? 100);
const BUDGET = args.includes("--budget");
const APP_URL = process.env.APP_URL ?? "http://localhost:5055/";

// ---- synthetic payloads for scaling runs ----
const FIRST = [
  "Olivia",
  "Liam",
  "Emma",
  "Noah",
  "Ava",
  "Elijah",
  "Sophia",
  "Lucas",
  "Isabella",
  "Mason",
  "Mia",
  "Ethan",
  "Amelia",
  "Logan",
  "Harper",
  "Aiden",
  "Evelyn",
  "James",
  "Abigail",
  "Benjamin",
  "Ella",
  "Alexander",
  "Scarlett",
  "Michael",
  "Grace",
  "Daniel",
  "Chloe",
  "Matthew",
  "Victoria",
  "Henry",
];
const LAST = [
  "Smith",
  "Johnson",
  "Williams",
  "Brown",
  "Jones",
  "Garcia",
  "Miller",
  "Davis",
  "Rodriguez",
  "Martinez",
  "Hernandez",
  "Lopez",
  "Gonzalez",
  "Wilson",
  "Anderson",
  "Thomas",
  "Taylor",
  "Moore",
  "Jackson",
  "Martin",
  "Lee",
  "Perez",
  "Thompson",
  "White",
  "Harris",
  "Sanchez",
  "Clark",
  "Ramirez",
  "Lewis",
  "Robinson",
];
const REGIONS = [
  "CH-ZH",
  "US-NY",
  "US-CA",
  "GB-ENG",
  "DE-BE",
  "FR-IDF",
  "ES-MD",
  "JP-13",
  "AU-NSW",
  "CA-ON",
];
// Answers /api/people and /api/absences for the scaling runs without reseeding the
// database. The shape (names, regions, the per-person absence plan) mirrors
// tools/Seed/Program.cs, which is the source of truth; keep the plan below in step with
// its AbsencePlan.
//
// Absences are generated for whichever year a request asks for, so the calendar always has
// segments to draw: the year it opens on is the current one, and the year switch moves to
// the one before. The same people and the same plan each year, from a seed per year.
function synthetic(n) {
  const people = Array.from({ length: n }, (_, i) => ({
    id: i + 1,
    username: `user${i}`,
    displayName: `${FIRST[i % 30]} ${LAST[Math.floor(i / 30) % 30]}${i >= 900 ? " " + i : ""}`,
    homeCity: REGIONS[i % 10],
  }));
  people.sort((a, b) => a.displayName.localeCompare(b.displayName));
  const regionLabels = Object.fromEntries(
    REGIONS.flatMap((r) => [
      [r, r + " label"],
      [r.split("-")[0], r.split("-")[0] + " country"],
    ]),
  );
  const byYear = new Map();
  return {
    people: JSON.stringify({ people, regionLabels }),
    absences: (year) => {
      if (!byYear.has(year)) byYear.set(year, JSON.stringify({ absences: absencesFor(people, year) }));
      return byYear.get(year);
    },
  };
}

function absencesFor(people, year) {
  let seed = 7 + year;
  const rnd = () => (seed = (seed * 48271) % 2147483647) / 2147483647;
  const iso = (d) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
  const absences = [];
  // Ids unique across years, as the database's are.
  let id = 1 + (year % 100) * 1_000_000;
  // The seed's plan (tools/Seed), so a synthetic team draws the same rows a real one does.
  const plan = [
    ["Vacation", 8, 2, 7],
    ["Other", 18, 1, 3],
    ["Appointment", 26, 1, 1],
  ];
  for (const p of people)
    for (const [type, count, minD, maxD] of plan)
      for (let k = 0; k < count; k++) {
        const start = new Date(year, 0, 1 + Math.floor(rnd() * 358));
        const len = minD + Math.floor(rnd() * (maxD - minD + 1));
        const end = new Date(start);
        end.setDate(end.getDate() + len - 1);
        const timed = type === "Appointment";
        absences.push({
          id: id++,
          personId: p.id,
          startDate: iso(start),
          endDate: iso(end),
          type,
          note: null,
          createdAt: `${year}-01-01T09:00:00`,
          startTime: timed ? "09:00:00" : null,
          endTime: timed ? "10:00:00" : null,
        });
      }
  absences.sort(
    (a, b) => a.startDate.localeCompare(b.startDate) || a.id - b.id,
  );
  return absences;
}

/**
 * One measured run: a fresh browser opens the app, performs every interaction under a
 * trace, and the trace is read back into one result per interaction. Writes the trace to
 * `traceFile`; returns the results and what the run found it could not measure.
 */
async function measureOnce(traceFile) {
  const browser = await launch();
  const page = browser.page;
  const send = (m, p) => page.send(m, p);
  const evaluate = (expression, awaitPromise) => page.evaluate(expression, awaitPromise);
  const frames = (k) => page.frames(k);
  const mark = (label) =>
    evaluate(`console.timeStamp(${JSON.stringify(label)})`, false);

  if (PEOPLE !== 100) {
    const bodies = synthetic(PEOPLE);
    await send("Fetch.enable", {
      patterns: [
        { urlPattern: "*/api/people" },
        { urlPattern: "*/api/absences?*" },
      ],
    });
    page.on("Fetch.requestPaused", (p) => {
      const url = new URL(p.request.url);
      const body = url.pathname.endsWith("/api/people")
        ? bodies.people
        : bodies.absences(Number(url.searchParams.get("from")?.slice(0, 4)));
      send("Fetch.fulfillRequest", {
        requestId: p.requestId,
        responseCode: 200,
        responseHeaders: [{ name: "Content-Type", value: "application/json" }],
        body: Buffer.from(body).toString("base64"),
      });
    });
  }


  // ---- tracing ----
  let traceChunks = [];
  page.on("Tracing.dataCollected", (p) => traceChunks.push(...p.value));
  const tracingComplete = () =>
    new Promise((r) => page.on("Tracing.tracingComplete", r));
  await send("Tracing.start", {
    traceConfig: {
      includedCategories: [
        "devtools.timeline",
        "disabled-by-default-devtools.timeline",
        "blink.console",
        "blink.user_timing",
        "v8.execute",
        "disabled-by-default-devtools.timeline.frame",
      ],
      recordMode: "recordContinuously",
    },
    transferMode: "ReportEvents",
  });

  // What the run found that means a number below would not be measuring what it says:
  // reported, and failing the budget, alongside the budget's own failures.
  const problems = [];

  /** The absence segments drawn in the rows in view. */
  const segmentCount = () =>
    evaluate(`document.querySelectorAll('.calendar-row [role=gridcell]').length`);

  /** Clicks the first element matching the selector, failing the run when there is none
   * rather than measuring a click on nothing. */
  const clickFirst = (selector) =>
    evaluate(
      `(()=>{const e=document.querySelector(${JSON.stringify(selector)});if(!e)throw new Error(${JSON.stringify(`nothing matches ${selector}`)});e.click()})()`,
    );
  /** Clicks the control a data-perf hook names (see the components). */
  const clickHook = (hook) => clickFirst(`[data-perf="${hook}"]`);
  /** Opens or closes an IconPopover, whose hook sits on its root, around the trigger. */
  const togglePopover = (hook) => clickFirst(`[data-perf="${hook}"] > button`);

  // DOM size after each interaction, read once its window has closed so the count is not
  // part of what was measured.
  const domNodes = new Map();
  const countNodes = () => evaluate(`document.getElementsByTagName('*').length`);

  async function measure(name, action, settleFrames = 6) {
    await mark(`start:${name}`);
    await action();
    await frames(settleFrames);
    await settle(30);
    await mark(`end:${name}`);
    domNodes.set(name, await countNodes());
  }

  // ---- scenarios ----
  {
    const navStart = Date.now();
    await mark("start:cold-load");
    await send("Page.navigate", { url: APP_URL });
    for (let i = 0; ; i++) {
      // Only the rows in view exist, so "ready" is any row at all.
      if (await evaluate(`document.querySelectorAll('.calendar-row').length > 0`))
        break;
      if (i === 600) throw new Error(`the grid showed no row within 30 s of opening ${APP_URL}`);
      await settle(50);
    }
    await frames(3);
    await settle(100);
    await mark("end:cold-load");
    console.log(
      `grid ready after ${Date.now() - navStart} ms wall (${PEOPLE} rows)`,
    );
    const domStats = await evaluate(
      `({nodes: document.getElementsByTagName('*').length, cells: document.querySelectorAll('[role=gridcell]').length, rows: document.querySelectorAll('.calendar-row').length})`,
    );
    console.log("DOM:", JSON.stringify(domStats));
    domNodes.set("cold-load", domStats.nodes);
    // Rows with nothing in them cost next to nothing to render: a grid with no absence drawn
    // would pass every budget while measuring none of the work the budget is about.
    if ((await segmentCount()) === 0)
      problems.push("no absence segment is drawn after the cold load, so nothing real was measured");

    // Search: type "liam" one char at a time, then clear with Escape.
    await evaluate(`document.getElementById('personSearch').focus()`);
    for (const ch of "liam") {
      await measure(
        `search-type-'${ch}'`,
        () => send("Input.insertText", { text: ch }),
        8,
      );
    }
    await measure(
      "search-clear-escape",
      () =>
        send("Input.dispatchKeyEvent", {
          type: "keyDown",
          key: "Escape",
          code: "Escape",
          windowsVirtualKeyCode: 27,
        }),
      8,
    );
    await send("Input.dispatchKeyEvent", {
      type: "keyUp",
      key: "Escape",
      code: "Escape",
      windowsVirtualKeyCode: 27,
    });
    await evaluate(`document.getElementById('personSearch').blur()`);

    // Sort modes via the popover, each option found by its hook.
    async function sortTo(mode) {
      await togglePopover("sort");
      await frames(2);
      await clickHook(`sort-${mode}`);
    }
    for (const [name, mode] of [
      ["sort-region", "region"],
      ["sort-absent-today", "absentToday"],
      ["sort-name", "name"],
    ]) {
      await measure(name, () => sortTo(mode), 8);
    }

    // Hover: sweep the mouse across 40 columns on the 6th row, one move per two frames.
    const rowRect = await evaluate(
      // Geometry read off the rendered grid (corner cell width, a day header's width, a row's
      // height) rather than restated here, so the sweep keeps landing on cells if it changes.
      `(()=>{const row=document.querySelectorAll('.calendar-row')[5];const r=row.getBoundingClientRect();const c=document.querySelector('.overflow-auto').getBoundingClientRect();const [corner, day]=document.querySelectorAll('[role=columnheader]');return {x:c.left+1+corner.getBoundingClientRect().width+3.5*day.getBoundingClientRect().width,y:r.top+r.height/2,w:day.getBoundingClientRect().width,h:r.height}})()`,
    );
    await send("Input.dispatchMouseEvent", {
      type: "mouseMoved",
      x: Math.round(rowRect.x),
      y: Math.round(rowRect.y),
    });
    await frames(2);
    await measure(
      "hover-sweep-40-columns",
      async () => {
        for (let i = 0; i < 40; i++) {
          await send("Input.dispatchMouseEvent", {
            type: "mouseMoved",
            x: Math.round(rowRect.x + i * rowRect.w),
            y: Math.round(rowRect.y),
          });
          await frames(2);
        }
      },
      4,
    );
    // Same sweep with synthetic mouseover events (bypasses the hit test, exercises the app's handler + style + paint).
    await measure(
      "hover-synthetic-40-columns",
      async () => {
        for (let i = 0; i < 40; i++) {
          await evaluate(
            `(()=>{const c=document.querySelector('.overflow-auto');const t=document.elementFromPoint(${Math.round(rowRect.x + i * rowRect.w)}, ${Math.round(rowRect.y)}) ?? c;t.dispatchEvent(new MouseEvent('mouseover', { bubbles: true, clientX: ${Math.round(rowRect.x + i * rowRect.w)}, clientY: ${Math.round(rowRect.y)} }));t.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: ${Math.round(rowRect.x + i * rowRect.w)}, clientY: ${Math.round(rowRect.y)} }));})()`,
            false,
          );
          await frames(2);
        }
      },
      4,
    );
    const overlays = await evaluate(
      `[...document.querySelectorAll('.calendar-hover')].filter(e => !e.hidden).length`,
    );
    console.log("hover overlays shown after sweep:", overlays, "(expect 2)");
    // The sweeps measure moving these; with them not shown, the pointer missed the grid.
    if (overlays !== 2)
      problems.push(`${overlays} hover overlays shown after the sweep, not 2: the hover sweeps hit nothing`);
    // Hover: sweep down 20 rows in one column.
    await measure(
      "hover-sweep-20-rows",
      async () => {
        for (let i = 0; i < 20; i++) {
          await send("Input.dispatchMouseEvent", {
            type: "mouseMoved",
            x: Math.round(rowRect.x + 5 * rowRect.w),
            y: Math.round(rowRect.y + i * rowRect.h),
          });
          await frames(2);
        }
      },
      4,
    );

    // Scroll: vertical wheel inside the grid, then horizontal.
    const box = await evaluate(
      `(()=>{const c=document.querySelector('.overflow-auto');const r=c.getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2}})()`,
    );
    await measure(
      "scroll-vertical-20-ticks",
      async () => {
        for (let i = 0; i < 20; i++) {
          await send("Input.dispatchMouseEvent", {
            type: "mouseWheel",
            x: box.x,
            y: box.y,
            deltaX: 0,
            deltaY: 120,
          });
          await frames(1);
        }
      },
      6,
    );
    await measure(
      "scroll-horizontal-20-ticks",
      async () => {
        for (let i = 0; i < 20; i++) {
          await send("Input.dispatchMouseEvent", {
            type: "mouseWheel",
            x: box.x,
            y: box.y,
            deltaX: 240,
            deltaY: 0,
          });
          await frames(1);
        }
      },
      6,
    );

    // Region filter: pick the first region (shows skeleton, then applies), then unpick. A
    // region's own checkbox, not "All", which only ever clears the filter.
    async function toggleFirstRegion() {
      await togglePopover("filter");
      await frames(2);
      await clickHook("region");
      await frames(2);
      const checked = await evaluate(`document.querySelector('[data-perf="region"]').checked`);
      await togglePopover("filter");
      return checked;
    }
    let filtered;
    await measure("region-filter-on", async () => (filtered = await toggleFirstRegion()), 10);
    if (filtered !== true) problems.push("the region filter did not turn on");
    await measure("region-filter-off", async () => (filtered = await toggleFirstRegion()), 10);
    if (filtered !== false) problems.push("the region filter did not turn off");

    // Full grid rebuild: previous year (every row's cells rebuild, cache miss), then back.
    // Not optional: a control that cannot be found fails the run like any other.
    async function setYear(hook) {
      await togglePopover("filter");
      await frames(2);
      await clickHook(hook);
      await frames(2);
      await togglePopover("filter");
    }
    await measure("year-switch-prev", () => setYear("year-previous"), 30);
    if ((await segmentCount()) === 0)
      problems.push("no absence segment is drawn in the previous year, so its switch measured an empty grid");
    await measure("year-switch-next", () => setYear("year-next"), 30);
  }

  await settle(200);
  const done = tracingComplete();
  await send("Tracing.end");
  await withTimeout(done, 60_000, "the browser to hand over the trace");

  // ---- analysis ----
  const stamps = traceChunks.filter(
    (e) => e.name === "TimeStamp" && e.args?.data?.message,
  );
  const mainTid = stamps[0]?.tid;
  const mainPid = stamps[0]?.pid;
  const windows = new Map();
  for (const s of stamps) {
    const m = s.args.data.message;
    const [kind, ...rest] = m.split(":");
    const name = rest.join(":");
    const w = windows.get(name) ?? windows.set(name, {}).get(name);
    w[kind] = s.ts;
  }
  const TOP = "RunTask";
  const CATS = [
    "FunctionCall",
    "EventDispatch",
    "UpdateLayoutTree",
    "Layout",
    "PrePaint",
    "Paint",
    "Layerize",
    "HitTest",
    "TimerFire",
    "Commit",
    "ParseHTML",
    "EvaluateScript",
  ];
  const main = traceChunks.filter(
    (e) =>
      e.pid === mainPid &&
      e.tid === mainTid &&
      e.ph === "X" &&
      e.dur !== undefined,
  );
  const out = [];
  const results = [];
  out.push(
    `\n${"interaction".padEnd(30)} ${"busy".padStart(8)} ${"longest".padStart(8)} ${"blocks>16".padStart(8)} ${"script".padStart(8)} ${"style".padStart(8)} ${"layout".padStart(8)} ${"prepaint".padStart(8)} ${"paint".padStart(8)} ${"wall".padStart(7)}  (ms, renderer main thread; busy=union of all events, blocks>16=contiguous busy runs over a frame)`,
  );
  for (const [name, w] of windows) {
    if (w.start === undefined || w.end === undefined) continue;
    const inWin = main
      .filter((e) => e.ts >= w.start && e.ts + e.dur <= w.end)
      .sort((a, b) => a.ts - b.ts);
    // Busy = union of every main-thread event interval (script, style, layout, paint all
    // count, whichever top-level task they sit under). Longest = longest contiguous busy run.
    const merged = [];
    let cur = null;
    for (const e of inWin) {
      const s0 = e.ts,
        e0 = e.ts + e.dur;
      if (cur && s0 <= cur[1]) cur[1] = Math.max(cur[1], e0);
      else merged.push((cur = [s0, e0]));
    }
    const sum = (n) =>
      inWin.filter((e) => e.name === n).reduce((a, e) => a + e.dur, 0) / 1000;
    const busy = merged.reduce((a, [s0, e0]) => a + (e0 - s0), 0) / 1000;
    const longest = Math.max(0, ...merged.map(([s0, e0]) => e0 - s0)) / 1000;
    const over16 = merged.filter(([s0, e0]) => e0 - s0 > 16000).length;
    const wall = (w.end - w.start) / 1000;
    results.push({ name, busy, longest, blocksOver16: over16, wall, domNodes: domNodes.get(name) });
    out.push(
      `${name.padEnd(30)} ${busy.toFixed(1).padStart(8)} ${longest.toFixed(1).padStart(8)} ${String(over16).padStart(8)} ${(sum("FunctionCall") + sum("EvaluateScript")).toFixed(1).padStart(8)} ${sum("UpdateLayoutTree").toFixed(1).padStart(8)} ${sum("Layout").toFixed(1).padStart(8)} ${sum("PrePaint").toFixed(1).padStart(8)} ${sum("Paint").toFixed(1).padStart(8)} ${wall.toFixed(0).padStart(7)}`,
    );
  }
  console.log(out.join("\n"));
  // Loadable in DevTools' Performance panel for a closer look at any window.
  writeFileSync(traceFile, JSON.stringify({ traceEvents: traceChunks }));
  console.log(`trace written to ${traceFile}`);
  browser.close();
  return { results, problems };
}

// ---- budget (PERF-1, PERF-2) ----
// baseline.json holds the "after, 1000 people" column of README.md. Each interaction is
// held to the baseline whose key its name starts with, the longest such key: every
// search keystroke to "search-type", every sort mode to "sort", and so on. An interaction
// with no baseline (the year switch) is held to the DOM budget only.
const baseline = JSON.parse(
  readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "baseline.json"), "utf8"),
);
// The budgets themselves are SPEC.md's, not measurements. The floor is one frame
// (docs/decisions/0017): three times a sub-millisecond baseline is a margin CI noise
// crosses, and a block shorter than a frame never costs one.
const MAX_DOM_NODES = 5000; // PERF-1
const SLACK = 3; // PERF-2
const FLOOR_MS = 16; // PERF-2

// Every interaction the run is meant to measure. One missing means a step was skipped or
// its markers never reached the trace, and the budget would pass on what was not measured.
const EXPECTED = [
  "cold-load",
  "search-type-'l'",
  "search-type-'i'",
  "search-type-'a'",
  "search-type-'m'",
  "search-clear-escape",
  "sort-region",
  "sort-absent-today",
  "sort-name",
  "hover-sweep-40-columns",
  "hover-synthetic-40-columns",
  "hover-sweep-20-rows",
  "scroll-vertical-20-ticks",
  "scroll-horizontal-20-ticks",
  "region-filter-on",
  "region-filter-off",
  "year-switch-prev",
  "year-switch-next",
];
const budgetOf = (name) =>
  Object.keys(baseline.longestBlockMs)
    .filter((key) => name.startsWith(key))
    .sort((a, b) => b.length - a.length)[0];

/** Holds one run to PERF-1 and PERF-2. Returns its failures, and whether every one of them
 * is a timing breach, the only kind a second run may clear. */
function judge({ results, problems }) {
  const failures = [...problems];
  for (const name of EXPECTED)
    if (!results.some((r) => r.name === name)) failures.push(`${name}: not measured`);
  if (BUDGET && PEOPLE !== baseline.people)
    failures.push(`the budget is for ${baseline.people} people, not ${PEOPLE}`);
  const timing = [];
  for (const r of results) {
    if (r.domNodes > MAX_DOM_NODES)
      failures.push(`${r.name}: ${r.domNodes} DOM nodes, over the budget of ${MAX_DOM_NODES}`);
    const key = budgetOf(r.name);
    if (key === undefined) continue;
    r.budgetMs = Math.max(baseline.longestBlockMs[key] * SLACK, FLOOR_MS);
    if (r.longest > r.budgetMs)
      timing.push(
        `${r.name}: longest block ${r.longest.toFixed(1)} ms, over its budget of ${r.budgetMs.toFixed(1)} ms (the larger of ${SLACK} x its baseline of ${baseline.longestBlockMs[key]} ms and ${FLOOR_MS} ms)`,
      );
  }
  return { failures: [...failures, ...timing], timingOnly: failures.length === 0 && timing.length > 0 };
}

const traceOut =
  process.env.TRACE_OUT ?? path.join(tmpdir(), `foo-calendar-trace-${PEOPLE}.json`);
const resultFile =
  process.env.RESULT_OUT ?? path.join(tmpdir(), `foo-calendar-perf-${PEOPLE}.json`);

// A timing breach is measured once more, and fails only when the second run breaches too
// (docs/decisions/0018): a shared CI machine can stall one run, a regression slows both.
// A DOM breach, or a run that measured nothing, is never noise and fails at once.
const attempts = [];
for (;;) {
  const traceFile =
    attempts.length === 0 ? traceOut : traceOut.replace(/(\.json)?$/, ".retry$1");
  const run = await measureOnce(traceFile);
  const verdict = judge(run);
  attempts.push({ interactions: run.results, failures: verdict.failures, traceFile });
  for (const p of run.problems) console.log(`warning: ${p}`);
  if (!BUDGET || !verdict.timingOnly || attempts.length === 2) break;
  console.log(
    `\ntiming breach, measuring once more before failing:\n${verdict.failures.map((f) => `  ${f}`).join("\n")}`,
  );
}
const final = attempts.at(-1);

// Machine-readable, for CI to keep whatever the outcome: the last attempt's numbers at the
// top, as before the retry existed, and every attempt in order.
writeFileSync(
  resultFile,
  JSON.stringify(
    { people: PEOPLE, interactions: final.interactions, failures: final.failures, attempts },
    null,
    2,
  ),
);
console.log(`results written to ${resultFile}`);

// Every interaction's longest block on the CI run's summary page, passing or not, so the
// distribution of CI timings can be read without downloading an artifact.
if (process.env.GITHUB_STEP_SUMMARY) {
  const header = attempts.map((_, i) => `attempt ${i + 1}`);
  const rows = final.interactions.map((r) => {
    const cells = attempts.map(
      (a) => a.interactions.find((x) => x.name === r.name)?.longest.toFixed(1) ?? "n/a",
    );
    return `| ${r.name} | ${cells.join(" | ")} | ${r.budgetMs?.toFixed(1) ?? "DOM only"} | ${r.domNodes} |`;
  });
  appendFileSync(
    process.env.GITHUB_STEP_SUMMARY,
    [
      `### Performance at ${PEOPLE} people: longest main-thread block, ms`,
      "",
      `| interaction | ${header.join(" | ")} | budget | DOM nodes |`,
      `| --- | ${header.map(() => "---:").join(" | ")} | ---: | ---: |`,
      ...rows,
      "",
      final.failures.length === 0 ? "Within budget." : `Over budget: ${final.failures.length} failure(s).`,
      "",
    ].join("\n"),
  );
}

if (BUDGET) {
  if (final.failures.length > 0) {
    const retried = attempts.length === 2 ? " (and again when measured a second time)" : "";
    console.log(`\nbudget FAILED${retried}:\n${final.failures.map((f) => `  ${f}`).join("\n")}`);
    // Annotations on the run's summary page, when run by GitHub Actions.
    if (process.env.GITHUB_ACTIONS)
      for (const f of final.failures) console.log(`::error title=Performance budget::${f}`);
    process.exit(1);
  }
  const retried =
    attempts.length === 2 ? ", on a second run after the first breached a timing budget" : "";
  console.log(
    `\nbudget OK${retried}: every interaction under ${MAX_DOM_NODES} DOM nodes and within the larger of ${SLACK} x its baseline and ${FLOOR_MS} ms`,
  );
}
process.exit(0);
