# オープニング・保留・本編開始・結果

Issue: #42 ／ ブランチ: `repo/feature-opening-result`（`repo/develop` から作成）
設計書: `TREASURE_HAUL_DESIGN.md` 3.1・3.2・8章・9章・9.1 ／要件定義書 F-FLOW-2・F-TUT-1・F-TUT-6・F-SCORE-3〜5・F-OPS-2/3・N-9・F-SAFE-3
Issue の本文: 待機画面の目的の一文と持ち方の一文／オープニング（ナレーション＋字幕＋効果音、最後に荷台を光らせる。音声は仮素材で可）／保留（上限で自動解除）・練習中はいつでも押せる「本編開始」／本編開始時に荷台を光らせ「ここへ運ぼう」、残り時間／結果／待機画面のクレジット欄（N-9）／見た目・音・文言は `ThContentSet` から取る。Issue に追記（コメント）はなし。
あわせて `TODO.md` の #41 を「完了」にする（PR #57 はマージ済み）。

## 目的
体験者が、待機 → オープニング → 練習 → 本編 → 結果の流れを、スタッフの追加説明なしで追えるようにする。運営PCからは練習の延長（保留）と、練習を切り上げての本編開始ができるようにする。
- 待機中は「何をするゲームか」と「どう持つか」が目の前に出ている。
- オープニングで、世界観の言葉で目的を聞く。
- 本編では残り時間と目標額がいつでも見え、目標に届いたら全員が知る。
- 結果で、回収額と目標額、クリアかどうかがひと目でわかる。

## ユーザーと決めたこと（2026-10-21 の壁打ち）
- **結果画面は「回収額／目標額」と「クリア・失敗」だけ**。企画書 5.7・要件 F-SCORE-5 の「一番高かったお宝」「損害額」は外す（R.E.P.O 由来ではなく、初見でひと目で読めることを優先）。U-9 は「不要になった」として閉じる。
  - 文言 `ResultLossFormat`（「今回の損害：{0:N0}G」）は列挙から消せないので残し、「使わない」とコメントする。ホストの `DamageTotal` は記録（#44）用にそのまま残す。
- **目標額は、本編中ずっと手元の帯に出す**（「残り 2:15 ／ 目標 2,500G」）。目標に届いた瞬間、全員に「目標達成！」を数秒大きく出し、音を鳴らす（音は素材が来るまで空）。荷台の表示板は今のまま合計額だけ（F-SCORE-2）。
- **体験者に見せる文字（待機の一文・オープニングの字幕・残り時間と目標額・目標達成・結果）は、頭について来る小さな面に出す**（視界の正面やや下。少し遅れてついて来る。半透明の背景で、全画面は覆わない。F-SAFE-3）。
- **荷台の光と「ここへ運ぼう」は作らない**（企画書 4.2・4.3、要件 F-TUT-1 の最後・F-SCORE-3 から外す）。練習を荷台の目の前で行い、練習用のお宝を実際に荷台へ運ぶので、本編の開始時には全員が荷台の場所を知っているため。第1検証（企画書 10.1「本編開始後、迷わず荷台へ運べたか」）で迷う人がいたら足す。演出 `CargoGlow`・文言 `HintGoHere` は列挙から消せないので残し、「使わない」とコメントする。
- オーケストレーターが企画書 4.2・4.3・5.4・5.7、要件 F-TUT-1・F-SCORE-3〜5・U-9、設計書 3.2・9章を更新する（実装エージェントは文書を触らない）。

