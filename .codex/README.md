# Codex 用のガードレール

Codex（OpenAI Codex CLI / VS Code 拡張）でこのリポジトリを触るときの安全弁。
**Claude Code の `.claude/hooks/guard.py` とルールを共有**し、二重管理しない。

```
Codex のツール呼び出し
  └─ PreToolUse フック
       └─ .codex/hooks/guard_codex.py   ← Codex のツール名・入力形を正規化
            └─ .claude/hooks/guard.py   ← 判定ルール本体（Claude Code と共通）
```

## 構成

| 置き場所 | 内容 | 役割 |
| :--- | :--- | :--- |
| `~/.codex/config.toml` | `[mcp_servers.blender]` の `enabled_tools` | 危ないツールを**そもそもモデルに見せない**許可リスト。フックに依存しない一番硬い歯止め |
| `~/.codex/hooks.json` | SessionStart / PreToolUse | **実際に効いている設定**（マシンごと・リポジトリ外） |
| `.codex/hooks.json` | 同じ内容（相対パス） | 原本。リポジトリで版管理する。**Codex がこのフォルダを信頼するまで読まれない**（下記） |
| `.codex/hooks/guard_codex.py` | Codex 用アダプタ | ツール名・`tool_input` を Claude Code 形に正規化して guard.py に渡す |
| `.codex/hooks/session_start.py` | セッション開始時の確認役 | 構成ファイルの欠けを検出し、絶対ルールを文脈に入れる |
| `.codex/hooks/selftest.py` | 自己テスト | ガードを直したら必ず通す |
| `AGENTS.md`（リポジトリ直下） | Codex 向けの指示 | Codex は `CLAUDE.md` を読まないため別に用意している |

### なぜ2か所にあるか
Codex のプロジェクト層（`<repo>/.codex/`）の設定は、**そのフォルダを Codex で「信頼する」まで読み込まれない**
（`~/.codex/config.toml` の `[projects."<パス>"] trust_level = "trusted"`）。
実測で、信頼していない状態ではリポジトリ側の `.codex/hooks.json` は読まれなかった。
そのため、確実に効くグローバル側（`~/.codex/hooks.json`）を正とし、リポジトリ側は版管理用の原本として置いている。

- グローバル側は**絶対パス**でこのリポジトリのスクリプトを指している。
  **リポジトリを移動・改名したら `~/.codex/hooks.json` のパスを直す必要がある。**
- プロジェクト層を有効にしたい場合は、Codex の TUI でこのフォルダを信頼するか、
  `~/.codex/config.toml` に `trust_level` を書く。ただし信頼するとコマンド実行の承認が緩くなる点に注意。

## 何が止まるか
`uv run --no-project .codex/hooks/selftest.py` で実際の挙動を確認できる（18 ケース）。

- 履歴を壊す Git 操作（force push、`reset --hard`、`clean -fd`、履歴書き換え）
- `master` / `main` / `develop` への直接 push、`gh pr merge`
- `.unity` / `.prefab` / `.asset` / `.meta` のテキスト直接編集、`Library/` 等の生成物への書き込み
- `Assets/` 等を対象にした `rm`、Unity 管理下ファイルの削除
- API キー・トークン・秘密鍵らしき文字列の書き込み
- 従量課金 API（OpenAI / ElevenLabs / 3D 生成サービス等）へのアクセス
- Blender MCP の有料 AI 3D 生成（`generate_3d` / Hyper3D / Hunyuan3D / Tripo）
- Blender MCP の外部素材取り込み（`search_assets` / `import_asset`。素材ごとに権利が違うため人が判断する）
- Blender を終了させる操作

Claude Code と違い **Codex の PreToolUse には「ユーザーに確認する」がない**ため、
Claude Code では確認を求めていた操作（従量課金 API 等）は Codex ではブロックになる。

## 注意
- フックが**実行できない**とき（`uv` が無い、スクリプトが見つからない等）、PreToolUse は
  ブロックではなく**素通り**になる。`session_start.py` がその検出役なので、
  セッション開始時に「警告」が出たら作業を始めないこと。
- 実行には `uv`（`uv run --no-project` が uv 管理の Python を使う）が必要。
  このマシンの `python` / `python3` は Microsoft Store のスタブで**動かない**ため、
  `python3` 直叩きのフック設定にしてはいけない。

## 有効化に必要な操作（2026-10-18 実測）

### 1. フックは「信頼」するまで実行されない
Codex のフックには信頼（trust）の仕組みがあり、**承認するまで一切実行されない**
（`codex` に `--dangerously-bypass-hook-trust` というフラグが存在することがその証拠）。
非対話の `codex exec` では警告も出ずに黙って無効のままなので、気づきにくい。

対話セッション（VS Code 拡張 / TUI）を一度起動すると次の選択肢が出る:

- **「Trust all and continue」を選ぶ** ← これでフックが有効になる
- 「Continue without trusting (hooks won't run)」を選ぶと**ガードレールは動かない**

信頼は `sha256` のハッシュで記録され、`hooks.json` を変更すると「1 hook is new or changed.」として
再度確認される。**ガードを直したら、もう一度信頼し直す必要がある**。

なお `~/.codex/config.toml` の `enabled_tools`（有料 3D 生成などの除外）は
フックの信頼とは無関係に常に効いている。Codex 側から見えるツールが
`get_addon_status` / `get_scene_info` / `execute_blender_code` / `look` の4つだけであることを実測で確認済み。

### 2. `[windows] sandbox` は `elevated` にしない
`elevated` にすると `CreateProcessWithLogonW failed: 267` でシェルが一切実行できない
（サンドボックス用の別ユーザーから `C:\Users\hama8\...` が見えないため）。
`unelevated` では正常に動作する。

### 3. MCP 呼び出しは既定で都度承認を求める
`codex exec`（`approval: never`）では `MCP tool call requires approval` で弾かれる。
対話セッションなら都度プロンプトが出るので作業はできるが、モデリングのように
`execute_blender_code` を何百回も呼ぶ用途では現実的でない。
都度承認をやめるなら `~/.codex/config.toml` に次を足す（**どのツールを自動承認するかは人が決めること**）:

```toml
[mcp_servers.blender.tools.execute_blender_code]
approval_mode = "approve"   # auto / prompt / writes / approve
```

`enabled_tools` で4ツールに絞ってあり、`execute_blender_code` の中身は PreToolUse フックが
検査するため、この4つに限れば自動承認でも歯止めは残る。
