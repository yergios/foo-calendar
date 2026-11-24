// Tests of privacy-review.mjs, run with `node --test scripts/privacy-review.test.mjs`. Each
// builds a small repository in a temporary directory and drives git in it for real.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { after, test } from "node:test";
import { fileURLToPath } from "node:url";

import {
  commitDiff,
  diffHash,
  readRules,
  stagedDiff,
  touchesNotes,
  trailerLine,
  verify,
} from "./privacy-review.mjs";

const script = fileURLToPath(new URL("privacy-review.mjs", import.meta.url));

// Fixture commits need an identity, and must not pick up the developer's signing setup.
Object.assign(process.env, {
  GIT_AUTHOR_NAME: "Test",
  GIT_AUTHOR_EMAIL: "test@example.invalid",
  GIT_COMMITTER_NAME: "Test",
  GIT_COMMITTER_EMAIL: "test@example.invalid",
  GIT_CONFIG_GLOBAL: "/dev/null",
  GIT_CONFIG_NOSYSTEM: "1",
});

const RULES = `pattern: \\bnotes?\\b
src/Api/Dtos/AbsenceDto.cs
docs/data-protection.md
`;

const repos = [];
after(() => {
  for (const repo of repos) rmSync(repo, { recursive: true, force: true });
});

/** A new repository holding the rules file and one first commit. */
function fixture() {
  const repo = mkdtempSync(path.join(tmpdir(), "privacy-review-"));
  repos.push(repo);
  git(repo, "init", "--quiet", "--initial-branch=master");
  write(repo, ".claude/privacy-paths.txt", RULES);
  write(repo, "README.md", "A repository.\n");
  commit(repo, "first");
  return repo;
}

function git(repo, ...args) {
  return execFileSync("git", args, { cwd: repo, encoding: "utf8" });
}

function write(repo, file, content) {
  mkdirSync(path.dirname(path.join(repo, file)), { recursive: true });
  writeFileSync(path.join(repo, file), content);
}

function commit(repo, message) {
  git(repo, "add", "--all");
  git(repo, "commit", "--quiet", "--allow-empty", "--message", message);
  return git(repo, "rev-parse", "HEAD").trim();
}

const touches = (repo, diff) => touchesNotes(diff, readRules(repo));

test("[AGT-4] a staged diff and the commit made from it hash the same", () => {
  const repo = fixture();
  write(repo, "src/a.txt", "one\ntwo\n");
  write(repo, "src/to-rename.txt", "a file that will move, long enough to be seen as a rename\n");
  write(repo, "src/script.sh", "echo hi\n");
  write(repo, "src/doomed.txt", "going\n");
  commit(repo, "files");

  // Every kind of change a diff header can describe: an edit, a rename, a mode change, a
  // binary file, an empty new file, a deletion, and a path that needs quoting.
  write(repo, "src/a.txt", "one\nthree\n");
  git(repo, "mv", "src/to-rename.txt", "src/renamed.txt");
  chmodSync(path.join(repo, "src/script.sh"), 0o755);
  writeFileSync(path.join(repo, "src/image.bin"), Buffer.from([0, 1, 2, 255, 0, 7]));
  write(repo, "src/empty.txt", "");
  rmSync(path.join(repo, "src/doomed.txt"));
  write(repo, "src/naïve name.txt", "quoted\n");
  git(repo, "add", "--all");

  const staged = diffHash(stagedDiff(repo));
  const made = commit(repo, "change");
  assert.equal(diffHash(commitDiff(repo, made)), staged);
});

test("[AGT-4] an amended commit hashes as the staged diff --amend gave", () => {
  const repo = fixture();
  write(repo, "src/a.txt", "one\n");
  commit(repo, "a");
  write(repo, "src/a.txt", "one\ntwo\n");
  git(repo, "add", "--all");

  const staged = diffHash(stagedDiff(repo, { amend: true }));
  git(repo, "commit", "--quiet", "--amend", "--no-edit");
  assert.equal(diffHash(commitDiff(repo, "HEAD")), staged);
});

test("[AGT-4] a root commit hashes as its staged diff did", () => {
  const repo = mkdtempSync(path.join(tmpdir(), "privacy-review-"));
  repos.push(repo);
  git(repo, "init", "--quiet");
  write(repo, "src/a.txt", "one\n");
  git(repo, "add", "--all");

  const staged = diffHash(stagedDiff(repo));
  const made = commit(repo, "root");
  assert.equal(diffHash(commitDiff(repo, made)), staged);
});

test("a different change hashes differently", () => {
  const repo = fixture();
  write(repo, "src/a.txt", "one\n");
  git(repo, "add", "--all");
  const first = diffHash(stagedDiff(repo));
  write(repo, "src/a.txt", "two\n");
  git(repo, "add", "--all");
  assert.notEqual(diffHash(stagedDiff(repo)), first);
});

test("a listed file touches notes, whatever the change", () => {
  const repo = fixture();
  write(repo, "src/Api/Dtos/AbsenceDto.cs", "public sealed record AbsenceDto(int Id);\n");
  git(repo, "add", "--all");
  assert.match(touches(repo, stagedDiff(repo)).join(), /AbsenceDto\.cs is listed/);
});

