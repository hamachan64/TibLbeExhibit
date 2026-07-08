# 具現化ポーカー 実装ドキュメント

GI_POKER_PLAN.md の企画をフル実装したMRゲームの構成・検証結果・実機テスト手順。

## 全体アーキテクチャ

```
[OperatorClient シーン]                [UserClient シーン]
 Button → OperatorClient               GIPoker (GameObject)
   └ Rpc.ContentStart 送信 ──NetSync──▶ ├ GameSession        … 5分セッション管理（RPC購読/エディタ自動開始）
                                        ├ CardEffectService  … 全カード効果の発動（錬金/水晶/よろい/複製/ダイス）
                                        └ GIPokerBootstrap   … 以下をランタイム生成
                                            ├ GIPoker_LeftHand / RightHand
                                            │   ├ HandPinchTracker      … XR Hands親指-人差し指ピンチ判定
                                            │   ├ CardGrabInteractor    … 掴む/追従/離す/収納
                                            │   └ FingertipTouchCollider… 指先の HandBone コライダー
                                            ├ GainGestureDetector       … 両手引き裂き=ゲイン判定
                                            ├ GIPoker_Binder (CardBinder + BinderView)
                                            └ GIPoker_ResultView

[Environment シーン]
 GIPoker_ItemSpawner (ItemSpawnManager + ItemCatalog)
   └ セッション開始で初期11個 → 12秒毎に上限まで追加出現（S・レア枠は後半のドラマ演出）
```

- **状態はすべて R3**（`ReactiveProperty` / `Observable` / `ObservableList`）、**非同期はすべて UniTask**、**演出は DOTween**。Coroutine / UnityEvent は不使用。
- カードの見た目・バインダー・リザルトUIは `CardVisualFactory` 等による**プロシージャル生成**（Prefabに依存しない）。日本語は Noto Sans JP SDF。

## アイテムの一生（状態機械）

```
Item ─素手で触れる→ FloatingCard ⇄ Held ─バインダー圏内で離す→ Stored
                        │            │
                        │            └─両手で引き裂く→ Gained（効果発動・再カード化は永久不可）
                        └─60秒経過（1分ルール）→ Expired（消滅）
```

## ジェスチャー仕様（コントローラー不使用）

| 操作 | ジェスチャー | 誤爆対策 |
| :--- | :--- | :--- |
| カード化 | 素手（指先）でアイテムに触れる | HandBone レイヤー(8)のコライダーのみ判定 |
| 掴む | 親指と人差し指のピンチ（2cm以下） | ヒステリシス（解除は4cm）＋3フレーム連続判定 |
| 収納 | バインダーの上（半径20cm）で離す | 圏外なら再浮遊するだけでロストしない |
| ゲイン | カードを持ったまま逆手でピンチ→両手を25cm引き離す | 「両手・カード近接開始・距離」の3条件 |

## カードラインナップ（14種）

| ランク | カード | 効果 |
| :--- | :--- | :--- |
| D | 花 / 酒瓶 / きのこ / 石ころ | なし |
| D | 剣（脇差モデル） | 通常錬金: 近くのDアイテムをCに変換 |
| C | キングホワイトオオクワガタ / 銀の懐中時計 | なし |
| C | 錬金キット | バインダー内のDカード1枚をCに変換 |
| B | 占い水晶 | 12秒間、全カードの浮遊が遅くなる |
| B | 彷徨うよろい | 15秒間、1分ルールを停止 |
| B | ギャラリーフェイク | バインダー内カード1枚を複製（A/SS不可） |
| A | リスキーダイス | d20: 18以上=大吉（SS出現+花火）/ 3以下=大凶（カード3枚破壊+骸骨） |
| S | ブループラネット | 100点（後半にのみ出現） |
| SS | 一坪の海岸線 | 300点。大吉イベント時のみ出現 |

スコア: D=1 C=2 B=3 A=5 S=100 SS=300。役: ストレート(D-C-B-A-S)+200 / S-A-A+150 / 同ランク3枚+50（貪欲法・1枚1役）。

## 生成済みアセット

- `Assets/_Content/Scripts/GIPoker/` … 全19スクリプト
- `Assets/_Content/GIPoker/CardDefinitions/` … カード定義14種（ScriptableObject）
- `Assets/_Content/GIPoker/Prefabs/` … アイテムPrefab14種（プリミティブ合成＋脇差）
- `Assets/_Content/GIPoker/Materials/` … 専用マテリアル22種（URP Lit）
- `Assets/_Content/GIPoker/ItemCatalog.asset` … 出現数・上限の定義（企画書の限度枚数準拠）
- レイヤー8 `HandBone` 追加済み / VFXは JMO Cartoon FX Remaster を割り当て済み

