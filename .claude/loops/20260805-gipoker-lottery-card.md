# 新カード「宝籤（ロトリー）」の実装

## 目的
『HUNTER×HUNTER』グリードアイランド編モチーフの新規Bランクカード「宝籤（ロトリー）」を追加する。
ゲインすると宝箱（TreasureChest）が崩れ去り、代わりに別のアイテムカードがランダムに具現化する——という「開けてみるまで何が出るか分からない」ワクワク感を実装する。
併せて、既存の `CardEffectService` に「①（カードを1枚出現させる効果。未実装）」として残されていたリスキーダイス大吉の権利消費フック（`HasPendingDiceJackpot` / `ConsumeDiceJackpot`）を、この宝籤の効果で実際に消費する形にする。

## 前提・調査結果（実装者は着手前に必ず読むこと）
- `CardEffectType.DuplicateCard` は既存の別カード「ギャラリーフェイク」（`b_gallery_fake.asset`、バインダー内の手持ちカードを複製する効果）に使用済み。**宝籤には新しい enum 値 `Lottery` を追加すること**（`DuplicateCard` の流用・上書きは禁止）。
- `CardEffectService.cs` のコメント（47-45行目付近）に「①（カードを1枚出現させる効果。未実装）を使うと必ずSSが出る」という権利フラグの説明があり、`HasPendingDiceJackpot` / `ConsumeDiceJackpot()` は**この宝籤のために用意された未消費フック**である。宝籤のSS分岐はこれをそのまま消費する形で実装する。
- `GI_POKER_PLAN.md` 記載のランク別種類数（D:5種、C:3種、B:3種など）は実装側で既に上書きされている（現状 D:6種、C:6種、B:3種、A:2種、S:1種、SS:1種）。企画書のスコープ外の追加カードが既に多数存在するため、種類数の突き合わせは行わない。ただし「1分ルール」「ゲイン制限（一度ゲインしたオブジェクトは二度とカード化不可）」「出現上限（カード化限度枚数）」等のルールは遵守する。

## 受け入れ基準
- [ ] `CardTypes.cs` に `CardEffectType.Lottery` が追加されている
- [ ] `Assets/_Content/GIPoker/CardDefinitions/b_lottery.asset` が存在し、`rank=B`, `effectType=Lottery`, `displayName="宝籤（ロトリー）"` が設定されている
- [ ] `Assets/_Content/GIPoker/Prefabs/Item_b_lottery.prefab` が存在し、TreasureChestモデル（`Assets/_Content/Models/Items/TreasureChest/source/treasure_chest.fbx`）を使用したURPマテリアル（BaseColor/Metallic/Normal/Roughnessの4テクスチャを割当）・Collider(IsTrigger)・Rigidbody・`CardizableItemBase`（definition=b_lottery）を持つ
- [ ] `ItemCatalog.asset` に上記プレハブの新規エントリが追加されている（initialCount/maxCountは既存Bランクアイテム相当・目安1〜3）
- [ ] 宝籤をゲインすると `CardEffectService.Execute` の switch で `Lottery` ケースに入り、`ExecuteLottery` が実行される
- [ ] `HasPendingDiceJackpot == false` のとき: 再現されるカードのランクは重み付き抽選で決まり、**SSランクは絶対に選ばれない**。ランクが高いほど選ばれる重みが小さい（単調減少）。デフォルト重みの目安: D=40, C=24, B=16, A=12, S=8（合計100、SerializeFieldで調整可能にする）
- [ ] `HasPendingDiceJackpot == true` のとき: 宝籤の効果は必ずSSランクカードを出現させ、既存の `SpawnSsCardAt(position, announce:true)` を呼んだ上で `ConsumeDiceJackpot()` を呼ぶ（権利は以後trueになるまで再度消費できない）
- [ ] 再現候補のランク・アイテムは、その `ItemCatalog` エントリの出現上限（`maxCount`）に達していないもの限定で選ばれる（達しているランクは抽選対象から除外・重みを再配分する）。全ランクが上限到達済みで候補が1つもない場合は、カードの具現化は行わないが宝箱は削除される（不発は許容）
- [ ] ゲイン時の演出は次の順序で再生される: **①宝箱が開くアニメーション → ②新しいカードが出現 → ③宝箱が崩れて消える（VFX＋DOTweenの縮小演出、瞬間消滅ではない）**。カード出現と宝箱削除を同時に処理しない
- [ ] 上記すべての挙動が、宝籤をゲインした本人だけでなく **他クライアントにも同期される**（宝箱の開く演出・新カードの出現・宝箱の削除のすべて）。ParrelSyncの2クライアントで目視確認する
- [ ] `CardEffectService.ResolveLotteryRank`（または同等の純粋staticメソッド）に対するEditModeテストが追加され、**リスキーダイス使用時（jackpot=true）** と **未使用時（jackpot=false）** の両方の分岐をカバーしている

