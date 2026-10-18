# AGENTS.md（Codex 向け）

このリポジトリで Codex が作業するときのルール。**Claude Code 向けの指示は `CLAUDE.md` にあり、
内容は共通**なので、迷ったら `CLAUDE.md` と `DEVELOPMENT.md` も読むこと。

## プロジェクト
Unity 6000.3.10f1 / URP の MR（Mixed Reality）展示アプリ。Inter BEE 2026 出展に向けた協力型 MR 体験
**Treasure Haul**（壊れやすいお宝を仲間と運び出す）を開発している。ハンドトラッキング専用・
フルカラーパススルー前提で、端末は主に PICO。

- 企画: `TREASURE_HAUL_PLAN.md` / 要件: `TREASURE_HAUL_REQUIREMENTS.md` / 設計: `TREASURE_HAUL_DESIGN.md`
- 進み具合: `TODO.md`
- 技術ルール: `DEVELOPMENT.md` と `.claude/rules/` 以下

## 絶対ルール

### 1. Unity アセットを壊さない（`.claude/rules/unity_asset_rules.md`）
- `.unity` / `.prefab` / `.asset` / `.meta` を**テキストとして編集・生成しない**。
  読み取り・調査のための閲覧は問題ない。
- これらの変更は Unity のエディタ API（`SerializedObject` / `AssetDatabase` / `PrefabUtility`）で行う。
  Codex から Unity を操作する手段がない場合は、**自分でやらずにユーザーへ依頼する**。
- `Assets/` 配下にファイルを追加・移動・削除したら `AssetDatabase.Refresh()` が必要。`.meta` を
  単独で作成・削除・編集しない。guid を書き換えない。

### 2. 権利・費用・セキュリティ（`.claude/rules/license_cost_security.md`）
外部の素材・パッケージ・サービス・AI 生成物を使う前に、**毎回**4点を確認する。
1つでも確認できなければ使わず、ユーザーに報告する。

1. **商用利用できるか**（Inter BEE の企業ブース展示は商用利用）
2. **権利に問題がないか**（ライセンスは公式ページの原文で確認。実在ブランド・人物、R.E.P.O の素材は使わない）
3. **無料か**（有料プラン・従量課金 API・クレジット消費サービスにアクセスしない）
4. **セキュリティ**（入手元は公式のみ。API キーをファイルやコマンドに書かない）

使うと決めた物は `THIRD_PARTY_NOTICES.md` に1行記録する。

### 3. Blender MCP の使い方
- 使っていいのは **`execute_blender_code` による自作モデリング**と、`get_scene_info` / `look` /
  `get_addon_status` などの確認系だけ。
- **使わない**: `generate_3d`（有料・クレジット消費の AI 3D 生成）、`search_assets` / `import_asset`
  （素材ごとにライセンスが違い、人の判断が必要）。これらは `~/.codex/config.toml` の
  `enabled_tools` で除外済みで、フックでも二重に止めている。
- Blender はユーザーの作業中のアプリ。**保存していない .blend を壊す操作をしない**
  （新規シーン作成、終了、既存オブジェクトの一括削除など）。何をどう変えるか先に述べてから実行する。
- 成果物は `BlenderAssets/` に `.blend` で置き、Unity へ渡す形式（FBX / glTF）に書き出す。
- 作業後は `look` で自分の目で確認する。見ていない結果を「できた」と報告しない。

### 4. Git
- ブランチは `repo/develop` から `repo/feature-<slug>` を切る。PR の向き先は `repo/develop`。
- `master` / `develop` / `repo/develop` へ直接コミット・push しない。
  `develop` / `master` は前作（展示のフォールバック）の系統なので混ぜない。
- force push・`git reset --hard`・`git clean -fd`・履歴の書き換えは禁止。
- **PR のマージは必ず人間が行う**。`gh pr merge` を実行しない。
- PR には `Closes #<番号>` で Issue を紐づける。

### 5. コードを書くときの約束（`DEVELOPMENT.md`）
- 非同期は `Coroutine` ではなく **UniTask**。イベント・状態監視は `UnityEvent` / `event Action` ではなく **R3**。
- 演出・補間は Update 内の自前計算ではなく **DOTween**。
- コントローラー入力に依存する実装は禁止（**ハンドトラッキング専用**）。
- アセンブリ構成: `TibLbeExhibit.TreasureHaul`（`Assets/_Content/TreasureHaul/`）は
  **Core のみ参照可**。前作の `TibLbeExhibit.GIPoker` を参照しない。

## ガードレール
`.codex/hooks.json` の PreToolUse フックが、上のうち機械的に判定できるものを実際にブロックする
（中身は `.codex/hooks/guard_codex.py` → `.claude/hooks/guard.py`。Claude Code とルールを共有）。

- **ブロックされたら迂回しない。** 何をなぜやりたいのかと、4点の確認結果を報告して指示を仰ぐ。
- Codex には「ユーザーに確認する」プロンプトがないため、確認が必要な操作は**禁止**として扱われる。
- セッション開始時にガードレールの状態が表示される。「警告」が出たら作業を始めずユーザーに報告する。

## 検証
C# やアセットを変更したら、`uloop-cli` でコンパイルと EditMode テストを通すのが本来の手順
（`.claude/skills/treasure-haul-verify/`）。Codex から Unity を動かせない場合は、
**何を検証できていないかを明記して報告する**。通っていないものを「完了」と書かない。
