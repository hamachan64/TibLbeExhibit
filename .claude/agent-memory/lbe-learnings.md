# LBE & PICO Enterprise 開発の学習メモ

（Claudeが自律的にここに追記していきます）

## 現在の初期知見
- 本プロジェクトは PICO (PXR_Enterprise) を使用しており、コントローラーではなくハンドトラッキング専用。
- UniTask と R3 をベースに非同期・イベント駆動を構築している。
- Unityメタファイルの破壊を防ぐための Hook (`protect.sh`) が稼働中。

## TMP日本語フォントの豆腐（□）問題（2026-07-10 解決済み）
- 症状: PlayModeで「体験」「見習」など一部の漢字だけ□で表示される（かなや頻出漢字は正常）。
- 原因: `Noto Sans JP SDF` は Dynamic モードだが **1024x1024 の単一アトラス＋pointSize 90 で約130グリフが上限**。
  マルチアトラス（isMultiAtlasTexturesEnabled）が無効だと満杯以降の新規グリフが追加できず豆腐になる。
- 対策（適用済み）: マルチアトラス有効化＋アトラス 2048x2048 化＋ `ClearFontAssetData(true)` で再構築。
  変更はエディタAPI（SerializedObject / uloop-execute-dynamic-code）経由で行うこと。
- 注意: UIに新しい漢字を追加したら PlayMode で一度表示して豆腐が出ないか確認する。
  実機（PICO）ビルドでは動的グリフ生成のためソースフォント（.ttf）が同梱される必要がある → 実機での表示確認は必須。

## ゲームフロー状態機械（2026-07-10 拡張）
- SessionState: WaitingForStart（タイトル表示）→ Tutorial（固定60秒）→ Playing（5分）→ Finished（リザルト）。
- チュートリアルが固定時間なのは ContentStart RPC が全クライアント同報のため、
  Playing 開始時刻（＝シード付きスポーン開始）を全員で揃える必要があるから。進行度で短縮してはいけない。
- 新しい状態を足す場合、購読側は「特定状態かどうか」だけで判定する（switchの網羅に依存しない）と壊れない。
- 練習アイテム等を CardNetworkSync に同期させたくない場合は Register() を呼ばなければよい（NetworkId=-1 のまま）。

## サブエージェント運用（2026-07-10）
- git worktree 内のエージェントは Unity コンパイル不可。API実在確認は「該当ファイルを開いてシグネチャ確認」を義務付けると
  統合時のコンパイルが一発で通る（今回3PR・+1892行でエラー0件）。
- SendMessage でエージェントを transcript 再開すると worktree を離れてメインツリーで作業することがある。
  再開時は作業ディレクトリとブランチを必ず確認すること。

## MRパススルー用シェーダー（2026-07-12 Issue #7）
- パススルー合成の premultiplied / straight 規約差に依存しないためには、
  「黒(0,0,0)＋alpha蓄積（Blend の第2指定 `One OneMinusSrcAlpha`）」の影、
  「加算＋alpha も加算（`One One, One One`）」の発光、のように framebuffer alpha を必ず書くのが安全。
- radial dissolve は侵蝕度 0 のとき中心（dist=0）でフロンティア=0 となり縁の発光バンドが点として残る。
  `clip(erosion - ε)` ＋ 微小域のフェード係数を必ず入れること。
- グローバル値（`Shader.SetGlobalFloat`）は `CBUFFER_START(UnityPerMaterial)` に入れてはいけない
  （入れると SRP Batcher がマテリアル値として扱いグローバル更新が反映されない）。
- depth-only オクルーダーは URP の `DepthOnly` パス（`ColorMask R` / `return positionCS.z`）も
  併設し、Depth Texture 要求時の整合を取る。ShadowCaster パスは持たせない（現実メッシュの二重影防止）。
