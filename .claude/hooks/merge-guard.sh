#!/bin/sh
# PreToolUse(Bash) の前段。判定はしない。Node を起こすかどうかだけを決める。
#
# なぜ sh を挟むか: PreToolUse は Bash の呼び出しのたびに走る。merge-guard.mjs を直接掛けると、
# ls や cat を含むすべての Bash が Node の起動を待つ（Windows で 1 回 0.7〜1.9 秒の実測がある）。
# merge-guard.mjs が確認を出すのは、入力に gh か merge の語があるときだけなので、それ以外では Node を起こさない。
#
# ここで「本当にマージか」を判定してはいけない。引用符やヒアドキュメントの扱いは merge-guard.mjs が決める。
# 絞り込みを広げすぎると確認を出すべきものを見落とす。迷ったら Node に渡す。
#
# 出力と終了コード: Node のものをそのまま返す。Node を起こさなければ何も出さずに 0。
set -u

INPUT=$(cat)

# grep を呼ばない。プロセスを 1 つ起こすだけで、Bash の呼び出しのたびに時間が増えるため。
case "$INPUT" in
    *gh*|*[Mm][Ee][Rr][Gg][Ee]*) ;;
    *) exit 0 ;;
esac

# MERGE_GUARD_NODE は試験だけが使う。Node を起こしたかを、node を差し替えて確かめるため。
printf '%s' "$INPUT" | "${MERGE_GUARD_NODE:-node}" "$(dirname "$0")/merge-guard.mjs"
