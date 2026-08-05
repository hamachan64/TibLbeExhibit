# リスキーダイス: ゲイン後に「投げて転がして大吉演出」機能追加

## 目的
現状の `CardEffectService.Execute()` は、リスキーダイスをゲイン（両手引き裂きジェスチャー）した瞬間に自動で d20 ロールを行い、大吉/大凶を判定している（`CardEffectService.cs:120-121`, 既存の `ExecuteRiskyDice` メソッド `CardEffectService.cs:267-296`）。

これを、「ゲインしたダイスを自分の手で振って（投げて）ロールできる」体験に変更する。壁打ちの結果、次の設計に決定した:

- 新しいハンドジェスチャーは追加しない。ゲイン後のダイスを既存の「掴む（`GainedHeld`）→離す（`Release()`）」操作で離した瞬間をトリガーにする。
- 離した瞬間、実物理（重力・コライダー衝突）には任せず、DOTweenによるスクリプト演出（軽くバウンド→回転しながら転がる→減速して停止、合計約2〜2.5秒）を再生する。時間で必ず完了するため「転がり続けて止まらない」という物理依存のリスクが無い。
- 演出が終わった時点で必ず大吉として確定し、大吉のVFX・「大吉」の文字・SSカード出現を発火する。大凶は今回スコープ外（「本来は大吉か大凶が出るが、今回は大吉に絞る」というユーザー要望どおり）。
- 1個のダイスにつき、この演出とロールは初回リリース時の1回のみ発火する（無制限だとSSカード出現上限1枚が崩れるため）。

これは `GI_POKER_PLAN.md` の「リスキーダイス（効果：使用するとダイスを振る）」という原案にも合致する（現行の「ゲイン即ロール」実装より原案に忠実になる）。

## 環境上の制約
このセッションには実行中の Unity Editor が存在しない（`Library/` フォルダなし、`Assets/JMO Assets/`＝CFXR系VFXアセットも `.gitignore` によりチェックアウトに存在しない）。そのため:
- 新規VFXアセットの追加やシーン/プレハブへの新規SerializeField結線は行わない。既存で `GIPokerBootstrap` から `GIPokerAssets` に配線済みのVFX（`CardSparkleVfx` / `DiceBlessVfx`）を流用する。
- `.unity` / `.prefab` の直接編集はしない。今回の演出トリガーは `CardEffectService` 内で対象アイテムの `State`（`ReadOnlyReactiveProperty`）を購読する形で実装し（`CardNetworkSync.Register` と同じ既存パターンを踏襲）、`Item_a_risky_dice.prefab` に新規コンポーネントを追加しない。
- 本タスクはC#スクリプト変更のみで完結させる。PlayMode/実機確認が必要な項目は明示して残す。

## 受け入れ基準
- [ ] リスキーダイスをゲインした直後はロール演出が発生せず、通常の `Gained` アイテム（既存のGainVfxのみ）になる。
- [ ] ゲイン後のダイスを掴んで（`GainedHeld`）離すと、バウンド＋回転して停止する演出（約2〜2.5秒）が再生され、終了時に必ず大吉として解決される（大凶・ハズレは発生しない）。
- [ ] 演出中は「転がっている最中のエフェクト」（`CardSparkleVfx` 流用）が再生され、大吉解決時には既存の `DiceBlessVfx` ＋ SSカード出現 ＋「大吉」の文字（新規 `DiceResultLabel`）が表示される。
- [ ] 演出の途中でダイスを再度掴んでも、大吉確定（VFX・文字・SS出現）は所定の時間で必ず起きる（Tweenが中断されても解決処理自体は止まらない）。
- [ ] 同一ダイスは初回リリースで1回ロールしたら、以後何度掴んで離しても再発動しない（同じ個体からSSカードが複数回出現しない）。
- [ ] リスキーダイス以外のゲイン済みアイテムを掴んで離してもこの効果は発動しない。
- [ ] マルチプレイ時、「転がっている最中のエフェクト」「大吉の文字」「大吉のエフェクト」がRPC経由で他クライアントにも同期される（新規 `GiDiceRoll` ＋ 既存 `GiGain`/`GiDice` の拡張）。ゲインでアイテム化されたダイス自体は既存の `GiGain` RPCで引き続き同期されることを確認する（変更不要だが回帰していないこと）。
- [ ] EditModeテスト: ロール開始判定の純粋関数（`ShouldStartRoll` 相当）に対し、(a) `GainedHeld→Gained`かつ未ロールなら開始する、(b) 既にロール済みなら開始しない、(c) それ以外の状態遷移では開始しない、を検証するテストが追加される。

