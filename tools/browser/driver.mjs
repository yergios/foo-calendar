// A dependency-free DevTools-protocol driver for chrome-headless-shell, shared by the
// performance harness (tools/perf) and the end-to-end suite (tools/e2e). It launches the
// browser, holds one websocket session to its first tab, and records what the page says:
// console messages, uncaught exceptions and Content-Security-Policy violations.
//
// Node 22+ (global WebSocket and fetch). See docs/decisions/0016 for why this exists
// rather than Playwright.
import { spawn } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";

/** Where the browser is looked for when CHROME_HEADLESS_SHELL is not set. */
export const DEFAULT_BROWSER = path.join(
  process.env.HOME ?? "",
  ".cache/chrome-headless-shell/chrome-headless-shell-linux64/chrome-headless-shell",
);

export const settle = (ms) => new Promise((r) => setTimeout(r, ms));

/** One websocket to one DevTools target: commands with replies, and events by name. */
class Session {
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
    const session = new Session(ws);
    ws.onmessage = (m) => {
      const msg = JSON.parse(m.data);
      if (msg.id !== undefined) {
        const p = session.pending.get(msg.id);
        session.pending.delete(msg.id);
        if (msg.error) p.rej(new Error(`${msg.error.message} ${msg.error.data ?? ""}`));
        else p.res(msg.result);
      } else for (const l of session.listeners.get(msg.method) ?? []) l(msg.params);
    };
    return session;
  }

  send(method, params = {}) {
    const id = ++this.id;
    this.ws.send(JSON.stringify({ id, method, params }));
    return new Promise((res, rej) => this.pending.set(id, { res, rej }));
  }

  on(method, fn) {
    (this.listeners.get(method) ?? this.listeners.set(method, []).get(method)).push(fn);
  }

  once(method) {
    return new Promise((resolve) => {
      const fn = (params) => {
        const list = this.listeners.get(method);
        list.splice(list.indexOf(fn), 1);
        resolve(params);
      };
      this.on(method, fn);
    });
  }
}

// Installed in every document before its own scripts run. A violation is reported through
// a binding rather than the console, so a page that silences or replaces console.* cannot
// hide one. Runs in the page's main world, which the page's own policy does not apply to:
// DevTools-injected scripts are exempt.
const VIOLATION_BINDING = "__reportCspViolation";
const VIOLATION_LISTENER = `document.addEventListener("securitypolicyviolation", (e) => {
  globalThis.${VIOLATION_BINDING}(JSON.stringify({
    directive: e.effectiveDirective,
    blockedURI: e.blockedURI,
    sourceFile: e.sourceFile,
    line: e.lineNumber,
    sample: e.sample,
  }));
});`;

/** The page in the browser's first tab, with what it has reported so far. */
class Page {
  constructor(session) {
    this.session = session;
    /** console.* calls and browser log entries: { type, text }. */
    this.console = [];
    /** Uncaught exceptions: { text }. */
    this.exceptions = [];
    /** Content-Security-Policy violations, from the event and from the browser's own
     * console report of one: { directive, blockedURI, sourceFile, line, sample } or
     * { report }. */
    this.violations = [];
  }

  async attach() {
    const s = this.session;
    s.on("Runtime.consoleAPICalled", (p) =>
      this.console.push({
        type: p.type,
        text: p.args.map((a) => a.value ?? a.description ?? "").join(" "),
      }),
    );
    s.on("Runtime.exceptionThrown", (p) =>
      this.exceptions.push({
        text: p.exceptionDetails.exception?.description ?? p.exceptionDetails.text,
      }),
    );
    s.on("Log.entryAdded", ({ entry }) => {
      this.console.push({ type: entry.level, text: entry.text, source: entry.source });
      if (/Content Security Policy/i.test(entry.text)) this.violations.push({ report: entry.text });
    });
    s.on("Runtime.bindingCalled", (p) => {
      if (p.name === VIOLATION_BINDING) this.violations.push(JSON.parse(p.payload));
    });

    await this.send("Page.enable");
    await this.send("Runtime.enable");
    await this.send("Log.enable");
    await this.send("Runtime.addBinding", { name: VIOLATION_BINDING });
    await this.send("Page.addScriptToEvaluateOnNewDocument", { source: VIOLATION_LISTENER });
  }

  send(method, params) {
    return this.session.send(method, params);
  }

  on(method, fn) {
    this.session.on(method, fn);
  }

  once(method) {
    return this.session.once(method);
  }

  /** The expression's value, awaited if it is a promise. Throws what the page threw. */
  async evaluate(expression, awaitPromise = true) {
    const r = await this.send("Runtime.evaluate", {
      expression,
      awaitPromise,
      returnByValue: true,
    });
    if (r.exceptionDetails)
      throw new Error("page threw: " + JSON.stringify(r.exceptionDetails).slice(0, 500));
    return r.result.value;
  }

