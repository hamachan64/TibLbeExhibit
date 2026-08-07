# カード／アイテム情報の一元管理（CardRankConfig 導入）

## 目的
現状、カード（アイテム）の情報が複数箇所に散らばっており、修正時に「どこを直せば反映されるか」が分かりにくい状態になっている。

- `CardDefinition`（ScriptableObject、カード1枚＝1アセット）はすでに存在するが、プレハブ・出現数は `ItemCatalog.Entry` 側にしかなく、`CardDefinition` を見るだけでは「このカードが実際どのプレハブで何個出るか」が分からない。
- ランクごとの単体得点（`PokerHandEvaluator.BasePoints`）、通常時／ボーナスタイムの出現重み（`ItemSpawnManager` のインスペクタ初期値）、宝籤の抽選重み（`CardEffectService.lotteryRankWeights`）、ランクのテーマカラー（`CardVisualFactory.GetRankColor` の switch 式）が、それぞれ別のC#ファイルにハードコードされている。
- SSランクのプレハブは `CardEffectService.ssItemPrefab` という独立フィールドでも別途参照されており、カタログとは別に管理されている（実質的な二重管理）。

これらを2つの「置き場」に整理する。
1. **`CardDefinition`**（カード単位）: 識別情報に加え、プレハブ・初期出現数・出現上限も持たせる。
2. **`CardRankConfig`**（ランク単位、新設・単一アセット）: D/C/B/A/S/SS 各ランクの単体得点・出現重み（通常／ボーナス）・宝籤抽選重み・テーマカラーを1つのリストにまとめる。

これにより「このカードの情報を直したい」ときはまず `CardDefinition` を、「このランクの数値バランスを直したい」ときは `CardRankConfig` を見れば良い状態にする。

## 受け入れ基準
- [ ] `CardDefinition` アセットを開くと、識別情報（ID/表示名/ランク/効果/説明）に加えて、対応するプレハブ・初期出現数・出現上限が同じインスペクタで確認・編集できる。
- [ ] `CardRankConfig` アセット（1つ）を開くと、D/C/B/A/S/SS 各ランクの単体得点・通常時出現重み・ボーナスタイム出現重み・宝籤抽選重み・テーマカラーが1つのリストで確認・編集できる。
- [ ] `PokerHandEvaluator.BasePoints` のハードコード辞書、`ItemSpawnManager` の `normalRankWeights`/`bonusRankWeights` インスペクタ初期値、`CardEffectService.lotteryRankWeights` の初期値、`CardVisualFactory.GetRankColor` の switch 式が撤去され、いずれも `CardRankConfig`（`GIPokerAssets.RankConfig` 経由）を参照する形になる（未設定時は現状と同じ値へのフォールバックを残し、既存の「null許容」方針を維持する）。
- [ ] `CardEffectService.ssItemPrefab` フィールドが撤去され、SSランクの `CardDefinition.Prefab` 経由で解決される。
- [ ] `ItemCatalog.Entry` が `CardDefinition` 参照ベースに再設計され、既存22件の `CardDefinition` アセットに `prefab`/`initialCount`/`maxCount` が移行される。移行前後で `ItemCatalog.asset` の `entries` の並び順・値が完全一致する（決定論的スポーンの回帰がないこと）。
- [ ] 既存の EditMode テスト（`PokerHandEvaluatorTests` / `CardBinderTests` / `CardEffectServiceTests` / `SpawnScheduleBuilderTests` 等）が全て緑のまま。
- [ ] 新規 EditMode テストを追加: `CardRankConfig` のランク別エントリ取得、`PokerHandEvaluator` の新オーバーロード（basePoints注入）、`ItemCatalog` の `CardDefinition` ベース参照解決（`FindPrefabByCardId` 等）。