## 変更対象
- `Assets/_Content/Scripts/GIPoker/CardEffectService.cs`:
  - `Execute()` の `case CardEffectType.RiskyDice:` を、即時ロールではなく `PrepareRiskyDiceRoll(gainedCard)` を呼ぶだけに変更する。
  - `blessThreshold` / `curseThreshold` / `curseDiscardCount` フィールドと、大凶分岐（`binder.DiscardRandom` 呼び出し）を含む既存の `ExecuteRiskyDice` は、ゲイン時自動発動が無くなり到達不能になるため削除する。
  - 新規 `HashSet<CardizableItemBase> rolledRiskyDice`（1回制限用）、`Dictionary<CardizableItemBase, Tween> activeRollTweens`（再グラブ時にTweenを止めるため）。
  - 新規 `PrepareRiskyDiceRoll(CardizableItemBase dice)`: `CardNetworkSync.Register` と同じ「`previous`/`current` を手動追跡する `item.State.Subscribe(...).AddTo(item)`」パターンで、ゲインした瞬間に一度だけ購読を張る。
    - `previous == GainedHeld && current == Gained && !rolledRiskyDice.Contains(dice)` の遷移（`ShouldStartRoll` 相当の純粋関数として切り出し、EditModeテスト対象にする）を検知したら `rolledRiskyDice.Add(dice)` の上で `ExecuteRiskyDiceRoll(dice, destroyCancellationToken).Forget();`
    - `current == GainedHeld` になった時点で `activeRollTweens` にTweenがあれば `Kill()` して除去する（`CardGrabInteractor.FollowPinchPose` とのTransform書き込み競合を避けるため）。
  - 新規 `private async UniTask ExecuteRiskyDiceRoll(CardizableItemBase dice, CancellationToken ct)`:
    1. `startPosition = dice.transform.position` を記録。
    2. `dice.GetComponent<Rigidbody>()`（`DropWithGravity()` により既に存在する想定）を `isKinematic = true; useGravity = false;` に戻す（実物理を止めてスクリプト演出に切り替える。Unity 6の Rigidbody APIは `velocity` ではなく `linearVelocity` である点に注意し、実装時に実際のAPI名を確認すること）。
    3. `GIPokerAssets.SpawnVfx(GIPokerAssets.CardSparkleVfx, startPosition, ...)` を再生（「転がっている最中」の演出。既存アセット流用）。
    4. 新規 `Subject<Vector3> diceRollStarted`（公開 `Observable<Vector3> DiceRollStarted`）に `OnNext(startPosition)`。
    5. バウンド＋回転して減速・静止するDOTweenシーケンスを構築して `activeRollTweens[dice]` に格納し再生する（見た目のみ。詳細イージング数値は実装時の裁量、合計時間は次のステップの待機時間と概ね一致させる）。
    6. `await UniTask.Delay(TimeSpan.FromSeconds(diceRollAnimationSeconds), cancellationToken: ct);`（大吉確定はTween完了コールバックに依存させず、固定時間待機で必ず行う）。
    7. `activeRollTweens.Remove(dice)`。残っていればTweenを `Complete()` して片付ける。
    8. `resolvePosition = dice.transform.position`（再グラブされていればその時点の位置）。
    9. 必ず大吉として `new DiceResult(roll: 20, isBless: true, isCurse: false, resolvePosition)` を作り既存の `diceRolled.OnNext(result)`（既存の `CardNetworkSync` 購読がそのまま `GiDice` RPCとして配信＝変更不要）。
    10. `GIPokerAssets.SpawnVfx(GIPokerAssets.DiceBlessVfx, resolvePosition, 8f);` → 既存の `SpawnSsCardAt(...)` 呼び出しロジック（カメラ前方への出現、`announce: true`）をそのまま流用。
    11. `DiceResultLabel.ShowAt(resolvePosition, "大吉");`
    12. 完了時点で `dice.State.CurrentValue == CardizableState.Gained` であれば Rigidbody を通常状態（`isKinematic=false; useGravity=true;`）に戻し、以後は普通の物理小物として扱う。