## オーケストレーターが決めた実装の方針（設計書の範囲内の具体化）
- **保留**: Tutorial 中だけ押せる切り替え（保留 ⇔ 保留を解除）。保留の間は練習の残り時間が止まる。1回の練習で保留した時間の合計が〔保留の上限〕（`holdMaxSeconds`、30秒）に達したら自動で解除し、残りの練習時間から数え直す。解除して何度でも押し直せるが、合計は上限まで。〔保留の上限〕が 0 なら保留できない。保留の有無と延長した時間（合計）はホストが持つ（CSV は #44）。
- **本編開始**: Tutorial 中なら、保留の有無にかかわらず即 Playing（F-TUT-6）。
- **保留の配布**: 保留の切り替えでホストは状態の通し番号を +1 して `ThState` を送り直す。`ThState`・`ThHeartbeat` に `held` を足し、端末は保留中は残り秒を数えない（Heartbeat の時間合わせも同じ規則でそのまま動く）。
- **目標額と結果はホストが決める**（F-FLOW-5）: `ThState` に `target`（目標額。本編の参加端末が確定した時点＝Playing 以降。それより前は 0）と `result`（Result のときだけ: 回収額・目標額・クリアか）を足す。目標額は `ThParams.layout.rows[参加人数-1].targetAmount`（参加人数は 1〜4 に丸める）。結果を `ThState` に載せるので、取りこぼしても `ThResync` で取り直せる。
- **目標達成の知らせ**: 端末は、ホストから来た合計額（今の `SessionClientCore.Total`）が `target` 以上になった瞬間に、その回1回だけ出す（本編中だけ。結果に入った後は出さない）。
- **オープニングの字幕**: 字幕は5枚（`TREASURE_HAUL_NARRATION.md` の op01〜op05 の**案**をそのまま仮に入れる。U-5 は未決のまま）。〔オープニング〕を5等分して1枚ずつ出す（10秒なら2秒ずつ）。各枚の始まりで、その文のナレーション（新しい音の ID。1文1ファイル）を鳴らす。オープニングの始まりで効果音（`OpeningSfx`）を鳴らす。音が空なら無音で字幕だけ進む。既存の `OpeningNarration`（1つの音）は使わず、「1文ずつの ID に分けた」とコメントする。
- **頭について来る面**: XR Interaction Toolkit の `LazyFollow`（`UnityEngine.XR.Interaction.Toolkit.UI`）を使い、カメラの前やや下に置く。中身は TMP の文字と半透明の背景だけ。待機・オープニング・本編・結果で出す内容を切り替え、練習中（#43 で案内を足す）と、回の途中から入って次の回を待っている間は何も出さない。
- **判断は純粋クラス**に置く（例: 状態・残り秒・〔オープニング〕・合計額・目標額・結果から、面に出す内容・字幕の番号・目標達成の合図を返す）。MonoBehaviour は配線と見た目だけ。

## 受け入れ基準

### A. ホストの進行（`HostRoundMachine`・`SessionHostCore`）
- [ ] `TryHold(now)`／`TryReleaseHold(now)`（名前は任意）: Tutorial 中だけ。保留の間は Tutorial の経過時間が進まない（`RemainingSeconds` が止まる）。保留の合計が〔保留の上限〕に達したら `Tick` で自動解除される。解除後は残りの練習時間から進む。保留の合計は上限を超えない。〔保留の上限〕0 なら保留できない
- [ ] 保留の切り替え（自動解除を含む）で `Seq` が +1 し、ホストは `ThState` を送る
- [ ] 保留中に時間がたっても Tutorial → Playing に進まない。解除後、残りの練習時間がたてば進む
- [ ] `TryBeginPlay(now)`（名前は任意）: Tutorial 中なら保留の有無にかかわらず即 Playing（参加端末の確定は今の Tutorial → Playing と同じ）。Tutorial 以外では何もしない
- [ ] 保留の有無・保留した時間の合計を、その回の間（次の「開始」まで）読める（#44 の CSV 用）。新しい回の「開始」で 0 に戻る
- [ ] Playing 以降の `ThState` に `target`（目標額）、Result の `ThState` に `result`（回収額・目標額・クリアか）が載る。Waiting に戻ったら `target` 0・`result` なし
- [ ] `ThState`・`ThHeartbeat` に `held` が載る
- [ ] 運営UIが読む値を足す: 保留中か・保留の残り（上限まであと何秒）・目標額（Playing 以降）・合計額

### B. メッセージ（`ThMessages`）
- [ ] `ThStateMsg` の**末尾に** `held`（bool）・`target`（int）・`result`（結果の DTO。回収額・目標額・クリアか）を足す
- [ ] `ThHeartbeatMsg` の**末尾に** `held`（bool）を足す
- [ ] 古い送り手からは既定値（`held=false`・`target=0`・結果なし）で、今の動きが変わらない

