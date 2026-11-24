// Tests of privacy-review.mjs, run with `node --test scripts/privacy-review.test.mjs`. Each
// builds a small repository in a temporary directory and drives git in it for real.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { after, test } from "node:test";

import { commitDiff, diffHash, readRules, stagedDiff, touchesNotes } from "./privacy-review.mjs";

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
