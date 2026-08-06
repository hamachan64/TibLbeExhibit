# ボーナスタイム（終盤の出現率・レア度アップ＋告知UI）の実装

## 目的
残り時間が少なくなった終盤（デフォルト: 残り30秒）に「ボーナスタイム」を発生させ、
- アイテムの追加出現の**間隔を短く**（出現率アップ）
- 追加出現するアイテムの**レア度（ランク）が高いものほど選ばれやすく**
- ボーナスタイムに入ったことと、レアアイテムの出現率が上がることを**各プレイヤーにテキストで告知**
することで、5分間のセッションのラストスパートを盛り上げる。
あわせて、通常時（ボーナスタイム前）は高ランクのアイテムがやや出にくくなるよう重みを調整する
（現状 `ItemCatalog.asset` では Aランクの一部（リスキーダイス・アレキサンドライト）が `initialCount=1` で
セッション開始直後に確定出現しており、レアなアイテムが早期に出やすい状態になっている）。
これらのパラメータは全て Unity のヒエラルキーで `ItemSpawnManager` を選択した Inspector 上から
後から簡単に調整できるようにする。

## 前提・調査結果（実装者は着手前に必ず読むこと）
- スポーンは **完全に決定論的**。`GameSession.SessionSeed`（全クライアント共有のRPCシード）から
  `SpawnScheduleBuilder.Build()` がセッション開始時に全出現スケジュール（何が・いつ・どこに出るか）を
  一括で事前計算し、`ItemSpawnManager.RunScheduleAsync` は経過時間に応じてそれを順番に消化するだけ
  （ランタイムの乱数消費は一切ない）。**この決定論性・マルチクライアント同期を絶対に壊さないこと**。
- 追加出現（trickle分）は `SpawnScheduleBuilder.Build()` 内で「残数をエントリ順に列挙 → Fisher-Yatesで
  シャッフル → `trickleInterval * (k+1)` 秒ごとに1個ずつ配置」という現行ロジック（244-253行目）で決まる。
  **このFisher-Yatesの既存コードパスは変更しないこと**（後方互換テストがビット単位で一致することを要求している）。
  新しい「重み付き抽選」ロジックは、ボーナス/重み付け機能が実際に有効なとき（新パラメータが渡されたとき）
  だけ通る別コードパスとして追加する。無効時（新パラメータ未指定のデフォルト）は必ず従来のFisher-Yatesが
  実行され、既存の `SpawnScheduleBuilderTests.cs` が一切変更なしで全てパスすること。
- 各エントリの `maxCount`（カード化限度枚数／出現上限）は絶対に超えないこと。ボーナスタイムであっても
  この上限を超えて追加出現させてはならない（`GI_POKER_PLAN.md` の出現上限ルールを厳守）。
- `initialCount`（セッション開始時点=時刻0の即時出現）分の挙動・タイミングは変更しない。今回の対象は
  「追加出現（trickle）」のみ。
- 現在の `ItemCatalog.asset`（`Assets/_Content/GIPoker/ItemCatalog.asset`）は20エントリ、
  D:6種(計12) / C:7種(計19) / B:4種(計7) / A:2種(計6) / S:1種(計1) の構成。
  Aランクの `a_risky_dice` と `a_alexandrite` は `initialCount=1` のため毎セッション開始直後に確定出現する。
  この2エントリの `initialCount` 自体を変更するのは本タスクのスコープ外（データ変更ではなく、
  重み付け機構の提供が目的）。
- `sessionDurationSeconds`（デフォルト300秒）は `GameSession.SessionDurationSeconds` で取得可能。
  ボーナスタイム開始時刻 = `sessionDurationSeconds - bonusTimeSeconds`（0未満にならないよう clamp）。
- 宝籤（`CardEffectType.Lottery`）等、`CardEffectService` 経由の動的出現（`SpawnLotteryCardAt` /
  `NotifySpawned`）は本タスクの対象外（通常のスケジュール出現のみが対象）。
- **告知UIはネットワーク同期不要**。`GameSession.RemainingTime` は各クライアントが同じ
  `ContentStart` RPCタイミングを起点にローカルで `Time.deltaTime` を減算しているだけであり、
  既存の `StageDirector`（`unstableThresholdSeconds=60` で残り時間しきい値越えを検知して
  演出を切り替える）と全く同じ仕組みで、RPC追加なしに「各プレイヤーの画面にほぼ同時に表示される」を
  実現できる。告知テキストは各クライアントがローカルに `GameSession.RemainingTime` を購読して
  しきい値越えを検知し、自分のUIを表示するだけでよい。
