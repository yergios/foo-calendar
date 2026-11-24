// The app's main flows in a real browser, against the production build in Development
// (holidays off): most of them as the dev identity, and the sign-in flows at the end
// through a stub OIDC provider this file starts (oidc-stub.mjs).
// `node --test --test-timeout=120000 tools/e2e/flows.test.mjs`. See README.md.
//
// Each test's name carries the requirement IDs it verifies in brackets, which
// scripts/check-spec-trace.mjs reads like Vitest's. Every flow also fails on any
// Content-Security-Policy violation it does not name as expected, and on any uncaught
// exception the page reports while it runs, and saves a screenshot and what the page said
// during that flow when it fails.
//
// The tests run in order and build on each other: onboarding creates the profile the
// calendar flows use.
import assert from "node:assert/strict";
import { mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { after, before, test } from "node:test";

import { launch, settle } from "../browser/driver.mjs";
import { startOidcStub } from "./oidc-stub.mjs";

/** The app with every permission. */
const APP_URL = new URL(process.env.APP_URL ?? "http://localhost:8080/");
/** A second instance whose dev identity holds no permission
 * (Auth:DevUser:GrantAllPermissions=false). */
const NO_PERMISSIONS_URL = new URL(process.env.APP_URL_NO_PERMISSIONS ?? "http://localhost:8081/");
/** Instances that sign in through the stub provider (Auth:Provider=Oidc): one whose
 * Auth:Authority is the stub's own-origin tenant, and two on its split tenant, whose token
 * endpoint is on a second origin, without and with that origin in Auth:ExtraOrigins. */
const OIDC_URL = new URL(process.env.APP_URL_OIDC ?? "http://localhost:8082/");
const OIDC_SPLIT_URL = new URL(process.env.APP_URL_OIDC_SPLIT ?? "http://localhost:8083/");
const OIDC_SPLIT_EXTRA_URL = new URL(
  process.env.APP_URL_OIDC_SPLIT_EXTRA_ORIGIN ?? "http://localhost:8084/",
);
/** Where the stub provider listens: its issuer's port, and its second origin's. The app
 * instances above are configured with these. */
const OIDC_STUB_PORT = Number(process.env.OIDC_STUB_PORT ?? 9000);
const OIDC_STUB_TOKEN_PORT = Number(process.env.OIDC_STUB_TOKEN_PORT ?? 9001);
/** Where a failing flow leaves its screenshot, console and violations. */
const OUT = process.env.E2E_OUT ?? path.join(tmpdir(), "foo-calendar-e2e");

/** The prefix everything the app stores in the browser lives under (lib/browserCache.ts). */
const STORAGE_PREFIX = "foo-calendar:";

let browser;
let page;
let stub;

before(async () => {
  stub = await startOidcStub({ port: OIDC_STUB_PORT, tokenPort: OIDC_STUB_TOKEN_PORT });
  browser = await launch();
  page = browser.page;
});

after(async () => {
  browser?.close();
  await stub?.close();
});

/** Calls the API directly. The dev identity authenticates every request, so no token. */
async function api(base, route, init = {}) {
  const response = await fetch(new URL(route, base), {
    ...init,
    headers: { "Content-Type": "application/json", ...init.headers },
  });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}

/** Polls a condition on what the test process holds, such as the violations reported. */
async function until(condition, what, timeout = 5_000) {
  const deadline = Date.now() + timeout;
  while (!condition()) {
    if (Date.now() > deadline) throw new Error(`timed out after ${timeout} ms waiting for ${what}`);
    await settle(50);
  }
}

function slug(name) {
  return name.replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").toLowerCase();
}

/** How many flows there are, and how many ran to the end with nothing unexpected: what
 * the [SEC-3] summary at the end reads. */
let flowCount = 0;
let flowsPassed = 0;

/** Wraps a flow for test(): it must leave no violation behind that `expected` does not
 * match, and no uncaught exception, and on any failure it saves what the page showed and
 * what it said while this flow ran.
 *
 * @param {(t: import("node:test").TestContext) => Promise<void>} fn
 * @param {{ expected?: ((violation: object) => boolean)[] }} [options] Violations the flow
 *   expects, each matched by a predicate, so a known one never hides a new one.
 */
function flow(fn, { expected = [] } = {}) {
  flowCount++;
  return async (t) => {
    const from = {
      console: page.console.length,
      violations: page.violations.length,
      exceptions: page.exceptions.length,
    };
    const own = (kind) => page[kind].slice(from[kind]);
    try {
      await fn(t);
      const unexpected = own("violations").filter((v) => !expected.some((match) => match(v)));
      assert.deepEqual(unexpected, [], "Content-Security-Policy violations");
      assert.deepEqual(own("exceptions"), [], "uncaught exceptions");
      flowsPassed++;
    } catch (error) {
      mkdirSync(OUT, { recursive: true });
      const base = path.join(OUT, slug(t.name));
      await page.screenshot(`${base}.png`).catch(() => {});
      for (const kind of ["console", "violations", "exceptions"])
        writeFileSync(`${base}.${kind}.json`, JSON.stringify(own(kind), null, 2));
      throw error;
    }
  };
}

/** The path of a URL, or "" when it is not one (an inline script has no file). */
function pathOf(url) {
  try {
    return new URL(url).pathname;
  } catch {
    return "";
  }
}

/** Whether a violation, as the event or as the console report, is the API reference's
 * one known one: Scalar's bundle probing whether eval works, which the reference's policy
 * refuses (docs/decisions/0017). Matched by directive, blocked URI and file, never by
 * line, so the same probe moving within the bundle still matches and nothing else does. */
function scalarEvalProbe(v) {
  const fromScalar = (url) => pathOf(url).endsWith("/scalar/scalar.js");
  if ("report" in v) return /'unsafe-eval'/.test(v.report) && fromScalar(v.url);
  return /^script-src(-elem)?$/.test(v.directive) && v.blockedURI === "eval" && fromScalar(v.sourceFile);
}

/** Whether a violation is an inline script refused, as the event or the console report. */
function inlineScriptRefused(v) {
  if ("report" in v) return /^Executing inline script violates/.test(v.report);
  return v.directive === "script-src-elem" && v.blockedURI === "inline";
}

/** The calendar with the signed-in member's own row in it. */
async function openCalendar() {
  await page.navigate(APP_URL.href);
  await page.waitFor(`document.querySelector(".calendar-mine")`, { what: "your own row" });
}

/** A weekday of this year in view in your own row, away from the name column: where to
 * click, and its date. Every day header exists, in order from 1 January, so a header's
 * position in the list is its day of the year. */
function ownDayInView() {
  return page.evaluate(`(() => {
    const mine = document.querySelector(".calendar-mine").getBoundingClientRect();
    const grid = document.querySelector("[role=grid]").getBoundingClientRect();
    const [corner, ...days] = document.querySelectorAll("[role=columnheader]");
    const left = corner.getBoundingClientRect().right;
    const year = new Date().getFullYear();
    for (const [i, header] of days.entries()) {
      const r = header.getBoundingClientRect();
      const date = new Date(year, 0, 1 + i, 12);
      const weekday = date.getDay() !== 0 && date.getDay() !== 6;
      if (weekday && r.left > left + 2 * r.width && r.right < grid.right - r.width) {
        const iso = [date.getFullYear(), date.getMonth() + 1, date.getDate()]
          .map((n) => String(n).padStart(2, "0")).join("-");
        return { x: r.left + r.width / 2, y: mine.top + mine.height / 2, iso };
      }
    }
    return null;
  })()`);
}

async function ownAbsencesOn(iso) {
  const me = await api(APP_URL, "api/people/me");
  const { body } = await api(APP_URL, `api/absences?from=${iso}&to=${iso}`);
  return body.absences.filter((a) => a.personId === me.body.id);
}

/** Sets a form control's value the way typing or picking would, so v-model sees it. */
function setValue(selector, value) {
  return page.evaluate(`(() => {
    const e = document.querySelector(${JSON.stringify(selector)});
    e.value = ${JSON.stringify(value)};
    e.dispatchEvent(new Event(e.tagName === "SELECT" ? "change" : "input", { bubbles: true }));
  })()`);
}

const modalOpen = `document.querySelector("[aria-modal=true]")`;
const modalClosed = `!document.querySelector("[aria-modal=true]")`;

test(
  "[PPL-1] onboarding: the first visit sets up a profile",
  flow(async () => {
    // A rerun against the same database starts from no profile too.
    await api(APP_URL, "api/people/me", { method: "DELETE" });

    await page.navigate(APP_URL.href);
    await page.click("header button", { text: "Set up profile" });
    await page.waitFor(modalOpen, { what: "the onboarding dialog" });
    await setValue("#displayName", "E2E Member");
    await page.click("[aria-modal=true] button", { text: "Get started" });
    await page.waitFor(modalClosed, { what: "the onboarding dialog to close" });
    await page.waitFor(`document.querySelector(".calendar-mine")`, { what: "your own row" });

    const me = await api(APP_URL, "api/people/me");
    assert.equal(me.status, 200);
    assert.equal(me.body.displayName, "E2E Member");
  }),
);

test(
  "[UI-4] add an absence from an empty day in your own row, edit it, delete it",
  flow(async () => {
    await openCalendar();
    const day = await ownDayInView();
    assert.ok(day, "no weekday of this year is in view");
    assert.deepEqual(await ownAbsencesOn(day.iso), [], `${day.iso} is not empty`);

    await page.clickAt(day.x, day.y);
    await page.waitFor(modalOpen, { what: "the add absence dialog" });
    assert.equal(await page.evaluate(`document.querySelector("#absenceStart").value`), day.iso);
    await page.click("[aria-modal=true] button", { text: "Add absence" });
    await page.waitFor(modalClosed, { what: "the dialog to close after adding" });
    const [added] = await ownAbsencesOn(day.iso);
    assert.equal(added?.type, "Vacation");

    await page.clickAt(day.x, day.y);
    await page.waitFor(`document.querySelector("#absenceType")?.value === "Vacation"`, {
      what: "the absence in the edit dialog",
    });
    await setValue("#absenceType", "Other");
    await page.click("[aria-modal=true] button", { text: "Save changes" });
    await page.waitFor(modalClosed, { what: "the dialog to close after saving" });
    const [edited] = await ownAbsencesOn(day.iso);
    assert.equal(edited?.id, added.id);
    assert.equal(edited.type, "Other");

    await page.clickAt(day.x, day.y);
    await page.waitFor(`document.querySelector("#absenceType")?.value === "Other"`, {
      what: "the edited absence in the edit dialog",
    });
    await page.click("[aria-modal=true] button", { text: "Delete" });
    await page.waitFor(modalClosed, { what: "the dialog to close after deleting" });
    assert.deepEqual(await ownAbsencesOn(day.iso), []);
  }),
);

test(
  "[UI-10] with every permission the admin entry is shown",
  flow(async () => {
    await openCalendar();
    await page.waitFor(
      `[...document.querySelectorAll("header nav a")].some((a) => a.textContent.trim() === "Admin")`,
      { what: "the Admin entry" },
    );
  }),
);

test(
  "[UI-10] with no permission there is no admin entry, and /admin is the 403 page",
  flow(async () => {
    // The navigation shows once there is a profile, so there must be one to look at it.
    const created = await api(NO_PERMISSIONS_URL, "api/people", {
      method: "POST",
      body: JSON.stringify({ displayName: "E2E No Permissions" }),
    });
    assert.ok([201, 409].includes(created.status), `creating the profile: ${created.status}`);

    await page.navigate(NO_PERMISSIONS_URL.href);
    const links = await page.waitFor(
      `(() => {
        const links = [...document.querySelectorAll("header nav a")].map((a) => a.textContent.trim());
        return links.includes("Profile") ? links : null;
      })()`,
      { what: "the navigation" },
    );
    assert.deepEqual(links, ["Home", "Profile"]);

    await page.navigate(new URL("admin", NO_PERMISSIONS_URL).href);
    await page.waitFor(`location.pathname.endsWith("/403")`, { what: "the 403 page" });
    await page.waitFor(`document.body.innerText.includes("403")`, { what: "the 403 page's text" });
  }),
);

test(
  "[AUTH-8] [UI-9] signing out leaves nothing under the app's storage prefix",
  flow(async () => {
    await openCalendar();
    // The calendar has cached what it loaded, so there is something to remove; and
    // another app on the same origin has keys of its own, which must stay.
    await page.waitFor(
      `Object.keys(localStorage).some((k) => k.startsWith(${JSON.stringify(STORAGE_PREFIX)}))`,
      { what: "the app's cache in localStorage" },
    );
    await page.evaluate(`localStorage.setItem("other-app:kept", "1"),
      sessionStorage.setItem("other-app:kept", "1")`);

    // Read by the next document before any of its own scripts run: what signing out left
    // behind, before the reloaded app stores anything again.
    const { identifier } = await page.send("Page.addScriptToEvaluateOnNewDocument", {
      source: `window.__storageAtLoad = {
        local: Object.keys(localStorage),
        session: Object.keys(sessionStorage),
      };`,
    });
    try {
      await page.click("header button", { text: "Sign out" });
      const left = await page.waitFor(`window.__storageAtLoad`, { what: "the page after signing out" });

      const ours = [...left.local, ...left.session].filter((k) => k.startsWith(STORAGE_PREFIX));
      assert.deepEqual(ours, []);
      assert.ok(left.local.includes("other-app:kept"), "another app's localStorage key was removed");
      assert.ok(left.session.includes("other-app:kept"), "another app's sessionStorage key was removed");
    } finally {
      await page.send("Page.removeScriptToEvaluateOnNewDocument", { identifier });
      await page.evaluate(`localStorage.removeItem("other-app:kept"),
        sessionStorage.removeItem("other-app:kept")`);
    }
  }),
);

// Its policy refuses eval, which Scalar's bundle probes for on every load and then does
// without: that one violation is expected, and any other fails the flow.
test(
  "[SEC-4] the API reference loads under its own policy",
  flow(
    async () => {
      await page.navigate(new URL("scalar/", APP_URL).href);
      // Rendered from the OpenAPI document, which it fetched itself.
      await page.waitFor(`document.body.innerText.includes("/api/config/bootstrap")`, {
        what: "the API reference listing the routes",
        timeout: 20_000,
      });
      // Anything it would fetch late, such as icons, has had its chance to be refused.
      await settle(500);
    },
    { expected: [scalarEvalProbe] },
  ),
);

test(
  "[SEC-1] the shell refuses an injected inline script, and its source file is not served",
  flow(async () => {
    await openCalendar();
    // What an injection would amount to: a script element carrying inline code, inserted
    // into the live page. The policy allows the bootstrap script by its hash and nothing
    // else inline.
    await page.evaluate(`(() => {
      const script = document.createElement("script");
      script.textContent = "window.__injected = true";
      document.head.append(script);
    })()`);
    // The report arrives as an event after the refusal, not with it.
    await until(() => page.violations.some(inlineScriptRefused), "the refusal to be reported");
    assert.equal(await page.evaluate(`window.__injected === true`), false, "the inline script ran");

    // Served off disk it would have no policy at all; it is a missing file instead.
    await page.navigate(new URL("index.html", APP_URL).href);
    assert.equal(await page.evaluate(`typeof window.__bootstrap`), "undefined", "the SPA booted");
    assert.match(await page.evaluate(`document.body.innerText`), /404/);
  }, { expected: [inlineScriptRefused] }),
);

// ---- Signing in through an OIDC provider ------------------------------------------------
//
// These run against instances with a real bearer handler, signing in through the stub
// provider, which is on another origin than the app as any provider is. The stub's origin
// is http, which Auth:Authority accepts only in Development (CFG-2), so the instances run
// in Development with Auth:Provider=Oidc set: the dev identity is off, tokens are validated
// as in production, and only the scheme rule and RequireHttpsMetadata are Development's.

/** Records the app's API requests while `fn` runs: the URL, its Authorization header, and
 * the status it was answered with. */
async function apiRequestsDuring(fn) {
  const requests = new Map();
  let recording = true;
  page.on("Network.requestWillBeSent", ({ requestId, request }) => {
    if (recording && new URL(request.url).pathname.includes("/api/"))
      requests.set(requestId, { url: request.url, authorization: request.headers.Authorization });
  });
  page.on("Network.responseReceived", ({ requestId, response }) => {
    const request = requests.get(requestId);
    if (recording && request) request.status = response.status;
  });
  await page.send("Network.enable");
  try {
    await fn();
  } finally {
    recording = false;
    await page.send("Network.disable");
  }
  return [...requests.values()];
}

/** Opens the app, which sends the browser through the stub's sign-in and back, and waits
 * for it to land signed in: the profile setup offered, or the calendar with your row. */
async function signIn(app) {
  await page.navigate(app.href);
  await page.waitFor(
    `location.origin === ${JSON.stringify(app.origin)} && !location.pathname.endsWith("/callback")
      && (document.querySelector(".calendar-mine") || [...document.querySelectorAll("header button")]
        .some((b) => b.textContent.trim() === "Set up profile"))`,
    { what: `signed in at ${app.origin}`, timeout: 20_000 },
  );
}

/** Calls the API as the stub's member, with a token minted as the stub's /token would. */
function apiAsMember(base, route, init = {}) {
  return api(base, route, {
    ...init,
    headers: { Authorization: `Bearer ${stub.accessToken()}`, ...init.headers },
  });
}

/** Whether a violation is the browser refusing a fetch to the stub's second origin. */
function tokenOriginRefused(v) {
  const origin = `http://localhost:${OIDC_STUB_TOKEN_PORT}`;
  if ("report" in v) return v.report.includes(origin) && /Refused to connect|connect-src/.test(v.report);
  return v.directive === "connect-src" && v.blockedURI.startsWith(origin);
}

test(
  "[AUTH-1] [SEC-1] [SEC-3] signing in through the provider, every API call carries its token",
  flow(async () => {
    // A rerun against the same database starts from no profile too.
    await apiAsMember(OIDC_URL, "api/people/me", { method: "DELETE" });
    const callsBefore = stub.calls.length;

    const requests = await apiRequestsDuring(async () => {
      await signIn(OIDC_URL);
      await page.click("header button", { text: "Set up profile" });
      await page.waitFor(modalOpen, { what: "the onboarding dialog" });
      await setValue("#displayName", "E2E Provider Member");
      await page.click("[aria-modal=true] button", { text: "Get started" });
      await page.waitFor(modalClosed, { what: "the onboarding dialog to close" });
      await page.waitFor(`document.querySelector(".calendar-mine")`, { what: "your own row" });
    });

    // Through the provider: its authorize endpoint, then its token endpoint, on its origin.
    const paths = stub.calls.slice(callsBefore).map((c) => c.path);
    assert.ok(paths.includes("/authorize"), `the provider's endpoints called: ${paths}`);
    assert.ok(paths.includes("/token"), `the provider's endpoints called: ${paths}`);

    // Everything but the anonymous bootstrap went with the provider's token, and the API
    // accepted it: the profile was created and the calendar loaded.
    const authenticated = requests.filter((r) => !r.url.includes("/api/config/bootstrap"));
    assert.ok(authenticated.length > 0, "the app made no API call");
    for (const r of authenticated) {
      assert.match(r.authorization ?? "", /^Bearer [\w-]+\.[\w-]+\.[\w-]+$/, `${r.url} carried no token`);
      assert.ok(r.status < 400 || r.status === 404, `${r.url} was answered ${r.status}`);
    }
    const me = await apiAsMember(OIDC_URL, "api/people/me");
    assert.equal(me.body?.displayName, "E2E Provider Member");
  }),
);

test(
  "[AUTH-8] [SEC-3] signing out ends the session at the provider's end-session endpoint",
  flow(async () => {
    await signIn(OIDC_URL);
    const idToken = await page.evaluate(`(() => {
      const key = Object.keys(sessionStorage).find((k) => k.startsWith(${JSON.stringify(`${STORAGE_PREFIX}oidc.user:`)}));
      return key && JSON.parse(sessionStorage.getItem(key)).id_token;
    })()`);
    assert.ok(idToken, "no session stored after signing in");
    const callsBefore = stub.calls.length;

    await page.click("header button", { text: "Sign out" });
    await until(
      () => stub.calls.slice(callsBefore).some((c) => c.path === "/end-session"),
      "the end-session endpoint to be called",
      10_000,
    );

    const endSession = stub.calls.slice(callsBefore).find((c) => c.path === "/end-session");
    assert.equal(endSession.query.id_token_hint, idToken);
    assert.equal(endSession.query.post_logout_redirect_uri, OIDC_URL.href);
    // Back at the app, which the stub, holding no session, signs straight in again.
    await page.waitFor(`location.origin === ${JSON.stringify(OIDC_URL.origin)}`, {
      what: "the browser back at the app",
    });
  }),
);

test(
  "[SEC-1] a token endpoint on another origin is refused unless Auth:ExtraOrigins names it",
  flow(
    async () => {
      const callsBefore = stub.calls.length;
      await page.navigate(OIDC_SPLIT_URL.href);
      // The authorize step is a navigation, which the policy does not govern; exchanging
      // the code is a fetch to the second origin, which it refuses.
      await until(() => page.violations.some(tokenOriginRefused), "the token request to be refused", 20_000);
      await page.waitFor(`location.pathname.endsWith("/500")`, { what: "the sign-in to fail" });
      const reached = stub.calls.slice(callsBefore);
      assert.ok(reached.some((c) => c.path === "/split/authorize"), "the sign-in never started");
      assert.ok(!reached.some((c) => c.path === "/split/token"), "the token request got through");
    },
    { expected: [tokenOriginRefused] },
  ),
);

test(
  "[SEC-1] [SEC-3] with the token endpoint's origin in Auth:ExtraOrigins, signing in passes",
  flow(async () => {
    const callsBefore = stub.calls.length;

    const requests = await apiRequestsDuring(() => signIn(OIDC_SPLIT_EXTRA_URL));

    const tokenCall = stub.calls
      .slice(callsBefore)
      .find((c) => c.path === "/split/token" && c.origin === `http://localhost:${OIDC_STUB_TOKEN_PORT}`);
    assert.ok(tokenCall, "the code was not exchanged at the second origin");
    const authenticated = requests.filter((r) => !r.url.includes("/api/config/bootstrap"));
    assert.ok(authenticated.length > 0, "the app made no API call");
    for (const r of authenticated)
      assert.ok(r.status !== 401 && r.authorization, `${r.url} was not authenticated`);
  }),
);

// Every flow above already fails on a violation it did not expect. This is the summary for
// SEC-3, and it holds only when every one of them ran and passed: run on its own, or with
// any flow skipped, filtered out or failing, it fails rather than passing on nothing.
test("[SEC-3] every flow ran without a Content-Security-Policy violation it did not expect", () => {
  assert.equal(flowsPassed, flowCount, "flows that did not run, or did not pass");
});
