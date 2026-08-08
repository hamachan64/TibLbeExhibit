# 全テキストフォントをDela_Gothic_Oneに統一

## 目的
現在、UI・ゲーム内テキストは主に `Noto_Sans_JP` を使用している。展示物としての視認性・世界観統一のため、全テキストの表示フォントを `Dela_Gothic_One`（`Assets/_Content/Fonts/Dela_Gothic_One/DelaGothicOne-Regular SDF.asset`, guid: `1633e64c790e64dbe8a7c693e2bdb8f5`）に統一する。

## 事前調査で判明した実態
- `UserClient.unity` の GameObject `GIPoker`（`GIPokerBootstrap` コンポーネント）の `japaneseFont` フィールドが `GIPokerAssets.JapaneseFont` として実行時に共有され、`TitleView` / `BinderView` / `TutorialView` / `ResultView` / `BonusTimeAnnouncementView` / `CardVisualFactory` / `PlayerNameTagView` / `DiceResultLabel` / `CardLifetimeLabel` が全てこの共有フォントを参照している。**この1フィールドを差し替えるだけでGIPokerゲーム内テキストのほぼ全てに反映される。**
- `OperatorClient.unity` の `Canvas` 配下には、Inspector上で個別に `Font Asset` が設定された `TextMeshProUGUI` が7個存在し、全て `Noto_Sans_JP` 参照（guid: `ab041cb34e0c84558b8359ca7a47eff9`）。対象GameObject: `RemainingTimeText`, `LiveTop3Text`, `DailyTop3Text`, `LiveTop3Label`, `DailyTop3Label`, および無名の `Text (TMP)` が2個（説明文ラベル）。
- `Environment.unity` の `StageScoreMonitorView.japaneseFont` は**既に作業ツリー上で未コミットのまま `Dela_Gothic_One` に変更済み**（本タスクと無関係な他の未コミット変更と同一ファイルに混在）。このタスクでは触らない・コミットもしない。
- プロジェクト全体で他に `Noto_Sans_JP` の guid を参照しているシーン/プレハブ/アセットは上記2ファイル（`OperatorClient.unity`, `UserClient.unity`）のみ（grep確認済み）。

## 受け入れ基準
- [ ] `UserClient.unity` の `GIPoker` GameObject上、`GIPokerBootstrap.japaneseFont` が `Dela_Gothic_One` (guid: `1633e64c790e64dbe8a7c693e2bdb8f5`) を参照している。
- [ ] `OperatorClient.unity` 上の7個の `TextMeshProUGUI`（`RemainingTimeText`, `LiveTop3Text`, `DailyTop3Text`, `LiveTop3Label`, `DailyTop3Label`, 無名`Text (TMP)`×2）が全て `Font Asset` = `Dela_Gothic_One` になっている。
- [ ] コンパイルエラーなし。
- [ ] 変更はシーンYAMLの直接編集ではなく、`uloop-execute-dynamic-code` 経由で `SerializedObject` を用いて行う（`.claude/rules/unity_asset_rules.md` 準拠）。
- [ ] `Environment.unity` には触れない（既存の未コミット差分をそのまま維持、追加の変更もしない）。

## 変更対象
- `Assets/_Content/Scenes/UserClient.unity`: `GIPoker` GameObjectの `GIPokerBootstrap` コンポーネント、`japaneseFont` フィールドをDela_Gothic_Oneに差し替え。
- `Assets/_Content/Scenes/OperatorClient.unity`: 上記7個のTMPテキストの `Font Asset` をDela_Gothic_Oneに差し替え。

## やらないこと（スコープ外）
- フォントサイズ・位置（RectTransform）の変更は行わない（今回は「フォント統一」のみの依頼）。
- `Environment.unity` の編集・コミット。
- コード側（`CreateText(...)` 呼び出しのfontSize等）の変更。
- 英字専用フォント `Inter` を使っている箇所（もしあれば）の扱い — 調査の結果、Interを直接参照している箇所は無かったため対象なし。

## 制約
- `.unity` はテキストエディタ的な直接編集禁止。`uloop-execute-dynamic-code` 経由で `SerializedObject.FindProperty("japaneseFont").objectReferenceValue` 等を使い、変更後は該当シーンを保存する。
- 既存の無関係な未コミット変更（`CardDefinitions/*.asset`, `ItemCatalog.asset`, 各種`.mat`, `Item_c_bubble_horse.prefab`, `Environment.unity`, 未追跡の `Polytope Studio/`）には一切触れない。コミット時も対象ファイル（`UserClient.unity`, `OperatorClient.unity`）のみを `git add` する。

## 検証方法
- EditModeテスト: フォント参照はデータ変更のみのため新規テスト追加は不要。既存テストが通ることを確認。
- PlayMode/目視: UserClient側はコード生成UIのためPlayModeでGIPokerのタイトル/バインダー/リザルト等いずれかの画面をスクリーンショットし、Dela_Gothic_Oneが適用されていることを確認。OperatorClient側はシーンをロードしてCanvas内テキストのInspectorのFont Asset欄を確認（スクリーンショットまたはSerializedObject読み取りで確認）。

## 実機確認が必要な項目
- なし（フォント参照の差し替えのみで、PICO実機固有の挙動には影響しない）。
