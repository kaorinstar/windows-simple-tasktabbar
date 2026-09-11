#!/bin/sh
# Exercises check-main.sh against a scratch repository.
#
# The check can fail in two directions. It can miss a push that should have been
# refused, and it can refuse something that is not that push at all. The second is the
# one that has actually happened: a branch deletion was refused, and so was a search
# whose pattern contained the words. A check that stops work it has no business
# stopping teaches whoever meets it to route around it.
#
# So every form of git push that has a reason to be typed is listed below, with what it
# should do. Add the next one here before changing the detection.
#
# Run it with:
#
#     sh .claude/hooks/check-main.test.sh
#
# It builds its own repositories under a temporary directory, needs no network, and
# touches nothing outside that directory. It exits 0 when every case passes.

set -u

HOOK=$(cd "$(dirname "$0")" && pwd)/check-main.sh
[ -f "$HOOK" ] || { echo "check-main.sh not found beside this script"; exit 1; }

WORKDIR=$(mktemp -d) || exit 1
trap 'rm -rf "$WORKDIR"' EXIT INT TERM

FAILURES=0
CASES=0

# Two clones of one repository: "work" is the branch being written, "other" stands for
# the session that got to main first.
build_repository() {
    rm -rf "$WORKDIR/origin.git" "$WORKDIR/work" "$WORKDIR/other"
    (
        cd "$WORKDIR" || exit 1
        git init -q --bare -b main origin.git
        git init -q -b main work
        cd work || exit 1
        git config user.email test@example.com
        git config user.name test
        git config push.negotiate false
        mkdir -p src .claude/hooks
        printf 'shared\n' >src/shared.txt
        cp "$HOOK" .claude/hooks/check-main.sh
        git add -A
        git commit -qm base
        git remote add origin ../origin.git
        git push -q -u origin main
        git checkout -qb feat/x
        printf 'mine\n' >>src/shared.txt
        git commit -qam "work on this branch"
        cd .. || exit 1
        git clone -q origin.git other
        cd other || exit 1
        git config user.email test@example.com
        git config user.name test
        git config push.negotiate false
        printf 'theirs\n' >>src/shared.txt
        git commit -qam "work that reached main first"
        git push -q origin main
    ) >/dev/null 2>&1
}

# $1 what the case is, $2 expected exit code, $3 the command the session is running
expect_exit() {
    CASES=$((CASES + 1))
    rm -f "$WORKDIR/work/.git/claude-main-check"
    actual=$(
        cd "$WORKDIR/work" || exit 1
        printf '{"tool_name":"Bash","tool_input":{"command":"%s"}}' "$3" \
            | sh .claude/hooks/check-main.sh PreToolUse >/dev/null 2>&1
        echo $?
    )
    if [ "$actual" = "$2" ]; then
        printf 'ok   %s\n' "$1"
    else
        printf 'FAIL %s (expected exit %s, got %s)\n' "$1" "$2" "$actual"
        FAILURES=$((FAILURES + 1))
    fi
}

# $1 what the case is, $2 "silent" or "speaks", $3 arguments to the hook
expect_output() {
    CASES=$((CASES + 1))
    rm -f "$WORKDIR/work/.git/claude-main-check"
    out=$(cd "$WORKDIR/work" && sh .claude/hooks/check-main.sh $3 2>/dev/null)
    if [ "$2" = "silent" ] && [ -z "$out" ]; then
        printf 'ok   %s\n' "$1"
    elif [ "$2" = "speaks" ] && [ -n "$out" ]; then
        printf 'ok   %s\n' "$1"
    else
        printf 'FAIL %s (expected %s)\n' "$1" "$2"
        FAILURES=$((FAILURES + 1))
    fi
}

echo "The branch is one commit behind main."
build_repository

echo
echo "A push that carries this branch's work is refused:"
expect_exit "an ordinary push"            2 "git push -u origin feat/x"
expect_exit "a push with no arguments"    2 "git push"
expect_exit "a push after another command" 2 "git status \&\& git push"
expect_exit "a push to an explicit ref"   2 "git push origin HEAD:refs/heads/feat/x"

echo
echo "A push that carries none of it is allowed:"
expect_exit "deleting a branch"           0 "git push origin --delete claude/old"
expect_exit "deleting with -d"            0 "git push origin -d claude/old"
expect_exit "deleting with a refspec"     0 "git push origin :claude/old"
expect_exit "pushing tags"                0 "git push origin --tags"
expect_exit "a dry run"                   0 "git push --dry-run origin feat/x"

echo
echo "A command that is not a push is allowed, whatever it says:"
expect_exit "a search for the words"      0 "grep -rn 'git push' docs/"
expect_exit "a message about pushing"     0 "echo 'remember to git push later'"
expect_exit "an unrelated command"        0 "git status"
expect_exit "a command named like one"    0 "git pushall"

echo
echo "The report itself:"
expect_output "it speaks when behind"        speaks ""
expect_output "it speaks at the session start" speaks "SessionStart"

echo
echo "It stays quiet where it has nothing to say:"
(cd "$WORKDIR/work" && git checkout -q main) >/dev/null 2>&1
expect_output "on the base branch"           silent ""
(cd "$WORKDIR/work" && git checkout -q feat/x && git merge -q --no-edit FETCH_HEAD 2>/dev/null || git checkout -q --theirs . 2>/dev/null; git add -A >/dev/null 2>&1; git commit -qm merged >/dev/null 2>&1) >/dev/null 2>&1
expect_output "once main is merged in"       silent ""
build_repository
(cd "$WORKDIR/work" && git checkout -q --detach HEAD) >/dev/null 2>&1
expect_output "on a detached HEAD"           silent ""
build_repository
(cd "$WORKDIR/work" && git remote rename origin upstream) >/dev/null 2>&1
expect_output "with no origin remote"        silent ""

echo
build_repository
CASES=$((CASES + 1))
first=$(cd "$WORKDIR/work" && printf '{"tool_input":{"command":"ls"}}' | sh .claude/hooks/check-main.sh PreToolUse 2>/dev/null)
second=$(cd "$WORKDIR/work" && printf '{"tool_input":{"command":"ls"}}' | sh .claude/hooks/check-main.sh PreToolUse 2>/dev/null)
if [ -n "$first" ] && [ -z "$second" ]; then
    echo "ok   the interval holds between two Bash calls"
else
    echo "FAIL the interval holds between two Bash calls"
    FAILURES=$((FAILURES + 1))
fi

CASES=$((CASES + 1))
case "$first" in
    '{'*'}') echo "ok   the hook output is a JSON object" ;;
    *)
        echo "FAIL the hook output is a JSON object"
        FAILURES=$((FAILURES + 1))
        ;;
esac

echo
if [ "$FAILURES" = "0" ]; then
    printf '%s cases, all passed.\n' "$CASES"
    exit 0
fi
printf '%s cases, %s failed.\n' "$CASES" "$FAILURES"
exit 1