- 告知テキストのしきい値は `ItemSpawnManager` に追加する `bonusTimeSeconds` と**必ず同じ値**を使うこと
  （出現率アップのタイミングと告知のタイミングがズレると体験として破綻する）。二重定義を避けるため、
  `ItemSpawnManager` に `public float BonusTimeSeconds => bonusTimeSeconds;` のような読み取り専用
  プロパティを追加し、UI側はそれを参照する。`ItemSpawnManager` は Environment シーン、UI（UserClient側）は
  別シーンなので、`ItemSpawnManager.Instance`（既存のシングルトン）を `UniTask.WaitUntil` で待ってから
  参照する（`ItemSpawnManager` 自身が `GameSession.Instance` を待つのと対称的なパターン）。
- 告知UIのビジュアル実装は、本プロジェクトの既存パターンをそのまま踏襲すればよい:
  `DiceResultLabel.cs`（ワールド空間TMP・DOTweenでポップイン→一定時間表示→フェードアウトして自己破棄・
  常にカメラを向くビルボード）が最も近いテンプレート。表示位置は特定オブジェクトではなく
  プレイヤー正面（`TutorialView` / `BinderView` の `followDistance` / `heightBelowHead` 追従パターンを
  参考にしてよいが、常時追従させる必要はなく、表示→数秒後に自動で消える一過性のポップアップで十分）。
  日本語フォントは `GIPokerAssets.JapaneseFont` を使う。
- 告知の発火元は `GIPokerBootstrap.cs`（UserClient シーンのエントリーポイント。`TitleView` /
  `ResultView` 等と同様に `new GameObject(...)` → `AddComponent` → `Init(session)` のパターンで
  各Viewを生成・結線している）に新しいViewを追加する形でよい。

## 受け入れ基準
- [ ] `ItemSpawnManager` に以下がヒエラルキーの Inspector から調整可能な `[SerializeField]` として追加されている:
  - `bonusTimeSeconds`（デフォルト30, 残りこの秒数でボーナスタイム開始）
  - `bonusTrickleInterval`（デフォルトは `trickleInterval` より短い値、目安4秒。ボーナス中の追加出現間隔）
  - `normalRankWeights`（ランク別の重み。ランクが高いほどやや小さい値をデフォルトにする。
    例: D=1.0, C=1.0, B=0.85, A=0.6, S=0.4, SS=0.4）
  - `bonusRankWeights`（ランク別の重み。ランクが高いほど大きい値をデフォルトにする。
    例: D=1.0, C=1.4, B=2.2, A=3.2, S=4.5, SS=4.5）
- [ ] セッション経過時間が `sessionDurationSeconds - bonusTimeSeconds` に達すると、以降にスケジュールされる
  追加出現アイテムの間隔が `bonusTrickleInterval` になり、通常時より高頻度でアイテムが出現する
- [ ] ボーナスタイムの時間帯にスケジュールされる追加出現アイテムは `bonusRankWeights` に従って選ばれ、
  通常時より高ランクのアイテムが選ばれやすくなる（同一シードで、ボーナス重みを高ランクに寄せた場合と
  寄せない場合を比較し、ボーナス時間帯に含まれるアイテムの平均ランクが上がることをテストで確認する）
- [ ] 通常時（ボーナスタイム前）の追加出現も `normalRankWeights` に従って選ばれる
- [ ] いずれの場合も各エントリの `maxCount` を超えて出現することはない
- [ ] 同一シード・同一パラメータであれば、全クライアントで出現スケジュール（どのエントリが・いつ・どこに）が
  完全に一致する（決定論性の維持）
- [ ] ボーナス/重み付け機能が無効な呼び出し（既存の `SpawnScheduleBuilderTests.cs` が使っている
  6引数コンストラクタ＋旧 `Build()` 呼び出し）では、**既存のテストが一切変更なしで全てパスする**
  （Fisher-Yatesコードパスがそのまま使われることの確認）
- [ ] `initialCount` 分（時刻0の出現）の挙動・件数・タイミングは変更されていない
- [ ] ボーナスタイムに入った瞬間（`RemainingTime` が `bonusTimeSeconds` 以下になった瞬間）に、
  各プレイヤーのUserClient画面に「ボーナスタイム開始・レアアイテムの出現率アップ」を伝える
  日本語テキストが1回だけ表示される（連打的な多重表示にならないこと。R3の
  `Where(...).DistinctUntilChanged()` や `TutorialView.lastCountdownSecond` のような
  edge-trigger判定パターンを使うこと）