### C. 端末の追従（`ClientRoundTracker`・`SessionClientCore`）
- [ ] `held` の間は残り秒を数えない（`ThState` で受けたとき・`ThHeartbeat` で変わったときの両方）。解除されたらその時点の残り秒から数える
- [ ] 目標額と結果を読める（例: `Tracker.Target`・`Tracker.Result`）。回が変わる・Waiting に戻ると消える

### D. 体験者の面（新規: 判断の純粋クラス＋`LazyFollow` の View。`TreasureHaulUserClient` シーンに置く）
- [ ] Waiting: 目的の一文（`GoalSentence`）・持ち方の一文（`GripBeam`）・小さくクレジット欄（新しい文言 `Credits`）。持ち方の設定が Beam 以外でも、今は `GripBeam` を出す（Direct・Swing の文言は追加要素 #4 で足す）
- [ ] Opening: 字幕 `OpeningLine1`〜`OpeningLine5` を〔オープニング〕の5等分で1枚ずつ。各枚の始まりで `OpeningLine1`〜`5` の音、オープニングの始まりで `OpeningSfx` を鳴らす（全員に同じように聞こえる音。2D）
- [ ] Tutorial: 何も出さない（#43 で案内を足す）
- [ ] Playing: 帯「残り m:ss ／ 目標 N,NNNG」（新しい文言 `HudPlayingFormat`）を常時。合計額が目標額以上になった瞬間に「目標達成！」（`TargetReached`）を数秒大きく出し、音 `TargetReached` を鳴らす（その回1回だけ）
- [ ] Result: 「N,NNNG ／ 目標 N,NNNG」（`ResultAmountFormat`）と、クリアなら「クリア！」（`ResultClear`）、届かなければ「あと N,NNNG でクリア」（`ResultFailFormat`）。値はホストの `result` から取る
- [ ] 回の途中から入って次の回を待っている端末（`WaitNextRound`）は、Opening〜Result の間は何も出さない（待機の一文も出さない）
- [ ] 面は半透明の背景と文字だけで、全画面を覆わない（F-SAFE-3）。`ScreenFader` は使わない
- [ ] 日本語フォントは今の値札と同じ物（Noto Sans JP の SDF ＋予備フォント）

### F. 運営UI（`OperatorView`・`TreasureHaulOperatorClient` シーン）
- [ ] 「保留」ボタン: Tutorial 中かつ保留できる（上限に余りがある）ときだけ押せる。保留中はラベルが「保留を解除」（`OpHoldRelease`）になり、押すと解除。保留中は「保留中（あと N 秒で自動解除）」（`OpHoldingFormat`）を出す
- [ ] 「本編開始」ボタン（`OpBeginPlay`）: Tutorial 中だけ押せる
- [ ] 合計額と目標額（`OpTotalFormat`「合計 {0:N0}G ／ 目標 {1:N0}G」。目標額は Playing 以降。それより前は合計だけでも、目標 0 と出してもよい）
- [ ] ボタン・文字のシーンへの追加は uloop 経由（YAML を直接書かない）。今のボタンの見た目に合わせる

### G. 文言・音（`ThTextTable`・`ThAudioCatalog`。列挙の末尾にだけ足す）
- [ ] 文言（`ThTextTable.asset` に入れる。案のまま。承認後に変える場合はオーケストレーターが直す）:
  | ID | 文言 |
  | :--- | :--- |
  | `OpeningLine1` | 発掘隊のみんな、よく来てくれた。 |
  | `OpeningLine2` | この遺跡は、もうすぐ崩れる。 |
  | `OpeningLine3` | お宝を、搬出リフトまで運び出してくれ。 |
  | `OpeningLine4` | ぶつけると値打ちが下がるぞ。 |
  | `OpeningLine5` | みんなで、目標額を目指そう！ |
  | `HudPlayingFormat` | 残り {0}:{1:00} ／ 目標 {2:N0}G |
  | `TargetReached` | 目標達成！ |
  | `ResultAmountFormat` | {0:N0}G ／ 目標 {1:N0}G |
  | `ResultClear` | クリア！ |
  | `ResultFailFormat` | あと {0:N0}G でクリア |
  | `Credits` | フォント: Noto Sans JP（SIL Open Font License 1.1） |
  | `OpHold` | 保留 |
  | `OpHoldRelease` | 保留を解除 |
  | `OpHoldingFormat` | 保留中（あと {0} 秒で自動解除） |
  | `OpBeginPlay` | 本編開始 |
  | `OpTotalFormat` | 合計 {0:N0}G ／ 目標 {1:N0}G |
