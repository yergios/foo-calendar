#!/usr/bin/env node
// The commit hook (AGT-3): a PreToolUse hook on every Bash command. Before Claude Code runs
// a command that would create a commit whose diff touches absence notes
// (.claude/privacy-paths.txt, as committed at HEAD), the privacy-reviewer subagent must have
// passed that exact diff, and the message must carry its trailer on the line just above
// "Co-authored by AI.". Anything else is let through at once. Wired in .claude/settings.json;
// tested by commit-privacy.test.mjs with a stub `claude`.
//
// The flow, for a `git commit` that touches notes:
// 1. A passed verdict for the diff's hash is looked up in .git/privacy-reviews/.
// 2. With none, the reviewer runs headlessly (scripts/privacy-review.mjs). A failed
//    verdict, or a review that could not run, blocks the commit with the reason.
// 3. A passed verdict is cached. Without the trailer in the message, the commit is blocked
//    once with the exact line to add; with it, it goes through.
// The diff is the staged one; while a merge is being concluded, the merge's remerge diff,
// what it adds beyond merging its parents automatically, as CI checks it.
//
// Finding the git commands. Claude Code's own `if` filter only sees a command that starts
// with `git`, so this hook runs on every Bash command and reads it as a shell would, well
// enough to find git wherever it is run: after `cd`, `&&`, `;` or a pipe, in a subshell, a
// group or a command substitution, behind `env`, `command`, `timeout`, `nice`, `xargs` and
// the like, with variable assignments in front, by path (`/usr/bin/git`), through
// `bash -c`, `sh -c` or `eval`, through a shell variable set earlier in the same command,
// and through a git alias. Git's own options before the subcommand (`-C`, `-c`, `--git-dir`,
// `--work-tree`) are honoured. A command with no "git" in it exits before anything is
// loaded. What it cannot see, a script file or another program that runs git itself, CI
// still checks (AGENTS.md lists it).
//
// The commands that create commits:
// - `git commit` commits the staged diff, which is reviewed. With -a, -i, -o, -p, a
//   pathspec, or after a command in the same call that changes the index, it commits
//   something else; when what it could commit touches notes, it is refused, and the change
//   is to be staged first, in a command of its own.
// - `git merge` making a merge commit by itself adds nothing a clean automatic merge would
//   not, and is let through. With a strategy or strategy option, or several branches at
//   once, its result can differ from that, and when the branches touch notes it is refused
//   in favour of `git merge --no-commit` then `git commit`, which is reviewed. `git pull`
//   likewise, and as a rebase it is judged as one.
// - `git rebase`, `git cherry-pick` and `git revert` replay commits onto another base,
//   which gives them new diffs and so new hashes. When a commit they would replay touches
//   notes, they are refused in favour of `--no-commit` (or cherry-picking one at a time)
//   followed by `git commit`. Their `--continue`, and `git merge --continue` and
//   `git am --continue`, commit the staged resolution, and are refused when it touches
//   notes, to be committed with `git commit` first.
// - `git am` is judged on its patch files, `git commit-tree` on its tree against its first
//   parent, and refused when they touch notes or cannot be read.
import { readFileSync } from "node:fs";

const input = JSON.parse(readFileSync(0, "utf8") || "{}");
const command = input.tool_input?.command ?? "";

// Cheap enough for every Bash call: nothing that never names git gets past this line.
if (input.tool_name !== "Bash" || !command.includes("git")) process.exit(0);

const { spawnSync } = await import("node:child_process");
const { existsSync, statSync } = await import("node:fs");
const path = (await import("node:path")).default;
const review = await import("../privacy-review.mjs");

// ---- reading the command ----

/**
 * The simple commands in a shell command line, in order, each with its words (quotes
 * removed), its redirections and any heredoc it reads. Commands inside `$(...)`, backticks
 * and subshells are listed too, before the command that contains them.
 */
