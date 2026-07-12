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

## TIB_20260429（会場スキャン）のオクルーダー化と空間アンカー不在（2026-07-12）
- TIB_20260429 に `ColorMask 0` の深度オンリーオクルーダーを適用すると、実機パススルーでは
  「現実の柱・壁の裏に回った仮想物を正しく隠す」ために機能するが、**Unityエディタ単体再生では
  色を一切描かないため背景色（グレー等）がそのまま透けて見える**。バグではなく仕様。
  エディタで元のテクスチャを見たい場合は `#if UNITY_EDITOR` プレビュー分岐の追加を検討する
  （本プロジェクトでは未実装、必要になったら追加）。
- **重要な未解決前提**: 本プロジェクトには「実機の現実空間とEnvironmentシーン（TIB_20260429含む）を
  位置合わせする空間アンカー/キャリブレーションの仕組みが存在しない**（CLAUDE.md には
  「空間アンカーなどの透明な基準ベース」という設計意図の記述はあるが未実装）。
  そのためオクルーダーが実機で正しく機能するかは未検証。PICOのパススルーがプラットフォーム側で
  深度オクルージョンを自動提供するかどうかも未確認。実機確認で位置ズレ・見た目の破綻が出た場合は
  TIB_20260429 のオクルーダーを `SetActive(false)` にする方針で合意済み（2026-07-12時点）。
  この判断はどちらの前提が真か実機で確認してから最終決定する。
- `Apply Occluder To TIB Mesh` のような「配下の全MeshRendererを機械的に対象とする」メニューは、
  会場ジオメトリではない配置基準マーカー（例: CenterPosition）も巻き込みうる。
  対象選定は名前パターンや専用タグで絞るか、適用後に非ジオメトリオブジェクトが混入していないか
  目視確認すること。

## TutorialView演出リッチ化とTMP新規漢字の回避策（2026-07-13 Issue #15）
- worktree内でUnityコンパイルができない状況でも「新しい漢字を追加しない」制約は守れる。
  やり方: `TitleView.cs` / `BinderView.cs` / `ResultView.cs` / `ResultGrade.cs` /
  変更前の `TutorialSequence.cs` など、既に実機/PlayModeで描画実績のある View から
  `grep -ohoP '"[^"]*[぀-ヿ㐀-鿿][^"]*"'` で日本語リテラルを洗い出し、そこに含まれる漢字だけで
  新しい文言を組み立てれば、TMPアトラスへの新規グリフ追加ゼロを机上で保証できる。
  PR本文には「使用した漢字はすべて既存UI由来（新規グリフなし）」と明記すると豆腐リスクの
  説明責任を果たせる。
- `Material.DOColor(Color, string/int propertyId, float duration)` や `Material.DOFade` /
  `Material.DOFloat` はこのプロジェクトの Demigiant/DOTween 同梱ソース（Modules配下の .cs）を
  grep しても定義が見つからない（コンパイル済み DOTween 本体DLL側にショートカット拡張が
  含まれているため）。実在確認は `Assets/Plugins/Demigiant/DOTween/DOTween.XML` を
  `grep -n "DOColor(UnityEngine.Material"` のように検索すると確実（既存コードの
  `StagePedestal.cs` / `BorderCircle.cs` が実例）。
- `TextMeshPro`（3D版）には `DOFade` 拡張が無い（UGUIのGraphic/Image限定）。
  代わりに `DOTween.To(() => tmp.alpha, a => tmp.alpha = a, target, duration)` を使う
  （`ResultView.FadeText` が既存パターン）。同様に色遷移も
  `DOTween.To(() => tmp.color, c => tmp.color = c, target, duration)` で代替できる
  （DOTween本体の `DOTween.To(DOGetter<Color>, DOSetter<Color>, Color, float)` オーバーロードは
  Modules非依存でコア機能として存在する）。
