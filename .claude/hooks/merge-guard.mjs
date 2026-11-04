/**
 * PreToolUse hook: decides whether `gh pr merge` needs the user's approval, by the pull request's base branch.
 *
 * A permission rule sees only the command text, and `gh pr merge 12` does not name its base branch, so a rule
 * cannot tell a merge into develop from a merge into main. This hook looks the base branch up and lets a merge
 * into an allowed base (CLAUDE_AUTO_MERGE_BASES, default "develop") run without a prompt. Every other merge, and
 * every merge it cannot check, gets permissionDecision "ask".
 *
 * The hook passes only the forms it has verified: a lone `gh pr merge` into an allowed base. Any other command that
 * contains "merge" asks, unless it is plain git or a program that only reads or prints text. `gh api` writes and
 * GraphQL calls, and `gh alias set`, ask even without the word. Recognising every shell form one by one leaves gaps,
 * so the default is to ask. The `ask` rule for `gh pr merge` was removed from settings.json because a hook's
 * "allow" cannot override an `ask` rule (https://code.claude.com/docs/en/hooks, PreToolUse decision control).
 *
 * Output: nothing (the permission rules decide), or a JSON "ask" decision on stdout. The exit code is always 0.
 */

import { execFileSync } from "node:child_process";
import { pathToFileURL } from "node:url";

const EXIT_OK = 0;
const LOOKUP_TIMEOUT_MS = 10000;
const DEFAULT_AUTO_MERGE_BASES = "develop";

// 確認なしで通す。値を取らないフラグ。
const BOOL_FLAGS = new Set([
  "-m",
  "--merge",
  "-s",
  "--squash",
  "-r",
  "--rebase",
  "-d",
  "--delete-branch",
  "--auto",
  "--disable-auto",
]);
const BOOL_SHORT_LETTERS = new Set(["m", "s", "r", "d"]);
// 値を取るフラグ。値は PR の指定と取り違えないよう読み飛ばす。
const VALUE_FLAGS = new Set([
  "-R",
  "--repo",
  "-b",
  "--body",
  "-F",
  "--body-file",
  "-t",
  "--subject",
  "-A",
  "--author-email",
  "--match-head-commit",
]);
const REPO_FLAGS = new Set(["-R", "--repo"]);
// merge の語を含んでいても、その語を実行しないコマンド。awk・sed・find は中から別のコマンドを動かせるので入れない。
const TEXT_ONLY_PROGRAMS = new Set([
  "cat",
  "cut",
  "diff",
  "echo",
  "grep",
  "head",
  "ls",
  "printf",
  "rg",
  "sort",
  "tail",
  "tee",
  "tr",
  "uniq",
  "wc",
]);
// gh api で書き込みになるフラグ。merge の語が無くても、ファイルや標準入力からマージの指示を送れる。
const GH_API_WRITE_FLAGS = /^(-X|--method|-f|-F|--field|--raw-field|--input)(=|$)|^-[XfF]./;
// gh の組み込みのコマンドのうち、マージをしないもの。ここに無い語は別名（gh alias）かもしれないので確認を出す。
const GH_NON_MERGE_COMMANDS = new Set([
  "agent-task",
  "alias",
  "attestation",
  "auth",
  "browse",
  "cache",
  "codespace",
  "completion",
  "config",
  "copilot",
  "extension",
  "gist",
  "gpg-key",
  "help",
  "issue",
  "label",
  "org",
  "preview",
  "project",
  "release",
  "repo",
  "ruleset",
  "run",
  "search",
  "secret",
  "ssh-key",
  "status",
  "variable",
  "version",
  "workflow",
]);
// gh pr のサブコマンドのうち、マージをしないもの。
const GH_PR_NON_MERGE_COMMANDS = new Set([
  "checkout",
  "checks",
  "close",
  "comment",
  "create",
  "diff",
  "edit",
  "list",
  "lock",
  "ready",
  "reopen",
  "revert",
  "review",
  "status",
  "unlock",
  "update-branch",
  "view",
]);