function parseShell(src) {
  const commands = [];
  let i = 0;

  /** The body of a $(...) or `...` just entered, parsed into `commands`. As a word's text
   * it becomes what it would print when that is plain: a heredoc piped through `cat`, or
   * `echo`'s arguments. Otherwise its source text. */
  function substitution(stop) {
    const start = i - (stop === ")" ? 2 : 1);
    const before = commands.length;
    parseList(stop);
    const inner = commands.slice(before);
    if (inner.length === 1) {
      const [cmd] = inner;
      if (cmd.words.length === 1 && cmd.words[0] === "cat" && cmd.heredoc !== null)
        return cmd.heredoc.replace(/\n+$/, "");
      if (cmd.words[0] === "echo") return cmd.words.slice(1).join(" ");
    }
    return src.slice(start, i);
  }

  function doubleQuoted() {
    let text = "";
    while (i < src.length && src[i] !== '"') {
      const c = src[i];
      if (c === "\\" && i + 1 < src.length && '$`"\\\n'.includes(src[i + 1])) {
        if (src[i + 1] !== "\n") text += src[i + 1];
        i += 2;
      } else if (c === "$" && src[i + 1] === "(" && src[i + 2] !== "(") {
        i += 2;
        text += substitution(")");
      } else if (c === "`") {
        i += 1;
        text += substitution("`");
      } else {
        text += c;
        i += 1;
      }
    }
    i += 1;
    return text;
  }

  function ansiQuoted() {
    const escapes = {
      n: "\n",
      t: "\t",
      r: "\r",
      "\\": "\\",
      "'": "'",
      '"': '"',
      a: "\x07",
      e: "\x1b",
    };
    let text = "";
    while (i < src.length && src[i] !== "'") {
      if (src[i] === "\\" && i + 1 < src.length) {
        text += escapes[src[i + 1]] ?? src[i + 1];
        i += 2;
      } else text += src[i++];
    }
    i += 1;
    return text;
  }

  /** One word from `i`, or null when there is none there. */
  function readWord(stop) {
    let text = "";
    let any = false;
    while (i < src.length) {
      const c = src[i];
      if (" \t\n;&|<>()".includes(c) || (c === "`" && stop === "`")) break;
      any = true;
      if (c === "\\") {
        if (src[i + 1] !== "\n") text += src[i + 1] ?? "";
        i += 2;
      } else if (c === "'") {
        const end = src.indexOf("'", i + 1);
        const stopAt = end < 0 ? src.length : end;
        text += src.slice(i + 1, stopAt);
        i = stopAt + 1;
      } else if (c === "$" && src[i + 1] === "'") {
        i += 2;
        text += ansiQuoted();
      } else if (c === '"') {
        i += 1;
        text += doubleQuoted();
      } else if (c === "$" && src[i + 1] === "(" && src[i + 2] === "(") {
        const end = src.indexOf("))", i);
        text += "0";
        i = end < 0 ? src.length : end + 2;
      } else if (c === "$" && src[i + 1] === "(") {
        i += 2;
        text += substitution(")");
      } else if (c === "`") {
        i += 1;
        text += substitution("`");
      } else {
        text += c;
        i += 1;
      }
    }
    return any ? text : null;
  }

  function parseList(stop) {
    let cmd = { words: [], redirs: [], heredoc: null };
    const heredocs = [];
    const finish = () => {
      if (cmd.words.length > 0 || cmd.redirs.length > 0) commands.push(cmd);
      cmd = { words: [], redirs: [], heredoc: null };
    };
    const readHeredocs = () => {
      for (const { target, delimiter, strip } of heredocs.splice(0)) {
        let body = "";
        while (i < src.length) {
          const end = src.indexOf("\n", i);
          const line = src.slice(i, end < 0 ? src.length : end);
          i = end < 0 ? src.length : end + 1;
          if ((strip ? line.replace(/^\t+/, "") : line) === delimiter) break;
          body += line + "\n";
        }
        target.heredoc = body;
      }
    };

    while (i < src.length) {
      const c = src[i];
      if ((stop === ")" && c === ")") || (stop === "`" && c === "`")) {
        i += 1;
        finish();
        return;
      }
      if (c === " " || c === "\t") {
        i += 1;
      } else if (c === "\n") {
        i += 1;
        finish();
        readHeredocs();
      } else if (c === "#") {
        while (i < src.length && src[i] !== "\n") i += 1;
      } else if (c === ";" || c === "&" || c === "|") {
        if (c === "&" && src[i + 1] === ">") {
          i += src[i + 2] === ">" ? 3 : 2;
          while (src[i] === " " || src[i] === "\t") i += 1;
          cmd.redirs.push({ op: ">", target: readWord(stop) });
          continue;
        }
        i += src[i + 1] === c || (c === "|" && src[i + 1] === "&") ? 2 : 1;
        finish();
      } else if (c === "(") {
        i += 1;
        finish();
        parseList(")");
      } else if (c === ")") {
        i += 1;
        finish();
      } else if (c === "<" || c === ">") {
        const op = /^(<<-|<<<|<<|<&|<>|<|>>|>&|>\||>)/.exec(src.slice(i))[1];
        i += op.length;
        while (src[i] === " " || src[i] === "\t") i += 1;
        const target = readWord(stop) ?? "";
        if (op === "<<" || op === "<<-")
          heredocs.push({ target: cmd, delimiter: target, strip: op === "<<-" });
        else if (op === "<<<") cmd.heredoc = target + "\n";
        else cmd.redirs.push({ op, target });
      } else {
        const word = readWord(stop);
        if (word === null) {
          i += 1;
          continue;
        }
        // A file descriptor number directly before a redirection belongs to it.
        if (/^\d+$/.test(word) && (src[i] === "<" || src[i] === ">")) continue;
        cmd.words.push(word);
      }
    }
    finish();
    readHeredocs();
  }

  parseList(null);
  return commands;
}

const ASSIGNMENT = /^([A-Za-z_][A-Za-z0-9_]*)=(.*)$/s;
const RESERVED = new Set([
  "{",
  "}",
  "!",
  "if",
  "then",
  "elif",
  "else",
  "do",
  "while",
  "until",
  "time",
]);

/** The words of a space-separated list. */
const list = (text) => text.split(/\s+/).filter(Boolean);

/** Wrapper commands that run the rest of their words as a command, with their options that
 * take a value (as a separate word) and how many plain arguments come before the command. */
