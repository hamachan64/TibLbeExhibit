---
name: dev-loop
description: 「やりたいこと」を受け取り、壁打ち・承認 → Sonnet実装 → Opusレビュー → 検証 → コミット → PR発行までを一気通貫で回すオーケストレーション手順。機能追加・修正の依頼を受けたとき、および /dev-loop と言われたときに使用する。
---

# dev-loop: AI駆動開発ループ

あなた（Opus）は**オーケストレーター**です。自分で実装コードを書かず、仕様を決め、Sonnetに実装させ、レビューし、検証し、PRを出すまでを回します。

**このループの停止点は Phase 0 の承認ただ1回だけ**です。承認後は Phase 5 の完了報告まで、ユーザーに確認を求めず走り切ってください。

---

## Phase 0 — 壁打ちと承認【唯一の停止点】

1. **現状把握**（並列で実行）: `git status` / `git branch --show-current` / 関連ファイルの `Grep`・`Read`。
   既存実装を読まずに仕様を書かないこと。
2. **企画との突き合わせ**: `TREASURE_HAUL_PLAN.md` の該当箇所を読み、ゲームルールと矛盾しないか確認する。企画書に未記載のルールを前提にする場合は、壁打ちで確認してから企画書にも追記する。
3. **壁打ち**: 曖昧さがあれば `AskUserQuestion` で詰める。聞くべきは「答えによって作るものが変わる」ことだけ。最大2回まで。典型的な論点:
   - 受け入れ基準（何が動けば完成か）
   - スコープの境界（今回やらないこと）
   - 既存挙動を変えてよいか
   - 実機（PICO）確認が必要な部分をどう扱うか
4. **仕様書を書く**: `.claude/loops/<yyyymmdd>-<kebab-slug>.md` に下記テンプレで作成する。
5. **承認を取る**: 仕様書の要約（目的 / 受け入れ基準 / 変更対象ファイル / やらないこと / ブランチ名）を提示し、`ExitPlanMode` で承認を得る。

### 仕様書テンプレート

```markdown
# <タスク名>

## 目的
<なぜやるか。ユーザー体験としてどう変わるか>

## 受け入れ基準
- [ ] <検証可能な条件。「〜が動く」ではなく「〜したとき〜になる」>

## 変更対象
- <ファイルパス>: <何をするか>

## やらないこと（スコープ外）
- <明示的に除外する範囲>

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、.unity/.prefab/.asset は uloop経由で編集
- <このタスク固有の制約>

## 検証方法
- EditModeテスト: <追加するテストと保証内容>
- PlayMode/目視: <確認手順。実機必須なら明記>

## 実機確認が必要な項目
- <PICO実機でしか確認できないこと。無ければ「なし」>

## 外部の素材・サービス（権利・費用・セキュリティ）
- <使う外部の物と、.claude/rules/license_cost_security.md の4点の確認結果。無ければ「なし」>
```

---

## Phase 1 — ブランチ準備【承認後・無停止】

- Treasure Haul の統合ブランチは `repo/develop`。現在のブランチが `repo/develop` / `develop` / `master` なら、`repo/develop` から `repo/feature-<slug>` を切る。
  既に `repo/feature-*` ブランチ上で関連作業中ならそのまま使う。
- `develop` / `master` は GIPoker（展示のフォールバック）の系統。Treasure Haul の作業をこちらに混ぜない。
- 無関係な未コミット変更があれば `git stash` せず、**そのまま残して触らない**（ユーザーの作業の可能性がある）。コミット時に対象ファイルだけを `git add` する。

## Phase 2 — 実装をSonnetに委譲

`Agent` ツールで `unity-implementer`（Sonnet）を **`run_in_background: false`** で起動する。プロンプトには必ず:

- 仕様書の**絶対パス**（「まず読め」と明示）
- 触ってよいファイルの範囲
- 「treasure-haul-verify の手順でコンパイル・テストを通してから報告すること」
- 「git 操作はするな」

タスクが独立した2つ以上の機能に分かれる場合でも、**Unityエディタは1インスタンスしかない**ため uloop 検証が直列化する。原則1エージェントずつ順に回すこと。

## Phase 3 — Opusレビュー

実装エージェントの完了報告を**鵜呑みにしない**。以下を自分で確認する:

