#!/usr/bin/env node
// The Stop hook (AGT-2): runs scripts/check.sh every time a Claude Code session stops in
// this repository, and on a failure blocks the stop, telling Claude which step failed and
// how its output ended. Wired in .claude/settings.json; tested by stop-check.test.mjs with
// a stub check.sh.
//
// Loop protection. Claude Code sets stop_hook_active once a stop hook has kept the turn
// going, and after eight continuations in a row (CLAUDE_CODE_STOP_HOOK_BLOCK_CAP) ends the
// turn whatever the hook says. This hook counts its own consecutive blocks, from the last
// stop that was not already a continuation, and when it reaches that cap it ends the turn
// itself with continue: false and the failure as the stop reason, so a check that keeps
// failing ends the session visibly red rather than with the block silently overridden.
// Its count does not reset when Claude calls a tool, as Claude Code's does, so it may end
// the turn a few blocks sooner; it never ends it later.
//
// Nothing is printed when the check passes: the stop goes ahead.
import { spawn, spawnSync } from "node:child_process";
import { createWriteStream, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const check = path.join(repo, "scripts", "check.sh");

/** How long the check may run: about twice the 57 s a full run took when measured
 * (2026-11-24, warm build caches). .claude/settings.json gives this hook 180 s, so the
 * check is stopped first, its trap stops the instances it started, and this hook still
 * reports the timeout rather than being killed without a word. */
const CHECK_TIMEOUT_MS = 120_000;

/** How many lines of the check's output go back to Claude with a failure. */
const TAIL_LINES = 40;

const cap = Number(process.env.CLAUDE_CODE_STOP_HOOK_BLOCK_CAP) || 8;

const input = JSON.parse(readFileSync(0, "utf8") || "{}");

const gitDir = spawnSync("git", ["rev-parse", "--absolute-git-dir"], {
  cwd: repo,
  encoding: "utf8",
}).stdout.trim();
const stateDir = path.join(gitDir, "stop-check");
mkdirSync(stateDir, { recursive: true });
const session = String(input.session_id ?? "unknown").replace(/[^\w-]/g, "_");
const countFile = path.join(stateDir, `${session}.blocks`);
const logFile = path.join(stateDir, "last-run.log");

/** Runs the check in a process group of its own, so a timeout stops everything it
 * started (the API instances, the browser), and returns its exit code and output. */
function runCheck() {
  return new Promise((resolve) => {
    const log = createWriteStream(logFile);
    const child = spawn("bash", [check], {
      cwd: repo,
      detached: true,
      stdio: ["ignore", "pipe", "pipe"],
    });
    const chunks = [];
    const collect = (chunk) => {
      chunks.push(chunk);
      log.write(chunk);
    };
    child.stdout.on("data", collect);
    child.stderr.on("data", collect);

    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      try {
        process.kill(-child.pid, "SIGTERM");
      } catch {
        // Already gone.
      }
    }, CHECK_TIMEOUT_MS);

    child.on("close", (code) => {
      clearTimeout(timer);
      log.end();
      resolve({ code, timedOut, output: Buffer.concat(chunks).toString("utf8") });
    });
  });
}

const { code, timedOut, output } = await runCheck();

if (code === 0 && !timedOut) {
  rmSync(countFile, { force: true });
  process.exit(0);
}

const failedStep = [...output.matchAll(/^check\.sh: step failed: (.+?)(?: \(\d+ s\))?$/gm)].at(-1);
const what = timedOut
  ? `scripts/check.sh did not finish within ${CHECK_TIMEOUT_MS / 1000} s and was stopped`
  : failedStep
    ? `scripts/check.sh failed at step "${failedStep[1]}"`
    : `scripts/check.sh exited with code ${code} without naming a step`;
const tail = output.trimEnd().split("\n").slice(-TAIL_LINES).join("\n");
const details = `Last ${TAIL_LINES} lines of its output:\n${tail}\n\nThe whole output is in ${logFile}.`;

let blocks = 0;
if (input.stop_hook_active) {
  try {
    blocks = Number(readFileSync(countFile, "utf8")) || 0;
  } catch {
    blocks = 0;
  }
}

if (blocks >= cap) {
  rmSync(countFile, { force: true });
  console.log(
    JSON.stringify({
      continue: false,
      stopReason: `${what}, and still fails after ${blocks} attempts to fix it in this turn. The session stops with the check failing.\n\n${details}`,
    }),
  );
  process.exit(0);
}

writeFileSync(countFile, String(blocks + 1));
console.log(
  JSON.stringify({
    decision: "block",
    reason: `${what}. A change is done when it passes: fix the cause, not the check, then stop again (attempt ${blocks + 1} of ${cap}).\n\n${details}`,
  }),
);