const WRAPPERS = {
  env: { values: ["-u", "--unset", "-C", "--chdir", "-S", "--split-string"], args: 0 },
  command: { values: [], args: 0 },
  builtin: { values: [], args: 0 },
  exec: { values: ["-a"], args: 0 },
  nohup: { values: [], args: 0 },
  time: { values: ["-f", "-o"], args: 0 },
  nice: { values: ["-n", "--adjustment"], args: 0 },
  ionice: { values: ["-c", "-n", "-p", "-P", "-u"], args: 0 },
  timeout: { values: ["-s", "--signal", "-k", "--kill-after"], args: 1 },
  xargs: {
    values: list("-I -i -n -L -l -P -d -E -e -s -a --arg-file --delimiter --max-args --max-procs"),
    args: 0,
  },
  sudo: { values: ["-u", "-g", "-h", "-p", "-C", "-D", "-r", "-t", "-U", "-T"], args: 0 },
  doas: { values: ["-u", "-C"], args: 0 },
  setsid: { values: [], args: 0 },
  stdbuf: { values: ["-i", "-o", "-e"], args: 0 },
  chronic: { values: [], args: 0 },
  unbuffer: { values: [], args: 0 },
  flock: { values: ["-w", "--timeout", "-E", "--conflict-exit-code"], args: 1 },
};

const SHELLS = new Set(["bash", "sh", "zsh", "dash", "ksh"]);

/**
 * Every git invocation a shell command line makes, as far as reading it can tell: the
 * directory and environment it runs in, its arguments after `git`, and its stdin when that
 * is a heredoc or a file. `cwd` is where the line runs.
 */
function gitCalls(line, cwd, vars = new Map(), depth = 0) {
  const calls = [];
  if (depth > 4) return calls;
  for (const cmd of parseShell(line)) {
    let words = [...cmd.words];
    while (words.length > 0 && RESERVED.has(words[0])) words.shift();
    const env = {};
    while (words.length > 0 && ASSIGNMENT.test(words[0])) {
      const [, name, value] = ASSIGNMENT.exec(words.shift());
      env[name] = value;
    }
    if (words.length === 0) {
      for (const [name, value] of Object.entries(env)) vars.set(name, value);
      continue;
    }
    if (["export", "declare", "typeset", "local", "readonly"].includes(words[0])) {
      for (const word of words.slice(1))
        if (ASSIGNMENT.test(word)) vars.set(...ASSIGNMENT.exec(word).slice(1));
      continue;
    }
    let here = cwd;
    // `$g`, `${g}`: a variable set earlier in the same line.
    const expand = (word) =>
      word.replace(/^\$\{?([A-Za-z_][A-Za-z0-9_]*)\}?$/, (whole, name) => vars.get(name) ?? whole);
    words[0] = expand(words[0]);

    for (let guard = 0; guard < 8 && words.length > 0; guard++) {
      const wrapper = WRAPPERS[path.basename(words[0])];
      if (!wrapper) break;
      const name = path.basename(words[0]);
      words.shift();
      while (words.length > 0 && words[0].startsWith("-") && words[0] !== "--") {
        const option = words.shift();
        const [flag, attached] = option.split(/=(.*)/s);
        let value = attached;
        if (attached === undefined && wrapper.values.includes(flag)) value = words.shift();
        if (name === "env" && (flag === "-C" || flag === "--chdir") && value)
          here = path.resolve(here, value);
        if (name === "env" && (flag === "-S" || flag === "--split-string") && value)
          words = [...value.split(/\s+/).filter(Boolean), ...words];
        if (name === "nice" && /^-\d+$/.test(flag)) continue;
      }
      if (words[0] === "--") words.shift();
      if (name === "env")
        while (words.length > 0 && ASSIGNMENT.test(words[0])) {
          const [, key, value] = ASSIGNMENT.exec(words.shift());
          env[key] = value;
        }
      words.splice(0, wrapper.args);
      if (words.length > 0) words[0] = expand(words[0]);
    }
    if (words.length === 0) continue;

    const program = path.basename(words[0]);
    if (program === "cd" || program === "pushd") {
      const target = words.slice(1).find((w) => !w.startsWith("-"));
      cwd = target ? path.resolve(cwd, target.replace(/^~(?=\/|$)/, process.env.HOME ?? "~")) : cwd;
      continue;
    }
    if (SHELLS.has(program)) {
      const flag = words.findIndex((w, k) => k > 0 && /^-[a-zA-Z]*c[a-zA-Z]*$/.test(w));
      if (flag > 0 && words[flag + 1] !== undefined)
        calls.push(...gitCalls(words[flag + 1], here, vars, depth + 1));
      continue;
    }
    if (program === "eval") {
      calls.push(...gitCalls(words.slice(1).join(" "), here, vars, depth + 1));
      continue;
    }
    if (program !== "git") continue;

    const stdinFile = cmd.redirs.find((r) => r.op === "<")?.target;
    calls.push({
      cwd: here,
      env: { ...Object.fromEntries([...vars].filter(([k]) => k.startsWith("GIT_"))), ...env },
      args: words.slice(1),
      stdin: cmd.heredoc ?? (stdinFile ? { file: path.resolve(here, stdinFile) } : null),
    });
  }
  return calls;
}

