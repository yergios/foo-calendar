#!/usr/bin/env node
// Checks that SPEC.md and the tests agree (SPEC.md, TRC-1 and TRC-2):
//
// - every requirement ID a test references exists in SPEC.md and is not retired;
// - every requirement verified by [test] is referenced by at least one test, unless it is
//   on the gap list (tests/spec-coverage-gaps.txt) with the reason it isn't yet;
// - nothing on the gap list is referenced by a test, so the list can only shrink.
//
// A .NET test references a requirement with [Trait("Spec", "ABS-5")]; a Vitest test puts
// the ID in brackets in its name, e.g. it("[UI-2] clamps to the year", ...). Neither counts
// when it is commented out or skipped ([Fact(Skip = ...)], it.skip, describe.skip, ...):
// a test that does not run verifies nothing.
//
// Its own tests: `node --test scripts/check-spec-trace.test.mjs`.
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

/** Where the string literal starting at `i` ends, or -1 if none starts there. Covers the
 * forms the tests use: "..." and '...' with backslash escapes, JavaScript `...`, and C#
 * @"..." (doubled quotes) and """...""" raw strings. Interpolation holes are not parsed. */
function stringEnd(source, i, csharp) {
  if (csharp) {
    const raw = /^"{3,}/.exec(source.slice(i, i + 16));
    if (raw) {
      const close = source.indexOf(raw[0], i + raw[0].length);
      return close < 0 ? source.length : close + raw[0].length;
    }
    const verbatim = /^(?:@\$?|\$@)"/.exec(source.slice(i, i + 3));
    if (verbatim) {
      let j = i + verbatim[0].length;
      while (j < source.length) {
        if (source[j] === '"' && source[j + 1] === '"') j += 2;
        else if (source[j] === '"') return j + 1;
        else j++;
      }
      return source.length;
    }
  }
  const quote = source[i];
  if (quote !== '"' && quote !== "'" && (csharp || quote !== "`")) return -1;
  let j = i + 1;
  while (j < source.length && source[j] !== quote) {
    // A line break ends an unterminated '...' or "...", which keeps a stray apostrophe from
    // swallowing the rest of the file.
    if (source[j] === "\n" && quote !== "`") return j;
    j += source[j] === "\\" ? 2 : 1;
  }
  return Math.min(j + 1, source.length);
}

/** Two copies of `source`, each the same length so offsets carry over: `text` with the
 * comments blanked out, and `code` with string contents blanked as well (quotes kept), so
 * brackets and keywords can be matched in it without a string or comment getting in the
 * way. */
