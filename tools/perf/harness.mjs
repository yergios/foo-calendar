// Drives a headless Chromium over the DevTools protocol against the running app, traces
// the renderer main thread, and reports what each calendar interaction costs. See
// README.md for setup and how to read the numbers.
//
//   node tools/perf/harness.mjs [people]   people = 100 (real data) | 500 | 1000 (synthetic)
//
// Interactions are bracketed with console.timeStamp('start:<name>') / ('end:<name>')
// markers in the page; the report is what the main thread did between them.
import { spawn } from "node:child_process";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";

const PEOPLE = Number(process.argv[2] ?? 100);
const PORT = 9333 + Math.floor(Math.random() * 100);
const APP_URL = process.env.APP_URL ?? "http://localhost:5055/";
const here = path.dirname(new URL(import.meta.url).pathname);
const BROWSER =
  process.env.CHROME_HEADLESS_SHELL ??
  path.join(
    process.env.HOME ?? "",
    ".cache/chrome-headless-shell/chrome-headless-shell-linux64/chrome-headless-shell",
  );

const profile = mkdtempSync(path.join(tmpdir(), "vivaldi-bench-"));
const browser = spawn(
  BROWSER,
  [
    `--remote-debugging-port=${PORT}`,
    `--user-data-dir=${profile}`,
    "--window-size=1920,1080",
    "--no-first-run",
    "--no-default-browser-check",
    "--disable-extensions",
    "--disable-background-networking",
    "--hide-scrollbars",
    "about:blank",
  ],
  { stdio: ["ignore", "ignore", "pipe"] },
);
browser.stderr.on("data", () => {});
process.on("exit", () => browser.kill("SIGKILL"));

async function waitForDevtools() {
  for (let i = 0; i < 100; i++) {
    try {
      return await (
        await fetch(`http://127.0.0.1:${PORT}/json/version`)
      ).json();
    } catch {
      await new Promise((r) => setTimeout(r, 100));
    }
  }
  throw new Error("browser did not start");
}

class CDP {
  constructor(ws) {
    this.ws = ws;
    this.id = 0;
    this.pending = new Map();
    this.listeners = new Map();
  }
  static async connect(url) {
    const ws = new WebSocket(url);
    await new Promise((res, rej) => {
      ws.onopen = res;
      ws.onerror = rej;
    });
    const c = new CDP(ws);
    ws.onmessage = (m) => {
      const msg = JSON.parse(m.data);
      if (msg.id !== undefined) {
        const p = c.pending.get(msg.id);
        c.pending.delete(msg.id);
        if (msg.error)
          p.rej(new Error(`${msg.error.message} ${msg.error.data ?? ""}`));
        else p.res(msg.result);
      } else for (const l of c.listeners.get(msg.method) ?? []) l(msg.params);
    };
    return c;
  }
  send(method, params = {}, sessionId) {
    const id = ++this.id;
    this.ws.send(JSON.stringify({ id, method, params, sessionId }));
    return new Promise((res, rej) => this.pending.set(id, { res, rej }));
  }
  on(method, fn) {
    (
      this.listeners.get(method) ?? this.listeners.set(method, []).get(method)
    ).push(fn);
  }
}

const { webSocketDebuggerUrl } = await waitForDevtools();
const pages = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
const page = pages.find((t) => t.type === "page");
const cdp = await CDP.connect(page.webSocketDebuggerUrl);
const send = (m, p) => cdp.send(m, p);
await send("Page.enable");
await send("Runtime.enable");
await send("Network.enable");
await send("Emulation.setDeviceMetricsOverride", {
  width: 1920,
  height: 1080,
  deviceScaleFactor: 1,
  mobile: false,
});

async function evaluate(expression, awaitPromise = true) {
  const r = await send("Runtime.evaluate", {
    expression,
    awaitPromise,
    returnByValue: true,
  });
  if (r.exceptionDetails)
    throw new Error(
      "page threw: " + JSON.stringify(r.exceptionDetails).slice(0, 500),
    );
  return r.result.value;
}
const frames = (k) =>
  evaluate(
    `new Promise(r=>{let n=0;const f=()=>{if(++n>=${k})r();else requestAnimationFrame(f)};requestAnimationFrame(f)})`,
  );
