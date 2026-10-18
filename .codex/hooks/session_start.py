#!/usr/bin/env python3
"""Codex セッション開始時の確認役（カナリア）。

目的は2つ。
1. ガードレール（PreToolUse の guard_codex.py）が実行できる状態かを、作業を始める前に確かめる。
   フックが壊れていても PreToolUse は「素通り」になるだけで気づけないので、ここで声を上げる。
2. 絶対ルールの要点をセッションの文脈に入れる（AGENTS.md を読み落としても効く保険）。

stdout に書いた内容が追加コンテキストとしてモデルに渡る。
"""

import pathlib
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, OSError, ValueError):
        pass

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]

REQUIRED = [
    REPO_ROOT / ".claude" / "hooks" / "guard.py",
    REPO_ROOT / ".codex" / "hooks" / "guard_codex.py",
    REPO_ROOT / ".claude" / "rules" / "license_cost_security.md",
    REPO_ROOT / "AGENTS.md",
]

missing = [str(p.relative_to(REPO_ROOT)) for p in REQUIRED if not p.is_file()]

if missing:
    print(
        "【警告】ガードレールの構成ファイルが欠けています: "
        + ", ".join(missing)
        + "\nこの状態では危険な操作が止まりません。作業を始めず、ユーザーに報告してください。"
    )
    sys.exit(0)

print(
    "ガードレール有効（.codex/hooks.json → guard_codex.py → .claude/hooks/guard.py）。\n"
    "このリポジトリの絶対ルール（詳細は AGENTS.md）:\n"
    "- .unity / .prefab / .asset / .meta をテキスト編集しない（uloop 経由のエディタ API を使う）\n"
    "- 外部の素材・パッケージ・サービスを使う前に .claude/rules/license_cost_security.md の4点を確認し、"
    "THIRD_PARTY_NOTICES.md に記録する\n"
    "- 有料・クレジット消費の 3D 生成（Hyper3D / Hunyuan3D / Tripo）と、外部ライブラリからの素材取り込みは使わない\n"
    "- master / develop / repo/develop に直接コミット・push しない。PR のマージは人間が行う\n"
    "- ガードレールにブロックされたら迂回せず、理由を報告して指示を仰ぐ"
)
sys.exit(0)
