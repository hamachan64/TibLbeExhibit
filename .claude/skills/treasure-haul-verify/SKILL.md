---
name: treasure-haul-verify
description: Treasure Haul（本プロジェクト）のC#・アセット変更後の標準検証手順。コンパイル→EditModeテスト→必要に応じてPlayMode・スクリーンショット・ログ確認までを一括で行う。C#編集後や「動作確認して」と言われたときに使用する。
---

# Treasure Haul 標準検証フロー

C#やアセットを変更したら、以下の順で検証する。途中で失敗したらそこで止めて原因を修正し、最初からやり直す。

## 1. コンソールのクリア
`uloop-clear-console` を実行し、過去のログが今回の結果に混ざらないようにする。

## 2. コンパイル
`uloop-compile` を実行。
- **エラー0件**であることを確認する。警告は件数と内容を報告する（修正は必須ではない）。
- `Assets/` 配下にファイルを新規作成した直後の場合、コンパイル前に `uloop-execute-dynamic-code` で `AssetDatabase.Refresh()` を実行して `.meta` を生成させる。

## 3. EditModeテスト
`uloop-run-tests` で EditMode テストを実行する。
- 純ロジック（スコア計算・お宝の価値計算など）を変更・追加した場合は、対応するテストも追加すること。
- **全件成功を基準にする**（失敗が1件でもあれば止めて直す）。
  前作（GIPoker）のテストは、`repo/develop` では保守しないため 2026-10-18 に削除した（前作は `develop` / `master` 系統で扱う）。

## 4. PlayMode確認（ゲームプレイに関わる変更のみ）
1. アクティブなビルドプロファイルが `TreasureHaulUserClient` であることを確認する
   （新シーンの追加ロードはプロファイルのシーン一覧に依存する）。
2. `Assets/_Content/TreasureHaul/Scenes/TreasureHaulUserClient.unity` を開き、`uloop-control-play-mode` で再生開始。
   `GameSession` はエディタでは自動開始する。
3. 手の操作の擬似発火: 実機なしでは、`HandBone` レイヤーのコライダー付きオブジェクトを
   `uloop-execute-dynamic-code` で対象に接触・移動させることで、触れる・掴む・運ぶを再現する。
4. `uloop-screenshot` で Game View を撮影し、見た目を確認する。
   パススルー前提のためカメラ背景は透明な黒。何も映らない＝不具合とは限らないので、対象物がカメラの視野内にあるかも確認する。
5. `uloop-get-logs` でエラー・例外がないことを確認する。
6. 確認後は必ず再生を停止する。

## 5. 報告
- コンパイル結果（エラー/警告数）、テスト結果（成功/失敗数）、PlayModeで確認した内容とスクリーンショットの所見を報告する。
- 失敗を修正した場合は、何をどう直したかを含める。

## 補足
- 実機（PICO）でしか確認できない項目（ハンドトラッキング実挙動、パススルー描画、PXR_Enterprise API）は「実機確認が必要」と明示して報告する。
- マルチプレイヤー同期の検証は ParrelSync でのクローンエディタ起動が必要。`styly-netsync-helper` スキルを参照する。