## 変更対象
- 新規 `Assets/_Content/Scripts/GIPoker/CardRankConfig.cs`: ランク単位の設定を持つ ScriptableObject。
- 新規 `Assets/_Content/GIPoker/CardRankConfig.asset`: 現行のハードコード値（得点・出現重み・宝籤重み・テーマカラー）を初期値として作成（uloop経由）。
- `CardDefinition.cs`: `prefab`（GameObject）/`initialCount`（int）/`maxCount`（int）フィールドと公開プロパティを追加。
- `ItemCatalog.cs`: `Entry` を `CardDefinition` 参照ベースに再設計（`prefab`/`initialCount`/`maxCount` は `definition` からの委譲プロパティに変更）。`FindPrefabByCardId` を簡素化。
- `Assets/_Content/GIPoker/ItemCatalog.asset` と 22件の `CardDefinition` アセット: 現行の `ItemCatalog.asset` の Entry データ（prefab/initialCount/maxCount）を対応する `CardDefinition` へ移行し、`ItemCatalog.asset` 側は `definition` 参照のみに更新（uloop-execute-dynamic-code 経由、並び順維持）。
- `GIPokerAssets.cs`: `public static CardRankConfig RankConfig;` を追加。
- `GIPokerBootstrap.cs`: `[SerializeField] private CardRankConfig rankConfig;` を追加し `Awake()` で流し込む。
- `PokerHandEvaluator.cs`: `Evaluate(ranks, basePoints = null)` のオプション引数化。未指定時は既存のデフォルト値にフォールバック（既存呼び出し・既存テストは無改修で動作させる）。
- `CardBinder.cs`: `PokerHandEvaluator.Evaluate` 呼び出しに `GIPokerAssets.RankConfig?.BasePoints` を渡すよう変更。
- `ItemSpawnManager.cs`: `RankSpawnWeight` クラス・`normalRankWeights`/`bonusRankWeights` フィールドを撤去し、`GIPokerAssets.RankConfig` から重みを取得（未設定時は現行値にフォールバック）。
- `CardEffectService.cs`: `lotteryRankWeights`/`ssItemPrefab` を撤去し、`GIPokerAssets.RankConfig` とカタログの SS `CardDefinition.Prefab` を参照するよう変更。
- `CardVisualFactory.cs`: `GetRankColor` を `GIPokerAssets.RankConfig` 参照に変更（未設定時は現行switchの値にフォールバック）。
- `PokerHandEvaluatorTests.cs`: 新オーバーロードのテストケースを追加（既存ケースは変更不要な想定）。

## やらないこと（スコープ外）
- 役ボーナス定数（ストレート+200／指定コンボ+150／同ランク3枚+50）の `CardRankConfig` への統合は対象外。ランク単位ではなく役（コンボ）単位の定義であり、今回の「カード・ランク情報の集約」とは別軸のため。
- `CardEffectService.Execute` のカード効果本体（switch分岐によるロジック実装）のデータ駆動化は対象外。効果は挙動そのものであり、データ化するとかえって追いにくくなるリスクがあるため、今回は「参照情報の集約」に留める。
- `Assets/_Content/GIPoker/Materials` 配下のマテリアル自体の整理・命名変更・`CardDefinition` への直接参照追加は対象外。`CardDefinition.Prefab` を辿れば必要なマテリアルに到達できるため二重参照は持たせない。
- 新規カードの追加、既存カードのランク・得点・出現数などバランス値そのものの変更は対象外（今回は「置き場」を作り、現状の数値をそのまま移行するのみ）。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity`/`.prefab`/`.asset` は uloop 経由で編集（`.claude/rules/unity_asset_rules.md` 準拠）。
- マルチプレイ同期のための「決定論的スポーン」を壊さないこと。`ItemCatalog.entries` の並び順は移行前後で完全一致させる。
- 既存コンポーネントの null 許容方針（`GIPokerAssets` は「未設定でもロジックは動作する」）を踏襲し、`CardRankConfig` 未設定時のフォールバック値を必ず用意する。

## 検証方法
- EditModeテスト: 上記の新規・更新テストを含め、`gipoker-verify` スキルで全件緑を確認。
- PlayMode: `GIPokerBootstrap` 経由でセッションを開始し、通常出現／ボーナスタイム出現／宝籤／リスキーダイス大吉→SS出現が従来通り機能することを uloop-control-play-mode で確認（可能ならスクリーンショットで見た目回帰も確認）。
- 目視: `CardRankConfig.asset` と、代表的な `CardDefinition` アセット（例: SランクのブループラネットDefinition）を Unity Editor で開き、意図した情報が1画面で揃っていることを確認。

## 実機確認が必要な項目
- なし。今回はデータ配置のリファクタであり、見た目・数値挙動を変えない想定のため実機固有の確認は不要。ただしMR空間での見た目に万一差分が出た場合は実機確認を追加で行う。