## 変更対象
- `Assets/_Content/Scripts/GIPoker/CardTypes.cs`: `CardEffectType.Lottery` を追加
- `Assets/_Content/GIPoker/CardDefinitions/b_lottery.asset`: 新規作成（rank=B, effectType=Lottery）
- `Assets/_Content/Models/Items/TreasureChest/`: 既存FBX・テクスチャからURPマテリアルを新規作成（未コミットの未追跡ファイル。`.meta` の生成・追従を確認すること）
- `Assets/_Content/GIPoker/Prefabs/Item_b_lottery.prefab`: 新規作成（既存 `Item_b_armor.prefab` 等を参考に、Collider/Rigidbody/CardizableItemBase構成を揃える）
- `Assets/_Content/GIPoker/ItemCatalog.asset`: 新規エントリ追加
- `Assets/_Content/Scripts/GIPoker/CardEffectService.cs`:
  - `ExecuteLottery(CardizableItemBase chest)` を実装し、`Execute` のswitchに `case CardEffectType.Lottery` を追加。内部は次の順で処理する非同期フロー（`UniTask`）にする:
    1. `await PlayChestOpenAnimation(chest, ct)` — 宝箱が開く演出。TreasureChest FBXに独立した蓋メッシュ/ボーンがあればDOTweenで開閉回転、無ければバウンド＋発光の高まりなど「開く」ことを感じさせる代替演出で良い（実装者が事前にFBXの階層を確認して判断する）
    2. ランクを解決し、`SpawnSsCardAt`（SS時）または `SpawnLotteryCardAt`（通常時）でカードを出現させる
    3. `await PlayChestCrumbleAndDestroy(chest, ct)` — 崩れて消えるVFX＋DOScale縮小演出を再生し、完了後に `Destroy(chest.gameObject)`
  - `PlayChestOpenAnimation` / `PlayChestCrumbleAndDestroy` は private だが、リモート側から呼べるよう `PlayChestOpenAndCrumbleSequence(CardizableItemBase chest)`（public UniTaskVoid、開く→崩れる→Destroyのみを行い、カード出現は含まない）も用意する
  - ランク抽選の核となる純粋staticメソッド（例: `ResolveLotteryRank(bool hasPendingDiceJackpot, IReadOnlyList<(CardRank rank, float weight)> availableWeights, float randomValue01)` → `CardRank?`）を追加し、EditModeテストから直接呼べるようにする（`ShouldStartRoll` の設計パターンを踏襲）
  - `SpawnLotteryCardAt(CardDefinition definition, Vector3 position, bool announce)` を追加（`SpawnSsCardAt` に準拠。announce時は新設の `lotterySpawned` Observableを発火）
  - ランク別重みをSerializeFieldで公開（デザイナー調整用）
  - **既存の `SpawnSsCardAt` / `HasPendingDiceJackpot` / `ConsumeDiceJackpot` のシグネチャ・既存呼び出し元の挙動は変更しないこと**（回帰防止）
- `Assets/_Content/Scripts/GIPoker/ItemSpawnManager.cs`:
  - `public static ItemSpawnManager Instance { get; private set; }` を追加（`GameSession.Instance` 等の既存パターンに準拠）
  - エントリごとの累計出現数を追跡し、`public bool HasRemainingCapacity(CardDefinition definition)` のような形で公開する
  - `Spawn()` 実行時にカウントを加算する。`CardEffectService.SpawnLotteryCardAt` からの出現も同じカウントに加算されるようにする
