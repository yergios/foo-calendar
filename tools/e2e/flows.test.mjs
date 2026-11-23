// The app's main flows in a real browser, against the production build in Development
// (the dev identity, holidays off): `node --test tools/e2e/flows.test.mjs`. See README.md.
//
// Each test's name carries the requirement IDs it verifies in brackets, which
// scripts/check-spec-trace.mjs reads like Vitest's. Every flow also fails on any
// Content-Security-Policy violation or uncaught exception the page reports while it runs,
// and saves a screenshot, the console and the violations when it fails.
//
// The tests run in order and build on each other: onboarding creates the profile the
// calendar flows use.
import assert from "node:assert/strict";
import { mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { after, before, test } from "node:test";

import { launch, settle } from "../browser/driver.mjs";

/** The app with every permission. */
const APP_URL = new URL(process.env.APP_URL ?? "http://localhost:8080/");
/** A second instance whose dev identity holds no permission
 * (Auth:DevUser:GrantAllPermissions=false). */
const NO_PERMISSIONS_URL = new URL(process.env.APP_URL_NO_PERMISSIONS ?? "http://localhost:8081/");
/** Where a failing flow leaves its screenshot, console and violations. */
const OUT = process.env.E2E_OUT ?? path.join(tmpdir(), "foo-calendar-e2e");

/** The prefix everything the app stores in the browser lives under (lib/browserCache.ts). */
const STORAGE_PREFIX = "foo-calendar:";

let browser;
let page;

before(async () => {
  browser = await launch();
  page = browser.page;
});

after(() => browser?.close());

/** Calls the API directly. The dev identity authenticates every request, so no token. */
async function api(base, route, init = {}) {
  const response = await fetch(new URL(route, base), {
    ...init,
    headers: { "Content-Type": "application/json", ...init.headers },
  });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}

function slug(name) {
  return name.replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").toLowerCase();
}

/** Wraps a flow: it must leave no new violation or uncaught exception behind, and on any
 * failure saves what the page showed and said. */
function flow(fn) {
  return async (t) => {
    const violations = page.violations.length;
    const exceptions = page.exceptions.length;
    try {
      await fn(t);
      assert.deepEqual(page.violations.slice(violations), [], "Content-Security-Policy violations");
      assert.deepEqual(page.exceptions.slice(exceptions), [], "uncaught exceptions");
    } catch (error) {
      mkdirSync(OUT, { recursive: true });
      const base = path.join(OUT, slug(t.name));
      await page.screenshot(`${base}.png`).catch(() => {});
      writeFileSync(`${base}.console.json`, JSON.stringify(page.console, null, 2));
      writeFileSync(`${base}.violations.json`, JSON.stringify(page.violations, null, 2));
      writeFileSync(`${base}.exceptions.json`, JSON.stringify(page.exceptions, null, 2));
      throw error;
    }
  };
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

test(
  "[SEC-4] the API reference loads under its own policy",
  flow(async () => {
    await page.navigate(new URL("scalar/", APP_URL).href);
    // Rendered from the OpenAPI document, which it fetched itself.
    await page.waitFor(`document.body.innerText.includes("/api/config/bootstrap")`, {
      what: "the API reference listing the routes",
      timeout: 20_000,
    });
    // Anything it would fetch late, such as icons, has had its chance to be refused.
    await settle(500);
  }),
);

test("[SEC-3] no flow caused a Content-Security-Policy violation", () => {
  assert.deepEqual(page.violations, []);
});
