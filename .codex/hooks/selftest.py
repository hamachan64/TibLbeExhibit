#!/usr/bin/env python3
"""ガードレールの自己テスト。

`uv run --no-project .codex/hooks/selftest.py` で実行する。
guard_codex.py を実際に別プロセスで起動し、ブロック（exit 2）と許可（exit 0）が
期待どおりかを確認する。ガードの修正後はこれを通すこと。
"""

import json
import pathlib
import subprocess
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, OSError, ValueError):
        pass

HERE = pathlib.Path(__file__).resolve().parent
GUARD = HERE / "guard_codex.py"

BLOCK = 2
ALLOW = 0

# (説明, 期待 exit code, payload)
CASES = [
    (
        "Bash: argv 配列での履歴破壊 push",
        BLOCK,
        {"tool_name": "Bash", "tool_input": {"command": ["bash", "-lc", "git " + "push --force origin main"]}},
    ),
    (
        "Bash: 保護ブランチへの直接 push",
        BLOCK,
        {"tool_name": "Bash", "tool_input": {"command": ["bash", "-lc", "git " + "push origin develop"]}},
    ),
    (
        "Bash: PR のマージ",
        BLOCK,
        {"tool_name": "Bash", "tool_input": {"command": ["bash", "-lc", "gh pr " + "merge 46 --squash"]}},
    ),
    (
        "Bash: Assets の rm",
        BLOCK,
        {"tool_name": "Bash", "tool_input": {"command": ["bash", "-lc", "rm -rf Assets/_Content/TreasureHaul"]}},
    ),
    (
        "Bash: 従量課金 API へのアクセス（Codex では ask がないのでブロック）",
        BLOCK,
        {"tool_name": "Bash", "tool_input": {"command": ["bash", "-lc", "curl https://api.openai.com/v1/models"]}},
    ),
    (
        "Bash: 無害なコマンド",
        ALLOW,
        {"tool_name": "Bash", "tool_input": {"command": ["bash", "-lc", "git status --porcelain"]}},
    ),
    (
        "apply_patch: .prefab のテキスト直接編集",
        BLOCK,
        {
            "tool_name": "apply_patch",
            "tool_input": {
                "input": "*** Begin Patch\n"
                "*** Update File: Assets/_Content/TreasureHaul/Prefabs/Treasure.prefab\n"
                "@@\n-foo\n+bar\n*** End Patch"
            },
        },
    ),
    (
        "apply_patch: .meta の追加",
        BLOCK,
        {
            "tool_name": "apply_patch",
            "tool_input": {
                "input": "*** Begin Patch\n*** Add File: Assets/_Content/Models/Treasure.fbx.meta\n+guid: 0\n*** End Patch"
            },
        },
    ),
    (
        "apply_patch: Library/ への書き込み",
        BLOCK,
        {
            "tool_name": "apply_patch",
            "tool_input": {
                "input": "*** Begin Patch\n*** Update File: Library/ArtifactDB\n+x\n*** End Patch"
            },
        },
    ),
    (
        "apply_patch: 通常の C# ファイル",
        ALLOW,
        {
            "tool_name": "apply_patch",
            "tool_input": {
                "input": "*** Begin Patch\n"
                "*** Add File: Assets/_Content/TreasureHaul/Scripts/Foo.cs\n"
                "+public class Foo {}\n*** End Patch"
            },
        },
    ),
    (
        "apply_patch: API キーの書き込み",
        BLOCK,
        {
            "tool_name": "apply_patch",
            "tool_input": {
                "input": "*** Begin Patch\n*** Add File: Assets/_Content/TreasureHaul/Scripts/Key.cs\n"
                '+const string K = "sk-' + "proj-" + "a" * 32 + '";\n*** End Patch'
            },
        },
    ),
    (
        "MCP: 有料の AI 3D 生成",
        BLOCK,
        {"tool_name": "blender__generate_3d", "tool_input": {"prompt": "a golden mask"}},
    ),
    (
        "MCP: 外部ライブラリからの素材取り込み",
        BLOCK,
        {"tool_name": "blender__import_asset", "tool_input": {"asset_id": "x"}},
    ),
    (
        "MCP: 素材検索",
        BLOCK,
        {"tool_name": "search_assets", "tool_input": {"query": "rubble"}},
    ),
    (
        "MCP: Blender の終了",
        BLOCK,
        {
            "tool_name": "blender__execute_blender_code",
            "tool_input": {"code": "import bpy\nbpy.ops.wm.quit_blender()"},
        },
    ),
    (
        "MCP: 通常のモデリングコード",
        ALLOW,
        {
            "tool_name": "blender__execute_blender_code",
            "tool_input": {"code": "import bpy\nbpy.ops.mesh.primitive_cube_add(size=0.3)"},
        },
    ),
    (
        "MCP: シーン情報の取得",
        ALLOW,
        {"tool_name": "blender__get_scene_info", "tool_input": {}},
    ),
    (
        "対象外のツールは素通し",
        ALLOW,
        {"tool_name": "update_plan", "tool_input": {"plan": []}},
    ),
]


def main():
    failures = []
    for label, expected, payload in CASES:
        result = subprocess.run(
            ["uv", "run", "--no-project", str(GUARD)],
            input=json.dumps(payload),
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
        ok = result.returncode == expected
        mark = "OK  " if ok else "NG  "
        want = "ブロック" if expected == BLOCK else "許可"
        got = "ブロック" if result.returncode == BLOCK else "許可(%d)" % result.returncode
        print("%s%s / 期待=%s 実際=%s" % (mark, label, want, got))
        if result.returncode == BLOCK and result.stderr.strip():
            print("      理由: " + result.stderr.strip().splitlines()[0])
        if not ok:
            failures.append(label)

    print()
    if failures:
        print("失敗 %d 件: %s" % (len(failures), ", ".join(failures)))
        return 1
    print("全 %d 件が期待どおり。" % len(CASES))
    return 0


if __name__ == "__main__":
    sys.exit(main())