- `Assets/_Content/Scripts/GIPoker/ItemCatalog.cs`: `public GameObject FindPrefabByCardId(string cardId)` を追加（cardId→prefabの逆引き。宝籤の候補選定とリモートRPC受信の両方で使う）
- `Assets/_Content/Scripts/System/Rpc.cs`: `GiSpawnLottery`（引数: `[0]=newItemId, [1]=cardId, [2..4]=位置xyz`）を追加
- `Assets/_Content/Scripts/GIPoker/CardNetworkSync.cs`:
  - `effects.LotterySpawned` を購読し `Rpc.GiSpawnLottery` を送信（`SsSpawned` の購読パターンに準拠）
  - `Rpc.GiSpawnLottery` の受信処理を追加（cardId→prefab解決→Instantiate→TryCardify→Register。`GiSpawnSS` 受信処理に準拠）
  - `Rpc.GiGain` の受信処理内で、対象アイテムの `Definition.EffectType == CardEffectType.Lottery` の場合に `effectService.PlayChestOpenAndCrumbleSequence(item)` を呼ぶ（宝箱の開く→崩れる演出だけは専用RPCを増やさず、GiGainの受信自体から決定論的に行う。ローカル送信側は開くアニメーション完了後にカード出現RPCを送るため、多少のズレはあっても概ね同じタイミングで他クライアントにもカードが出現する）
- `Assets/_Content/Tests/EditMode/CardEffectServiceTests.cs`: `ResolveLotteryRank` のテストケース追加（jackpot=true→常にSS、jackpot=false→SSは絶対に選ばれない、重みの単調性、容量0のランクが除外されること）

## やらないこと（スコープ外）
- リスキーダイスの大凶（Curse）関連の実装・調整
- 宝籤専用の新規ハンドジェスチャー（既存の両手ピンチ「引き裂く」ゲインジェスチャーをそのまま使用する）
- 出現位置の凝ったアンカー演出（`StageSpawnAnchor` 連携）。宝箱があった位置の近傍にそのまま出現させれば良い
- `GI_POKER_PLAN.md` のランク別種類数・企画書自体の更新
- 候補が1つも無い（全ランク上限到達済み）場合の専用の「不発」演出。既存のゲインVFXのみで良い

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity`/`.prefab`/`.asset` は uloop経由で編集
- 既存の `SpawnSsCardAt` / `HasPendingDiceJackpot` / `ConsumeDiceJackpot` の挙動・シグネチャは変更しない
- 新規RPC `GiSpawnLottery` は `Rpc.cs` の末尾に追記する（既存の並び順を変更しない）

## 検証方法
- EditModeテスト: `ResolveLotteryRank`（または同等メソッド）の境界値テスト。**jackpot=true（リスキーダイス使用時）** と **jackpot=false（未使用時）** の両方を網羅する `[TestCase]` を追加。`gipoker-verify` スキルでコンパイル＋テスト実行を確認する。
- PlayMode/目視（uloop）:
  1. 通常時（リスキーダイス未使用）に宝籤を複数回ゲインし、低ランクほど出やすい・SSは出ないことを確認
  2. リスキーダイスをゲイン→掴んで離して大吉確定→宝籤をゲイン→SSカードが出現し、`HasPendingDiceJackpot` が消費されて以降は再度SSが出ないことを確認
  3. 宝籤ゲイン時、「宝箱が開く→カードが出現→宝箱が崩れて消える」の順で演出が再生されることを確認（同時発生や瞬間消滅になっていないか）
  4. ParrelSyncで2クライアントを起動し、一方で宝籤をゲインした結果（宝箱の削除・新カードの出現）がもう一方にも反映されることを確認

## 実機確認が必要な項目
- TreasureChestモデルの見た目（マテリアル質感・スケール感）は、PICO実機のフルカラーパススルー環境での最終確認が望ましい
- 削除エフェクトの視認性（MR環境の明るさでの見えやすさ）
