# ClaudeCode向け プロジェクト概要

## プロジェクトの目的と性質
本プロジェクト（TibLbeExhibit）は、**ロケーションベースエンターテインメント（LBE）**を想定した、展示・体験用のUnity MR（Mixed Reality）アプリケーションです。
STYLYプラットフォームの技術（`styly-netsync`等）を活用し、複数人での体験を同期・管理します。

## 主要アーキテクチャ
- **Unityバージョン**: 6000.3.10f1
- **レンダリングパイプライン**: URP (Universal Render Pipeline)
- **ターゲットデバイス**: 主にPICO（OpenXR / PXR_Enterprise）、およびMeta Quest等のパススルー対応デバイス
- **コアメカニクス**: コントローラー非使用。完全な**ハンドトラッキング**専用、および**フルカラーパススルー（MR）**前提。

## シーン構成と役割
- `Environment.unity`: 共有空間（MR用の環境光、Shadow Catcher、空間アンカーなどの透明な基準ベース）
- `UserClient.unity`: 体験者用XR Rig、バインダーUI、ハンドインタラクション
- `OperatorClient.unity`: 運営者用UI、タイマー・状態管理

## ClaudeCode への指示（絶対ルール）
- 詳細は必ず以下のファイルを参照すること。
- **開発・技術ルール全般**: @DEVELOPMENT.md を参照。特に UniTask / R3 の使用について遵守すること。
- **ゲーム企画・実装要件**: @GI_POKER_PLAN.md を参照。ポーカーの役やハンドトラッキングでの「ゲイン」実装について遵守すること。
- **特定タスクルール**: `.claude/rules/` 以下のルールファイルを適宜参照すること。
