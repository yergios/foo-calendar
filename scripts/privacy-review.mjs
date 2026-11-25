#!/usr/bin/env node
// The privacy review of changes that touch absence notes (AGT-3 to AGT-6), shared by the
// commit hook (scripts/hooks/commit-privacy.mjs) and CI, so both agree on what a diff is,
// what it hashes to, and whether it touches notes.
//
// - `node scripts/privacy-review.mjs hash [--amend]`: whether the staged diff touches notes,
//   and the trailer line it would need.
// - `node scripts/privacy-review.mjs review [--amend]`: runs the privacy reviewer on the
//   staged diff and, on a pass, prints the trailer line to put directly above "Co-authored
//   by AI." (or at the end of the message). What a person committing by hand runs.
// - `node scripts/privacy-review.mjs verify <from> <to>`: the CI step (AGT-5). Every
//   non-merge commit in from..to that touches notes must carry a trailer whose hash equals
//   its own diff's.
//
// Its tests: `node --test scripts/privacy-review.test.mjs`. No dependencies: Node and git.
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

/** The diff options of AGT-4. The hash is of the exact bytes these print, so they never
 * change without every recorded trailer changing meaning. */
export const DIFF_OPTIONS = ["--no-color", "--no-ext-diff", "--full-index", "--binary"];

/** Where the touch rules live, relative to the repository root. */
export const RULES_FILE = ".claude/privacy-paths.txt";

/** The directories whose added and removed lines the note pattern is matched against. */
const PATTERN_ROOTS = ["src/", "tests/", "tools/"];

/** What the reviewer must answer, passed to `claude --json-schema`. */
export const VERDICT_SCHEMA = {
  type: "object",
  properties: {
    verdict: { type: "string", enum: ["passed", "failed"] },
    summary: { type: "string" },
    findings: {
      type: "array",
      items: {
        type: "object",
        properties: {
          file: { type: "string" },
          line: { type: "integer" },
          rule: { type: "string" },
          problem: { type: "string" },
        },
        required: ["file", "line", "rule", "problem"],
      },
    },
  },
  required: ["verdict", "summary", "findings"],
};

const ZERO_SHA = /^0+$/;

/** Runs git in `repo` and returns stdout as a Buffer, or throws with git's stderr. The
 * user's and the system's git configuration are ignored: a personal diff.algorithm,
 * diff.noprefix or diff.renames would otherwise change the diff's bytes, and so its hash,
 * between a laptop and CI. */
export function git(repo, args, { input, okCodes = [0] } = {}) {
  const run = spawnSync("git", args, {
    cwd: repo,
    input,
    maxBuffer: 1024 * 1024 * 1024,
    env: { ...process.env, GIT_CONFIG_GLOBAL: "/dev/null", GIT_CONFIG_NOSYSTEM: "1" },
  });
  if (run.error) throw run.error;
  if (!okCodes.includes(run.status))
    throw new Error(`git ${args.join(" ")} failed: ${run.stderr.toString().trim()}`);
  return run.stdout;
}

const gitText = (repo, args, options) => git(repo, args, options).toString("utf8").trim();

/** The repository `cwd` is in, or null outside one. */
export function repoRoot(cwd) {
  const run = spawnSync("git", ["rev-parse", "--show-toplevel"], { cwd, encoding: "utf8" });
  return run.status === 0 ? run.stdout.trim() : null;
}

/** The commit `rev` names, or null when there is none (an unborn HEAD, a root's parent). */
function commitOrNull(repo, rev) {
  const out = git(repo, ["rev-parse", "--verify", "--quiet", `${rev}^{commit}`], {
    okCodes: [0, 1],
  });
  return out.length > 0 ? out.toString().trim() : null;
}

/** The empty tree, in whichever hash the repository uses. */
function emptyTree(repo) {
  return gitText(repo, ["hash-object", "-t", "tree", "--stdin"], { input: "" });
}

/** The staged diff, against HEAD, or with `amend` against HEAD's first parent, since that
 * is what the amended commit's diff will be. */
export function stagedDiff(repo, { amend = false } = {}) {
  const base = commitOrNull(repo, amend ? "HEAD^1" : "HEAD") ?? emptyTree(repo);
  return git(repo, ["diff", "--cached", ...DIFF_OPTIONS, base]);
}

