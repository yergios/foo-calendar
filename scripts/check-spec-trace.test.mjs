// Tests of check-spec-trace.mjs, run with `node --test scripts/check-spec-trace.test.mjs`.
// Each one runs a copy of the script against a small made-up repository, since the script
// reads the repository it sits in.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { copyFileSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const script = path.join(path.dirname(fileURLToPath(import.meta.url)), "check-spec-trace.mjs");

const spec = `# Spec

- **A-1** Tested. \`[test]\`
- **A-2** Not tested yet. \`[test]\`
- **A-3** Gone. *Retired* \`[test]\`
- **A-4** Looked at. \`[review]\`
`;

/** Runs the script on a repository holding SPEC.md above, A-2 on the gap list, and the
 * given test sources: .NET, Vitest, end-to-end and a script's node:test suite. */
function check({ cs = "", ts = "", e2e = "", scriptTest = "", gaps = "A-2  needs a test\n" } = {}) {
  const repo = mkdtempSync(path.join(tmpdir(), "spec-trace-"));
  try {
    mkdirSync(path.join(repo, "scripts"));
    mkdirSync(path.join(repo, "tests"));
    mkdirSync(path.join(repo, "src", "Web", "src"), { recursive: true });
    copyFileSync(script, path.join(repo, "scripts", "check-spec-trace.mjs"));
    writeFileSync(path.join(repo, "SPEC.md"), spec);
    writeFileSync(path.join(repo, "tests", "spec-coverage-gaps.txt"), gaps);
    writeFileSync(path.join(repo, "tests", "Tests.cs"), cs);
    writeFileSync(path.join(repo, "src", "Web", "src", "tests.spec.ts"), ts);
    mkdirSync(path.join(repo, "tools", "e2e"), { recursive: true });
    writeFileSync(path.join(repo, "tools", "e2e", "flows.test.mjs"), e2e);
    mkdirSync(path.join(repo, "scripts", "hooks"), { recursive: true });
    writeFileSync(path.join(repo, "scripts", "hooks", "hook.test.mjs"), scriptTest);

    const run = spawnSync(process.execPath, [path.join(repo, "scripts", "check-spec-trace.mjs")], {
      encoding: "utf8",
    });
    return { ok: run.status === 0, output: run.stdout + run.stderr };
  } finally {
    rmSync(repo, { recursive: true, force: true });
  }
}

const unreferenced = /A-1 is verified by \[test\] but no test references it/;

test("passes when every [test] requirement is referenced or on the gap list", () => {
  const result = check({ cs: '[Fact]\n[Trait("Spec", "A-1")]\npublic void X() {}' });
  assert.ok(result.ok, result.output);
});

test("fails on a [test] requirement neither referenced nor on the gap list", () => {
  assert.match(check().output, unreferenced);
});

test("fails on an ID that is not in SPEC.md", () => {
  const result = check({ cs: '[Fact, Trait("Spec", "A-1"), Trait("Spec", "B-9")]' });
  assert.ok(!result.ok);
  assert.match(result.output, /B-9 is referenced by tests\/Tests.cs but is not in SPEC.md/);
});

test("fails on a retired ID", () => {
  const result = check({ ts: 'it("[A-1] [A-3] does it", () => {});' });
  assert.ok(!result.ok);
  assert.match(result.output, /A-3 is retired but still referenced/);
});

test("fails on a gap-listed ID that a test now references", () => {
  const result = check({ ts: 'it("[A-1] [A-2] does it", () => {});' });
  assert.ok(!result.ok);
  assert.match(result.output, /A-2 is now referenced by .*remove it from the gap list/);
});

test("reads Trait anywhere in a run of attribute lists", () => {
  for (const cs of [
    '[Fact, Trait("Spec", "A-1")]',
    '[Theory]\n[InlineData("https://example.test/")]\n[Trait( "Spec" , "A-1" )]',
    '[Fact] [Trait("Spec", "A-1")]',
  ])
    assert.ok(check({ cs }).ok, cs);
});

test("reads a Vitest name Prettier moved onto the next line", () => {
  assert.ok(check({ ts: 'it(\n  "[A-1] does it",\n  () => {},\n);' }).ok);
});

test("ignores commented-out references", () => {
  for (const cs of [
    '// [Trait("Spec", "A-1")]',
    '[Fact] // [Trait("Spec", "A-1")]',
    '/*\n[Fact]\n[Trait("Spec", "A-1")]\n*/',
  ])
    assert.match(check({ cs }).output, unreferenced, cs);

  assert.match(check({ ts: '// it("[A-1] does it", () => {});' }).output, unreferenced);
});

test("ignores skipped .NET tests", () => {
  for (const cs of [
    '[Fact(Skip = "later")]\n[Trait("Spec", "A-1")]',
    '[Trait("Spec", "A-1")]\n[Theory(DisplayName = "x", Skip = "later")]',
    '[Fact(Skip = "later"), Trait("Spec", "A-1")]',
  ])
    assert.match(check({ cs }).output, unreferenced, cs);
});

test("ignores skipped and todo Vitest tests, and tests in a skipped describe", () => {
  for (const ts of [
    'it.skip("[A-1] does it", () => {});',
    'test.skip("[A-1] does it", () => {});',
    'it.todo("[A-1] does it");',
    'xit("[A-1] does it", () => {});',
    'it.skipIf(true)("[A-1] does it", () => {});',
    'describe.skip("group", () => {\n  it("[A-1] does it", () => {});\n});',
    'describe.skip.each([1])("group %i", () => {\n  it("[A-1] does it", () => {});\n});',
  ])
    assert.match(check({ ts }).output, unreferenced, ts);
});

test("still counts a test after a skipped one", () => {
  const ts = 'describe.skip("group", () => {});\nit("[A-1] does it", () => {});';
  assert.ok(check({ ts }).ok);
});

test("reads end-to-end test names in tools/e2e", () => {
  assert.ok(check({ e2e: 'test("[A-1] does it in a browser", flow(async () => {}));' }).ok);
});

test("ignores end-to-end tests skipped or left todo by node:test's options", () => {
  for (const e2e of [
    'test("[A-1] does it", { skip: true }, async () => {});',
    'test("[A-1] does it", { timeout: 5000, todo: "later" }, async () => {});',
    'test.skip("[A-1] does it", async () => {});',
  ])
    assert.match(check({ e2e }).output, unreferenced, e2e);
});

test("still counts an end-to-end test whose options do not skip it", () => {
  assert.ok(check({ e2e: 'test("[A-1] does it", { timeout: 5000 }, async () => {});' }).ok);
});

test("reads node:test names under scripts, subdirectories included", () => {
  assert.ok(check({ scriptTest: 'test("[A-1] the hook blocks", () => {});' }).ok);
});
