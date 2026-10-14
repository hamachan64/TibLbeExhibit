# ClaudeCode向け プロジェクト概要

## プロジェクトの目的と性質
本プロジェクト（TibLbeExhibit）は、**ロケーションベースエンターテインメント（LBE）**を想定した、展示・体験用のUnity MR（Mixed Reality）アプリケーションです。
STYLYプラットフォームの技術（`styly-netsync`等）を活用し、複数人での体験を同期・管理します。

このブランチ系統（`repo/develop`）では、Inter BEE 2026 出展に向けた新作 **Treasure Haul（仮）** を開発しています。
「壊れやすいお宝を、仲間とドタバタしながら運び出す」協力型MR体験です。

## 主要アーキテクチャ
- **Unityバージョン**: 6000.3.10f1
- **レンダリングパイプライン**: URP (Universal Render Pipeline)
- **ターゲットデバイス**: 主にPICO（OpenXR / PXR_Enterprise）、およびMeta Quest等のパススルー対応デバイス
- **コアメカニクス**: コントローラー非使用。完全な**ハンドトラッキング**専用、および**フルカラーパススルー（MR）**前提。

## コードの区分（アセンブリ）
| アセンブリ | 場所 | 役割 |
| :--- | :--- | :--- |
| `TibLbeExhibit.Core` | `Assets/_Content/Scripts/`（`GIPoker/` を除く） | 複数コンテンツで使う共通基盤（通信・セッション進行・手の判定・リーダーボード等） |
| `TibLbeExhibit.TreasureHaul` | `Assets/_Content/TreasureHaul/` | 新作のゲーム固有コード。**Core のみ参照可** |
| `TibLbeExhibit.GIPoker` | `Assets/_Content/Scripts/GIPoker/` | 前作（具現化ポーカー）。新作からは参照しない。移行時に置いていく |

将来、hacha さんが配布する Inter BEE 用プロジェクトへ移行する予定。その際は Core と TreasureHaul だけを持っていく。

## シーン構成と役割
- `TreasureHaulEnvironment.unity`: 共有空間（MR用の環境光、Shadow Catcher、境界、スコアモニター等）
- `TreasureHaulUserClient.unity`: 体験者用XR Rig、ハンドインタラクション、`GameSession`
- `TreasureHaulOperatorClient.unity`: 運営者用UI、開始トリガー、スコア・リーダーボード管理
- ビルドプロファイル: `TreasureHaulUserClient` / `TreasureHaulOperatorClient(mac)`（`Assets/Settings/Build Profiles/`）

## ClaudeCode への指示（絶対ルール）
- 詳細は必ず以下のファイルを参照すること。
- **開発・技術ルール全般**: @DEVELOPMENT.md を参照。特に UniTask / R3 の使用について遵守すること。
- **ゲーム企画・実装要件**: @TREASURE_HAUL_PLAN.md を参照。未確定の項目を推測で埋めず、決める必要があればユーザーに確認すること。
- **特定タスクルール**: `.claude/rules/` 以下のルールファイルを適宜参照すること。
- **過去の学習内容（記憶）**: LBE・マルチプレイ・ハンドトラッキング関連タスクの着手前に `.claude/agent-memory/` 内の関連ドキュメントを確認し、新たにハマった点や回避策を発見したら同フォルダに追記すること。

## 開発ループ（機能追加・修正の進め方）
- 機能追加・バグ修正の依頼を受けたら、**まず `dev-loop` スキルを起動する**。壁打ち→承認→Sonnetによる実装→レビュー→検証→コミット→PR発行を1本のループとして回す。
- ブランチは `repo/develop` から `repo/feature-<slug>` を切り、PRの向き先は `repo/develop`。`develop` / `master` は前作（展示のフォールバック）の系統なので混ぜない。
- 承認はループの最初の1回だけ。以降はPR発行まで確認を求めずに進める（例外条件は `.claude/skills/dev-loop/SKILL.md` に定義）。
- ガードレール（`.claude/settings.json` の deny/allow と `.claude/hooks/guard.py`）にブロックされた操作は、**迂回してはならない**。何をなぜやりたいのかを報告して指示を仰ぐこと。
- PRのマージは常に人間が行う。
