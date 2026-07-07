#!/bin/bash
# PreToolUse Hook: AIによる破壊的な操作を防止するフック
# Claude Code がツール（シェルコマンド等）を実行する前に呼び出されることを想定
COMMAND="$*"

# rm コマンドが含まれており、かつ .meta または Assets フォルダを対象としているかチェック
if [[ "$COMMAND" == *"rm "* ]]; then
    if [[ "$COMMAND" == *".meta"* ]]; then
        echo "ERROR: [Claude Hook] .meta ファイルの直接削除は許可されていません。Unity内で行うか、ペアとなるアセットと同時に処理してください。" >&2
        exit 1
    fi
    if [[ "$COMMAND" == *"Assets/"* && "$COMMAND" == *"-r"* ]]; then
        echo "ERROR: [Claude Hook] Assets フォルダに対する再帰的な削除 (rm -rf) はブロックされました。" >&2
        exit 1
    fi
fi

# コマンドの実行を許可
exit 0
