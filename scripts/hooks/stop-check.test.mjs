// Tests of the Stop hook, run with `node --test scripts/hooks/stop-check.test.mjs`. Each
// copies the hook into a fresh repository beside a stub scripts/check.sh, and feeds it the
// Stop input Claude Code 2.1.331 sent when recorded (fixtures/stop-input.json, paths
// replaced), so its behaviour is tested without running the real check.
import assert from "node:assert/strict";
import { execFileSync, spawn, spawnSync } from "node:child_process";
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  utimesSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { after, test } from "node:test";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const recorded = JSON.parse(readFileSync(path.join(here, "fixtures", "stop-input.json"), "utf8"));

Object.assign(process.env, {
  GIT_AUTHOR_NAME: "Test",
  GIT_AUTHOR_EMAIL: "test@example.invalid",
  GIT_COMMITTER_NAME: "Test",
  GIT_COMMITTER_EMAIL: "test@example.invalid",
  GIT_CONFIG_GLOBAL: "/dev/null",
  GIT_CONFIG_NOSYSTEM: "1",
});

const repos = [];
after(() => {
  for (const repo of repos) rmSync(repo, { recursive: true, force: true });
});

/** A repository with one commit, the hook in it and a check.sh that runs `body`. Each run
 * of the check leaves a mark in .git, outside what the hook fingerprints. */
function fixture(body) {
  const repo = mkdtempSync(path.join(tmpdir(), "stop-check-"));
  repos.push(repo);
  const git = (...args) => execFileSync("git", args, { cwd: repo });
  git("init", "--quiet");
  mkdirSync(path.join(repo, "scripts", "hooks"), { recursive: true });
  copyFileSync(
    path.join(here, "stop-check.mjs"),
    path.join(repo, "scripts", "hooks", "stop-check.mjs"),
  );
  writeFileSync(path.join(repo, "README.md"), "A repository.\n");
  writeFileSync(path.join(repo, ".gitignore"), "build/\n");
  setCheck(repo, body);
  git("add", "--all");
  git("commit", "--quiet", "--message", "first");
  return repo;
}

function setCheck(repo, body) {
  writeFileSync(
    path.join(repo, "scripts", "check.sh"),
    `#!/usr/bin/env bash\necho run >> "${path.join(repo, ".git", "runs")}"\n${body}\n`,
  );
}

const passing = 'echo "check.sh: all 18 steps passed in 1 s"';
const failing = [
  'echo "==> [7/18] SPA lint"',
  'for i in $(seq 1 60); do echo "lint output line $i"; done',
  'echo "check.sh: step failed: SPA lint (3 s)"',
  "exit 1",
].join("\n");

/** Runs the hook with the recorded input, changed by `input`. */
function stop(repo, input = {}, env = {}) {
  const run = spawnSync(process.execPath, [path.join(repo, "scripts", "hooks", "stop-check.mjs")], {
    input: JSON.stringify({ ...recorded, cwd: repo, ...input }),
    encoding: "utf8",
    env: { ...process.env, ...env },
  });
  assert.equal(run.status, 0, run.stderr);
  return { output: run.stdout.trim() ? JSON.parse(run.stdout) : null, runs: runs(repo) };
}

const runs = (repo) =>
  existsSync(path.join(repo, ".git", "runs"))
    ? readFileSync(path.join(repo, ".git", "runs"), "utf8").split("\n").filter(Boolean).length
    : 0;

/** Whether a process whose command line contains `marker` is running. */
const running = (marker) =>
  spawnSync("pgrep", ["-f", marker], { encoding: "utf8" }).stdout.trim() !== "";

const settle = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

test("[AGT-2] a passing check lets the session stop, silently", () => {
  const repo = fixture(passing);
  const { output, runs } = stop(repo);
  assert.equal(output, null);
  assert.equal(runs, 1);
});

test("[AGT-2] a failing check blocks the stop with the step's name and the output's end", () => {
  const repo = fixture(failing);
  const { output } = stop(repo);
  assert.equal(output.decision, "block");
  assert.match(output.reason, /scripts\/check\.sh failed at step "SPA lint"/);
  assert.match(output.reason, /fix the cause, not the check/);
  assert.match(output.reason, /lint output line 60/);
  assert.doesNotMatch(output.reason, /lint output line 1\n/);
  assert.match(output.reason, /stop-check\/last-run\.log/);
});