- [ ] `ResultLossFormat` に「使わない（2026-10-21、結果画面は回収額・目標額・クリアだけにした）」、`HintGoHere`（文言）と `CargoGlow`（演出）に「使わない（2026-10-21、荷台の案内はやめた）」とコメント。`OpeningNarration` に「使わない（1文ずつの `OpeningLine1`〜`5` に分けた）」とコメント
- [ ] 音の ID を末尾に足す: `OpeningLine1`〜`OpeningLine5`（2D）、`TargetReached`（2D）。中身は空（`ThAudioCatalog.asset` に定義だけ足す）
- [ ] 文言を足した後、予備フォントのエディタメニュー（`ThFontExtraMenu`）を実行し、全文字が表示できるかのテスト（`ThContentTests`）を通す

### H. テスト・確認
- [ ] EditMode テスト（すべて成功。既存のテストも全件成功のまま）:
  - ホスト: 保留で残り秒が止まる／保留中は Playing に進まない／上限で自動解除され、残りの練習時間で Playing に進む／解除して押し直せるが合計は上限まで／上限 0 なら保留できない／Tutorial 以外では保留・本編開始できない／本編開始で即 Playing（保留中でも）・参加端末が確定する／保留の切り替えで `Seq` +1 と `ThState`（`held`）／保留の合計が回をまたいで残らない
  - ホスト: Playing の `ThState` の `target` が人数の行の目標額／Result の `ThState` の `result`（クリア・失敗の両方）／Waiting で消える
  - 端末: `held` の `ThState`・`ThHeartbeat` で残り秒が止まり、解除で再開する／`target`・`result` を読める・回が変わると消える
  - 面の判断（純粋クラス）: 状態ごとの内容（Waiting・Opening・Tutorial・Playing・Result・次の回を待つ）／字幕の番号が〔オープニング〕の5等分で進む／目標達成の合図は目標額をまたいだときに1回だけ（結果に入った後・既に届いていた値を後から知った場合の扱いを決めてテストする）／残り時間の書式（例 135秒 → 2:15、切り上げか切り捨てかを決めてテストする）
  - 文言・音・演出の一覧の `Validate`（欠け・重複なし）と、全文字の表示（既存のテスト）
  - **ホストと端末のつなぎ合わせ**（今の偽の通信でホスト1＋端末1〜2）: 開始 → Opening → Tutorial で保留 → 端末の残り秒が止まる → 解除 → 本編開始 → 端末が Playing・`target` を持つ → 回収で合計が目標以上 → 結果で `result` が全端末で一致
- [ ] エディタでの確認（#38〜#41 と同じやり方。結果を報告する）:
  - 端末側をメインのエディタで動かし、ホストは ParrelSync のクローンを batchmode で起動する（時間の短縮はメモリ上の上書き。`.claude/agent-memory/lbe-learnings.md`）。運営UIのボタンは、クローン側で `SessionHost` のメソッドを呼ぶか、運営UI側を動かせるならボタンで確かめる
  - 確かめること: 待機中に面へ目的の一文・持ち方の一文・クレジットが出る／オープニングで字幕が5枚進む／保留で端末の残り時間が止まり、解除で進む／本編開始で帯に残り時間と目標額が出る／合計額が目標額に届くと「目標達成！」が出る（届かせるのが難しければ、目標額をメモリ上で小さくしてよい）／結果に回収額・目標額・クリア（または「あと N G でクリア」）が出る／中断で全部消える。面・運営UIのスクリーンショットを撮る
  - 確認が終わったら、起動したクローンのプロセスと Play を止める

