#!/bin/bash
# PreToolUse Hook: AIによる破壊的な操作を防止するフック
# Claude Code のフックは stdin から JSON を受け取る（引数ではない）。
# Bash ツールの場合、実行コマンドは .tool_input.command に入っている。
# exit 2 = ブロック（stderr の内容が Claude へのフィードバックになる）
# exit 0 = 許可

INPUT=$(cat)
COMMAND=$(printf '%s' "$INPUT" | python3 -c "import json,sys; print(json.load(sys.stdin).get('tool_input',{}).get('command',''))" 2>/dev/null)

# JSONが解析できない場合は安全側に倒さずそのまま許可（他ツールのイベント等）
[ -z "$COMMAND" ] && exit 0

if [[ "$COMMAND" == *"rm "* ]]; then
    if [[ "$COMMAND" == *".meta"* ]]; then
        echo "[Claude Hook] .meta ファイルの直接削除は許可されていません。uloop-execute-dynamic-code で AssetDatabase.DeleteAsset を使い、ペアとなるアセットと同時に処理してください。" >&2
        exit 2
    fi
    if [[ "$COMMAND" == *"Assets/"* && "$COMMAND" == *"-r"* ]]; then
        echo "[Claude Hook] Assets フォルダに対する再帰的な削除 (rm -r) はブロックされました。AssetDatabase.DeleteAsset を使用してください。" >&2
        exit 2
    fi
fi

exit 0
