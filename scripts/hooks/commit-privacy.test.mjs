// Tests of the commit hook, run with `node --test scripts/hooks/commit-privacy.test.mjs`.
// Each copies the hook and scripts/privacy-review.mjs into a fresh repository, puts a stub
// `claude` first on PATH that answers a set verdict and records what it was asked, and
// feeds the hook the PreToolUse input Claude Code 2.1.331 sent when recorded
// (fixtures/pretooluse-input.json, paths replaced) with the command under test.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import {
  chmodSync,
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { after, test } from "node:test";
import { fileURLToPath } from "node:url";

import { diffHash, stagedDiff, trailerLine } from "../privacy-review.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const recorded = JSON.parse(
  readFileSync(path.join(here, "fixtures", "pretooluse-input.json"), "utf8"),
);

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

const passed = { verdict: "passed", summary: "Notes stay with their author.", findings: [] };
const failed = {
  verdict: "failed",
  summary: "The note is logged.",
  findings: [{ file: "src/Api/A.cs", line: 3, rule: "PRIV-1", problem: "logs absence.Note" }],
};

/** A repository with the hook, the shared script, the rules, and a stub claude that
 * answers `verdict` (or fails with `exitCode`). */
function fixture({ verdict = passed, exitCode = 0 } = {}) {
  const repo = mkdtempSync(path.join(tmpdir(), "commit-privacy-"));
  repos.push(repo);
  const git = (...args) => execFileSync("git", args, { cwd: repo });
  git("init", "--quiet");
  mkdirSync(path.join(repo, "scripts", "hooks"), { recursive: true });
  mkdirSync(path.join(repo, ".claude"));
  for (const file of ["hooks/commit-privacy.mjs", "privacy-review.mjs"])
    copyFileSync(path.join(here, "..", file), path.join(repo, "scripts", file));
  writeFileSync(
    path.join(repo, ".claude", "privacy-paths.txt"),
    "pattern: \\bnotes?\\b\nsrc/Api/Dtos/AbsenceDto.cs\n",
  );
  writeFileSync(path.join(repo, "README.md"), "A repository.\n");
  git("add", "--all");
  git("commit", "--quiet", "--message", "first");

  // Outside the repository, so it is never part of a diff.
  const bin = mkdtempSync(path.join(tmpdir(), "commit-privacy-bin-"));
  repos.push(bin);
  const result = JSON.stringify({ type: "result", structured_output: verdict });
  writeFileSync(
    path.join(bin, "claude"),
    `#!/usr/bin/env bash
printf '%s\\n' "$@" > "${bin}/args"
cat > "${bin}/stdin"
echo run >> "${bin}/runs"
${exitCode ? `echo "not signed in" >&2; exit ${exitCode}` : `cat <<'EOF'\n${result}\nEOF`}
`,
  );
  chmodSync(path.join(bin, "claude"), 0o755);
  return { repo, bin, git };
}

function write(repo, file, content) {
  mkdirSync(path.dirname(path.join(repo, file)), { recursive: true });
  writeFileSync(path.join(repo, file), content);
}

/** Runs the hook on a Bash call of `command`; returns its decision, or null to let the
 * call go ahead, and the reason. */
function hook({ repo, bin }, command, input = {}) {
  const run = spawnSync(
    process.execPath,
    [path.join(repo, "scripts", "hooks", "commit-privacy.mjs")],
    {
      input: JSON.stringify({
        ...recorded,
        cwd: repo,
        tool_input: { ...recorded.tool_input, command },
        ...input,
      }),
      encoding: "utf8",
      env: { ...process.env, PATH: `${bin}:${process.env.PATH}` },
    },
  );
  assert.equal(run.status, 0, run.stderr);
  if (!run.stdout.trim()) return { decision: null, reason: "" };
  const out = JSON.parse(run.stdout).hookSpecificOutput;
  return { decision: out.permissionDecision, reason: out.permissionDecisionReason };
}

const reviews = ({ bin }) =>
  existsSync(path.join(bin, "runs"))
    ? readFileSync(path.join(bin, "runs"), "utf8").split("\n").filter(Boolean).length
    : 0;

/** A commit command as Claude writes one, with the footer and, when given, the trailer. */
const commitCommand = (trailer) =>
  `git commit -F - <<'EOF'\nfix: change the note\n\nWhy.\n\n---\n\n${trailer ? `${trailer}\n` : ""}Co-authored by AI.\nEOF`;

function stageNoteChange(f) {
  write(f.repo, "src/Api/A.cs", "var x = absence.Note;\n");
  f.git("add", "--all");
  return trailerLine(diffHash(stagedDiff(f.repo)));
}

test("[AGT-3] a commit that does not touch notes goes ahead without a review", () => {
  const f = fixture();
  write(f.repo, "src/Api/Person.cs", "class Person {}\n");
  f.git("add", "--all");
  assert.equal(hook(f, commitCommand()).decision, null);
  assert.equal(reviews(f), 0);
});

test("[AGT-3] other commands are not looked at", () => {
  const f = fixture();
  stageNoteChange(f);
  for (const command of ["git status", "git log --grep commit", "git commit-tree HEAD^{tree}"])
    assert.equal(hook(f, command).decision, null, command);
  assert.equal(hook(f, "ls", { tool_name: "Read" }).decision, null);
  assert.equal(reviews(f), 0);
});