## マルチユーザー同期（STYLY NetSync）

カード状態は `CardNetworkSync`（GIPokerBootstrap がランタイム生成）が同期する。

**方式: 決定論的スポーン ＋ 離散イベント同期**

1. **スポーンは通信しない**: オペレーターの開始ボタン（`Rpc.ContentStart`）に乱数シードを載せ、
   全クライアントの `ItemSpawnManager` が同じシードから同一の配置表
   （NetworkId・種類・座標・出現時刻）を事前計算する。よって全員の空間に
   同じアイテムが同じIDで現れる（`System.Random` 使用・実行時状態に非依存）
2. **プレイヤー操作だけをRPC配信**: 各アイテムの `State`（R3）の遷移を購読して自動配信。
   ゲームプレイ側のコードは同期を一切意識しない
   - `GiCardify` / `GiGrab` / `GiRelease`(位置付き) / `GiStore` / `GiGain`(位置付き)
   - `GiConvert`（錬金の定義差し替え） / `GiEffect`（占い水晶・彷徨うよろい）
   - `GiDice`（大吉/大凶VFX共有） / `GiSpawnSS`（大吉のSSカード出現）
3. **再帰防止**: 受信適用中は `applyingRemote` フラグで再配信を抑制、
   自分のエコーは `ClientNo` で除外（オフラインループバックにも対応）
4. **1分ルールはローカルタイマー**: `Expired` は配信しない（イベント削減・耐障害性）。
   カード化イベントを受けた各クライアントが自分の60秒タイマーで消滅させる
5. **バインダーは個人所有**: 他人の `GiStore` を受けたらそのアイテムは空間から消えるだけ。
   大凶のカード破壊も発動者のバインダーのみ。スコアは `UserVariable.GiScore` で共有
   （対戦スコアボードUIの土台）

**既知のトレードオフ**: 掴んでいる間のカード位置は同期しない（リモートでは静止し、
離した位置で再配置）。連続位置同期が必要になったら NetSyncObject の導入を検討。
後から参加したクライアントは途中経過を受け取れない（展示では開始前に全員入室する運用）。

**2クライアント検証方法**: STYLY NetSync サーバーをローカルで起動し、
ParrelSync でエディタを複製 → 片方のOperatorClientから開始ボタン
→ 双方で同じ配置になり、片方でカード化・収納した結果が他方に反映されることを確認する。

## 検証済み（Unityエディタ PlayMode 実測）

- ✅ 役計算ユニット検証 8/8 PASS（ストレート311点等）
- ✅ セッション自動開始 → アイテム13個出現（位置・高さ正常）
- ✅ カード化 → 60秒カウントダウン → 収納 → スコア加算
- ✅ 役のライブ成立（D×3 + C×3 = +100ボーナス）
- ✅ ゲイン → 再カード化不可 / リスキーダイス出目判定
- ✅ TIME UP → リザルト（内訳＋カウントアップ演出）
- ✅ コンソールエラー 0 件

## 実機（PICO/Quest）テスト手順

1. UserClient シーンを起点にビルド（Environment は AdditiveSceneLoader が自動ロード）
2. オペレーターPCで OperatorClient を起動し「開始」ボタン → 5分セッション開始
   - スタンドアロン検証ならエディタ再生3秒後の自動開始でも可（`GameSession.autoStartInEditorDelay`）
3. 空間に浮かぶアイテムに素手で触れる → カード化・浮遊・キラキラVFX
4. ピンチで掴み、腰元のバインダーへ運んで離す → 収納・スコア反映
5. カードを持ったまま逆手でピンチ→左右に引き裂く → ゲイン
6. 実機での調整ポイント（すべてInspector）:
   - `HandPinchTracker`: pinchEnter/ExitDistance（手の大きさ・トラッキング精度に応じて）
   - `GainGestureDetector`: assistRadius / tearDistance
   - `BinderView`: followDistance / heightBelowHead
   - `ItemSpawnManager`: spawnAreaSize（実際の展示スペースに合わせる）

## 既知の制限 / 今後

- 掴んでいる間のカード位置はリモートに同期されない（離散イベント同期のため。上記参照）
- 途中参加クライアントへの状態スナップショット送信は未実装（展示運用でカバー）
- SE・BGMなし（音声アセットがプロジェクトに無いため）
- バインダーからの「取り出し」APIは実装済みだが、取り出しジェスチャーは未割り当て
- ピンチしきい値は実機での最終調整が必要