export const REASONS = {
  admin: "--admin はブランチ保護を飛ばすため、確認を出します。",
  alias: "gh の別名を作ると、別名からマージできるため確認を出します。",
  api: "gh api の書き込みと GraphQL は、マージかどうかを確かめられないため確認を出します。マージには gh pr merge を使ってください。",
  heredoc: "ヒアドキュメントの本文に gh とマージの語があるため、確認を出します。",
  mentionsMerge:
    "マージの語を含むコマンドのうち、確かめた形でないものは確認を出します。マージには gh pr merge を単独で使ってください。",
  chained: "gh pr merge は単独で実行してください。他のコマンドと並べると、マージ先を確かめられないため確認を出します。",
  lookup: "PR のマージ先を調べられなかったため、確認を出します。",
  nested: "別のシェルの中のマージは、マージ先を確かめられないため確認を出します。",
  prefixed:
    "gh の前に別のコマンドが付いていると、マージ先を確かめられないため確認を出します。gh pr merge を先頭に書いてください。",
  unknownCommand: "gh の別名かもしれないコマンドは、マージかどうかを確かめられないため確認を出します。",
  unparsable: "gh pr merge の引数を読めないため、確認を出します。",
  error: "フックの中で問題が起きたため、確認を出します。",
};

/**
 * Builds the reason for a merge into a base that needs approval.
 *
 * @param {string} base The base branch of the pull request.
 * @param {string[]} bases The bases that are merged without a prompt.
 * @returns {string} The reason shown to the user.
 */
export function baseReason(base, bases) {
  if (0 === bases.length) {
    return `マージ先は ${base} です。確認なしで取り込める先が設定されていないため、確認を出します。`;
  }
  return `マージ先は ${base} です。${bases.join("・")} 以外への取り込みは人が確認します。`;
}

/**
 * Splits a shell command into words and operators. Quoted text stays inside its word, so a search pattern
 * such as `grep "gh pr merge"` is one word and not a command.
 *
 * @param {string} command The raw Bash command.
 * @returns {{type: "word" | "op" | "redirect", value: string, dynamic?: boolean}[]} The tokens.
 */