test("renaming a listed file away touches notes", () => {
  const repo = fixture();
  write(repo, "docs/data-protection.md", "What is stored, and where it goes.\n");
  commit(repo, "doc");
  git(repo, "mv", "docs/data-protection.md", "docs/elsewhere.md");
  assert.match(touches(repo, stagedDiff(repo)).join(), /data-protection\.md is listed/);
});

test("an added or removed line matching the pattern under src, tests or tools touches notes", () => {
  for (const dir of ["src", "tests", "tools"]) {
    const repo = fixture();
    write(repo, `${dir}/Thing.cs`, "var x = absence.Note;\n");
    git(repo, "add", "--all");
    assert.match(touches(repo, stagedDiff(repo)).join(), /Thing\.cs adds or removes a line/, dir);

    commit(repo, "add");
    write(repo, `${dir}/Thing.cs`, "var x = 1;\n");
    git(repo, "add", "--all");
    assert.equal(touches(repo, stagedDiff(repo)).length, 1, `${dir}, removed`);
  }
});

test("the pattern is case-insensitive and matches whole words only", () => {
  const repo = fixture();
  write(repo, "src/a.ts", "const NOTES = 1;\nconst notebook = 2;\nconst denoted = 3;\n");
  git(repo, "add", "--all");
  const reasons = touches(repo, stagedDiff(repo));
  assert.equal(reasons.length, 1);
  assert.match(reasons[0], /NOTES/);
});

test("a matching line outside src, tests and tools does not touch notes", () => {
  const repo = fixture();
  write(repo, "docs/other.md", "Release notes.\n");
  write(repo, "scripts/x.mjs", "// the note pattern\n");
  git(repo, "add", "--all");
  assert.deepEqual(touches(repo, stagedDiff(repo)), []);
});

test("a matching line that is only context does not touch notes", () => {
  const repo = fixture();
  write(repo, "src/a.cs", "// a note\nvar x = 1;\n");
  commit(repo, "a");
  write(repo, "src/a.cs", "// a note\nvar x = 2;\n");
  git(repo, "add", "--all");
  assert.deepEqual(touches(repo, stagedDiff(repo)), []);
});

test("a removed line that reads like a diff header is still a removed line", () => {
  const repo = fixture();
  write(repo, "src/a.sql", "-- note: kept for the author\nselect 1;\n");
  commit(repo, "a");
  write(repo, "src/a.sql", "select 1;\n");
  git(repo, "add", "--all");
  assert.equal(touches(repo, stagedDiff(repo)).length, 1);
});

test("an unrelated change does not touch notes", () => {
  const repo = fixture();
  write(repo, "src/Api/Services/PersonService.cs", "class PersonService {}\n");
  git(repo, "add", "--all");
  assert.deepEqual(touches(repo, stagedDiff(repo)), []);
});

// The CI check (AGT-5). Each history starts with a commit that touches notes unreviewed,
// then the commit that adds scripts/privacy-review.mjs, from which the check applies.

/** A repository whose review starts at its second commit; returns it and that commit. */
function history() {
  const repo = fixture();
  write(repo, "src/Old.cs", "var note = 1;\n");
  commit(repo, "before the review existed");
  write(repo, "scripts/privacy-review.mjs", "// the script\n");
  const intro = commit(repo, "add the review");
  return { repo, intro };
}

/** Commits what is staged with the footer this repository uses and, when `trailer` is
 * given, that trailer on the line above its last line. */
function commitWith(repo, subject, trailer) {
  git(repo, "add", "--all");
  const footer = trailer ? `${trailer}\nCo-authored by AI.` : "Co-authored by AI.";
  git(repo, "commit", "--quiet", "--message", `${subject}\n\nWhy.\n\n---\n\n${footer}`);
  return git(repo, "rev-parse", "HEAD").trim();
}

/** Commits what is staged with the trailer its own staged diff calls for. */
function commitReviewed(repo, subject) {
  git(repo, "add", "--all");
  return commitWith(repo, subject, trailerLine(diffHash(stagedDiff(repo))));
}

test("[AGT-5] a reviewed commit passes", () => {
  const { repo, intro } = history();
  write(repo, "src/A.cs", "var note = 2;\n");
  const head = commitReviewed(repo, "reviewed");
  const result = verify(repo, intro, head);
  assert.deepEqual(result.problems, []);
  assert.equal(result.checked, 1);
});

test("[AGT-5] a commit touching notes without a trailer fails, naming it", () => {
  const { repo, intro } = history();
  write(repo, "src/A.cs", "var note = 2;\n");
  const head = commitWith(repo, "unreviewed");
  const { problems } = verify(repo, intro, head);
  assert.equal(problems.length, 1);
  assert.match(
    problems[0],
    new RegExp(`^${head.slice(0, 12)} "unreviewed".*no Privacy-Review trailer`),
  );
});