test("[AGT-3] a note-touching commit runs the reviewer headlessly on the staged diff", () => {
  const f = fixture();
  stageNoteChange(f);
  hook(f, commitCommand());
  assert.equal(reviews(f), 1);

  const args = readFileSync(path.join(f.bin, "args"), "utf8").split("\n");
  assert.ok(args.includes("-p"));
  assert.equal(args[args.indexOf("--agent") + 1], "privacy-reviewer");
  assert.equal(JSON.parse(args[args.indexOf("--settings") + 1]).disableAllHooks, true);
  assert.equal(args[args.indexOf("--allowedTools") + 1], "Read,Grep,Glob");
  assert.ok(JSON.parse(args[args.indexOf("--json-schema") + 1]).properties.verdict);
  assert.match(readFileSync(path.join(f.bin, "stdin"), "utf8"), /\+var x = absence\.Note;/);
});

test("[AGT-3] a failed verdict blocks the commit with its findings", () => {
  const f = fixture({ verdict: failed });
  const trailer = stageNoteChange(f);
  const { decision, reason } = hook(f, commitCommand(trailer));
  assert.equal(decision, "deny");
  assert.match(reason, /failed the staged diff/);
  assert.match(reason, /src\/Api\/A\.cs:3 \[PRIV-1\] logs absence\.Note/);
  // Not cached: the same diff is reviewed again next time.
  hook(f, commitCommand(trailer));
  assert.equal(reviews(f), 2);
});

test("[AGT-3] a reviewer that cannot run blocks the commit", () => {
  const f = fixture({ exitCode: 1 });
  const trailer = stageNoteChange(f);
  const { decision, reason } = hook(f, commitCommand(trailer));
  assert.equal(decision, "deny");
  assert.match(reason, /could not run: claude exited with 1: not signed in/);
});

test("[AGT-3] a passed verdict without the trailer blocks once, naming the line to add", () => {
  const f = fixture();
  const trailer = stageNoteChange(f);
  const { decision, reason } = hook(f, commitCommand());
  assert.equal(decision, "deny");
  assert.ok(reason.includes(trailer), reason);
  assert.match(reason, /directly above "Co-authored by AI\."/);

  // The retry with the trailer goes through on the cached verdict.
  assert.equal(hook(f, commitCommand(trailer)).decision, null);
  assert.equal(reviews(f), 1);
  assert.equal(readdirSync(path.join(f.repo, ".git", "privacy-reviews")).length, 1);
});

test("[AGT-3] the trailer must be the staged diff's, on the line above the footer", () => {
  const f = fixture();
  const trailer = stageNoteChange(f);
  const other = trailerLine("0".repeat(64));
  assert.equal(hook(f, commitCommand(other)).decision, "deny");

  // Elsewhere in the message, or in a -m paragraph of its own, it is not the trailer.
  const apart = `git commit -m "fix: x" -m "${trailer}" -m "Co-authored by AI."`;
  assert.equal(hook(f, apart).decision, "deny");
  const above = `git commit -m "fix: x\n\n${trailer}\n\n---\n\nCo-authored by AI."`;
  assert.equal(hook(f, above).decision, "deny");

  const quoted = `git commit -m "fix: x\n\n---\n\n${trailer}\nCo-authored by AI."`;
  assert.equal(hook(f, quoted).decision, null);
});

test("[AGT-3] a message file given with -F is read", () => {
  const f = fixture();
  const trailer = stageNoteChange(f);
  writeFileSync(
    path.join(f.bin, "message.txt"),
    `fix: x\n\n---\n\n${trailer}\nCo-authored by AI.\n`,
  );
  assert.equal(hook(f, `git commit -F ${path.join(f.bin, "message.txt")}`).decision, null);
});

test("[AGT-3] a staged diff changed after the review is reviewed again", () => {
  const f = fixture();
  const first = stageNoteChange(f);
  hook(f, commitCommand(first));
  write(f.repo, "src/Api/A.cs", "var y = absence.Note;\n");
  f.git("add", "--all");
  assert.equal(hook(f, commitCommand(first)).decision, "deny");
  assert.equal(reviews(f), 2);
});

test("[AGT-3] staging in the same command is refused when it could touch notes", () => {
  const f = fixture();
  write(f.repo, "src/Api/A.cs", "var x = absence.Note;\n");
  const { decision, reason } = hook(f, `git add -A && ${commitCommand()}`);
  assert.equal(decision, "deny");
  assert.match(reason, /Stage the change first/);
  assert.equal(hook(f, `git commit -am "x"`).decision, "deny");
  assert.equal(reviews(f), 0);
});

test("[AGT-3] staging in the same command is let through when nothing could touch notes", () => {
  const f = fixture();
  write(f.repo, "src/Api/Person.cs", "class Person {}\n");
  assert.equal(hook(f, `git add -A && ${commitCommand()}`).decision, null);
  assert.equal(hook(f, `git commit -a -m "x"`).decision, null);
});

test("[AGT-3] --amend reviews the diff the amended commit will have", () => {
  const f = fixture();
  write(f.repo, "src/Api/A.cs", "var x = absence.Note;\n");
  f.git("add", "--all");
  f.git("commit", "--quiet", "--message", "first try");
  // Nothing new is staged, but the amended commit still holds the note change.
  const { decision, reason } = hook(f, "git commit --amend --no-edit");
  assert.equal(decision, "deny");
  assert.match(reason, /Privacy-Review: passed sha256:/);
});