function blank(source, csharp) {
  // UTF-16 units, not [...source]'s code points, so indices into these match `source`.
  const text = source.split("");
  const code = source.split("");
  const spaces = (array, from, to) => {
    for (let k = from; k < to; k++) if (array[k] !== "\n") array[k] = " ";
  };

  let i = 0;
  while (i < source.length) {
    if (source.startsWith("//", i) || source.startsWith("/*", i)) {
      const line = source[i + 1] === "/";
      const close = source.indexOf(line ? "\n" : "*/", i + 2);
      const end = close < 0 ? source.length : line ? close : close + 2;
      spaces(text, i, end);
      spaces(code, i, end);
      i = end;
      continue;
    }
    const end = stringEnd(source, i, csharp);
    if (end > i) {
      // Past a C# @ or $ prefix, to the quote itself.
      const quote = /["'`]/.test(source[i]) ? i : source.indexOf('"', i);
      spaces(code, quote + 1, end - 1);
      i = end;
      continue;
    }
    i++;
  }

  return { text: text.join(""), code: code.join("") };
}

/** Index just past the bracket closing the one at `open`, in comment- and string-free
 * code. */
function closing(code, open) {
  const pairs = { "(": ")", "[": "]", "{": "}" };
  const stack = [];
  for (let i = open; i < code.length; i++) {
    if (pairs[code[i]]) stack.push(pairs[code[i]]);
    else if (code[i] === stack.at(-1)) {
      stack.pop();
      if (stack.length === 0) return i + 1;
    }
  }
  return code.length;
}

/** IDs referenced by a C# file: Trait("Spec", ID) anywhere in a run of attribute lists, as
 * long as none of them skips the test. */
function csharpReferences(source) {
  const { text, code } = blank(source, true);
  const trait = new RegExp(String.raw`\bTrait\s*\(\s*"Spec"\s*,\s*"(${ID})"\s*\)`, "g");
  const ids = [];

  // An attribute run starts a line; [Fact] [Trait(...)] on one line, or over several, is
  // one run, read as a whole because the skip and the trait may sit in different lists.
  let runEnd = 0;
  for (const start of code.matchAll(/^[ \t]*\[/gm)) {
    if (start.index < runEnd) continue;
    let end = start.index + start[0].length - 1;
    do {
      end = closing(code, end);
      const next = code.slice(end).search(/\S/);
      if (next < 0 || code[end + next] !== "[") break;
      end += next;
    } while (end < code.length);

    runEnd = end;
    if (/\bSkip\s*=/.test(code.slice(start.index, end))) continue;
    for (const match of text.slice(start.index, end).matchAll(trait)) ids.push(match[1]);
  }

  return ids;
}

/** IDs referenced by a Vitest file: [ID] in the name of an it() or test(), unless it, or
 * a describe() around it, is skipped or a todo. */
function vitestReferences(source) {
  let { text, code } = blank(source, false);

  // Blank each skipped call, arguments, body and all, so nothing inside it is read below.
  // A curried call such as describe.skip.each(table)(name, fn) is followed through to its
  // last argument list.
  const call = /\b(x?)(?:it|test|describe|suite)((?:\.\w+)*)\s*\(/g;
  for (const match of code.matchAll(call)) {
    const modifiers = match[2].split(".");
    if (match[1] !== "x" && !modifiers.some((m) => ["skip", "skipIf", "todo"].includes(m)))
      continue;
    // Inside a skipped call already blanked.
    if (code.slice(match.index, match.index + match[0].length) !== match[0]) continue;

    let end = match.index + match[0].length - 1;
    do {
      end = closing(code, end);
      const next = code.slice(end).search(/\S/);
      if (next < 0 || code[end + next] !== "(") break;
      end += next;
    } while (end < code.length);

    const blanked = (s) =>
      s.slice(0, match.index) + s.slice(match.index, end).replace(/[^\n]/g, " ") + s.slice(end);
    text = blanked(text);
    code = blanked(code);
  }

  // The test's name is the first argument of it()/test(), which Prettier may move onto the
  // next line, hence \s* rather than a space.
  const testName = /\b(?:it|test)(?:\.\w+)*\(\s*(["'`])/g;
  const bracketedId = new RegExp(String.raw`\[(${ID})\]`, "g");
  const ids = [];
  for (const match of code.matchAll(testName)) {
    const open = match.index + match[0].length - 1;
    const name = text.slice(open + 1, code.indexOf(match[1], open + 1));
    for (const id of name.matchAll(bracketedId)) ids.push(id[1]);
  }

  return ids;
}

/** ID -> files referencing it. */
function readReferences() {
  const references = new Map();
  const add = (id, file) => {
    const files = references.get(id) ?? new Set();
    files.add(path.relative(root, file));
    references.set(id, files);
  };

  for (const file of filesUnder(path.join(root, "tests"), (name) => name.endsWith(".cs"))) {
    for (const id of csharpReferences(readFileSync(file, "utf8"))) add(id, file);
  }

  const web = path.join(root, "src", "Web", "src");
  for (const file of filesUnder(web, (name) => name.endsWith(".spec.ts"))) {
    for (const id of vitestReferences(readFileSync(file, "utf8"))) add(id, file);
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