const mark = (label) =>
  evaluate(`console.timeStamp(${JSON.stringify(label)})`, false);
const settle = (ms) => new Promise((r) => setTimeout(r, ms));

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
function synthetic(n) {
  let seed = 7;
  const rnd = () => (seed = (seed * 48271) % 2147483647) / 2147483647;
  const iso = (d) =>
    `2026-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
  const people = Array.from({ length: n }, (_, i) => ({
    id: i + 1,
    username: `user${i}`,
    displayName: `${FIRST[i % 30]} ${LAST[Math.floor(i / 30) % 30]}${i >= 900 ? " " + i : ""}`,
    homeCity: REGIONS[i % 10],
    emailOnAbsenceReviewed: false,
    emailOnNewAbsenceRequest: false,
    emailOnWeeklyDigest: false,
  }));
  people.sort((a, b) => a.displayName.localeCompare(b.displayName));
  const absences = [];
  let id = 1;
  const plan = [
    ["Vacation", 8, 2, 7],
    ["Sick", 10, 1, 3],
    ["Other", 8, 1, 2],
    ["Appointment", 26, 1, 1],
  ];
  for (const p of people)
    for (const [type, count, minD, maxD] of plan)
      for (let k = 0; k < count; k++) {
        const start = new Date(2026, 0, 1 + Math.floor(rnd() * 358));
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
          status: rnd() < 0.1 ? "Pending" : "Approved",
          note: null,
          createdAt: "2026-09-02T17:10:42.4785229",
          startTime: timed ? "09:00:00" : null,
          endTime: timed ? "10:00:00" : null,
        });
      }
  absences.sort(
    (a, b) => a.startDate.localeCompare(b.startDate) || a.id - b.id,
  );
  const regionLabels = Object.fromEntries(
    REGIONS.flatMap((r) => [
      [r, r + " label"],
      [r.split("-")[0], r.split("-")[0] + " country"],
    ]),
  );
  return {
    people: JSON.stringify({ people, regionLabels }),
    absences: JSON.stringify({ absences }),
  };
}

if (PEOPLE !== 100) {
  const bodies = synthetic(PEOPLE);
  await send("Fetch.enable", {
    patterns: [
      { urlPattern: "*/api/people" },
      { urlPattern: "*/api/absences?*" },
    ],
  });
  cdp.on("Fetch.requestPaused", (p) => {
    const body = p.request.url.endsWith("/api/people")
      ? bodies.people
      : bodies.absences;
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
cdp.on("Tracing.dataCollected", (p) => traceChunks.push(...p.value));
const tracingComplete = () =>
  new Promise((r) => cdp.on("Tracing.tracingComplete", r));
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

const results = [];
async function measure(name, action, settleFrames = 6) {
  await mark(`start:${name}`);
  await action();
  await frames(settleFrames);
  await settle(30);
  await mark(`end:${name}`);
}

// ---- scenarios ----
{
  const navStart = Date.now();
  await mark("start:cold-load");
  await send("Page.navigate", { url: APP_URL });
  for (let i = 0; i < 600; i++) {
    // Old grid: one <tr> per person. New grid: only the rows in view exist, as .calendar-row.
    const n = await evaluate(
      `document.querySelectorAll('tbody tr[data-person-id], .calendar-row').length`,
    );
    if (
      n === PEOPLE ||
      (n > 0 &&
        n >= Math.min(PEOPLE, 10) &&
        (await evaluate(`!!document.querySelector('.calendar-row')`)))
    )
      break;
    await settle(50);
  }
  await frames(3);
  await settle(100);
  await mark("end:cold-load");
  console.log(
    `grid ready after ${Date.now() - navStart} ms wall (${PEOPLE} rows)`,
  );
  const domStats = await evaluate(
    `({nodes: document.getElementsByTagName('*').length, cells: document.querySelectorAll('tbody td, [role=gridcell]').length, rows: document.querySelectorAll('tbody tr, .calendar-row').length})`,
  );
  console.log("DOM:", JSON.stringify(domStats));

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

  // Sort modes via the popover.
  async function sortTo(label) {
    await evaluate(`document.querySelector('button[title="Sort"]').click()`);
    await frames(2);
    await evaluate(
      `[...document.querySelectorAll('button')].find(b => b.textContent.trim().endsWith(${JSON.stringify(label)})).click()`,
    );
  }
  for (const label of [
    "Region, then name",
    "Absent today, then name",
    "Name",
  ]) {
    await measure(
      `sort-${label.split(",")[0].toLowerCase().replace(/ /g, "-")}`,
      () => sortTo(label),
      8,
    );
  }

  // Hover: sweep the mouse across 40 columns on the 6th row, one move per two frames.
  const rowRect = await evaluate(
    `(()=>{const row=document.querySelectorAll('tbody tr[data-person-id], .calendar-row')[5];const r=row.getBoundingClientRect();const c=document.querySelector('.overflow-auto').getBoundingClientRect();return {x:c.left+1+160+3.5*28,y:r.top+r.height/2,w:28}})()`,
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
  console.log(
    "hover overlays shown after sweep:",
    await evaluate(
      `[...document.querySelectorAll('.calendar-hover')].filter(e => !e.hidden).length`,
    ),
    "(expect 2)",
  );
  // Hover: sweep down 20 rows in one column.
  await measure(
    "hover-sweep-20-rows",
    async () => {
      for (let i = 0; i < 20; i++) {
        await send("Input.dispatchMouseEvent", {
          type: "mouseMoved",
          x: Math.round(rowRect.x + 5 * rowRect.w),
          y: Math.round(rowRect.y + i * 36),
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

  // Region filter: pick the first checkbox (shows skeleton, then applies), then unpick.
  async function toggleFirstRegion() {
    await evaluate(
      `document.querySelector('button[title="Region and year"]').click()`,
    );
    await frames(2);
    await evaluate(`document.querySelector('input[type=checkbox]').click()`);
    await frames(2);
    await evaluate(
      `document.querySelector('button[title="Region and year"]').click()`,
    );
  }
  await measure("region-filter-on", toggleFirstRegion, 10);
  await measure("region-filter-off", toggleFirstRegion, 10);

  // Full grid rebuild: previous year (every row's cells rebuild, cache miss), then back.
  async function setYear(delta) {
    await evaluate(
      `document.querySelector('button[title="Region and year"]').click()`,
    );
    await frames(2);
    await evaluate(
      `[...document.querySelectorAll('button')].find(b => /${delta < 0 ? "previous|‹|<" : "next|›|>"}/i.test(b.getAttribute('aria-label') ?? b.textContent) && b.closest('[role=dialog], .absolute, div')).click()`,
    );
    await frames(2);
    await evaluate(
      `document.querySelector('button[title="Region and year"]').click()`,
    );
  }
  try {
    await measure("year-switch-prev", () => setYear(-1), 30);
    await measure("year-switch-next", () => setYear(1), 30);
  } catch (e) {
    console.log("year switch skipped:", e.message.slice(0, 120));
  }
}

await settle(200);
const done = tracingComplete();
await send("Tracing.end");
await done;

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
  out.push(
    `${name.padEnd(30)} ${busy.toFixed(1).padStart(8)} ${longest.toFixed(1).padStart(8)} ${String(over16).padStart(8)} ${(sum("FunctionCall") + sum("EvaluateScript")).toFixed(1).padStart(8)} ${sum("UpdateLayoutTree").toFixed(1).padStart(8)} ${sum("Layout").toFixed(1).padStart(8)} ${sum("PrePaint").toFixed(1).padStart(8)} ${sum("Paint").toFixed(1).padStart(8)} ${wall.toFixed(0).padStart(7)}`,
  );
  results.push({ name, busy, longest, over16 });
}
console.log(out.join("\n"));
// Loadable in DevTools' Performance panel for a closer look at any window.
const traceFile =
  process.env.TRACE_OUT ??
  path.join(tmpdir(), `foo-calendar-trace-${PEOPLE}.json`);
writeFileSync(traceFile, JSON.stringify({ traceEvents: traceChunks }));
console.log(`trace written to ${traceFile}`);
await send("Tracing.end").catch(() => {});
browser.kill("SIGKILL");
process.exit(0);