- [ ] 告知は数秒表示後に自動でフェードアウトする（`DiceResultLabel` と同様、表示しっぱなしにしない）
- [ ] 告知のしきい値は `ItemSpawnManager.BonusTimeSeconds`（出現率アップと同じ値）を参照しており、
  値がずれて二重管理にならない
- [ ] 告知UIはネットワークRPCを新設せず、各クライアントのローカル `GameSession.RemainingTime` 購読のみで
  動作する

## やらないこと（スコープ外）
- ボーナスタイムの告知テキスト以外の演出（画面の色変化、専用SE、パーティクル等）
- `ItemCatalog.asset` の `initialCount` / `maxCount` の値そのものの変更
- カード効果（ゲイン・宝籤・リスキーダイス等）のロジック変更
- 宝籤・リスキーダイス大吉などの動的出現へのボーナス適用

## 変更対象
- `Assets/_Content/Scripts/GIPoker/SpawnScheduleBuilder.cs`:
  - `SpawnEntrySpec` に `CardRank Rank` を追加（新しい3引数コンストラクタ。既存2引数コンストラクタは
    `Rank = CardRank.D` 等の後方互換デフォルトを与えて維持し、既存呼び出し箇所を壊さない）
  - `Build()` にボーナス/重み付け用の追加パラメータ（例: `bonusWindowStartSeconds`,
    `bonusTrickleInterval`, `IReadOnlyDictionary<CardRank,float> normalWeights`,
    `IReadOnlyDictionary<CardRank,float> bonusWeights`）を持つオーバーロードを追加する。
    既存の4引数 `Build(seed, entries, anchors)` はそのまま残し、内部でデフォルト（ボーナス無効・全重み1.0）
    を渡して新オーバーロードに委譲する形でよい
  - 追加出現分の割り当てロジックに、次の2分岐を設ける:
    - 新パラメータが未指定/デフォルト（ボーナス無効かつ全重み1.0相当）→ **既存のFisher-Yatesコードを
      1行も変更せずそのまま使う**
    - 新パラメータが指定されている → 重み付き抽選（下記アルゴリズム）を使う
  - 重み付き抽選アルゴリズムの例（実装の目安。細部は実装者の判断でよい）:
    ```
    currentTime = 0
    pool = remaining（entryIndexの多重集合。中身は現行の remaining と同じ作り方でよい）
    while pool.Count > 0:
        interval = (currentTime >= bonusWindowStartSeconds) ? bonusTrickleInterval : trickleInterval
        currentTime += interval
        weights = (currentTime >= bonusWindowStartSeconds) ? bonusWeights : normalWeights
        # pool内の各要素の重み = weights[Rank(要素のentryIndex)]
        # 重み付き乱数（rng.NextDouble() * 合計重み → 累積和で選択）で1件選び、poolから除去
        selectedEntryIndex = WeightedPick(pool, weights, rng)
        AddSpawn(schedule, ref networkId, selectedEntryIndex, currentTime, rng, placedPositions, availableAnchors)
    ```
    （アンカー選択・yaw乱数消費は既存の `AddSpawn` をそのまま使う。rng消費順が変わること自体は
    新機能有効時のみの話なので問題ない）
- `Assets/_Content/Scripts/GIPoker/ItemSpawnManager.cs`:
  - `[Header("ボーナスタイム")]` として `bonusTimeSeconds` / `bonusTrickleInterval` /
    `normalRankWeights` / `bonusRankWeights`（`[Serializable] class RankSpawnWeight { CardRank rank; float weight; }`
    の `List<RankSpawnWeight>` 等、Inspectorで見やすい形）を追加
  - `BuildEntrySpecs()` で各エントリの `CardRank` を解決して `SpawnEntrySpec` に渡す
    （`ResolveCardId` と同様のパターンで `entry.prefab.GetComponent<CardizableItemBase>().Definition.Rank` を使う）
  - `BuildSchedule(seed)` で `GameSession.Instance.SessionDurationSeconds` から
    `bonusWindowStartSeconds = Mathf.Max(0f, sessionDurationSeconds - bonusTimeSeconds)` を計算し、
    重みリストを `Dictionary<CardRank,float>` に変換して新しい `Build()` オーバーロードに渡す
  - `public float BonusTimeSeconds => bonusTimeSeconds;` を追加し、UI側から同じしきい値を参照できるようにする
