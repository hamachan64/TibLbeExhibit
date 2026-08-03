#!/usr/bin/env python3
"""PreToolUse ガードレール。

AI駆動開発ループ（承認なしで実装〜PR発行まで走らせる運用）における最後の安全弁。
自動承認ルール（settings.json の permissions.allow）で承認プロンプトを消した分、
「取り返しがつかない操作」だけをこのフックで確実に止める。

Claude Code のフックは stdin から JSON を受け取る。
  exit 0 = 許可 / exit 2 = ブロック（stderr の内容が Claude へのフィードバックになる）

対象ツール: Bash / Edit / Write / MultiEdit / NotebookEdit
"""

import json
import os
import re
import shlex
import sys

# シリアライズ済みUnityアセット（テキスト直接編集を禁止）
SERIALIZED_ASSET_EXT = (".unity", ".prefab", ".asset", ".meta")

# 直接触ってはいけないUnityの生成物ディレクトリ
GENERATED_DIRS = ("/Library/", "/Temp/", "/obj/", "/Logs/", "/UserSettings/")

USE_ULOOP = (
    "uloop-execute-dynamic-code で SerializedObject / AssetDatabase / PrefabUtility を"
    "使って操作してください（.claude/rules/unity_asset_rules.md）。"
)

# --- Bash: 取り返しがつかない操作 -------------------------------------------------
# (正規表現, ブロック理由)
BASH_RULES = [
    (r"\brm\b(?:\s+-\S+)*[^|;&]*\.meta\b",
     ".meta の直接削除は参照を壊します。AssetDatabase.DeleteAsset を使ってください。"),
    (r"\brm\b\s+(?:-\S+\s+)*[^|;&]*\b(?:Assets|Library|Packages|ProjectSettings)\b",
     "Unityプロジェクト管理下のディレクトリを rm で削除することは禁止です。" + USE_ULOOP),
    (r"\brm\b\s+(?:-\S+\s+)*(?:/|~|\$HOME)(?:\s|/\*|$)",
     "ルート/ホーム直下を対象にした rm は禁止です。"),
    (r"\bgit\b[^|;&]*\bpush\b[^|;&]*(?:--force(?:-with-lease)?\b|\s-f\b|\s\+refs/)",
     "force push は禁止です。履歴を壊さない方法（新規コミット / 新規ブランチ）に切り替えてください。"),
    (r"\bgit\b[^|;&]*\bpush\b[^|;&]*\b(?:master|main|develop)\b",
     "master / main / develop への直接 push は禁止です。feature ブランチに push して PR を作成してください。"),
    (r"\bgit\b[^|;&]*\breset\b[^|;&]*--hard",
     "git reset --hard は未コミットの作業を消します。git stash / git restore <path> を使ってください。"),
    (r"\bgit\b[^|;&]*\bclean\b[^|;&]*\s-[a-zA-Z]*[fdx]",
     "git clean は未追跡ファイル（生成前のアセット等）を消します。禁止です。"),
    (r"\bgit\b[^|;&]*(?:filter-branch|reflog\s+expire|update-ref\s+-d|gc\s+--prune=now)",
     "履歴を書き換える/失わせる git 操作は禁止です。"),
    (r"\bgit\b[^|;&]*\bbranch\b[^|;&]*\s-[dD]\s+(?:master|main|develop)\b",
     "保護ブランチの削除は禁止です。"),
    (r"\bgh\s+pr\s+merge\b",
     "PRのマージは必ず人間が行います。PRの作成までで停止し、URLを報告してください。"),
    (r"\bgh\s+(?:repo\s+delete|secret\b|auth\s+(?:token|logout)\b|release\s+(?:create|delete))",
     "リポジトリ/シークレット/リリースに対する破壊的・機密操作は禁止です。"),
    (r"(?:^|[;&|]\s*)sudo\b",
     "sudo の使用は禁止です。"),
    (r"\bchmod\s+(?:-R\s+)?777\b",
     "chmod 777 は禁止です。必要な権限だけを付与してください。"),
    (r"(?:curl|wget)[^|]*\|\s*(?:sudo\s+)?(?:ba|z|d)?sh\b",
     "ネットワークから取得したスクリプトの直接実行は禁止です。"),
    (r"(?:\bsed\s+-i|\bperl\s+-\S*i)[^|;&]*\.(?:unity|prefab|asset|meta)\b",
     "シリアライズ済みアセットの sed/perl による直接書き換えは禁止です。" + USE_ULOOP),
    (r">\s*\S+\.(?:unity|prefab|asset|meta)\b",
     "シリアライズ済みアセットへのシェルリダイレクト書き込みは禁止です。" + USE_ULOOP),
    (r"(?:shutil\.rmtree|os\.remove|os\.unlink)[\s\S]{0,200}?(?:Assets|Library|ProjectSettings|Packages)",
     "スクリプト経由でのUnity管理ディレクトリ削除は禁止です。" + USE_ULOOP),
]

