#!/usr/bin/env node
// The Stop hook (AGT-2): runs scripts/check.sh when a Claude Code session stops in this
// repository, and on a failure blocks the stop, telling Claude which step failed and how
// its output ended. Wired in .claude/settings.json; tested by stop-check.test.mjs with a
// stub check.sh.
//
// An unchanged tree is not checked again (docs/decisions/0018). After a pass, the hook
// records a fingerprint of the tree: HEAD, the staged and unstaged changes, and every
// untracked file that .gitignore does not exclude, with its content. When a stop finds the
// same fingerprint, the check is skipped and the earlier pass reported; any difference runs
// it in full. A pass is recorded only when the tree did not change while the check ran.
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
import { createHash } from "node:crypto";
import {
  createWriteStream,
  lstatSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  readlinkSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const check = path.join(repo, "scripts", "check.sh");

/** How long the check may run: about twice the 63 s a full run took when measured
 * (2026-11-25, warm build caches). .claude/settings.json gives this hook 180 s, so the
 * check is stopped first, its trap stops the instances it started, and this hook still
 * reports the timeout rather than being killed without a word. STOP_CHECK_TIMEOUT_SECONDS
 * overrides it, for the tests. */
const CHECK_TIMEOUT_MS = (Number(process.env.STOP_CHECK_TIMEOUT_SECONDS) || 120) * 1000;

/** How long a check stopped with SIGTERM gets to stop what it started before the whole
 * process group is killed. */
const GRACE_MS = 10_000;

/** How many lines of the check's output go back to Claude with a failure. */
const TAIL_LINES = 40;

/** A block count older than this belongs to a session that has ended. */
const STALE_MS = 24 * 60 * 60 * 1000;

const cap = Number(process.env.CLAUDE_CODE_STOP_HOOK_BLOCK_CAP) || 8;

const input = JSON.parse(readFileSync(0, "utf8") || "{}");

const git = (args) => spawnSync("git", args, { cwd: repo, maxBuffer: 1024 * 1024 * 1024 });
const gitDir = git(["rev-parse", "--absolute-git-dir"]).stdout.toString().trim();
const stateDir = path.join(gitDir, "stop-check");
mkdirSync(stateDir, { recursive: true });
const session = String(input.session_id ?? "unknown").replace(/[^\w-]/g, "_");
const countFile = path.join(stateDir, `${session}.blocks`);
const logFile = path.join(stateDir, "last-run.log");
const passFile = path.join(stateDir, "last-pass.json");

for (const name of readdirSync(stateDir))
  if (name.endsWith(".blocks")) {
    const file = path.join(stateDir, name);
    try {
      if (Date.now() - statSync(file).mtimeMs > STALE_MS) rmSync(file, { force: true });
    } catch {
      // Removed meanwhile.
    }
  }

/** A hash of everything the check would see of the tree that git tracks or could: HEAD,
 * the staged diff, the unstaged diff, and each untracked, not ignored file's path, mode and
 * content. Ignored files (build outputs, node_modules) are not part of it. */
function fingerprint() {
  const hash = createHash("sha256");
  const part = (label, data) => hash.update(`\0${label}\0`).update(data);
  const head = git(["rev-parse", "--verify", "--quiet", "HEAD"]).stdout.toString().trim();
  part("head", head || "unborn");
  const options = ["--no-color", "--no-ext-diff", "--no-textconv", "--binary", "--full-index"];
  part("staged", git(["diff", "--cached", ...options]).stdout);
  part("unstaged", git(["diff", ...options]).stdout);
  const untracked = git(["ls-files", "--others", "--exclude-standard", "-z"])
    .stdout.toString("utf8")
    .split("\0")
    .filter(Boolean);
  for (const file of untracked) {
    const full = path.join(repo, file);
    try {
      const stat = lstatSync(full);
      part("file", `${file}\0${stat.mode}`);
      part("content", stat.isSymbolicLink() ? readlinkSync(full) : readFileSync(full));
    } catch {
      part("gone", file);
    }
  }
  return hash.digest("hex");
}

/** Runs the check in a process group of its own, so a timeout, or a signal this hook
 * receives, stops everything it started (the API instances, the browser), and returns its
 * exit code and output. */
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

    const signalGroup = (signal) => {
      try {
        process.kill(-child.pid, signal);
      } catch {
        // Already gone.
      }
    };
    // SIGTERM first, so check.sh's trap stops what it started; then SIGKILL for whatever
    // ignored it.
    let killer = null;
    const stop = (signal) => {
      signalGroup(signal);
      killer ??= setTimeout(() => signalGroup("SIGKILL"), GRACE_MS);
    };

    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      stop("SIGTERM");
    }, CHECK_TIMEOUT_MS);

    // Claude Code stopping this hook (a timeout of its own, an interrupt) must not leave
    // the check running on its own, in its own process group.
    let signalled = null;
    for (const signal of ["SIGTERM", "SIGINT", "SIGHUP"])
      process.on(signal, () => {
        signalled = signal;
        stop(signal);
      });

    child.on("close", (code) => {
      clearTimeout(timer);
      clearTimeout(killer);
      log.end();
      if (signalled) process.exit(1);
      resolve({ code, timedOut, output: Buffer.concat(chunks).toString("utf8") });
    });
  });
}