- `Assets/_Content/Scripts/GIPoker/BonusTimeAnnouncementView.cs`（新規ファイル）:
  - `DiceResultLabel.cs` に準拠したワールド空間TMPポップアップ（ビルボード・DOTweenでポップイン→
    数秒表示→フェードアウトして自己破棄）
  - `Init(GameSession session)` で、`ItemSpawnManager.Instance` を `UniTask.WaitUntil` で待ってから
    `session.RemainingTime` を購読し、`RemainingTime <= ItemSpawnManager.Instance.BonusTimeSeconds` へ
    最初に入った瞬間（edge-trigger、1回きり）にプレイヤー正面付近へ告知テキストを表示する
  - 表示文言の例: 「ボーナスタイム！レアアイテムの出現率アップ！」（実装者調整可）
  - `SessionState.Playing` 中のみ判定すること（Tutorial中や別状態での誤発火防止。`GameSession.State` も
    合わせて参照する）
- `Assets/_Content/Scripts/GIPoker/GIPokerBootstrap.cs`:
  - 既存の `TitleView` / `ResultView` 生成パターンに倣い、`GIPoker_BonusTimeAnnouncement` の
    GameObjectを生成して `BonusTimeAnnouncementView` を `AddComponent` し、`Init(session)` で結線する
- `Assets/_Content/Tests/EditMode/SpawnScheduleBuilderTests.cs`:
  - 新機能のテストを追加（既存テストは1つも変更しない）:
    - 同一シード・同一ボーナス設定 → 完全に同一のスケジュールになること
    - ボーナス時間帯以降は `bonusTrickleInterval` の間隔になっていること
    - ボーナス重みを高ランクに寄せたとき、ボーナス時間帯（`Time >= bonusWindowStartSeconds`）に
      入るスポーンの平均ランクが、通常重みのみの場合より高くなること
    - 各エントリの出現数が `maxCount` を超えないこと（ボーナス機能ON時も）
    - 新パラメータ未指定の `Build()` 呼び出しが、既存のレガシー参照実装
      （`LegacyBoxOnlyBuild` / `LegacyGenericAnchorBuild`）とビット単位で一致すること
      （＝既存テストが担保している内容の再確認）

## 制約
- 規約: UniTask / R3 / DOTween。告知UI（`BonusTimeAnnouncementView`）も `R3` 購読＋
  `UniTask.WaitUntil`＋`DOTween`演出という既存Viewの規約通りの構成にすること（コルーチン禁止）
- 既存Fisher-Yatesコードパス（244-253行目付近）は無変更で温存すること
- 各エントリの `maxCount` を厳守すること
- マルチクライアント間の決定論性（同一シードで同一結果）を維持すること
- 告知UIはRPC/NetSyncを新設しないこと（各クライアントのローカル判定のみでよい）
- `.unity` / `.prefab` / `.asset` はテキスト直接編集禁止。Inspectorフィールド追加はC#コード変更のみで
  完結する（ItemSpawnManagerコンポーネントの新フィールドはコード追加だけでUnity側に自動反映される。
  シーン上のコンポーネントのデフォルト値上書き等、シーン側の編集が必要になった場合のみ
  `uloop-execute-dynamic-code` 経由で行うこと）

## 検証方法
- EditModeテスト: 出現ロジックの新規テストを追加し、`gipoker-verify` の手順でコンパイル→EditModeテストを実行する
- PlayMode/目視: 告知UIについては `gipoker-verify` のPlayMode確認手順で、`GameSession` の
  `sessionDurationSeconds` / `ItemSpawnManager` の `bonusTimeSeconds` を一時的に小さい値にしてPlayModeへ入り、
  ボーナスタイムに切り替わるタイミングで告知テキストが1回表示されフェードアウトすることを
  `uloop-screenshot` 等で確認する（確認用に変更した値は確認後に元へ戻すこと）

## 実機確認が必要な項目
- ボーナスタイムの体感（出現率アップ・レア出現の増加・告知テキストの見え方や表示位置が
  ハンドトラッキングMR環境で違和感ないか）は実機での5分プレイテストでの確認が望ましいが、
  本PRでは自動テスト＋エディタPlayMode確認までとし、ユーザー側で実機確認後にパラメータ
  （`bonusTimeSeconds` / `bonusTrickleInterval` / 各重み / 告知文言）をInspectorから微調整することを想定する