# --- uloop execute-dynamic-code: 実行される C# の中身を検査 ------------------------
CSHARP_RULES = [
    (r"\bDirectory\.Delete\b", "Directory.Delete によるディレクトリ削除"),
    (r"\bFile\.Delete\b", "File.Delete によるファイル削除"),
    (r"\bFileUtil\.Delete", "FileUtil.Delete"),
    (r"\bAssetDatabase\.DeleteAssets\b", "AssetDatabase.DeleteAssets（一括削除）"),
    (r"AssetDatabase\.DeleteAsset\s*\(\s*\"Assets/?\"\s*\)", "ルートパス指定の DeleteAsset"),
    (r"\bAssetDatabase\.DeleteAsset\b[\s\S]{0,400}?(?:\bforeach\b|\bfor\s*\(|FindAssets|GetAllAssetPaths)",
     "検索結果/ループに対する一括 DeleteAsset"),
    (r"(?:FindAssets|GetAllAssetPaths)[\s\S]{0,400}?\bAssetDatabase\.DeleteAsset\b",
     "検索結果/ループに対する一括 DeleteAsset"),
    (r"\bEditorApplication\.Exit\b|\bApplication\.Quit\b", "エディタ/アプリの強制終了"),
    (r"UnityEditor\.PackageManager\.Client\.(?:Remove|Add|Resolve)\b", "パッケージ構成の変更"),
    (r"\b(?:EditorPrefs|PlayerPrefs)\.DeleteAll\b", "設定の全削除"),
    (r"\bEditorSceneManager\.NewScene\b", "現在のシーンを破棄する NewScene"),
    (r"File\.Write(?:AllText|AllBytes)[^;]{0,200}\.(?:unity|prefab|asset|meta)\b",
     "シリアライズ済みアセットへの生ファイル書き込み"),
]


def block(message):
    sys.stderr.write("[guard] " + message + "\n")
    sys.exit(2)


def extract_dynamic_code(command):
    """execute-dynamic-code に渡される C# 本文を可能な限り取り出す。"""
    chunks = []
    try:
        tokens = shlex.split(command)
    except ValueError:
        # クォートが解析できない場合はコマンド全文をそのまま検査対象にする
        return [command]

    for i, tok in enumerate(tokens):
        if tok in ("--code", "-c") and i + 1 < len(tokens):
            chunks.append(tokens[i + 1])
        elif tok == "--code-file" and i + 1 < len(tokens):
            path = tokens[i + 1]
            try:
                with open(path, "r", encoding="utf-8", errors="replace") as fp:
                    chunks.append(fp.read())
            except OSError:
                # 読めない場合は検査不能 → 安全側に倒してブロック
                block(
                    "--code-file '%s' を読めず内容を検査できませんでした。"
                    "パスを確認するか --code でインラインに渡してください。" % path
                )
    return chunks or [command]


def check_bash(tool_input):
    command = tool_input.get("command", "")
    if not command:
        return

    for pattern, reason in BASH_RULES:
        if re.search(pattern, command):
            block(reason + "\n  該当コマンド: " + command[:300])

    if "execute-dynamic-code" in command:
        for code in extract_dynamic_code(command):
            for pattern, reason in CSHARP_RULES:
                if re.search(pattern, code):
                    block(
                        "execute-dynamic-code の中に破壊的な処理が含まれています: %s\n"
                        "  この操作はユーザーの明示的な承認が必要です。実行せず、"
                        "何をなぜ行いたいのかを報告して指示を仰いでください。" % reason
                    )


def check_file_write(tool_input):
    path = tool_input.get("file_path") or tool_input.get("notebook_path") or ""
    if not path:
        return

    lowered = path.lower()
    if lowered.endswith(SERIALIZED_ASSET_EXT):
        block(
            "%s はシリアライズ済みUnityアセットです。テキストとしての直接編集・生成は禁止されています。\n"
            "  %s" % (os.path.basename(path), USE_ULOOP)
        )

    normalized = "/" + path.strip("/") + "/"
    for directory in GENERATED_DIRS:
        if directory in normalized:
            block("%s はUnityの生成物ディレクトリです。直接編集しないでください。" % directory.strip("/"))


def main():
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        sys.exit(0)  # 解析できないイベントは素通し

    tool_name = payload.get("tool_name", "")
    tool_input = payload.get("tool_input", {}) or {}

    if tool_name == "Bash":
        check_bash(tool_input)
    elif tool_name in ("Edit", "Write", "MultiEdit", "NotebookEdit"):
        check_file_write(tool_input)

    sys.exit(0)


if __name__ == "__main__":
    main()
