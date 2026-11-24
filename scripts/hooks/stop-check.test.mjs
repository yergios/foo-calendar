// Tests of the Stop hook, run with `node --test scripts/hooks/stop-check.test.mjs`. Each
// copies the hook into a fresh repository beside a stub scripts/check.sh, and feeds it the
// Stop input Claude Code 2.1.331 sent when recorded (fixtures/stop-input.json, paths
// replaced), so its behaviour is tested without running the real check.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { after, test } from "node:test";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const recorded = JSON.parse(readFileSync(path.join(here, "fixtures", "stop-input.json"), "utf8"));

const repos = [];
after(() => {
  for (const repo of repos) rmSync(repo, { recursive: true, force: true });
});

/** A repository with the hook in it and a check.sh that runs `body`. */
function fixture(body) {
  const repo = mkdtempSync(path.join(tmpdir(), "stop-check-"));
  repos.push(repo);
  execFileSync("git", ["init", "--quiet"], { cwd: repo });
  mkdirSync(path.join(repo, "scripts", "hooks"), { recursive: true });
  copyFileSync(
    path.join(here, "stop-check.mjs"),
    path.join(repo, "scripts", "hooks", "stop-check.mjs"),
  );
  // Each run leaves a mark, so a test can tell the check ran.
  writeFileSync(
    path.join(repo, "scripts", "check.sh"),
    `#!/usr/bin/env bash\necho run >> "${path.join(repo, "runs")}"\n${body}\n`,
  );
  return repo;
}

const passing = 'echo "check.sh: all 17 steps passed in 1 s"';
const failing = [
  'echo "==> [7/17] SPA lint"',
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
  existsSync(path.join(repo, "runs"))
    ? readFileSync(path.join(repo, "runs"), "utf8").split("\n").filter(Boolean).length
    : 0;

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
  assert.match(output.reason, /lint output line 60/);
  assert.doesNotMatch(output.reason, /lint output line 1\n/);
  assert.match(output.reason, /stop-check\/last-run\.log/);
});

test("[AGT-2] the check runs on every stop, a continued one included", () => {
  const repo = fixture(passing);
  stop(repo);
  stop(repo, { stop_hook_active: true });
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

test("[AGT-2] a check that fails without naming a step is reported by its exit code", () => {
  const repo = fixture('echo "something broke"\nexit 3');
  const { output } = stop(repo);
  assert.equal(output.decision, "block");
  assert.match(output.reason, /exited with code 3 without naming a step/);
  assert.match(output.reason, /something broke/);
});

test("[AGT-2] a pass after failures clears the count", () => {
  const repo = fixture(failing);
  const env = { CLAUDE_CODE_STOP_HOOK_BLOCK_CAP: "1" };
  stop(repo, {}, env);
  writeFileSync(path.join(repo, "scripts", "check.sh"), `#!/usr/bin/env bash\n${passing}\n`);
  assert.equal(stop(repo, { stop_hook_active: true }, env).output, null);
  writeFileSync(path.join(repo, "scripts", "check.sh"), `#!/usr/bin/env bash\n${failing}\n`);
  assert.equal(stop(repo, { stop_hook_active: true }, env).output.decision, "block");
});