/** Git's own options before the subcommand, applied: the directory, the environment
 * --git-dir and --work-tree stand for, and -c settings. */
function gitGlobals(call) {
  let { cwd } = call;
  const env = { ...call.env };
  const config = {};
  const args = [...call.args];
  const takesValue = [
    "--git-dir",
    "--work-tree",
    "--namespace",
    "--config-env",
    "--attr-source",
    "--super-prefix",
    "--list-cmds",
  ];
  while (args.length > 0 && args[0].startsWith("-")) {
    const option = args.shift();
    const [flag, attached] = option.split(/=(.*)/s);
    if (option === "-C") cwd = path.resolve(cwd, args.shift() ?? ".");
    else if (option === "-c") {
      const [key, value = "true"] = (args.shift() ?? "").split(/=(.*)/s);
      config[key.toLowerCase()] = value;
    } else if (takesValue.includes(flag)) {
      const value = attached ?? args.shift();
      if (flag === "--git-dir") env.GIT_DIR = path.resolve(cwd, value);
      if (flag === "--work-tree") env.GIT_WORK_TREE = path.resolve(cwd, value);
    } else if (
      [
        "-h",
        "--help",
        "-v",
        "--version",
        "--exec-path",
        "--html-path",
        "--man-path",
        "--info-path",
      ].includes(flag)
    )
      return null;
  }
  if (args.length === 0) return null;
  const prefix = call.args.slice(0, call.args.length - args.length);
  return { cwd, env, config, prefix, sub: args[0], args: args.slice(1), stdin: call.stdin };
}

/** Subcommands git runs itself; an alias of the same name is never used. */
const BUILTINS = new Set(
  (
    "add am apply bisect blame branch bundle cat-file checkout cherry cherry-pick clean clone " +
    "commit commit-tree config describe diff diff-files diff-index diff-tree fetch for-each-ref " +
    "format-patch fsck gc grep help init log ls-files ls-remote ls-tree merge merge-base " +
    "merge-file merge-tree mv notes pull push range-diff read-tree rebase reflog remote " +
    "replace reset restore rev-list rev-parse revert rm shortlog show show-branch show-ref " +
    "sparse-checkout stash status submodule switch symbolic-ref tag update-index update-ref " +
    "verify-commit worktree write-tree"
  ).split(" "),
);

/** The git commands an invocation amounts to once its aliases are expanded: usually
 * itself, more than one for a shell alias that runs several. */
function resolveAliases(call, depth = 0) {
  const globals = gitGlobals(call);
  if (!globals) return [];
  if (BUILTINS.has(globals.sub) || depth > 4) return [globals];
  let alias = globals.config[`alias.${globals.sub.toLowerCase()}`];
  if (alias === undefined) {
    // The user's own configuration, global included: that is where aliases live.
    const run = spawnSync("git", ["config", "--get", `alias.${globals.sub}`], {
      cwd: existsSync(globals.cwd) ? globals.cwd : process.cwd(),
      encoding: "utf8",
      env: { ...process.env, ...globals.env },
    });
    alias = run.status === 0 ? run.stdout.trim() : undefined;
  }
  if (alias === undefined) return [globals];
  if (alias.startsWith("!"))
    return gitCalls(`${alias.slice(1)} ${globals.args.map(quote).join(" ")}`, globals.cwd).flatMap(
      (c) => resolveAliases(c, depth + 1),
    );
  const expanded = alias.split(/\s+/).filter(Boolean);
  return resolveAliases(
    { ...call, args: [...globals.prefix, ...expanded, ...globals.args] },
    depth + 1,
  );
}