/** A commit's diff against its first parent, or against the empty tree for a root. The
 * same options as stagedDiff, so a commit hashes as its staged diff did (AGT-4). */
export function commitDiff(repo, commit) {
  const base = commitOrNull(repo, `${commit}^1`) ?? emptyTree(repo);
  return git(repo, ["diff", ...DIFF_OPTIONS, base, commit]);
}

/** Everything a `git commit -a`, or a commit after a `git add -A`, could include: tracked
 * changes against HEAD and every untracked file that is not ignored. A superset of what
 * the commit will hold, for when the staged diff is not yet the committed one. */
export function workingTreeDiff(repo) {
  const base = commitOrNull(repo, "HEAD") ?? emptyTree(repo);
  const parts = [git(repo, ["diff", ...DIFF_OPTIONS, base])];
  const untracked = git(repo, ["ls-files", "--others", "--exclude-standard", "-z"])
    .toString("utf8")
    .split("\0")
    .filter(Boolean);
  for (const file of untracked)
    parts.push(
      git(repo, ["diff", "--no-index", ...DIFF_OPTIONS, "/dev/null", file], { okCodes: [0, 1] }),
    );
  return Buffer.concat(parts);
}

/** SHA-256 of a diff's bytes, as hex. */
export function diffHash(diff) {
  return createHash("sha256").update(diff).digest("hex");
}

/** The note patterns and the listed paths a rules file's text defines. */
export function parseRules(text) {
  const patterns = [];
  const paths = [];
  for (const raw of text.split("\n")) {
    const line = raw.trim();
    if (line === "" || line.startsWith("#")) continue;
    const match = /^pattern:\s*(.+)$/.exec(line);
    if (match) patterns.push(new RegExp(match[1], "i"));
    else paths.push(line);
  }
  if (patterns.length === 0) throw new Error(`${RULES_FILE} defines no "pattern:" line`);
  return { patterns, paths };
}

/**
 * The rules as committed in `revs`, merged: every pattern and every path any of them lists,
 * so adding a rev never narrows what touches notes. Null when none of them holds the file.
 *
 * Never the working tree's copy: a change is judged by rules already committed before it
 * (HEAD in the hook, the range's base and each commit's parent in CI), so an edit to the
 * rules, staged or not, cannot weaken the rules that judge it. Such an edit is itself
 * reviewed, because the rules file lists itself.
 */
export function rulesAt(repo, revs) {
  const merged = { patterns: [], paths: [] };
  let found = false;
  for (const rev of revs) {
    if (!rev) continue;
    const blob = git(repo, ["rev-parse", "--verify", "--quiet", `${rev}:${RULES_FILE}`], {
      okCodes: [0, 1],
    })
      .toString()
      .trim();
    if (!blob) continue;
    const rules = parseRules(git(repo, ["cat-file", "blob", blob]).toString("utf8"));
    found = true;
    for (const pattern of rules.patterns)
      if (!merged.patterns.some((p) => p.source === pattern.source)) merged.patterns.push(pattern);
    for (const file of rules.paths) if (!merged.paths.includes(file)) merged.paths.push(file);
  }
  return found ? merged : null;
}

/** The rules that judge a commit about to be made on HEAD: HEAD's, and with `amend` also
 * those of HEAD's first parent, which the amended commit will sit on. */
export function rulesForStaged(repo, { amend = false } = {}) {
  return rulesAt(repo, amend ? ["HEAD", commitOrNull(repo, "HEAD^1")] : ["HEAD"]);
}

/** A path as git prints it in a diff header: C-quoted, with octal escapes for bytes
 * outside printable ASCII, when it holds anything unusual. */
function unquote(text) {
  if (!text.startsWith('"')) return text;
  const bytes = [];
  const body = text.slice(1, text.lastIndexOf('"'));
  const escapes = { n: 10, t: 9, r: 13, '"': 34, "\\": 92, a: 7, b: 8, f: 12, v: 11 };
  for (let i = 0; i < body.length; i++) {
    if (body[i] !== "\\") {
      bytes.push(...Buffer.from(body[i], "utf8"));
    } else if (/[0-7]/.test(body[i + 1])) {
      bytes.push(parseInt(body.slice(i + 1, i + 4), 8));
      i += 3;
    } else {
      bytes.push(escapes[body[i + 1]] ?? body.charCodeAt(i + 1));
      i += 1;
    }
  }
  return Buffer.from(bytes).toString("utf8");
}