  /** Resolves after k animation frames. */
  frames(k) {
    return this.evaluate(
      `new Promise(r=>{let n=0;const f=()=>{if(++n>=${k})r();else requestAnimationFrame(f)};requestAnimationFrame(f)})`,
    );
  }

  /** Navigates and waits for the load event. */
  async navigate(url) {
    const loaded = this.once("Page.loadEventFired");
    const r = await this.send("Page.navigate", { url });
    if (r.errorText) throw new Error(`navigating to ${url}: ${r.errorText}`);
    await loaded;
  }

  /** Polls the expression until it is truthy, and returns its value. A page that is
   * mid-navigation answers with an error, which counts as not yet. */
  async waitFor(expression, { timeout = 10_000, interval = 50, what = expression } = {}) {
    const deadline = Date.now() + timeout;
    for (;;) {
      try {
        const value = await this.evaluate(expression);
        if (value) return value;
      } catch {
        // Not there yet, or the document is being replaced.
      }
      if (Date.now() > deadline) throw new Error(`timed out after ${timeout} ms waiting for ${what}`);
      await settle(interval);
    }
  }

  /** Clicks the centre of the first element matching the selector, and whose text is
   * `text` when given, as a real pointer would: hit-tested, so an element covered by
   * another is not what gets the click. Waits for an enabled one to appear. */
  async click(selector, { text, timeout } = {}) {
    const box = await this.waitFor(
      `(()=>{const e=[...document.querySelectorAll(${JSON.stringify(selector)})].find(e=>!e.disabled&&(${JSON.stringify(text ?? null)}===null||e.textContent.trim()===${JSON.stringify(text ?? null)}));if(!e)return null;e.scrollIntoView({block:"center",inline:"center"});const r=e.getBoundingClientRect();return r.width&&r.height?{x:r.left+r.width/2,y:r.top+r.height/2}:null})()`,
      { what: text ? `${selector} "${text}"` : selector, timeout },
    );
    await this.clickAt(box.x, box.y);
  }

  /** A left click at viewport coordinates. */
  async clickAt(x, y) {
    await this.send("Input.dispatchMouseEvent", { type: "mouseMoved", x, y });
    await this.send("Input.dispatchMouseEvent", { type: "mousePressed", x, y, button: "left", clickCount: 1 });
    await this.send("Input.dispatchMouseEvent", { type: "mouseReleased", x, y, button: "left", clickCount: 1 });
  }

  /** Writes a PNG of the viewport. */
  async screenshot(file) {
    const { data } = await this.send("Page.captureScreenshot", { format: "png" });
    writeFileSync(file, Buffer.from(data, "base64"));
  }
}

/**
 * Starts chrome-headless-shell with a fresh profile and connects to its first tab.
 * Resolves to { page, close }; close() kills the browser and removes the profile, and also
 * runs when the process exits.
 */
export async function launch({
  executable = process.env.CHROME_HEADLESS_SHELL ?? DEFAULT_BROWSER,
  width = 1920,
  height = 1080,
} = {}) {
  const port = 9333 + Math.floor(Math.random() * 100);
  const profile = mkdtempSync(path.join(tmpdir(), "foo-calendar-browser-"));
  const browser = spawn(
    executable,
    [
      `--remote-debugging-port=${port}`,
      `--user-data-dir=${profile}`,
      `--window-size=${width},${height}`,
      "--no-first-run",
      "--no-default-browser-check",
      "--disable-extensions",
      "--disable-background-networking",
      "--hide-scrollbars",
      "about:blank",
    ],
    { stdio: ["ignore", "ignore", "pipe"] },
  );
  // Kept only to say why, if the browser never comes up.
  let stderr = "";
  browser.stderr.on("data", (chunk) => {
    stderr = (stderr + chunk).slice(-2000);
  });

  let closed = false;
  const close = () => {
    if (closed) return;
    closed = true;
    browser.kill("SIGKILL");
    try {
      rmSync(profile, { recursive: true, force: true, maxRetries: 3 });
    } catch {
      // A browser still writing as it dies can leave the profile busy; it is in tmp.
    }
  };
  process.on("exit", close);

  let version = null;
  for (let i = 0; i < 100 && !version; i++) {
    try {
      version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
    } catch {
      await settle(100);
    }
  }
  if (!version) {
    close();
    throw new Error(`browser did not start: ${executable}\n${stderr}`);
  }

  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  const target = targets.find((t) => t.type === "page");
  const page = new Page(await Session.connect(target.webSocketDebuggerUrl));
  await page.attach();
  await page.send("Emulation.setDeviceMetricsOverride", {
    width,
    height,
    deviceScaleFactor: 1,
    mobile: false,
  });

  return { page, close };
}
