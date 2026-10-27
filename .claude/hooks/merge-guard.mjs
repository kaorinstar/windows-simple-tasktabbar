/**
 * PreToolUse hook: decides whether `gh pr merge` needs the user's approval, by the pull request's base branch.
 *
 * A permission rule sees only the command text, and `gh pr merge 12` does not name its base branch, so a rule
 * cannot tell a merge into develop from a merge into main. This hook looks the base branch up and lets a merge
 * into an allowed base (CLAUDE_AUTO_MERGE_BASES, default "develop") run without a prompt. Every other merge, and
 * every merge it cannot check, gets permissionDecision "ask".
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
// コマンド名の前に付いても、実行するコマンドを変えない語。オプションを取る形（env -u・timeout 60 など）は含めない。
const WRAPPERS = new Set(["command", "exec", "nohup", "time", "env"]);
// 区切りの直後に来ても、コマンド名ではない語。
const SHELL_KEYWORDS = new Set(["{", "}", "!", "if", "then", "elif", "else", "fi", "do", "done", "while", "until"]);
// 引数の文字列を、別のシェルとして実行するコマンド。
const NESTED_SHELLS = new Set(["bash", "sh", "zsh", "dash", "eval", "xargs", "pwsh", "powershell", "cmd"]);
// gh の組み込みのコマンドのうち、マージをしないもの。ここに無い語は別名（gh alias）かもしれないので確認を出す。
const GH_NON_MERGE_COMMANDS = new Set([
  "agent-task",
  "alias",
  "attestation",
  "auth",
  "browse",
  "cache",
  "co",
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
  api: "gh api でのマージは、マージ先を確かめられないため確認を出します。gh pr merge を使ってください。",
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
 * Tells whether a text names both gh and a merge.
 *
 * @param {string} text Any text.
 * @returns {boolean} True when the text could be a merge through gh.
 */
function mentionsGhMerge(text) {
  return /\bgh\b/.test(text) && /merge/i.test(text);
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
    const { value } = segment[index];
    const assignment = /^([A-Za-z_][A-Za-z0-9_]*)=(.*)$/s.exec(value);
    if (null !== assignment) {
      env[assignment[1]] = assignment[2];
      index += 1;
      continue;
    }
    if (WRAPPERS.has(value) || SHELL_KEYWORDS.has(value)) {
      index += 1;
      continue;
    }
    break;
  }

  const words = segment.slice(index);
  if (0 === words.length) {
    return { kind: "other" };
  }

  const name = programName(words[0].value);
  const rest = words.slice(1);

  if (NESTED_SHELLS.has(name) && mentionsGhMerge(rest.map((word) => word.value).join(" "))) {
    return { kind: "ask", reason: REASONS.nested };
  }

  if ("gh" === name) {
    return classifyGh(rest, env);
  }

  // timeout 60 gh pr merge・sudo gh pr merge のように、前のコマンドが gh を動かす形。
  for (let position = 1; position < words.length; position += 1) {
    if ("gh" === programName(words[position].value) && looksLikeMerge(words.slice(position + 1))) {
      return { kind: "ask", reason: REASONS.prefixed };
    }
  }

  if (words.some((word) => word.dynamic && mentionsGhMerge(word.value))) {
    return { kind: "ask", reason: REASONS.nested };
  }
  return { kind: "other" };
}

/**
 * Tells whether the words after `gh` could merge a pull request.
 *
 * @param {{value: string, dynamic: boolean}[]} rest The words after `gh`.
 * @returns {boolean} True when the words are `pr merge`, a flag in place of the subcommand, or an API call that names a merge.
 */
function looksLikeMerge(rest) {
  const [first, second] = rest.map((word) => word.value);
  if ("pr" === first) {
    return "merge" === second || (undefined !== second && second.startsWith("-"));
  }
  return "api" === first && rest.some((word) => /merge/i.test(word.value));
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
    return rest.some((word) => /merge/i.test(word.value)) ? { kind: "ask", reason: REASONS.api } : { kind: "other" };
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
      if (undefined === flagValue || (undefined === inline && args[index + 1].dynamic && REPO_FLAGS.has(flag))) {
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
  const args = ["pr", "view"];
  if ("" !== selector) {
    args.push(selector);
  }
  if ("" !== repo) {
    args.push("--repo", repo);
  }
  args.push("--json", "baseRefName", "--jq", ".baseRefName");
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