test("[AGT-2] an unchanged tree is not checked again, and the earlier pass is reported", () => {
  const repo = fixture(passing);
  stop(repo);
  const { output, runs } = stop(repo, { stop_hook_active: true });
  assert.equal(runs, 1);
  assert.match(output.systemMessage, /not run again: nothing has changed since it passed at/);
  assert.ok(existsSync(path.join(repo, ".git", "stop-check", "last-pass.json")));
});

test("[AGT-2] any change to the tree runs the check again", () => {
  const repo = fixture(passing);
  const git = (...args) => execFileSync("git", args, { cwd: repo });
  let expected = 1;
  stop(repo);
  const changes = [
    ["a tracked file edited", () => writeFileSync(path.join(repo, "README.md"), "Edited.\n")],
    ["the same edit staged", () => git("add", "README.md")],
    ["an untracked file", () => writeFileSync(path.join(repo, "new.txt"), "new\n")],
    ["an untracked file's content", () => writeFileSync(path.join(repo, "new.txt"), "newer\n")],
    ["a commit", () => git("commit", "--quiet", "--message", "edit")],
    ["the check itself", () => setCheck(repo, `${passing}\n# changed`)],
  ];
  for (const [what, change] of changes) {
    change();
    expected += 1;
    assert.equal(stop(repo).runs, expected, what);
    assert.equal(stop(repo).runs, expected, `${what}, then nothing`);
  }
});

test("[AGT-2] an ignored file is not part of the tree", () => {
  const repo = fixture(passing);
  stop(repo);
  mkdirSync(path.join(repo, "build"));
  writeFileSync(path.join(repo, "build", "out.dll"), "binary\n");
  assert.equal(stop(repo).runs, 1);
});

test("[AGT-2] a failing check is run again on the next stop, the same tree included", () => {
  const repo = fixture(failing);
  stop(repo);
  stop(repo, { stop_hook_active: true });
  assert.equal(runs(repo), 2);
});

test("[AGT-2] a pass is not recorded when the tree changed while the check ran", () => {
  const repo = fixture(`echo more >> README.md\n${passing}`);
  stop(repo);
  assert.equal(existsSync(path.join(repo, ".git", "stop-check", "last-pass.json")), false);
  stop(repo);
  assert.equal(runs(repo), 2);
});

test("[AGT-2] after the cap of blocks in a row, the turn ends reporting the failure", () => {
  const repo = fixture(failing);
  const env = { CLAUDE_CODE_STOP_HOOK_BLOCK_CAP: "2" };
  assert.equal(stop(repo, {}, env).output.decision, "block");
  assert.equal(stop(repo, { stop_hook_active: true }, env).output.decision, "block");

  const { output } = stop(repo, { stop_hook_active: true }, env);
  assert.equal(output.continue, false);
  assert.equal(output.decision, undefined);
  assert.match(output.stopReason, /failed at step "SPA lint", and still fails after 2 attempts/);
  assert.match(output.stopReason, /stops with the check failing/);
});

test("[AGT-2] the cap defaults to Claude Code's eight", () => {
  const repo = fixture(failing);
  const env = { CLAUDE_CODE_STOP_HOOK_BLOCK_CAP: "" };
  stop(repo, {}, env);
  for (let i = 0; i < 7; i++)
    assert.equal(stop(repo, { stop_hook_active: true }, env).output.decision, "block");
  assert.equal(stop(repo, { stop_hook_active: true }, env).output.continue, false);
});

test("[AGT-2] a new prompt's first stop starts the count again", () => {
  const repo = fixture(failing);
  const env = { CLAUDE_CODE_STOP_HOOK_BLOCK_CAP: "1" };
  stop(repo, {}, env);
  // stop_hook_active is false again once the user has sent a new prompt.
  assert.equal(stop(repo, { stop_hook_active: false }, env).output.decision, "block");
});

