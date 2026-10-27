import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { REASONS, autoMergeBases, decide } from "./merge-guard.mjs";

const HOOK = fileURLToPath(new URL("./merge-guard.mjs", import.meta.url));
const BASES = ["develop"];

/**
 * Builds a lookup that answers with a fixed base branch and records what it was asked.
 *
 * @param {string} base The base branch to answer.
 * @returns {{lookupBase: Function, calls: object[]}} The lookup and its calls.
 */
function fakeLookup(base) {
  const calls = [];
  return {
    calls,
    lookupBase: (target) => {
      calls.push(target);
      return base;
    },
  };
}

/**
 * Decides a command with a fake lookup.
 *
 * @param {string} command The raw Bash command.
 * @param {string} base The base branch the lookup answers.
 * @returns {{result: object, calls: object[]}} The decision and the lookups made.
 */
function run(command, base) {
  const { lookupBase, calls } = fakeLookup(base);
  return { result: decide(command, { bases: BASES, lookupBase }), calls };
}

const PASSES_INTO_DEVELOP = [
  "gh pr merge 12",
  "gh pr merge 12 --squash --delete-branch",
  "gh pr merge --squash",
  "gh pr merge -sd 12",
  "gh pr merge https://github.com/o/r/pull/12 --merge",
  "gh pr merge 12 -R o/r --squash",
  "gh pr merge 12 --repo=o/r",
  'gh pr merge 12 --squash --subject "fix: a; b && c" --body "x | y"',
  "gh pr merge 12 --squash 2>&1",
  "GH_REPO=o/r gh pr merge 12",
  "/usr/bin/gh pr merge 12",
  "gh pr merge 12 \\\n  --squash \\\n  --delete-branch",
  "! gh pr merge 12",
];

for (const command of PASSES_INTO_DEVELOP) {
  test(`develop へは確認なし: ${JSON.stringify(command)}`, () => {
    const { result, calls } = run(command, "develop");
    assert.deepEqual(result, { decision: "none" });
    assert.equal(calls.length, 1);
  });
}

for (const base of ["main", "staging"]) {
  test(`${base} へは確認を出す`, () => {
    const { result } = run("gh pr merge 12 --merge", base);
    assert.equal(result.decision, "ask");
    assert.match(result.reason, new RegExp(`マージ先は ${base} です`));
  });
}

test("PR の指定とリポジトリを gh pr view に渡す", () => {
  const { calls } = run('gh pr merge 12 -R o/r --body "b" --squash', "develop");
  assert.deepEqual(calls[0], { selector: "12", repo: "o/r", cwd: undefined, env: {} });
});

test("番号を省いたときは、今のブランチの PR を調べる", () => {
  const { calls } = run("gh pr merge --merge", "develop");
  assert.equal(calls[0].selector, "");
});

test("前に置いた環境変数を gh pr view に渡す", () => {
  const { calls } = run("GH_REPO=o/r gh pr merge 12", "develop");
  assert.deepEqual(calls[0].env, { GH_REPO: "o/r" });
});

const ASKS_WITHOUT_LOOKUP = [
  ["gh pr merge 12 --admin", REASONS.admin],
  ["gh pr merge 12 --squash && git pull", REASONS.chained],
  ["git fetch; gh pr merge 12", REASONS.chained],
  ["gh pr merge 12 | tail -1", REASONS.chained],
  ["gh pr merge 12\ngit status", REASONS.chained],
  ["echo $(gh pr merge 12)", REASONS.chained],
  ["(gh pr merge 12)", REASONS.chained],
  ["$(gh pr merge 12)", REASONS.chained],
  ["`gh pr merge 12`", REASONS.chained],
  ["{ gh pr merge 12; }", REASONS.chained],
  ["if true; then gh pr merge 12; fi", REASONS.chained],
  ["for i in 1; do gh pr merge 12; done", REASONS.chained],
  ["timeout 60 gh pr merge 12", REASONS.prefixed],
  ["nice gh pr merge 12", REASONS.prefixed],
  ["env -u X gh pr merge 12", REASONS.prefixed],
  ["command -p gh pr merge 12", REASONS.prefixed],
  ["sudo gh pr merge 12", REASONS.prefixed],
  ["timeout 60 gh api -X PUT repos/o/r/pulls/12/merge", REASONS.prefixed],
  ["gh pr \\\nmerge 12 --admin", REASONS.admin],
  ["gh m 12", REASONS.unknownCommand],
  ["gh pr m 12", REASONS.unknownCommand],
  ["gh pr --repo o/r merge 12", REASONS.unparsable],
  ["gh --repo o/r pr merge 12", REASONS.unparsable],
  ["gh api graphql -f query='mutation { mergePullRequest(input: {}) { clientMutationId } }'", REASONS.api],
  ["gh api -X PUT repos/o/r/pulls/12/merge", REASONS.api],
  ["gh api --method PUT /repos/o/r/pulls/12/merge -f merge_method=squash", REASONS.api],
  ['bash -c "gh pr merge 12"', REASONS.nested],
  ["eval gh pr merge 12", REASONS.nested],
  ['echo "$(gh pr merge 12)"', REASONS.nested],
  ["gh pr merge $PR", REASONS.unparsable],
  ["gh pr merge 12 13", REASONS.unparsable],
  ["gh pr merge 12 --unknown-flag x", REASONS.unparsable],
  ["gh pr merge 12 -R", REASONS.unparsable],
];

