#!/usr/bin/env node
// Checks that SPEC.md and the tests agree (SPEC.md, TRC-1 and TRC-2):
//
// - every requirement ID a test references exists in SPEC.md and is not retired;
// - every requirement verified by [test] is referenced by at least one test, unless it is
//   on the gap list (tests/spec-coverage-gaps.txt) with the reason it isn't yet;
// - nothing on the gap list is referenced by a test, so the list can only shrink.
//
// A .NET test references a requirement with [Trait("Spec", "ABS-5")]; a Vitest test puts
// the ID in brackets in its name, e.g. it("[UI-2] clamps to the year", ...).
//
// No dependencies, so CI can run it with nothing but Node: `node scripts/check-spec-trace.mjs`.
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const specPath = path.join(root, "SPEC.md");
const gapsPath = path.join(root, "tests", "spec-coverage-gaps.txt");

const ID = String.raw`[A-Z]+-\d+`;

/** Every file under `dir` whose name passes `keep`, skipping build output and packages. */
function filesUnder(dir, keep) {
  const skipped = new Set(["bin", "obj", "node_modules"]);
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return skipped.has(entry.name) ? [] : filesUnder(full, keep);
    return keep(entry.name) ? [full] : [];
  });
}

/** ID -> { verification, retired }, from the "- **ID** ..." bullets of SPEC.md. A bullet
 * runs until the next bullet, heading or blank line, and its verification is the last
 * `[test]`, `[ci]` or `[review]` tag in it. */
function readRequirements() {
  const requirements = new Map();
  const problems = [];
  const bullets = readFileSync(specPath, "utf8").split(/\n(?=- |#|\n)/);

  for (const bullet of bullets) {
    const match = bullet.match(new RegExp(String.raw`^- \*\*(${ID})\*\*`));
    if (!match) continue;
    const id = match[1];

    const tags = [...bullet.matchAll(/`\[(test|ci|review)\]`/g)].map((m) => m[1]);
    const retired = /\*Retired\*/.test(bullet);
    if (requirements.has(id)) problems.push(`SPEC.md defines ${id} more than once.`);
    if (tags.length === 0 && !retired)
      problems.push(`SPEC.md: ${id} has no [test], [ci] or [review] tag.`);

    requirements.set(id, { verification: tags.at(-1) ?? null, retired });
  }

  return { requirements, problems };
}

/** ID -> files referencing it. */
function readReferences() {
  const references = new Map();
  const add = (id, file) => {
    const files = references.get(id) ?? new Set();
    files.add(path.relative(root, file));
    references.set(id, files);
  };

  const trait = new RegExp(String.raw`\[\s*Trait\(\s*"Spec"\s*,\s*"(${ID})"\s*\)`, "g");
  for (const file of filesUnder(path.join(root, "tests"), (name) => name.endsWith(".cs"))) {
    for (const match of readFileSync(file, "utf8").matchAll(trait)) add(match[1], file);
  }

  // The test's name is the first argument of it()/test(), which Prettier may move onto the
  // next line, hence \s* rather than a space.
  const testName = /\b(?:it|test)(?:\.\w+)*\(\s*(["'`])((?:\\.|(?!\1).)*)\1/g;
  const bracketedId = new RegExp(String.raw`\[(${ID})\]`, "g");
  const web = path.join(root, "src", "Web", "src");
  for (const file of filesUnder(web, (name) => name.endsWith(".spec.ts"))) {
    for (const name of readFileSync(file, "utf8").matchAll(testName)) {
      for (const match of name[2].matchAll(bracketedId)) add(match[1], file);
    }
  }

  return references;
}

/** ID -> reason, from lines of "ID  reason". Blank lines and # comments are ignored. */
function readGaps() {
  const gaps = new Map();
  const problems = [];
  const lines = readFileSync(gapsPath, "utf8").split("\n");

  lines.forEach((line, index) => {
    const text = line.trim();
    if (text === "" || text.startsWith("#")) return;

    const match = text.match(new RegExp(String.raw`^(${ID})\s+(\S.*)$`));
    const where = `${path.relative(root, gapsPath)}:${index + 1}`;
    if (!match) {
      problems.push(`${where}: expected "<ID> <reason>", got "${text}".`);
      return;
    }
    if (gaps.has(match[1])) problems.push(`${where}: ${match[1]} is listed twice.`);
    gaps.set(match[1], match[2]);
  });

  return { gaps, problems };
}

const { requirements, problems } = readRequirements();
const references = readReferences();
const { gaps, problems: gapProblems } = readGaps();
problems.push(...gapProblems);

for (const [id, files] of references) {
  const requirement = requirements.get(id);
  const where = [...files].join(", ");
  if (!requirement) problems.push(`${id} is referenced by ${where} but is not in SPEC.md.`);
  else if (requirement.retired) problems.push(`${id} is retired but still referenced by ${where}.`);
}

for (const [id, { verification, retired }] of requirements) {
  if (verification !== "test" || retired) continue;
  if (!references.has(id) && !gaps.has(id))
    problems.push(
      `${id} is verified by [test] but no test references it, and it is not on the gap list.`,
    );
}

for (const id of gaps.keys()) {
  const requirement = requirements.get(id);
  if (!requirement) problems.push(`The gap list names ${id}, which is not in SPEC.md.`);
  else if (requirement.verification !== "test" || requirement.retired)
    problems.push(`The gap list names ${id}, which is not a [test] requirement.`);
  else if (references.has(id))
    problems.push(
      `${id} is now referenced by ${[...references.get(id)].join(", ")}: remove it from the gap list.`,
    );
}

const tested = [...requirements.values()].filter((r) => r.verification === "test" && !r.retired);
if (problems.length > 0) {
  for (const problem of problems) console.error(`error: ${problem}`);
  process.exit(1);
}

console.log(
  `Spec trace OK: ${tested.length - gaps.size} of ${tested.length} [test] requirements ` +
    `referenced by tests, ${gaps.size} on the gap list.`,
);