1. `git diff` で**差分を全部読む**（報告と実際の差分が食い違うことがある）。
2. 仕様書の受け入れ基準を1項目ずつ突き合わせる。
3. 規約違反を機械的に検査:
   ```
   git diff -U0 | grep -nE 'StartCoroutine|IEnumerator|UnityEvent|event Action|yield return'
   ```
   ヒットしたら差し戻し。
4. `code-reviewer` エージェントを起動して独立したレビューを取る。
5. **権利・費用・セキュリティ**: `git diff --stat` で `Packages/manifest.json` の変更・`Assets/` への外部ファイルの追加・外部 URL/API へのアクセスを洗い出し、`THIRD_PARTY_NOTICES.md` に「確認済み」で載っているか突き合わせる。載っていなければ差し戻す（`.claude/rules/license_cost_security.md`）。
6. `treasure-haul-verify` スキルで**自分でも**コンパイル + EditModeテストを実行する。ゲームプレイに関わる変更なら PlayMode 確認とスクリーンショットまで行う。

### 差し戻し判断

- **差し戻す**: 受け入れ基準の未達、規約違反、コンパイルエラー、テスト失敗、明らかなバグ（購読解除漏れ・null参照・ルール不整合）
- **自分で直す**: typo、命名、コメント、1〜2行で済む軽微な修正（往復コストの方が高い）
- 差し戻しは `SendMessage` で同じ実装エージェントに指摘を返す（コンテキストが残る）。**最大3往復**。3往復で受け入れ基準を満たせない場合は Phase 5 に進まず、状況をユーザーに報告して停止する。

## Phase 4 — コミットとPR発行

検証が全て緑になってから:

1. `git add` は**今回の変更ファイルのみ**を明示指定（`git add -A` は使わない）。
2. コミットメッセージは Conventional Commits + 日本語本文（既存履歴に合わせる）:
   `feat(treasure-haul): お宝の破損時に価値表示を減算する` 形式。
   末尾にはシステムが指定する `Co-Authored-By:` 行を付ける。
3. `git push -u origin repo/feature-<slug>`
4. `gh pr create --base repo/develop` でPRを作成。本文には:
   - 目的（仕様書の「目的」）
   - 変更点の要約
   - 検証結果（コンパイル / テスト / PlayMode所見）
   - **実機確認が必要な項目**（レビュアーが実機で見るべきこと）
   - 対応する Issue の `Closes #<番号>`
   - 末尾: `🤖 Generated with [Claude Code](https://claude.com/claude-code)`
5. **マージはしない。** `gh pr merge` は deny 済み。
6. `TODO.md` の該当行を「👀 PRレビュー中」にして PR 番号を書き、「現在地」を更新して同じブランチにコミット・push する（コミットは PR 作成の前に含めてもよい）。
7. ボード（https://github.com/users/hamachan64/projects/2 、project number 2）の該当 Issue の Status も「👀 PRレビュー中」にする。着手時（Phase 1）には「🔨 作業中」にする。
   ```
   gh project item-list 2 --owner hamachan64 --format json   # Issue 番号から item id を引く
   gh project field-list 2 --owner hamachan64 --format json  # Status の field id と option id を引く
   gh project item-edit --project-id <project id> --id <item id> --field-id <Status field id> --single-select-option-id <option id>
   ```

## Phase 5 — 完了報告

- PRのURL
- 受け入れ基準の達成状況（チェックリスト形式）
- 検証結果の要約
- **未達・積み残し・実機確認が必要な項目**（あれば必ず明示）
- 学びがあれば `.claude/agent-memory/` の該当ファイルに追記

---

## 途中でユーザーに戻ってよい例外条件

無停止が原則だが、以下は例外として作業を止めて報告する:

- ガードレール（`.claude/hooks/guard.py` / deny ルール）にブロックされ、それが**正当な操作だった**とき
  → 勝手に迂回路を探さない。何をなぜやりたいのかを報告して指示を仰ぐ。
- 仕様書に書いていない設計判断が必要になり、どちらを選ぶかで成果物が大きく変わるとき
- 実装3往復で受け入れ基準を満たせないとき
- 既存機能を壊さずに実装する方法が無いと判明したとき

**やってはいけない迂回**: deny/フックに阻まれたからといって、`python3` で同じ破壊的操作を書く、`git -C` で回避する、ファイルを別名で書いてから移す、などの抜け道を試すこと。ガードレールは仕様であり障害物ではない。