- `Assets/_Content/Scripts/GIPoker/DiceResultLabel.cs`（新規）: `CardLifetimeLabel.cs` と同じワールド空間TMPテキストのパターン（ビルボード、`GIPokerAssets.JapaneseFont` 使用）を踏襲した、一度だけ表示してフェードアウトし自己破棄するラベル。`public static void ShowAt(Vector3 position, string message)`。**注意**: `TextMeshPro`（3D版）には `DOFade` が無いため `DOTween.To(() => tmp.alpha, a => tmp.alpha = a, 0f, duration)` を使うこと（`ResultView.FadeText` と同じ書き方。`.claude/agent-memory/lbe-learnings.md` に既知の罠として記載済み）。
- `Assets/_Content/Scripts/GIPoker/CardNetworkSync.cs`:
  - `Initialize()` に購読追加: `effects.DiceRollStarted.Subscribe(pos => { if (!applyingRemote) Send(Rpc.GiDiceRoll, F(pos.x), F(pos.y), F(pos.z)); }).AddTo(this);`
  - `SubscribeInbound()` に受信追加: `Subscribe(Rpc.GiDiceRoll, args => GIPokerAssets.SpawnVfx(GIPokerAssets.CardSparkleVfx, V3(args, 0), ...));`
  - 既存の `Subscribe(Rpc.GiDice, ...)` ハンドラの `isBless` 分岐に `DiceResultLabel.ShowAt(pos, "大吉");` を追加。
- `Assets/_Content/Scripts/System/Rpc.cs`: `GiDiceRoll`（引数: `[0..2]`=位置xyz）をXMLコメント付きで追加。
- EditModeテスト用ファイル（`ShouldStartRoll` 相当の判定関数のテスト。既存EditModeテストの配置・asmdef規約に合わせる）。

## やらないこと（スコープ外）
- 大凶（curse）の実装（今回は必ず大吉に固定）。
- 新規VFXアセットの作成・シーンやプレハブへの新規SerializeField結線。
- 転がる演出の具体的なイージング・バウンド回数などのビジュアルチューニング（実装時の妥当な初期値でよく、最終調整は実機/PlayMode確認事項とする）。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、.unity/.prefab/.asset は直接編集しない。
- 上記「環境上の制約」節のとおり、新規SerializeField結線・シーン/プレハブ編集は行わない。

## 検証方法
- EditModeテスト: ロール開始判定関数（`ShouldStartRoll` 相当）の単体テストを追加。`gipoker-verify` スキルでコンパイル＋EditModeテストを実行する。
- PlayMode/目視: 実際のハンドトラッキング入力が無くても、`uloop-execute-dynamic-code` から `TryCardify()`→`TryGain()`→`TryGrab()`→`Release()` を直接呼んで一連の遷移を疑似再現できる。ロール発動・DOTween演出・「大吉」テキスト表示・SS出現をログ/スクリーンショットで確認する。このセッションでUnity Editorが起動できない場合は、コンパイル・EditModeテストの結果とともに「PlayMode/実機確認が未実施」であることを明記して報告する。

## 実機確認が必要な項目
- 実際のハンドトラッキングで「掴んで離す」操作の感触や、演出のタイミング・尺（約2〜2.5秒）が体験として自然か。
- 「大吉」というTMP文言が新規漢字「吉」を含むため、トーフ化しないか（`.claude/agent-memory/lbe-learnings.md` のマルチアトラス対応後の再確認）。
- マルチプレイ（ParrelSync/実機複数台）での同期演出の見え方。
- `CardSparkleVfx` 流用が「ダイスが転がっている感」の演出として視覚的に十分か（不十分なら専用VFX制作を別タスクとして起票）。