test("[AGT-5] a trailer copied onto a different diff fails", () => {
  const { repo, intro } = history();
  write(repo, "src/A.cs", "var note = 2;\n");
  git(repo, "add", "--all");
  const reviewed = trailerLine(diffHash(stagedDiff(repo)));
  write(repo, "src/A.cs", "var note = 3;\n");
  const head = commitWith(repo, "changed after the review", reviewed);
  const { problems } = verify(repo, intro, head);
  assert.equal(problems.length, 1);
  assert.match(problems[0], /trailer was written for a different change/);
});

test("[AGT-5] a trailer outside the message's last paragraph is not read", () => {
  const { repo, intro } = history();
  write(repo, "src/A.cs", "var note = 2;\n");
  git(repo, "add", "--all");
  const trailer = trailerLine(diffHash(stagedDiff(repo)));
  git(repo, "commit", "--quiet", "--message", `buried\n\n${trailer}\n\n---\n\nCo-authored by AI.`);
  const { problems } = verify(repo, intro, "HEAD");
  assert.match(problems.join(), /no Privacy-Review trailer/);
});

test("[AGT-5] a commit that does not touch notes passes without a trailer", () => {
  const { repo, intro } = history();
  write(repo, "src/Person.cs", "class Person {}\n");
  const head = commitWith(repo, "unrelated");
  assert.deepEqual(verify(repo, intro, head).problems, []);
});

test("[AGT-5] a merge commit is skipped; the commits it merges are checked", () => {
  const { repo, intro } = history();
  git(repo, "switch", "--quiet", "--create", "topic");
  write(repo, "src/A.cs", "var note = 2;\n");
  commitReviewed(repo, "reviewed on the branch");
  git(repo, "switch", "--quiet", "master");
  write(repo, "src/Person.cs", "class Person {}\n");
  commitWith(repo, "meanwhile on master");
  // The merge's diff against its first parent touches notes, and it carries no trailer.
  git(repo, "merge", "--quiet", "--no-ff", "--message", "merge topic", "topic");

  const result = verify(repo, intro, "HEAD");
  assert.deepEqual(result.problems, []);
  assert.equal(result.skipped, 1);
  assert.equal(result.checked, 2);
});

test("[AGT-5] commits from before the review was added are exempt", () => {
  const { repo } = history();
  const root = git(repo, "rev-list", "--max-parents=0", "HEAD").trim();
  write(repo, "src/Person.cs", "class Person {}\n");
  const head = commitWith(repo, "unrelated");
  // The range reaches back to the unreviewed "before the review existed" commit.
  const result = verify(repo, root, head);
  assert.deepEqual(result.problems, []);
  assert.equal(result.checked, 2);
});

test("[AGT-5] a push checks before..after, and the pushed commit alone when before is all zeros", () => {
  const { repo, intro } = history();
  write(repo, "src/A.cs", "var note = 2;\n");
  const unreviewed = commitWith(repo, "unreviewed, pushed earlier");
  write(repo, "src/A.cs", "var note = 3;\n");
  const head = commitReviewed(repo, "reviewed");

  assert.deepEqual(verify(repo, unreviewed, head).problems, []);
  assert.equal(verify(repo, intro, head).problems.length, 1);

  const zeros = "0".repeat(40);
  const alone = verify(repo, zeros, head);
  assert.deepEqual(alone.problems, []);
  assert.equal(alone.checked, 1);
  assert.equal(verify(repo, zeros, unreviewed).problems.length, 1);
  // A before this clone does not have, as after a force push: the pushed commit alone.
  assert.equal(verify(repo, "1".repeat(40), head).checked, 1);
});

test("[AGT-5] a pull request checks base..head, its own commits only", () => {
  const { repo } = history();
  write(repo, "src/A.cs", "var note = 2;\n");
  commitWith(repo, "unreviewed, already on master");
  const base = git(repo, "rev-parse", "HEAD").trim();
  git(repo, "switch", "--quiet", "--create", "pr");
  write(repo, "src/A.cs", "var note = 3;\n");
  commitReviewed(repo, "reviewed");
  write(repo, "src/B.cs", "var note = 4;\n");
  const head = commitWith(repo, "unreviewed in the pull request");

  const { problems, checked } = verify(repo, base, head);
  assert.equal(checked, 2);
  assert.equal(problems.length, 1);
  assert.match(problems[0], /unreviewed in the pull request/);
});

test("[AGT-5] the command fails on a problem and says squash merging bypasses the review", () => {
  const { repo, intro } = history();
  write(repo, "src/A.cs", "var note = 2;\n");
  const head = commitWith(repo, "unreviewed");
  const run = spawnSync(process.execPath, [script, "verify", intro, head], {
    cwd: repo,
    encoding: "utf8",
  });
  assert.equal(run.status, 1);
  assert.match(run.stderr, /unreviewed/);
  assert.match(run.stderr, /squash/);

  const ok = spawnSync(process.execPath, [script, "verify", head, head], {
    cwd: repo,
    encoding: "utf8",
  });
  assert.equal(ok.status, 0, ok.stderr);
});
