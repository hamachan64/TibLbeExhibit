#!/usr/bin/env python3
"""Codex CLI 用 PreToolUse ガードレール（Claude Code の guard.py のアダプタ）。

Codex のフックは Claude Code と同じ契約で動く:
  stdin から JSON / exit 0 = 許可 / exit 2 = ブロック（stderr がモデルへのフィードバック）

ただしツール名と tool_input の形が違うため、ここで Claude Code 形に正規化してから
`.claude/hooks/guard.py` のルール（同一の判定ロジック）に渡す。ルールの二重管理をしない。

Codex 側のツール名（`codex-rs/core/src/tools/hook_names.rs`）:
  シェル実行        -> "Bash"         tool_input.command は文字列または argv 配列
  ファイル編集      -> "apply_patch"  （matcher の別名として Write / Edit も通る）
  サブエージェント  -> "spawn_agent"
  MCP ツール        -> サーバー名を含む名前（例: blender の各ツール）

Claude Code と違い Codex の PreToolUse には「ユーザーに確認する」(ask) がないため、
guard.py が ask を出す場面（従量課金 API 等）はこのアダプタでブロックに倒す。
"""

import json
import pathlib
import re
import sys

for _stream in (sys.stdin, sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, OSError, ValueError):
        pass

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
GUARD_DIR = REPO_ROOT / ".claude" / "hooks"

if not (GUARD_DIR / "guard.py").is_file():
    sys.stderr.write(
        "[guard] ルール本体 .claude/hooks/guard.py が見つかりません（探した場所: %s）。\n"
        "ガードレールが機能しない状態では作業を続けられません。ユーザーに報告してください。\n"
        % GUARD_DIR
    )
    sys.exit(2)

sys.path.insert(0, str(GUARD_DIR))
import guard  # noqa: E402

LICENSE_RULE = "（.claude/rules/license_cost_security.md）"

# Codex には確認プロンプトがないので、確認が必要な操作はブロックする
def _ask_becomes_block(message):
    guard.block(
        "この操作はユーザーの確認が必要ですが、Codex には確認プロンプトがないため禁止です。\n  "
        + message
        + "\n  確認が取れているなら、Claude Code 側で実行するかユーザーに直接依頼してください。"
    )


guard.ask = _ask_becomes_block

# --- Blender MCP: 費用・権利の歯止め（config.toml の enabled_tools の二重化）---------
BLENDER_MCP_BLOCK = [
    (re.compile(r"generate_3d|hyper3d|rodin|hunyuan|tripo", re.I),
     "AI 3D 生成（Hyper3D Rodin / Hunyuan3D / Tripo）は Premium または自前 API キーが必要で、"
     "クレジットを消費し、生成物の商用利用条件も別途確認が必要です。使わずに "
     "execute_blender_code で自作してください"),
    (re.compile(r"import_asset|search_assets|sketchfab|poly_?pizza|polyhaven", re.I),
     "外部ライブラリからの素材取り込みは、素材ごとにライセンス（CC0 / CC-BY / 非商用）が違うため、"
     "4点確認と THIRD_PARTY_NOTICES.md への記録を人が行う必要があります。Codex からは取り込まず、"
     "必要ならユーザーに依頼してください"),
    (re.compile(r"disable_telemetry", re.I),
     "ユーザーの Blender の設定を変える操作です。勝手に行わないでください"),
]

# Codex の apply_patch が運ぶパッチ本文からファイルパスを拾う
PATCH_PATH_RE = re.compile(
    r"^\*\*\*\s+(?:Add|Update|Delete)\s+File:\s*(.+?)\s*$|^\*\*\*\s+Move\s+to:\s*(.+?)\s*$",
    re.MULTILINE,
)


def collect_strings(value, out):
    """tool_input の中の文字列をすべて集める（形が変わっても取りこぼさないため）。"""
    if isinstance(value, str):
        out.append(value)
    elif isinstance(value, dict):
        for item in value.values():
            collect_strings(item, out)
    elif isinstance(value, (list, tuple)):
        for item in value:
            collect_strings(item, out)


def normalize_command(tool_input):
    """Codex のシェル入力（文字列 or argv 配列）を1本のコマンド文字列にする。"""
    command = tool_input.get("command")
    if isinstance(command, str):
        return command
    if isinstance(command, (list, tuple)):
        return " ".join(str(part) for part in command)
    # 形が想定と違う場合は、検査を素通りさせないため全文を対象にする
    parts = []
    collect_strings(tool_input, parts)
    return "\n".join(parts)


def check_shell(tool_input):
    guard.check_bash({"command": normalize_command(tool_input)})


def check_patch(tool_input):
    body_parts = []
    collect_strings(tool_input, body_parts)
    body = "\n".join(body_parts)

    paths = [p or m for p, m in PATCH_PATH_RE.findall(body)]
    for path in paths:
        # パスごとの禁止（シリアライズ済みアセット・生成物ディレクトリ）を適用する。
        # 内容はパッチ全文を渡し、秘密情報の混入も同時に検査する。
        guard.check_file_write({"file_path": path, "content": body})

    if not paths:
        guard.check_secrets(body, "apply_patch のパッチ本文")


def check_mcp(tool_name, tool_input):
    for pattern, reason in BLENDER_MCP_BLOCK:
        if pattern.search(tool_name):
            guard.block("%s%s\n  ツール: %s" % (reason, LICENSE_RULE, tool_name))

    # execute_blender_code で Blender 経由の破壊的操作・秘密情報の書き出しを防ぐ
    parts = []
    collect_strings(tool_input, parts)
    code = "\n".join(parts)
    guard.check_secrets(code, "Blender に渡す Python コード")

    if re.search(r"\bbpy\.ops\.wm\.quit_blender\b", code):
        guard.block("Blender を終了させる操作です。ユーザーの作業中の .blend を失う恐れがあります。")
    if re.search(r"shutil\.rmtree|os\.remove|os\.unlink", code) and re.search(
        r"Assets|Library|ProjectSettings|Packages", code
    ):
        guard.block(
            "Blender 経由で Unity プロジェクト管理下のファイルを削除しようとしています。禁止です。"
        )


def main():
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        sys.exit(0)  # 解析できないイベントは素通し

    tool_name = payload.get("tool_name") or ""
    tool_input = payload.get("tool_input")
    if not isinstance(tool_input, dict):
        tool_input = {"value": tool_input} if tool_input is not None else {}

    lowered = tool_name.lower()

    if tool_name == "Bash" or lowered in ("shell", "local_shell", "exec_command"):
        check_shell(tool_input)
    elif tool_name in ("apply_patch", "Write", "Edit", "MultiEdit", "NotebookEdit"):
        check_patch(tool_input)
    elif "blender" in lowered or any(p.search(tool_name) for p, _ in BLENDER_MCP_BLOCK):
        check_mcp(tool_name, tool_input)

    sys.exit(0)


if __name__ == "__main__":
    main()