for (const [command, reason] of ASKS_WITHOUT_LOOKUP) {
  test(`調べずに確認を出す: ${JSON.stringify(command)}`, () => {
    const { result, calls } = run(command, "develop");
    assert.deepEqual(result, { decision: "ask", reason });
    assert.equal(calls.length, 0);
  });
}

test("gh が失敗したときは確認を出す", () => {
  const result = decide("gh pr merge 12", {
    bases: BASES,
    lookupBase: () => {
      throw new Error("no pull requests found");
    },
  });
  assert.deepEqual(result, { decision: "ask", reason: REASONS.lookup });
});

test("マージ先が空のときは確認を出す", () => {
  assert.deepEqual(run("gh pr merge 12", "").result, { decision: "ask", reason: REASONS.lookup });
});

const UNRELATED = [
  'grep -rn "gh pr merge" .claude',
  "grep -rn 'gh pr merge' . && git status",
  "git merge --ff-only origin/develop",
  "gh pr view 12 --json baseRefName",
  "gh pr list --state merged",
  "git -C x commit -F - <<'EOF'\ngh pr merge 12\nEOF",
  "cat > a.md <<-EOF\n\tgh api repos/o/r/pulls/1/merge\n\tEOF\ngit status",
  "git commit -F - <<\\EOF\ngh pr merge 12\nEOF",
  "gh pr checks 12 && gh pr view 12",
  "gh issue list; gh api repos/o/r/pulls/12",
  "grep -n gh a.txt",
  "gh --version",
  "gh",
  "ls # gh pr merge 12",
  "",
];

for (const command of UNRELATED) {
  test(`マージでないものは判定しない: ${JSON.stringify(command)}`, () => {
    const { result, calls } = run(command, "main");
    assert.deepEqual(result, { decision: "none" });
    assert.equal(calls.length, 0);
  });
}

test("確認なしで通すマージ先を環境変数で変えられる", () => {
  assert.deepEqual(autoMergeBases(undefined), ["develop"]);
  assert.deepEqual(autoMergeBases("develop, staging"), ["develop", "staging"]);
  assert.deepEqual(autoMergeBases(""), []);
  const { lookupBase } = fakeLookup("staging");
  assert.deepEqual(decide("gh pr merge 12", { bases: ["develop", "staging"], lookupBase }), { decision: "none" });
});

test("確認なしで通す先が空のときは、どのマージにも確認を出す", () => {
  const result = decide("gh pr merge 12", { bases: [], lookupBase: () => "develop" });
  assert.equal(result.decision, "ask");
  assert.match(result.reason, /確認なしで取り込める先が設定されていない/);
});

test("echo gh pr merge は確認を出す側に倒す（別のコマンドが gh を動かす形と区別しない）", () => {
  assert.deepEqual(run("echo gh pr merge", "main").result, { decision: "ask", reason: REASONS.prefixed });
});

/**
 * Runs the hook entry point with the given stdin.
 *
 * @param {string} stdin The raw hook input.
 * @param {Record<string, string>} [env] Extra environment variables.
 * @returns {import("node:child_process").SpawnSyncReturns<string>} The process result.
 */
function runHook(stdin, env = {}) {
  return spawnSync(process.execPath, [HOOK], { input: stdin, encoding: "utf8", env: { ...process.env, ...env } });
}

test("入口: 確認を出すときは ask の JSON を出す", () => {
  const result = runHook(JSON.stringify({ tool_name: "Bash", tool_input: { command: "gh pr merge 1 --admin" } }));
  assert.equal(result.status, 0);
  assert.deepEqual(JSON.parse(result.stdout), {
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "ask",
      permissionDecisionReason: REASONS.admin,
    },
  });
});

test("入口: gh を呼べないときは確認を出す", () => {
  const result = runHook(JSON.stringify({ tool_name: "Bash", tool_input: { command: "gh pr merge 1" } }), {
    PATH: "/nonexistent",
  });
  assert.equal(result.status, 0);
  assert.equal(JSON.parse(result.stdout).hookSpecificOutput.permissionDecision, "ask");
});

test("入口: マージでないコマンドでは何も出さない", () => {
  const result = runHook(JSON.stringify({ tool_name: "Bash", tool_input: { command: "git status" } }));
  assert.equal(result.status, 0);
  assert.equal(result.stdout, "");
});

test("入口: Bash 以外の道具では何も出さない", () => {
  const result = runHook(JSON.stringify({ tool_name: "Read", tool_input: { file_path: "merge" } }));
  assert.equal(result.stdout, "");
});

test("入口: 読めない入力にマージの語があれば確認を出す", () => {
  const result = runHook("{gh pr merge");
  assert.equal(result.status, 0);
  assert.equal(JSON.parse(result.stdout).hookSpecificOutput.permissionDecision, "ask");
});