function readPass() {
  try {
    return JSON.parse(readFileSync(passFile, "utf8"));
  } catch {
    return null;
  }
}

const before = fingerprint();
const pass = readPass();
if (pass?.fingerprint === before) {
  rmSync(countFile, { force: true });
  console.log(
    JSON.stringify({
      systemMessage: `scripts/check.sh not run again: nothing has changed since it passed at ${pass.at}.`,
    }),
  );
  process.exit(0);
}

const { code, timedOut, output } = await runCheck();

if (code === 0 && !timedOut) {
  rmSync(countFile, { force: true });
  if (fingerprint() === before)
    writeFileSync(
      passFile,
      JSON.stringify({ fingerprint: before, at: new Date().toISOString() }, null, 2) + "\n",
    );
  else rmSync(passFile, { force: true });
  process.exit(0);
}

const lines = output.trimEnd().split("\n");
const waitingAt = lines.findLastIndex((l) => /^check\.sh: another check is running/.test(l));
const stillWaiting = waitingAt >= 0 && !lines.slice(waitingAt).some((l) => l.startsWith("==> ["));
const failedStep = [...output.matchAll(/^check\.sh: step failed: (.+?)(?: \(\d+ s\))?$/gm)].at(-1);
const interruptedAt = [...output.matchAll(/^check\.sh: interrupted(?: during: (.+))?$/gm)].at(-1);
const seconds = CHECK_TIMEOUT_MS / 1000;

// Another session's check holding the lock is not something to fix in this tree.
let what;
let todo;
if (timedOut && stillWaiting) {
  what = `scripts/check.sh was still waiting for another check in this repository (${lines[waitingAt].replace(/^.*\(|\);.*$/g, "")}) after ${seconds} s, and was stopped`;
  todo = `Nothing in this tree failed and nothing needs fixing: another session is running its check. Stop again once it has finished`;
} else {
  what = timedOut
    ? `scripts/check.sh did not finish within ${seconds} s and was stopped`
    : interruptedAt
      ? `scripts/check.sh was interrupted${interruptedAt[1] ? ` during "${interruptedAt[1]}"` : ""}, by something other than this hook`
      : failedStep
        ? `scripts/check.sh failed at step "${failedStep[1]}"`
        : `scripts/check.sh exited with code ${code} without naming a step`;
  todo = `A change is done when it passes: fix the cause, not the check, then stop again`;
}
const tail = lines.slice(-TAIL_LINES).join("\n");
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
    reason: `${what}. ${todo} (attempt ${blocks + 1} of ${cap}).\n\n${details}`,
  }),
);