const quote = (word) => `'${word.replaceAll("'", `'\\''`)}'`;

/**
 * A subcommand's arguments, read as git's option parser reads them: long options by any
 * unambiguous prefix, `--name=value` or `--name value` for those that take a value, short
 * options clustered (`-qam msg`). Returns the options by long name, with every value given
 * (in order), and the plain arguments.
 */
function parseOptions(args, spec) {
  const options = new Map();
  const positionals = [];
  const add = (name, value) => options.set(name, [...(options.get(name) ?? []), value]);
  for (let k = 0; k < args.length; k++) {
    const arg = args[k];
    if (arg === "--") {
      positionals.push(...args.slice(k + 1));
      break;
    }
    if (arg.startsWith("--")) {
      const [given, attached] = arg.slice(2).split(/=(.*)/s);
      const matches = spec.long.filter((name) => name.startsWith(given));
      const name = spec.long.includes(given) ? given : matches.length === 1 ? matches[0] : given;
      const value =
        attached ?? (spec.values.includes(name) && k + 1 < args.length ? args[++k] : true);
      add(name, value);
    } else if (arg.startsWith("-") && arg.length > 1) {
      for (let c = 1; c < arg.length; c++) {
        const name = spec.short[arg[c]] ?? arg[c];
        if (spec.values.includes(name)) {
          add(name, c + 1 < arg.length ? arg.slice(c + 1) : args[++k]);
          break;
        }
        if (spec.attached?.includes(name)) {
          add(name, arg.slice(c + 1) || true);
          break;
        }
        add(name, true);
      }
    } else positionals.push(arg);
  }
  return { options, positionals, has: (name) => options.has(name) };
}

const SPECS = {
  commit: {
    long: list(`quiet verbose file author date message reedit-message reuse-message fixup squash
      reset-author trailer signoff template edit cleanup status gpg-sign all include interactive
      patch only no-verify dry-run short branch ahead-behind porcelain long null amend
      no-post-rewrite untracked-files pathspec-from-file pathspec-file-nul allow-empty
      allow-empty-message verify post-rewrite no-all no-include no-only no-patch no-interactive
      no-amend no-dry-run no-edit no-status no-gpg-sign`),
    values: list(
      "file author date message reedit-message reuse-message fixup squash trailer template cleanup pathspec-from-file",
    ),
    attached: ["untracked-files", "gpg-sign"],
    short: {
      a: "all",
      i: "include",
      o: "only",
      p: "patch",
      m: "message",
      F: "file",
      C: "reuse-message",
      c: "reedit-message",
      t: "template",
      q: "quiet",
      v: "verbose",
      s: "signoff",
      e: "edit",
      n: "no-verify",
      z: "null",
      u: "untracked-files",
      S: "gpg-sign",
    },
  },
  merge: {
    long: list(`stat summary log squash commit edit cleanup ff ff-only rerere-autoupdate
      verify-signatures strategy strategy-option message file into-name verbose quiet abort
      quit continue allow-unrelated-histories progress gpg-sign autostash overwrite-ignore
      signoff no-verify verify no-stat no-summary no-log no-squash no-commit no-edit no-ff`),
    values: ["cleanup", "strategy", "strategy-option", "message", "file", "into-name"],
    attached: ["gpg-sign", "log"],
    short: {
      s: "strategy",
      X: "strategy-option",
      m: "message",
      F: "file",
      e: "edit",
      n: "no-stat",
      q: "quiet",
      v: "verbose",
      S: "gpg-sign",
    },
  },
  pull: {
    long: list(`verbose quiet progress recurse-submodules rebase stat summary log signoff squash
      commit edit cleanup ff ff-only verify verify-signatures autostash strategy
      strategy-option gpg-sign allow-unrelated-histories all append upload-pack force tags
      prune jobs dry-run keep depth shallow-since shallow-exclude deepen unshallow
      update-shallow refmap server-option ipv4 ipv6 negotiation-tip show-forced-updates
      set-upstream no-rebase no-commit no-squash no-ff`),
    values: list(
      "cleanup strategy strategy-option upload-pack depth shallow-since shallow-exclude deepen refmap server-option negotiation-tip",
    ),
    attached: ["rebase", "gpg-sign", "jobs", "recurse-submodules", "log"],
    short: {
      r: "rebase",
      s: "strategy",
      X: "strategy-option",
      o: "server-option",
      q: "quiet",
      v: "verbose",
      n: "no-stat",
      S: "gpg-sign",
      j: "jobs",
    },
  },
  rebase: {
    long: list(`onto keep-base no-verify quiet verbose no-stat signoff committer-date-is-author-date
      reset-author-date ignore-date ignore-whitespace whitespace force-rebase no-ff continue
      skip abort quit edit-todo show-current-patch apply merge interactive rerere-autoupdate
      empty keep-empty autosquash update-refs gpg-sign autostash exec allow-empty-message
      rebase-merges fork-point strategy strategy-option root reschedule-failed-exec
      reapply-cherry-picks verify stat ff`),
    values: ["onto", "whitespace", "empty", "exec", "strategy", "strategy-option"],
    attached: ["gpg-sign", "rebase-merges"],
    short: {
      x: "exec",
      s: "strategy",
      X: "strategy-option",
      i: "interactive",
      m: "merge",
      r: "rebase-merges",
      q: "quiet",
      v: "verbose",
      n: "no-stat",
      f: "force-rebase",
      S: "gpg-sign",
      C: "context",
    },
  },
  "cherry-pick": {
    long: list(`quit continue abort skip cleanup no-commit edit signoff mainline rerere-autoupdate
      strategy strategy-option gpg-sign ff allow-empty allow-empty-message
      keep-redundant-commits commit`),
    values: ["cleanup", "mainline", "strategy", "strategy-option"],
    attached: ["gpg-sign"],
    short: {
      n: "no-commit",
      e: "edit",
      s: "signoff",
      m: "mainline",
      X: "strategy-option",
      S: "gpg-sign",
      x: "x",
    },
  },
  am: {
    long: list(`interactive no-verify binary 3way quiet signoff utf8 keep keep-non-patch message-id
      keep-cr scissors quoted-cr whitespace ignore-space-change ignore-whitespace directory
      exclude include patch-format reject resolvemsg continue resolved skip abort quit
      show-current-patch allow-empty committer-date-is-author-date ignore-date
      rerere-autoupdate gpg-sign empty rebasing verify`),
    values: list("whitespace directory exclude include patch-format resolvemsg empty"),
    attached: ["gpg-sign", "show-current-patch", "quoted-cr"],
    short: {
      i: "interactive",
      n: "no-verify",
      q: "quiet",
      s: "signoff",
      u: "utf8",
      k: "keep",
      m: "message-id",
      c: "scissors",
      r: "resolved",
      S: "gpg-sign",
      p: "p",
      C: "C",
    },
  },
};
SPECS.revert = { ...SPECS["cherry-pick"], long: [...SPECS["cherry-pick"].long, "reference"] };
SPECS.am.attached.push("p", "C");
SPECS.rebase.attached.push("context");

/** Subcommands that change the index or HEAD, so a commit later in the same Bash call
 * commits something other than what is staged now. */
const STAGING = new Set(
  "add rm mv reset restore stash apply checkout switch read-tree update-index merge pull rebase cherry-pick revert am commit".split(
    " ",
  ),
);

// ---- judging what a command would commit ----

const repoCache = new Map();

/** The repository a call runs in, with its environment applied to process.env for the
 * review's git calls; null outside one. */
function enter(call) {
  for (const name of [
    "GIT_DIR",
    "GIT_WORK_TREE",
    "GIT_INDEX_FILE",
    "GIT_OBJECT_DIRECTORY",
    "GIT_COMMON_DIR",
  ])
    if (call.env[name] !== undefined) process.env[name] = path.resolve(call.cwd, call.env[name]);
    else delete process.env[name];
  const key = JSON.stringify([call.cwd, process.env.GIT_DIR, process.env.GIT_WORK_TREE]);
  if (!repoCache.has(key))
    repoCache.set(key, existsSync(call.cwd) ? review.repoRoot(call.cwd) : null);
  return repoCache.get(key);
}

function readIfFile(file) {
  try {
    return statSync(file).isFile() ? readFileSync(file, "utf8") : null;
  } catch {
    return null;
  }
}

/** A list of commits that touch notes, for a refusal. */
const listed = (found) =>
  found
    .map(({ commit, subject, reason }) => `- ${commit.slice(0, 12)} "${subject}": ${reason}`)
    .join("\n");

/** The commits rev-list gives for `revs` that touch notes by HEAD's rules. */
function touchingCommits(repo, revs, rules) {
  let listedRevs;
  try {
    listedRevs = review.git(repo, ["rev-list", "--reverse", ...revs]).toString();
  } catch {
    return []; // Revisions git cannot resolve: the command fails on them by itself.
  }
  const found = [];
  for (const commit of listedRevs.split("\n").filter(Boolean)) {
    const reasons = review.touchesNotes(review.commitDiff(repo, commit), rules);
    if (reasons.length === 0) continue;
    const subject = review.git(repo, ["log", "-1", "--format=%s", commit]).toString().trim();
    found.push({ commit, subject, reason: reasons[0] });
  }
  return found;
}

/** Why the staged diff, committed by something that takes no message of ours (a
 * `--continue`), would need a review; null when it touches no notes. */
function stagedResolutionTouches(repo, rules, how) {
  const reasons = review.touchesNotes(review.stagedDiff(repo), rules);
  if (reasons.length === 0) return null;
  return (
    `${how} would commit the staged changes, which touch absence notes, with no review and ` +
    `no trailer. Commit them first with git commit (the hook reviews the diff and gives the ` +
    `trailer), then run ${how} again.\n${reasons.map((r) => `- ${r}`).join("\n")}`
  );
}

/** The review of a `git commit`: null to let it through, or the reason to refuse it. */
function judgeCommit(repo, call, { stagedBefore }) {
  const args = parseOptions(call.args, SPECS.commit);
  if (args.has("dry-run")) return null;
  const amend = args.has("amend");
  // HEAD's rules, never the working tree's (see rulesAt in privacy-review.mjs).
  const rules = review.rulesForStaged(repo, { amend });
  if (!rules) return null;

  const unstaged = ["all", "include", "only", "patch", "interactive", "pathspec-from-file"].filter(
    (name) => args.has(name),
  );
  if (stagedBefore || unstaged.length > 0 || args.positionals.length > 0) {
    const reasons = review.touchesNotes(review.workingTreeDiff(repo), rules);
    if (reasons.length === 0) return null;
    const why = stagedBefore
      ? "this Bash call changes the index before it commits"
      : `it commits from the working tree (${[...unstaged.map((n) => `--${n}`), ...(args.positionals.length > 0 ? [`the paths ${args.positionals.join(" ")}`] : [])].join(", ")})`;
    return (
      `This commit could include changes that touch absence notes that are not staged, and the ` +
      `privacy review covers the staged diff only: ${why}. Stage the change first (git add) ` +
      `in a command of its own, then run git commit alone, without -a, -i, -o, -p or paths.\n` +
      reasons.map((r) => `- ${r}`).join("\n")
    );
  }

  let diff;
  try {
    diff = review.stagedDiff(repo, { amend });
  } catch {
    return null; // An index git cannot commit (unresolved conflicts): git refuses it itself.
  }
  const reasons = review.touchesNotes(diff, rules);
  if (reasons.length === 0) return null;

  const hash = review.diffHash(diff);
  let verdict = review.cachedVerdict(repo, hash);
  if (!verdict) {
    try {
      verdict = review.runReviewer(repo, diff);
    } catch (error) {
      return (
        `The staged diff touches absence notes and must pass the privacy review, which could ` +
        `not run: ${error.message}\nFix that and commit again; a review that did not happen ` +
        `never counts as a pass.`
      );
    }
    if (verdict.verdict !== "passed")
      return (
        `The privacy reviewer failed the staged diff (sha256:${hash}). Fix these, stage the ` +
        `fix and commit again:\n${review.formatFindings(verdict)}`
      );
    review.cacheVerdict(repo, hash, verdict);
  }

  // The message as written in the command, -m paragraphs, and the files -F reads (stdin
  // being a heredoc or a file), and the commit -C reuses. A heredoc or a quoted -m puts
  // real newlines between the lines; $'...' quoting puts "\n".
  const messages = [command, (args.options.get("message") ?? []).join("\n\n")];
  for (const file of args.options.get("file") ?? []) {
    if (file === "-")
      messages.push(
        typeof call.stdin === "string" ? call.stdin : (readIfFile(call.stdin?.file ?? "") ?? ""),
      );
    else messages.push(readIfFile(path.resolve(call.cwd, file)) ?? "");
  }
  for (const commit of args.options.get("reuse-message") ?? [])
    messages.push(
      spawnSync("git", ["log", "-1", "--format=%B", commit], { cwd: repo, encoding: "utf8" })
        .stdout ?? "",
    );
  const trailer = review.trailerLine(hash);
  const placed = new RegExp(`(?:^|\\n|\\\\n)${trailer}(?:\\r?\\n|\\\\n)Co-authored by AI\\.`);
  if (messages.some((m) => placed.test(m))) return null;

  return (
    `The privacy reviewer passed the staged diff, which touches absence notes. Add this line ` +
    `to the commit message, on its own line directly above "Co-authored by AI.", then ` +
    `commit again:\n${trailer}`
  );
}

/** The review of a command that creates commits other than `git commit`: null to let it
 * through, or the reason to refuse it. */
function judgeOther(repo, call) {
  const { sub } = call;
  const rules = review.rulesForStaged(repo);
  if (!rules) return null;

  if (sub === "merge") {
    const args = parseOptions(call.args, SPECS.merge);
    if (args.has("abort") || args.has("quit") || args.has("no-commit") || args.has("squash"))
      return null;
    if (args.has("continue")) return stagedResolutionTouches(repo, rules, "git merge --continue");
    const strategies = [
      ...(args.options.get("strategy") ?? []),
      ...(args.options.get("strategy-option") ?? []),
    ];
    const custom = strategies.filter((s) => !["ort", "recursive"].includes(s));
    if (custom.length === 0 && args.positionals.length <= 1) return null;
    // Its result may differ from the automatic merge: whatever either side changed since
    // they forked could end up in, or out of, the merge commit by hand.
    const sides = args.positionals.flatMap((rev) => [`HEAD...${rev}`]);
    const found = sides.flatMap((range) => touchingCommits(repo, [range], rules));
    if (found.length === 0) return null;
    return (
      `This merge (${custom.length > 0 ? `strategy ${custom.join(", ")}` : "several branches at once"}) ` +
      `can produce a merge commit that differs from the automatic merge, and the branches ` +
      `touch absence notes, so that difference needs a review. Merge with git merge ` +
      `--no-commit (one branch at a time), then conclude it with git commit, which the hook ` +
      `reviews on the merge's remerge diff.\n${listed(found)}`
    );
  }

  if (sub === "pull") {
    const args = parseOptions(call.args, SPECS.pull);
    const rebaseOption = args.options.get("rebase")?.at(-1);
    const configured = spawnSync("git", ["config", "--get", "pull.rebase"], {
      cwd: repo,
      encoding: "utf8",
    }).stdout.trim();
    const rebase = args.has("no-rebase")
      ? false
      : rebaseOption !== undefined
        ? rebaseOption !== "false"
        : ["true", "merges", "interactive", "i", "m"].includes(configured);
    if (rebase) {
      const upstream = spawnSync("git", ["rev-parse", "--verify", "--quiet", "@{upstream}"], {
        cwd: repo,
        encoding: "utf8",
      }).stdout.trim();
      const found = touchingCommits(
        repo,
        upstream ? [`${upstream}..HEAD`] : ["HEAD", "--not", "--remotes"],
        rules,
      );
      if (found.length === 0) return null;
      return (
        `git pull --rebase would replay local commits that touch absence notes onto the ` +
        `fetched branch, which changes their diffs, so their Privacy-Review trailers would no ` +
        `longer match. Pull with git pull --no-rebase (a merge) instead.\n${listed(found)}`
      );
    }
    if (args.has("strategy") || args.has("strategy-option"))
      return (
        `git pull with a merge strategy or strategy option can produce a merge commit that ` +
        `differs from the automatic merge, which then needs a review this hook cannot give ` +
        `before the fetch. Run git fetch, then git merge --no-commit with the upstream branch, ` +
        `then git commit, which the hook reviews.`
      );
    return null;
  }

  if (sub === "rebase") {
    const args = parseOptions(call.args, SPECS.rebase);
    if (["abort", "quit", "skip", "edit-todo", "show-current-patch"].some((n) => args.has(n)))
      return null;
    if (args.has("continue")) return stagedResolutionTouches(repo, rules, "git rebase --continue");
    let revs;
    if (args.has("root")) {
      const start = review.commitOrNull(repo, review.REVIEW_START);
      revs = [args.positionals[0] ?? "HEAD", ...(start ? [`^${start}`] : [])];
    } else {
      const upstream = args.positionals[0] ?? "@{upstream}";
      revs = [`${upstream}..${args.positionals[1] ?? "HEAD"}`];
    }
    if (!args.has("rebase-merges")) revs.push("--no-merges");
    const found = touchingCommits(repo, revs, rules);
    if (found.length === 0) return null;
    return (
      `git rebase would rewrite commits that touch absence notes. Replayed onto another base, ` +
      `their diffs change, and so would their hashes: their Privacy-Review trailers would no ` +
      `longer match, and CI would fail them. Merge instead, or replay each with git ` +
      `cherry-pick --no-commit <commit> followed by git commit, which the hook reviews.\n` +
      listed(found)
    );
  }

  if (sub === "cherry-pick" || sub === "revert") {
    const args = parseOptions(call.args, SPECS[sub]);
    if (["abort", "quit", "skip"].some((n) => args.has(n)) || args.has("no-commit")) return null;
    if (args.has("continue")) return stagedResolutionTouches(repo, rules, `git ${sub} --continue`);
    const found = touchingCommits(repo, ["--no-walk", ...args.positionals], rules);
    if (found.length === 0) return null;
    return (
      `git ${sub} would commit changes that touch absence notes with no review of the diff it ` +
      `makes here: on this base the diff, and its hash, differ from the original's. Run git ` +
      `${sub} --no-commit, then git commit, which the hook reviews.\n${listed(found)}`
    );
  }

  if (sub === "am") {
    const args = parseOptions(call.args, SPECS.am);
    if (["abort", "quit", "skip", "show-current-patch"].some((n) => args.has(n))) return null;
    if (args.has("continue") || args.has("resolved"))
      return stagedResolutionTouches(repo, rules, "git am --continue");
    const patches =
      args.positionals.length > 0
        ? args.positionals.map((file) => readIfFile(path.resolve(call.cwd, file)))
        : [
            typeof call.stdin === "string"
              ? call.stdin
              : call.stdin
                ? readIfFile(call.stdin.file)
                : null,
          ];
    const advice = `Apply it with git apply --index <patch>, then commit with git commit, which the hook reviews.`;
    if (patches.some((p) => p === null))
      return `git am reads patches this hook cannot read beforehand, so it cannot tell whether they touch absence notes. ${advice}`;
    const reasons = patches.flatMap((p) => review.touchesNotes(p, rules));
    if (reasons.length === 0) return null;
    return `git am would commit patches that touch absence notes with no review. ${advice}\n${reasons.map((r) => `- ${r}`).join("\n")}`;
  }

  if (sub === "commit-tree") {
    const parents = [];
    let tree = null;
    for (let k = 0; k < call.args.length; k++) {
      const arg = call.args[k];
      if (arg === "-p") parents.push(call.args[++k]);
      else if (arg === "-m" || arg === "-F") k += 1;
      else if (!arg.startsWith("-")) tree ??= arg;
    }
    const advice = "Commit with git commit instead, which the hook reviews.";
    const resolve = (rev, type) =>
      rev &&
      spawnSync("git", ["rev-parse", "--verify", "--quiet", `${rev}^{${type}}`], {
        cwd: repo,
        encoding: "utf8",
      }).stdout.trim();
    const treeId = resolve(tree, "tree");
    const parentIds = parents.map((p) => resolve(p, "commit"));
    if (!treeId || parentIds.some((p) => !p))
      return `git commit-tree names a tree or parent this hook cannot resolve beforehand, so it cannot tell whether the commit touches absence notes. ${advice}`;
    const reasons = review.touchesNotes(review.treeDiff(repo, parentIds[0] ?? null, treeId), rules);
    if (reasons.length === 0) return null;
    return `git commit-tree would make a commit that touches absence notes with no review. ${advice}\n${reasons.map((r) => `- ${r}`).join("\n")}`;
  }

  return null;
}

const CREATES_COMMITS = new Set([
  "commit",
  "merge",
  "pull",
  "rebase",
  "cherry-pick",
  "revert",
  "am",
  "commit-tree",
]);

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

const calls = gitCalls(command, input.cwd ?? process.cwd()).flatMap((call) => resolveAliases(call));
let stagedBefore = false;
for (const call of calls) {
  if (CREATES_COMMITS.has(call.sub)) {
    const repo = enter(call);
    if (repo) {
      const reason =
        call.sub === "commit" ? judgeCommit(repo, call, { stagedBefore }) : judgeOther(repo, call);
      if (reason) deny(reason);
    }
  }
  if (STAGING.has(call.sub)) stagedBefore = true;
}
