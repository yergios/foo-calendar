#!/usr/bin/env node
// The commit hook (AGT-3): a PreToolUse hook on Bash. Before Claude Code runs a
// `git commit` whose staged diff touches absence notes (.claude/privacy-paths.txt), the
// privacy-reviewer subagent must have passed that exact diff, and the message must carry
// its trailer on the line just above "Co-authored by AI.". A commit that does not touch
// notes is let through at once. Wired in .claude/settings.json; tested by
// commit-privacy.test.mjs with a stub `claude`.
//
// The flow, for a commit that touches notes:
// 1. A passed verdict for the diff's hash is looked up in .git/privacy-reviews/.
// 2. With none, the reviewer runs headlessly (scripts/privacy-review.mjs). A failed
//    verdict, or a review that could not run, blocks the commit with the reason.
// 3. A passed verdict is cached. Without the trailer in the message, the commit is blocked
//    once with the exact line to add; with it, it goes through.
//
// The message is read from the command itself (and from a file given with -F), since it
// exists nowhere else until git runs. A command that stages files as well as committing, or
// a commit with -a, -i or -o, commits something other than the staged diff; when what it
// could commit touches notes, it is blocked and asked to stage first and commit alone.
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

import {
  cacheVerdict,
  cachedVerdict,
  diffHash,
  formatFindings,
  repoRoot,
  rulesForStaged,
  runReviewer,
  stagedDiff,
  touchesNotes,
  trailerLine,
  workingTreeDiff,
} from "../privacy-review.mjs";

const input = JSON.parse(readFileSync(0, "utf8") || "{}");
const command = input.tool_input?.command ?? "";

/** `git commit`, with any global options before the subcommand (`git -c k=v commit`), but
 * not `git commit-tree` or a commit inside a quoted message. */
const commitCall =
  /(?:^|[\s;&|(])git(?:\s+(?:-[cC]\s+\S+|--?[\w-]+(?:=\S+)?))*\s+commit(?=\s|$|[;&|)])/m;

if (input.tool_name !== "Bash" || !commitCall.test(command)) process.exit(0);

const repo = repoRoot(input.cwd ?? process.cwd());
if (!repo) process.exit(0);

function deny(reason) {
  console.log(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: "deny",
        permissionDecisionReason: reason,
      },
    }),
  );
  process.exit(0);
}

// What follows "commit", up to the next command: its options.
const commitArgs = command.slice(command.search(commitCall)).split(/&&|\|\||;|\|/)[0];
// Its options, with quoted text such as the message taken out so it cannot read as one.
const options = commitArgs.replace(/(["']).*?\1/gs, "");
const amend = /\s--amend(?=\s|$)/.test(options);

// The rules as committed at HEAD, never the working tree's copy, which the commit may not
// even include (see rulesAt). None there means the review does not exist yet at HEAD.
const rules = rulesForStaged(repo, { amend });
if (!rules) process.exit(0);
const stagesToo = /(?:^|[\s;&|(])git\s+(?:add|rm|mv|reset|restore|stash|apply)\b/.test(command);
const commitsUnstaged = /\s(?:-[a-zA-Z]*[aio][a-zA-Z]*|--all|--include|--only)(?=\s|$)/.test(
  options,
);

if (stagesToo || commitsUnstaged) {
  const reasons = touchesNotes(workingTreeDiff(repo), rules);
  if (reasons.length === 0) process.exit(0);
  deny(
    "This commit could include changes that touch absence notes, and the privacy review " +
      "covers the staged diff only. Stage the change first (git add) in a command of its " +
      "own, then run git commit alone, without -a, -i or -o.\n" +
      reasons.map((r) => `- ${r}`).join("\n"),
  );
}

const diff = stagedDiff(repo, { amend });
const reasons = touchesNotes(diff, rules);
if (reasons.length === 0) process.exit(0);

const hash = diffHash(diff);
let verdict = cachedVerdict(repo, hash);
if (!verdict) {
  try {
    verdict = runReviewer(repo, diff);
  } catch (error) {
    deny(
      `The staged diff touches absence notes and must pass the privacy review, which could ` +
        `not run: ${error.message}\nFix that and commit again; a review that did not happen ` +
        `never counts as a pass.`,
    );
  }
  if (verdict.verdict !== "passed") {
    deny(
      `The privacy reviewer failed the staged diff (sha256:${hash}). Fix these, stage the ` +
        `fix and commit again:\n${formatFindings(verdict)}`,
    );
  }
  cacheVerdict(repo, hash, verdict);
}

// The message as written in the command, plus any file given with -F or --file. A heredoc
// or a quoted -m puts real newlines between the lines; $'...' quoting puts "\n".
let message = command;
const fileArg = /\s(?:-F\s*|--file[=\s])\s*(["']?)([^\s"']+)\1/.exec(commitArgs);
if (fileArg) {
  const file = path.resolve(input.cwd ?? repo, fileArg[2]);
  if (existsSync(file)) message += "\n" + readFileSync(file, "utf8");
}
const trailer = trailerLine(hash);
const placed = new RegExp(`(?:^|\\n|\\\\n)${trailer}(?:\\r?\\n|\\\\n)Co-authored by AI\\.`);
if (placed.test(message)) process.exit(0);

deny(
  `The privacy reviewer passed the staged diff, which touches absence notes. Add this line ` +
    `to the commit message, on its own line directly above "Co-authored by AI.", then ` +
    `commit again:\n${trailer}`,
);