/** Strips the a/ or b/ prefix from a header path; null for /dev/null. */
function headerPath(text) {
  const value = unquote(text.trim());
  if (value === "/dev/null") return null;
  return value.replace(/^[ab]\//, "");
}

/**
 * Why a diff touches notes, one reason per file, or [] when it does not: it changes a path
 * listed in the rules, or adds or removes a line under src/, tests/ or tools/ that matches
 * one of the patterns.
 *
 * @param {Buffer | string} diff
 * @param {{ patterns: RegExp[], paths: string[] }} rules
 */
export function touchesNotes(diff, rules) {
  const reasons = new Map();
  const listed = (file) =>
    rules.paths.some((p) => (p.endsWith("/") ? file.startsWith(p) : file === p));
  const underPatternRoots = (file) => PATTERN_ROOTS.some((root) => file.startsWith(root));

  let files = [];
  let inHunk = false;
  const note = (reason) => {
    for (const file of files) if (!reasons.has(file)) reasons.set(file, reason);
  };

  const fileStarted = () => {
    for (const file of files) if (listed(file)) note(`${file} is listed in ${RULES_FILE}`);
  };

  for (const line of diff.toString("utf8").split("\n")) {
    if (line.startsWith("diff --git ")) {
      // "a/<path> b/<path>": unambiguous when both sides are the same path, which is every
      // case but a rename or copy, and those name both sides on lines of their own below.
      const rest = line.slice("diff --git ".length);
      const half = (rest.length - 1) / 2;
      const same = rest.slice(0, half).slice(2) === rest.slice(half + 1).slice(2);
      files = same ? [headerPath(rest.slice(0, half))] : [];
      inHunk = false;
      fileStarted();
      continue;
    }
    if (!inHunk) {
      const header = /^(?:--- |\+\+\+ |rename from |rename to |copy from |copy to )(.*)$/.exec(
        line,
      );
      if (header) {
        const file = /^(?:rename|copy) /.test(line) ? unquote(header[1]) : headerPath(header[1]);
        if (file && !files.includes(file)) {
          files.push(file);
          fileStarted();
        }
        continue;
      }
      if (line.startsWith("@@")) inHunk = true;
      continue;
    }
    if (line.startsWith("@@")) continue;
    if (!line.startsWith("+") && !line.startsWith("-")) continue;
    const pattern = rules.patterns.find((p) => p.test(line.slice(1)));
    if (pattern) {
      const roots = files.filter(underPatternRoots);
      for (const file of roots)
        if (!reasons.has(file))
          reasons.set(
            file,
            `${file} adds or removes a line matching ${pattern}: ${line.trim().slice(0, 120)}`,
          );
    }
  }

  return [...reasons.values()];
}

/** The line a commit message carries once the reviewer passed a diff of this hash. */
export function trailerLine(hash) {
  return `Privacy-Review: passed sha256:${hash}`;
}

/** The hashes of the Privacy-Review trailers git reads in a commit message. Two settings
 * make git read the trailer where this repository puts it, on the line above "Co-authored
 * by AI.": --no-divider, because git otherwise takes the footer's "---" line for the start
 * of a patch and reads nothing after it; and the key configured, because git only reads a
 * paragraph that is not all trailers when one of them is a configured one. */
export function trailerHashes(repo, message) {
  const parsed = gitText(
    repo,
    [
      "-c",
      "trailer.privacy-review.key=Privacy-Review",
      "interpret-trailers",
      "--parse",
      "--no-divider",
    ],
    { input: message },
  );
  return parsed
    .split("\n")
    .map((line) => /^Privacy-Review:\s*passed sha256:([0-9a-f]{64})\s*$/i.exec(line))
    .filter(Boolean)
    .map((match) => match[1].toLowerCase());
}

/** Where passed verdicts are cached, by diff hash: in the git directory, shared by every
 * worktree, and never committed. */
function cacheDir(repo) {
  const common = gitText(repo, ["rev-parse", "--git-common-dir"]);
  return path.join(path.resolve(repo, common), "privacy-reviews");
}

/** The cached passed verdict for this hash, or null. */
export function cachedVerdict(repo, hash) {
  const file = path.join(cacheDir(repo), hash);
  if (!existsSync(file)) return null;
  try {
    const verdict = JSON.parse(readFileSync(file, "utf8"));
    return verdict.verdict === "passed" ? verdict : null;
  } catch {
    return null;
  }
}

export function cacheVerdict(repo, hash, verdict) {
  const dir = cacheDir(repo);
  mkdirSync(dir, { recursive: true });
  writeFileSync(path.join(dir, hash), JSON.stringify(verdict, null, 2) + "\n");
}

/**
 * Runs the privacy-reviewer subagent headlessly on a diff and returns its verdict. Hooks
 * are disabled for that run, so it cannot trigger this review again, and it gets the read
 * tools only. Throws when the reviewer cannot be run or answers out of shape: a review
 * that did not happen never counts as a pass.
 */
export function runReviewer(repo, diff, { timeoutMs = 540_000 } = {}) {
  const args = [
    "-p",
    "--agent",
    "privacy-reviewer",
    "--output-format",
    "json",
    "--json-schema",
    JSON.stringify(VERDICT_SCHEMA),
    "--settings",
    JSON.stringify({ disableAllHooks: true }),
    "--allowedTools",
    "Read,Grep,Glob",
    "--no-session-persistence",
  ];
  const prompt = Buffer.concat([
    Buffer.from("Review this diff against your rules. It is the whole change.\n\n"),
    Buffer.isBuffer(diff) ? diff : Buffer.from(diff),
  ]);
  const run = spawnSync("claude", args, {
    cwd: repo,
    input: prompt,
    encoding: "utf8",
    maxBuffer: 64 * 1024 * 1024,
    timeout: timeoutMs,
  });
  if (run.error) throw new Error(`could not run claude: ${run.error.message}`);
  if (run.status !== 0)
    throw new Error(
      `claude exited with ${run.status}: ${(run.stderr || run.stdout).trim().slice(0, 2000)}`,
    );

  let result;
  try {
    result = JSON.parse(run.stdout);
  } catch {
    throw new Error(`claude printed no JSON result: ${run.stdout.trim().slice(0, 2000)}`);
  }
  const verdict = result.structured_output;
  if (
    !verdict ||
    !["passed", "failed"].includes(verdict.verdict) ||
    !Array.isArray(verdict.findings)
  )
    throw new Error(`the reviewer gave no verdict: ${JSON.stringify(result).slice(0, 2000)}`);
  return verdict;
}

/** A verdict's findings, one per line, for a person or for Claude to act on. */
export function formatFindings(verdict) {
  const lines = verdict.findings.map((f) => `- ${f.file}:${f.line} [${f.rule}] ${f.problem}`);
  return [verdict.summary, ...lines].filter(Boolean).join("\n");
}

/** The commit that added this script, the first one `verify` holds to it; commits before
 * it predate the review and are exempt. Null when `to` does not contain it. */
function introducingCommit(repo, to) {
  const added = gitText(repo, [
    "log",
    "--diff-filter=A",
    "--format=%H",
    "--reverse",
    to,
    "--",
    "scripts/privacy-review.mjs",
  ]);
  return added.split("\n").filter(Boolean)[0] ?? null;
}

/**
 * Checks every commit in from..to (CI, AGT-5): one that touches notes must carry a trailer
 * whose hash is its own diff's. Merge commits are skipped: what they bring in was reviewed
 * in the commits they merge. Returns { problems, checked, skipped, notes }.
 */
export function verify(repo, from, to) {
  const notes = [];
  const head = commitOrNull(repo, to);
  if (!head) throw new Error(`${to} is not a commit in this repository`);

  // A push that creates a branch reports `from` as all zeros, and after a force push it can
  // name a commit this clone does not have: either way, the pushed commit alone.
  const known = from && !ZERO_SHA.test(from) ? commitOrNull(repo, from) : null;
  if (!known) notes.push(`no earlier commit to start from; checking ${head.slice(0, 12)} alone`);
  const range = known
    ? [`${known}..${head}`]
    : commitOrNull(repo, `${head}^1`)
      ? [`${head}^!`]
      : [head];

  const intro = introducingCommit(repo, head);
  if (!intro)
    return {
      problems: [],
      checked: 0,
      skipped: 0,
      notes: [...notes, "the review predates every commit here"],
    };
  const introParent = commitOrNull(repo, `${intro}^1`);
  const exempt = introParent ? [`^${introParent}`] : [];

  const commits = gitText(repo, ["rev-list", "--reverse", "--no-merges", ...range, ...exempt])
    .split("\n")
    .filter(Boolean);
  const skipped = Number(gitText(repo, ["rev-list", "--count", "--merges", ...range, ...exempt]));

  // Each commit is judged by the rules at the range's base, which nothing in the push or the
  // pull request can change, together with those at its own parent, so a rule added earlier
  // in the range already applies to the commits after it.
  const problems = [];
  for (const commit of commits) {
    const rules = rulesAt(repo, [known, commitOrNull(repo, `${commit}^1`)]);
    if (!rules) continue;
    const diff = commitDiff(repo, commit);
    const reasons = touchesNotes(diff, rules);
    if (reasons.length === 0) continue;

    const hash = diffHash(diff);
    const message = git(repo, ["log", "-1", "--format=%B", commit]).toString("utf8");
    const hashes = trailerHashes(repo, message);
    const subject = message.split("\n")[0];
    const name = `${commit.slice(0, 12)} "${subject}"`;
    if (hashes.includes(hash)) continue;

    problems.push(
      hashes.length === 0
        ? `${name} touches absence notes (${reasons[0]}) but carries no Privacy-Review trailer; expected "${trailerLine(hash)}".`
        : `${name} touches absence notes, but its Privacy-Review trailer is for sha256:${hashes[0]}, not its own diff's sha256:${hash}: the trailer was written for a different change.`,
    );
  }

  return { problems, checked: commits.length, skipped, notes };
}

function main(argv) {
  const repo = repoRoot(process.cwd());
  if (!repo) {
    console.error("not inside a git repository");
    return 2;
  }
  const [command, ...args] = argv;

  if (command === "hash" || command === "review") {
    const diff = stagedDiff(repo, { amend: args.includes("--amend") });
    const hash = diffHash(diff);
    const rules = rulesForStaged(repo, { amend: args.includes("--amend") });
    const reasons = rules ? touchesNotes(diff, rules) : [];
    if (reasons.length === 0) {
      console.log(
        `The staged diff (sha256:${hash}) does not touch absence notes; no review needed.`,
      );
      return 0;
    }
    if (command === "hash") {
      console.log(`The staged diff touches absence notes:\n  ${reasons.join("\n  ")}`);
      console.log(trailerLine(hash));
      return 0;
    }
    const cached = cachedVerdict(repo, hash);
    const verdict = cached ?? runReviewer(repo, diff);
    if (verdict.verdict !== "passed") {
      console.error(`The privacy reviewer failed the staged diff:\n${formatFindings(verdict)}`);
      return 1;
    }
    if (!cached) cacheVerdict(repo, hash, verdict);
    console.log(`The privacy reviewer passed the staged diff${cached ? " (cached)" : ""}.`);
    console.log(`Put this line directly above "Co-authored by AI." in the commit message:`);
    console.log(trailerLine(hash));
    return 0;
  }

  if (command === "verify" && args.length === 2) {
    const { problems, checked, skipped, notes } = verify(repo, args[0], args[1]);
    for (const note of notes) console.log(note);
    if (problems.length > 0) {
      for (const problem of problems) console.error(`error: ${problem}`);
      console.error(
        "Each commit that touches absence notes needs the trailer the privacy review gave its own " +
          "diff (scripts/privacy-review.mjs review). A squash or rebase merge produces commits no " +
          "review covered: merge with a merge commit instead.",
      );
      return 1;
    }
    console.log(
      `Privacy review OK: ${checked} commit(s) checked, ${skipped} merge commit(s) skipped.`,
    );
    return 0;
  }

  console.error("usage: privacy-review.mjs hash [--amend] | review [--amend] | verify <from> <to>");
  return 2;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(`error: ${error.message}`);
    process.exitCode = 1;
  }
}