export function tokenize(command) {
  const tokens = [];
  let word = "";
  let inWord = false;
  let dynamic = false;
  let index = 0;
  const heredocs = [];

  const endWord = () => {
    if (inWord) {
      tokens.push({ type: "word", value: word, dynamic });
    }
    word = "";
    inWord = false;
    dynamic = false;
  };

  while (index < command.length) {
    const char = command[index];
    const next = command[index + 1];

    if (" " === char || "\t" === char) {
      endWord();
      index += 1;
      continue;
    }

    if ("#" === char && !inWord) {
      // コメントは改行まで読み飛ばす。
      while (index < command.length && "\n" !== command[index]) {
        index += 1;
      }
      continue;
    }

    if ("\\" === char && "\n" === next) {
      // 行末の \ は行の続きを示すだけで、語の一部ではない。
      index += 2;
      continue;
    }

    if ("\\" === char) {
      word += next ?? "";
      inWord = true;
      index += 2;
      continue;
    }

    if ("'" === char) {
      const end = command.indexOf("'", index + 1);
      const stop = -1 === end ? command.length : end;
      word += command.slice(index + 1, stop);
      inWord = true;
      index = stop + 1;
      continue;
    }

    if ('"' === char) {
      index += 1;
      inWord = true;
      while (index < command.length && '"' !== command[index]) {
        if ("\\" === command[index] && index + 1 < command.length) {
          word += command[index + 1];
          index += 2;
          continue;
        }
        if ("$" === command[index] || "`" === command[index]) {
          // 二重引用符の中の変数とコマンド置換は展開されるため、中身を確かめられない。
          dynamic = true;
        }
        word += command[index];
        index += 1;
      }
      index += 1;
      continue;
    }

    if ("<" === char && "<" === next && "<" !== command[index + 2]) {
      // ヒアドキュメントの本文は入力の文字列で、コマンドではない。終わりの印を覚え、改行のあとで読み飛ばす。
      const match = /^<<(-?)\s*(?:\\|(['"]?))([A-Za-z0-9_.-]+)\2/.exec(command.slice(index));
      if (null !== match) {
        endWord();
        heredocs.push({ delimiter: match[3], allowTabs: "-" === match[1] });
        index += match[0].length;
        continue;
      }
    }

    if ("\n" === char && 0 < heredocs.length) {
      endWord();
      tokens.push({ type: "op", value: char });
      index += 1;
      while (0 < heredocs.length) {
        const { delimiter, allowTabs } = heredocs.shift();
        while (index < command.length) {
          const lineEnd = command.indexOf("\n", index);
          const end = -1 === lineEnd ? command.length : lineEnd;
          const line = command.slice(index, end);
          index = end + 1;
          if ((allowTabs ? line.replace(/^\t+/, "") : line) === delimiter) {
            break;
          }
          // $((1<<x)) のように、<< がヒアドキュメントでない書き方がある。読み違えて本文を飛ばしても通さない。
          if (/\bgh\b/.test(line) && /merge/i.test(line)) {
            tokens.push({ type: "risk", value: "heredoc" });
          }
        }
      }
      continue;
    }

    if (">" === char || "<" === char || ("&" === char && ">" === next)) {
      // リダイレクトの記号。2>&1 の & を区切りと取り違えないよう、まとめて読む。
      const match = /^(&>>?|[<>]+&?-?[0-9]*)/.exec(command.slice(index));
      const fd = /^[0-9]+$/.test(word) ? word : "";
      if ("" !== fd) {
        word = "";
        inWord = false;
      }
      endWord();
      tokens.push({ type: "redirect", value: fd + match[0] });
      index += match[0].length;
      continue;
    }

    if ("$" === char && "(" === next) {
      // コマンド置換は中で別のコマンドが動くので、区切りとして扱う。
      endWord();
      tokens.push({ type: "op", value: "$(" });
      index += 2;
      continue;
    }

    if ("\n" === char || ";" === char || "&" === char || "|" === char || "(" === char || ")" === char || "`" === char) {
      endWord();
      tokens.push({ type: "op", value: char });
      index += 1;
      continue;
    }

    if ("$" === char) {
      dynamic = true;
    }
    word += char;
    inWord = true;
    index += 1;
  }

  endWord();
  return tokens;
}

/**
 * Groups tokens into simple commands. A redirect and its target are dropped from the arguments.
 *
 * @param {ReturnType<typeof tokenize>} tokens The tokens.
 * @returns {{value: string, dynamic: boolean}[][]} The simple commands, each a list of words.
 */
export function splitSegments(tokens) {
  const segments = [];
  let current = [];
  let skipNext = false;

  for (const token of tokens) {
    if ("op" === token.type) {
      if (0 < current.length) {
        segments.push(current);
      }
      current = [];
      skipNext = false;
      continue;
    }
    if ("risk" === token.type) {
      continue;
    }
    if ("redirect" === token.type) {
      // 2>&1 のように行き先を含む記号は、次の語を行き先として取らない。
      skipNext = !/&-?[0-9]*$/.test(token.value) || /^&>/.test(token.value);
      continue;
    }
    if (skipNext) {
      skipNext = false;
      continue;
    }
    current.push({ value: token.value, dynamic: token.dynamic });
  }

  if (0 < current.length) {
    segments.push(current);
  }
  return segments;
}

/**
 * Returns the program name of a word, without its directory and a Windows ".exe".
 *
 * @param {string} word A word.
 * @returns {string} The program name.
 */
function programName(word) {
  return word
    .replace(/^.*[\\/]/, "")
    .replace(/\.exe$/i, "")
    .toLowerCase();
}

/**
 * Classifies one simple command.
 *
 * @param {{value: string, dynamic: boolean}[]} segment The words of a simple command.
 * @returns {{kind: "merge", args: {value: string, dynamic: boolean}[], env: Record<string, string>} | {kind: "ask", reason: string} | {kind: "other"}} The classification.
 */
export function classifySegment(segment) {
  const env = {};
  let index = 0;
  while (index < segment.length) {
    const assignment = /^([A-Za-z_][A-Za-z0-9_]*)=(.*)$/s.exec(segment[index].value);
    if (null === assignment) {
      break;
    }
    if (segment[index].dynamic && /merge/i.test(segment.map((word) => word.value).join(" "))) {
      // GH_REPO=$R のように値が決まらないと、調べる PR と実際にマージする PR が食い違うおそれがある。
      return { kind: "ask", reason: REASONS.unparsable };
    }
    env[assignment[1]] = assignment[2];
    index += 1;
  }

  const words = segment.slice(index);
  if (0 === words.length) {
    return { kind: "other" };
  }

  // 変数やコマンド置換の中のマージは、実行するまで中身が決まらない。
  if (words.some((word) => word.dynamic && /merge/i.test(word.value))) {
    return { kind: "ask", reason: REASONS.nested };
  }

  const name = programName(words[0].value);
  const rest = words.slice(1);
  if ("gh" === name) {
    return classifyGh(rest, env);
  }

  // 確認なしで通すのは、確かめた形だけにする。シェルの書き方を1つずつ見抜く方式では、抜け道が残るため。
  if (!words.some((word) => /merge/i.test(word.value))) {
    return { kind: "other" };
  }
  if (TEXT_ONLY_PROGRAMS.has(name) || ("git" === name && isPlainGit(rest))) {
    return { kind: "other" };
  }
  const runsGh = rest.some((word) => "gh" === programName(word.value));
  return { kind: "ask", reason: runsGh ? REASONS.prefixed : REASONS.mentionsMerge };
}

/**
 * Tells whether a git command runs only git itself, without an alias or a config value that runs a shell command.
 *
 * @param {{value: string, dynamic: boolean}[]} rest The words after `git`.
 * @returns {boolean} True for commands such as `git merge --ff-only origin/develop` or `git log --merges`.
 */
function isPlainGit(rest) {
  // git -c alias.m='!gh pr merge 1' m のように、別名と設定から任意のコマンドを動かせる。
  if (rest.some((word) => /alias/i.test(word.value) || word.value.startsWith("!"))) {
    return false;
  }
  for (let index = 0; index < rest.length; index += 1) {
    const { value } = rest[index];
    if ("-C" === value) {
      index += 1;
      continue;
    }
    if (value.startsWith("-c") || value.startsWith("--config-env") || value.startsWith("--exec-path")) {
      return false;
    }
    if (!value.startsWith("-")) {
      return "config" !== value;
    }
  }
  return true;
}

/**
 * Classifies a command that runs gh.
 *
 * @param {{value: string, dynamic: boolean}[]} rest The words after `gh`.
 * @param {Record<string, string>} env The variables assigned before the command.
 * @returns {ReturnType<typeof classifySegment>} The classification.
 */
function classifyGh(rest, env) {
  const command = rest[0];
  if (undefined === command) {
    return { kind: "other" };
  }
  if (command.dynamic || command.value.startsWith("-")) {
    // フラグを先に書く形や変数は、どのコマンドが動くかを確かめられない。
    return ["--help", "-h", "--version"].includes(command.value)
      ? { kind: "other" }
      : { kind: "ask", reason: REASONS.unparsable };
  }
  if ("api" === command.value) {
    // GraphQL と書き込みは、merge の語が無くても、ファイルや標準入力からマージの指示を送れる。
    const writes = rest.some(
      (word) => "graphql" === word.value || GH_API_WRITE_FLAGS.test(word.value) || /merge/i.test(word.value),
    );
    return writes ? { kind: "ask", reason: REASONS.api } : { kind: "other" };
  }
  if ("alias" === command.value && ["set", "import"].includes(rest[1]?.value)) {
    return { kind: "ask", reason: REASONS.alias };
  }
  if ("pr" === command.value) {
    const sub = rest[1];
    if (undefined === sub) {
      return { kind: "other" };
    }
    if ("merge" === sub.value) {
      return { kind: "merge", args: rest.slice(2), env };
    }
    if (GH_PR_NON_MERGE_COMMANDS.has(sub.value) || "--help" === sub.value || "-h" === sub.value) {
      return { kind: "other" };
    }
    return { kind: "ask", reason: sub.value.startsWith("-") ? REASONS.unparsable : REASONS.unknownCommand };
  }
  if (GH_NON_MERGE_COMMANDS.has(command.value)) {
    return { kind: "other" };
  }
  return { kind: "ask", reason: REASONS.unknownCommand };
}

/**
 * Reads the pull request selector and the repository from the arguments of `gh pr merge`.
 *
 * @param {{value: string, dynamic: boolean}[]} args The words after `gh pr merge`.
 * @returns {{selector: string, repo: string} | {reason: string}} The parsed arguments, or why they need approval.
 */
export function parseMergeArgs(args) {
  const positionals = [];
  let repo = "";
  let index = 0;
  let onlyPositionals = false;

  while (index < args.length) {
    const { value, dynamic } = args[index];
    if (dynamic) {
      // 変数を含む引数は、実行するまで中身が決まらない。
      return { reason: REASONS.unparsable };
    }
    if (onlyPositionals || !value.startsWith("-") || "-" === value) {
      positionals.push(value);
      index += 1;
      continue;
    }
    if ("--" === value) {
      onlyPositionals = true;
      index += 1;
      continue;
    }
    if ("--admin" === value) {
      return { reason: REASONS.admin };
    }

    const [flag, inline] =
      value.startsWith("--") && value.includes("=")
        ? [value.slice(0, value.indexOf("=")), value.slice(value.indexOf("=") + 1)]
        : [value, undefined];

    if (VALUE_FLAGS.has(flag)) {
      const flagValue = undefined !== inline ? inline : args[index + 1]?.value;
      // -b "$(...)" のように、値の中で別のコマンドが動く形も通さない。
      if (undefined === flagValue || (undefined === inline && args[index + 1].dynamic)) {
        return { reason: REASONS.unparsable };
      }
      if (REPO_FLAGS.has(flag)) {
        repo = flagValue;
      }
      index += undefined !== inline ? 1 : 2;
      continue;
    }
    if (BOOL_FLAGS.has(flag) && undefined === inline) {
      index += 1;
      continue;
    }
    if (/^-[a-zA-Z]{2,}$/.test(value) && [...value.slice(1)].every((letter) => BOOL_SHORT_LETTERS.has(letter))) {
      // -sd のように、値を取らない短いフラグをまとめた形。
      index += 1;
      continue;
    }
    // 知らないフラグは値を取るかが分からず、PR の指定を読み違えるおそれがある。
    return { reason: REASONS.unparsable };
  }

  if (1 < positionals.length) {
    return { reason: REASONS.unparsable };
  }
  return { selector: positionals[0] ?? "", repo };
}

/**
 * Reads the bases that are merged without a prompt.
 *
 * @param {string | undefined} value The value of CLAUDE_AUTO_MERGE_BASES.
 * @returns {string[]} The base branch names.
 */
export function autoMergeBases(value) {
  const bases = (value ?? DEFAULT_AUTO_MERGE_BASES)
    .split(",")
    .map((base) => base.trim())
    .filter((base) => "" !== base);
  return bases;
}

/**
 * Looks up the base branch of a pull request with the GitHub CLI.
 *
 * @param {{selector: string, repo: string, cwd: string | undefined, env: Record<string, string>}} target The pull request.
 * @returns {string} The base branch name.
 */
export function lookupBaseWithGh({ selector, repo, cwd, env }) {
  const args = ["pr", "view", "--json", "baseRefName", "--jq", ".baseRefName"];
  if ("" !== repo) {
    args.push("--repo", repo);
  }
  if ("" !== selector) {
    // PR の指定がフラグとして読まれないよう、-- の後ろに置く。
    args.push("--", selector);
  }
  const output = execFileSync("gh", args, {
    cwd,
    env: { ...process.env, ...env },
    encoding: "utf8",
    timeout: LOOKUP_TIMEOUT_MS,
    stdio: ["ignore", "pipe", "ignore"],
  });
  return output.trim();
}

/**
 * Decides whether a Bash command needs approval because it merges a pull request.
 *
 * @param {string} command The raw Bash command.
 * @param {{bases?: string[], cwd?: string, lookupBase?: typeof lookupBaseWithGh}} [options] The allowed bases and the lookup.
 * @returns {{decision: "none"} | {decision: "ask", reason: string}} "none" leaves the decision to the permission rules.
 */
export function decide(command, options = {}) {
  const { bases = autoMergeBases(undefined), cwd, lookupBase = lookupBaseWithGh } = options;
  const tokens = tokenize(command);
  if (tokens.some((token) => "risk" === token.type)) {
    return { decision: "ask", reason: REASONS.heredoc };
  }
  const segments = splitSegments(tokens);
  const classified = segments.map(classifySegment);

  const asked = classified.find((item) => "ask" === item.kind);
  if (undefined !== asked) {
    return { decision: "ask", reason: asked.reason };
  }

  const merges = classified.filter((item) => "merge" === item.kind);
  if (0 === merges.length) {
    return { decision: "none" };
  }
  // 区切りや括弧が1つでもあれば、$( ) や { } の中のマージも含めて単独の実行ではない。
  if (1 < segments.length || tokens.some((token) => "op" === token.type)) {
    return { decision: "ask", reason: REASONS.chained };
  }

  const parsed = parseMergeArgs(merges[0].args);
  if ("reason" in parsed) {
    return { decision: "ask", reason: parsed.reason };
  }

  let base;
  try {
    base = lookupBase({ selector: parsed.selector, repo: parsed.repo, cwd, env: merges[0].env });
  } catch {
    return { decision: "ask", reason: REASONS.lookup };
  }
  if ("" === base) {
    return { decision: "ask", reason: REASONS.lookup };
  }
  if (bases.includes(base)) {
    return { decision: "none" };
  }
  return { decision: "ask", reason: baseReason(base, bases) };
}

/**
 * Formats an "ask" decision for Claude Code.
 *
 * @param {string} reason The reason shown to the user.
 * @returns {string} The JSON output of the hook.
 */
export function askOutput(reason) {
  return JSON.stringify({
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "ask",
      permissionDecisionReason: reason,
    },
  });
}

/**
 * Reads the hook input from stdin, writes the decision and returns the exit code.
 *
 * @returns {Promise<number>} The exit code for Claude Code.
 */
async function main() {
  let raw = "";
  for await (const chunk of process.stdin) {
    raw += chunk;
  }

  let input;
  try {
    input = JSON.parse(raw);
  } catch {
    // 読めない入力でも、マージかもしれないものは確認を出す側に倒す。
    if (/merge/i.test(raw)) {
      process.stdout.write(askOutput(REASONS.unparsable));
    }
    return EXIT_OK;
  }

  const command = input?.tool_input?.command;
  if ("Bash" !== input?.tool_name || "string" !== typeof command) {
    return EXIT_OK;
  }

  try {
    const result = decide(command, { bases: autoMergeBases(process.env.CLAUDE_AUTO_MERGE_BASES), cwd: input?.cwd });
    if ("ask" === result.decision) {
      process.stdout.write(askOutput(result.reason));
    }
  } catch {
    // ask の規則を外したため、ここで黙って終わると確認なしでマージが通る。マージかもしれなければ確認を出す。
    if (/merge/i.test(command)) {
      process.stdout.write(askOutput(REASONS.error));
    }
  }
  return EXIT_OK;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = await main();
}