## 変更対象
- `Assets/_Content/TreasureHaul/Scripts/Session/`: `HostRoundMachine.cs`（保留・本編開始）、`SessionHostCore.cs`（保留・本編開始の操作、`target`・`result`・`held` の送信、運営UI向けの値）、`SessionHost.cs`（運営UIからの呼び出し）、`ThMessages.cs`（`held`・`target`・`result`）、`ClientRoundTracker.cs`（保留中の残り秒・目標額・結果）、`SessionClientCore.cs`（必要なら）
- `Assets/_Content/TreasureHaul/Scripts/View/`: 新規の面の判断（純粋クラス）・面の View（`LazyFollow`）
- `Assets/_Content/TreasureHaul/Scripts/Operator/OperatorView.cs`: 保留・本編開始・合計額と目標額
- `Assets/_Content/TreasureHaul/Scripts/Content/`: `ThTextTable.cs`・`ThAudioCatalog.cs`（列挙の末尾に追加・コメント）、`ThFxCatalog.cs`（`CargoGlow` のコメントだけ）
- アセット（uloop 経由）: `Settings/ThTextTable.asset`・`ThAudioCatalog.asset`、予備フォント（`ThFontExtraMenu` の実行結果）、`TreasureHaulUserClient.unity`（面の View を置く）、`TreasureHaulOperatorClient.unity`（ボタン・文字）
- `Assets/_Content/Tests/EditMode/`: テストの追加
- 触らない: `Rpc.cs`（新しい RPC は要らない）、`ThParams.cs`（パラメータはすべてある）、`TreasureHaulEnvironment.unity`、`ThVenue.cs`、文書（企画書・要件定義書・設計書・ナレーション原稿・TODO はオーケストレーターが更新する）

## やらないこと（スコープ外）
- 練習の中身（荷台前の練習用お宝・色分け・「持ち手を握ろう」の案内・ヒント・最後の一文。#43）
- 目標額のみモード（目標到達で即終了）・本日のランキング・持ち方の切り替えの文言（追加要素）
- 記録 CSV（#44。保留の有無と延長時間は読めるようにするだけ）
- ナレーション音声・効果音・目標達成の音の素材（#47。ID だけ用意して空のまま）
- 荷台の光と「ここへ運ぼう」（今回の決定で作らない）
- 結果画面の「一番高かったお宝」「損害額」（今回の決定で出さない）

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity` / `.prefab` / `.asset` は uloop 経由で編集（YAML を直接書かない）
- 判断は純粋クラスに置き、MonoBehaviour は配線と見た目だけ（設計書 1章）。時刻は引数で渡す
- 数値は `ThParams`（`openingSeconds`・`practiceSeconds`・`holdMaxSeconds`・`playingSeconds`・`resultSeconds`・`layout.rows`）から取る。新しいパラメータは足さない（「目標達成！」の表示時間など見た目の調整値は、今の `HintSeconds` と同じくコードの定数）
- 列挙・メッセージのフィールドは**末尾にだけ**足す
- 状態遷移の判断は `HostRoundMachine.DecideNext` の1か所にまとめたまま（保留・本編開始もそこか、そこと同じ考え方で）
- 回の終わり（Result→Waiting、中断）で、View・Tween・R3 の購読を回ごとに破棄する（設計書 9章、N-2）
- 今の進行（開始・中断・時間合わせ・取りこぼしからの追いつき・お宝の流れ）を変えない

## 検証方法
- EditMode テスト: 上の H のとおり
- PlayMode/目視: 上の H の「エディタでの確認」

## 実機確認が必要な項目
- 頭について来る面の位置・大きさ・文字の大きさが、PICO のパススルーで読みやすく、邪魔でないか（足元・お宝が隠れないか）
- オープニングの字幕の速さ（2秒ずつ）で読み切れるか（ナレーション音声が入ったら、音声の長さと合うか）
- 会場の騒がしさの中で、目標達成の知らせに気づけるか

## 外部の素材・サービス（権利・費用・セキュリティ）
- なし（XRI の `LazyFollow` は既に入っている Unity 公式パッケージの部品。クレジット欄には、既に確認済みの Noto Sans JP を載せる）