test("[AGT-2] each session counts its own blocks", () => {
  const repo = fixture(failing);
  const env = { CLAUDE_CODE_STOP_HOOK_BLOCK_CAP: "1" };
  stop(repo, {}, env);
  const other = stop(repo, { session_id: "another-session", stop_hook_active: true }, env);
  assert.equal(other.output.decision, "block");
});

test("[AGT-2] a block count a day old is removed", () => {
  const repo = fixture(passing);
  const dir = path.join(repo, ".git", "stop-check");
  mkdirSync(dir, { recursive: true });
  const stale = path.join(dir, "ended-session.blocks");
  const fresh = path.join(dir, "running-session.blocks");
  writeFileSync(stale, "3");
  writeFileSync(fresh, "1");
  const old = Date.now() / 1000 - 2 * 24 * 60 * 60;
  utimesSync(stale, old, old);
  stop(repo);
  assert.equal(existsSync(stale), false);
  assert.equal(existsSync(fresh), true);
});

test("[AGT-2] a check that fails without naming a step is reported by its exit code", () => {
  const repo = fixture('echo "something broke"\nexit 3');
  const { output } = stop(repo);
  assert.equal(output.decision, "block");
  assert.match(output.reason, /exited with code 3 without naming a step/);
  assert.match(output.reason, /something broke/);
});

test("[AGT-2] an interrupted check is reported as interrupted, not as a failed step", () => {
  const repo = fixture('echo "==> [17/18] End-to-end tests"\necho "check.sh: interrupted during: End-to-end tests"\nexit 130');
  const { output } = stop(repo);
  assert.match(output.reason, /was interrupted during "End-to-end tests"/);
  assert.doesNotMatch(output.reason, /failed at step/);
});

test("[AGT-2] a pass after failures clears the count", () => {
  const repo = fixture(failing);
  const env = { CLAUDE_CODE_STOP_HOOK_BLOCK_CAP: "1" };
  stop(repo, {}, env);
  setCheck(repo, passing);
  assert.equal(stop(repo, { stop_hook_active: true }, env).output, null);
  setCheck(repo, failing);
  assert.equal(stop(repo, { stop_hook_active: true }, env).output.decision, "block");
});

test("[AGT-2] a check over its time is stopped, with everything it started", () => {
  const marker = `sleep 31.${process.pid}`;
  const repo = fixture(`echo "==> [16/18] Start the local instances"\n${marker} &\nwait`);
  const started = Date.now();
  const { output } = stop(repo, {}, { STOP_CHECK_TIMEOUT_SECONDS: "1" });
  assert.ok(Date.now() - started < 15_000);
  assert.equal(output.decision, "block");
  assert.match(output.reason, /did not finish within 1 s and was stopped/);
  assert.equal(running(marker), false);
});

test("[AGT-2] waiting for another session's check is not reported as something to fix", () => {
  const repo = fixture(
    `echo "check.sh: another check is running in this repository (pid 4242, started 10:00:00 in /x); waiting for it to finish"\nsleep 30 &\nwait`,
  );
  const { output } = stop(repo, {}, { STOP_CHECK_TIMEOUT_SECONDS: "1" });
  assert.equal(output.decision, "block");
  assert.match(output.reason, /still waiting for another check in this repository \(pid 4242/);
  assert.match(output.reason, /nothing needs fixing/);
  assert.doesNotMatch(output.reason, /fix the cause/);
});

for (const signal of ["SIGTERM", "SIGINT", "SIGHUP"])
  test(`[AGT-2] ${signal} to the hook stops the check it started`, async () => {
    const marker = `sleep 32.${process.pid}${signal.length}`;
    const repo = fixture(`${marker} &\nwait`);
    const hook = spawn(process.execPath, [path.join(repo, "scripts", "hooks", "stop-check.mjs")], {
      stdio: ["pipe", "ignore", "ignore"],
    });
    hook.stdin.end(JSON.stringify({ ...recorded, cwd: repo }));
    for (let i = 0; i < 50 && !running(marker); i++) await settle(100);
    assert.equal(running(marker), true, "the stub check started");
    const exited = new Promise((resolve) => hook.on("exit", resolve));
    hook.kill(signal);
    await exited;
    for (let i = 0; i < 30 && running(marker); i++) await settle(100);
    assert.equal(running(marker), false);
  });
