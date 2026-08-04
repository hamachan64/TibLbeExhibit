# チュートリアルの7ステップ化・タイトル追加・音声読み上げ対応

## 目的
現行チュートリアルは6ステップ（Intro/Cardify/Grab/Store/Gain/Rules）で、各ステップにタイトル表示欄はあるが内容は使わず常に固定文字列「チュートリアル」を表示している。また指示文の読み上げ（音声）は未実装。

体験者向けの分かりやすさを上げるため、ユーザー指定の新しい7ステップ構成（ルール／バインダーを出してみよう／カード化してみよう／バインダーに収納してみよう／カードをアイテムにしてみよう／注意事項／準備はいいか？）に差し替え、各ステップにタイトルを表示し、`Assets/_Content/Sounds/Step1.mp3`〜`Step7.mp3`（各ステップ用に用意済みの音声素材、7ステップ構成と1対1で対応）を各ステップ表示時に再生する。「説明文の読み上げが終わっている」は実際の音声再生完了をもって判定する。

壁打ちで確認済みの決定事項:
- ステップ2「バインダーを出してみよう」の達成条件は原文の「カード化できたら」ではなく、実際には**バインダーが表示されている（`BinderView.IsVisible`）**を使う。
- 音声読み上げはTTSではなく、`Assets/_Content/Sounds/StepN.mp3`をそのまま`AudioSource`で再生する形でよい。

## 受け入れ基準
- [ ] `TutorialStep`が`Title`（表示用タイトル）を新たに持ち、`TutorialSequence.CreateDefaultSteps()`が新仕様の7ステップを順序通り返す（EditModeテストで検証）。
  1. ルール（達成条件なし・自動送り）
  2. バインダーを出してみよう（達成条件あり）
  3. カード化してみよう（達成条件あり）
  4. バインダーに収納してみよう（達成条件あり）
  5. カードをアイテムにしてみよう（達成条件あり）
  6. 注意事項（達成条件なし・自動送り）
  7. 準備はいいか？（達成条件なし・自動送り）
- [ ] 各ステップの指示文（`Instruction`）とタイトル（`Title`）は下記「新ステップ内容」の記載どおり。
- [ ] `TutorialView`が現在ステップの`Title`をタイトル欄に表示する（従来の固定文字列「チュートリアル」表示は廃止。ただし1番目のステップに入った瞬間の大見出しフラッシュ演出＝`PlayIntroFlourish`相当は維持し、フラッシュ終了後にステップ1のタイトル「ルール」に収束する）。
- [ ] ステップ2の達成条件は`BinderView.IsVisible`が`true`になること（＋音声読み上げ完了）。既存の「Lサインジェスチャーでバインダーを呼び出す」導線をそのまま利用し、バインダー表示の新規検知ロジックは`BinderView.IsVisible`購読のみで完結させる（新規ジェスチャー判定は追加しない）。
- [ ] ステップ3の達成条件はアイテムのカード化（既存`CardizableState.FloatingCard`相当）。
- [ ] ステップ4の達成条件はバインダーへの収納（既存`CardizableState.Stored`相当）。従来「Grab（掴む）」は独立ステップとして表示しない（新仕様に掴む専用ステップが無いため）。ステップ3でカード化がタイムアウトしても、ステップ4開始時に練習アイテムを強制カード化するフォールバックは維持する（従来Grabステップが担っていた役割をStoreステップの開始時処理に移す）。
- [ ] ステップ5の達成条件はゲイン（既存`CardizableState.Gained`相当）。
- [ ] 達成条件を持つステップ（2〜5）は「音声読み上げ完了 かつ 達成」の両方が満たされるまで次に進まない。達成が音声再生中に起きた場合は、音声再生の完了を待ってから進む（音声を打ち切って進めない）。
- [ ] 達成条件を持たないステップ（1・6・7）は「音声読み上げ完了後+指定秒数」で自動送りする。
  - ステップ1・6: 読み上げ完了 + 1秒
  - ステップ7: 読み上げ完了 + 2秒待機 + カウントダウン5秒 → 本編（Playing）開始。既存の`GameSession.TutorialRemainingTime`によるカウントダウン表示（残り5秒からの強調演出）をそのまま使い、ステップ構成の合計時間から`GameSession`側`tutorialDurationSeconds`（現行60秒固定、シーン上の実値）を新ステップ構成に合わせて再チューニングする。
- [ ] 各ステップが表示された瞬間、対応する音声（`Step1.mp3`〜`Step7.mp3`、7ステップの並び順と対応）が自動再生される。前のステップの音声が再生中でも、次のステップに進んだら即座に停止し新しい音声に切り替わる。音声アセットが未設定（null）の場合は「読み上げ完了」を即時trueとして扱い、従来同様タイムアウトのみで進行できる（展示で詰まらせない設計を維持）。
- [ ] コンパイルエラー0件。既存EditModeテスト（`TutorialSequenceTests.cs`含む）を新仕様に合わせて更新し、全件成功する。
- [ ] Unityエディタ実行時限定で、Space（バインダー呼び出しデバッグ用に既存使用）とは別のキーで「現在のチュートリアルステップを強制スキップ（読み上げ完了待ち・達成条件を無視して次のステップへ進める）」できる（検証時間短縮用のデバッグ機能。実機ビルドには含めない）。

## 新ステップ内容（確定仕様）

### ステップ1
- タイトル: ルール
- 説明文: `アイテムに触れて得られたカードをバインダーに集めるゲーム\nレアリティの高いカードほど、高得点だぞ！`
- 達成条件: なし（読み上げ後1秒経過で自動送り）

### ステップ2
- タイトル: バインダーを出してみよう
- 説明文: `右のように、L字のポーズを顔の前ですると、バインダーが現れるぞ！`
- 達成条件: 読み上げ完了 かつ バインダーが表示されている（`BinderView.IsVisible == true`）

### ステップ3
- タイトル: カード化してみよう
- 説明文: `目の前のアイテムに手で触れるとカードになるぞ`
- 達成条件: 読み上げ完了 かつ 練習アイテムをカード化できたら

### ステップ4
- タイトル: バインダーに収納してみよう
- 説明文: `カードをバインダーに近づけると収納して、ポイントをゲットできるぞ！`
- 達成条件: 読み上げ完了 かつ ステップ3でカード化したカードをバインダーに収納できたら

### ステップ5
- タイトル: カードをアイテムにしてみよう
- 説明文: `カードの中には、効果を持つものがある。\nカードを両手で引き裂くと元のアイテムに戻りそのカード効果を使えるぞ！\nカードはアイテムにしたらカードに戻せないから気をつけろ！`
- 達成条件: 読み上げ完了 かつ カードをアイテム化（ゲイン）できたら

### ステップ6
- タイトル: 注意事項
- 説明文: `最後に注意事項だ。\n・カードはアイテムにしたらカードに戻せないから気をつけろ！\n・カード化から1分放置で消滅するぞ！`
- 達成条件: なし（読み上げ後1秒経過で自動送り）

### ステップ7
- タイトル: 準備はいいか？
- 説明文: `制限時間5分でハイスコアを狙え！`
- 達成条件: なし（読み上げ完了 → 2秒待機 → カウントダウン5秒 → 本編開始）

## 変更対象
- `Assets/_Content/Scripts/GIPoker/TutorialSequence.cs`: `TutorialStep`に`Title`プロパティを追加（コンストラクタ引数追加）。`TutorialStepKind`を新7ステップに合わせて再編（例: `Intro→BinderShow→Cardify→Store→Gain→Caution→Ready`。旧`Grab`は削除）。`CreateDefaultSteps()`を新仕様の7件に差し替え（`Title`/`Instruction`/`TimeoutSeconds`のフォールバック値/`RequiresAchievement`）。`TimeoutSeconds`は「音声アセットが無い場合のフォールバック値」を設定する（実際の音声長を使った上書きは`TutorialDirector`側で行う。詳細は下記）。
- `Assets/_Content/Scripts/GIPoker/TutorialDirector.cs`:
  - `Init`に`BinderView binderView`引数を追加し、`binderView.IsVisible`を購読してステップ2の達成判定に使う（既存の`practiceCardified`等と同様、一度trueになったら記録するのではなく`binderView.IsVisible.CurrentValue`を直接参照する形でよい。購読は`EvaluateAchievement()`の再評価トリガーとしてのみ使う）。
  - `OnStepEntered()`のswitchを新`TutorialStepKind`に合わせて更新（`Cardify`→練習アイテム1出現、`Store`→アイテム1が未カード化ならカード化を強制、`Gain`→練習アイテム2出現）。
  - `EvaluateAchievement()`に「現在ステップの音声読み上げが完了しているか」の条件を追加し、達成条件を持つステップは「読み上げ完了 かつ 達成」の両方が揃うまで`sequence.NotifyAchieved()`を呼ばない。読み上げ完了状態は`TutorialView`から取得する（例: `view.IsVoiceFinished`のような読み取り専用プロパティを新設）。
  - `StartTutorial()`で`TutorialSequence.CreateDefaultSteps()`をベースに、`GIPokerAssets`の音声クリップ長を使って各ステップの`TimeoutSeconds`を「音声長 + バッファ秒」に上書きした新しい`List<TutorialStep>`を組み立て、それを`TutorialSequence`に渡す（`TutorialStep`はイミュータブルなので、`Kind`/`Title`/`Instruction`/`RequiresAchievement`はコピーしつつ`TimeoutSeconds`だけ差し替えた新インスタンスを作る）。音声クリップが無い場合は`CreateDefaultSteps()`のフォールバック値をそのまま使う。
  - `#if UNITY_EDITOR`限定のデバッグ機能として、`public void SkipCurrentStep()`（仮称）を追加する。`sequence.NotifyAchieved()`を試み、進まなければ`sequence.Tick(sequence.CurrentStep.TimeoutSeconds)`で強制的にタイムアウトさせて`OnStepEntered()`する（読み上げ完了待ち・達成条件を無視する）。`GIPokerBootstrap.cs`の既存Spaceキー購読（バインダートグル用）と同じ`#if UNITY_EDITOR`＋`Observable.EveryUpdate()`パターンで、Space以外のキー（例: `N`キー）にこれを割り当てる。実機ビルドには含めない。
- `Assets/_Content/Scripts/GIPoker/TutorialView.cs`:
  - タイトル欄に`step.Title`を表示するよう`ShowStep`/`UpdateInstruction`周りを更新（固定文字列「チュートリアル」表示は削除。ただし1番目のステップの`PlayIntroFlourish`演出は維持し、フラッシュ終了後に`titleText.text`をそのステップの`Title`に戻す）。
  - `AudioSource`を1つ生成（`BuildPanel()`内、`playOnAwake=false`、2D再生=`spatialBlend=0`でノイズの多い展示環境でも聞き取りやすくする）。`ShowStep`で`GIPokerAssets.TutorialStepVoiceClips[stepIndex]`があれば`Stop()`→`clip`差し替え→`Play()`。
  - 読み上げ完了判定用の公開プロパティ（例: `IsVoiceFinished`）を追加。クリップ未設定なら常に`true`、設定済みなら`!audioSource.isPlaying`（再生開始前の1フレーム目に誤って`true`にならないよう、`Play()`呼び出し直後は`false`から始める実装にする）。
- `Assets/_Content/Scripts/GIPoker/GIPokerAssets.cs`: `public static AudioClip[] TutorialStepVoiceClips;`を追加（7要素、null許容）。
- `Assets/_Content/Scripts/GIPoker/GIPokerBootstrap.cs`: `[SerializeField] private AudioClip[] tutorialStepVoiceClips;`を追加し`Awake()`で`GIPokerAssets.TutorialStepVoiceClips = tutorialStepVoiceClips;`。`tutorialDirector.Init(session, binder)`の呼び出しに`binderView`を追加（`tutorialDirector.Init(session, binder, binderView)`。`binderView`は同メソッド内で既に生成済み）。
- `Assets/_Content/Sounds/Step1.mp3`〜`Step7.mp3`: 現状`.meta`が存在せず未インポート。まず`AssetDatabase.Refresh()`（uloop-execute-dynamic-code経由）でAudioClipとしてインポートさせること。
- シーン側（uloop経由、`.unity`直接編集は禁止）:
  - `UserClient.unity`の`GIPokerBootstrap`コンポーネントに、新規`tutorialStepVoiceClips`配列（7要素、`Step1.mp3`〜`Step7.mp3`を順にアサイン）を設定する。
  - `UserClient.unity`の`GameSession`コンポーネントの`tutorialDurationSeconds`を、新7ステップ構成の合計時間（各ステップの読み上げ長+バッファの見積り合計。音声を実際にインポートした後、各`AudioClip.length`を読んで再計算すること）に合わせて調整する。
- `Assets/_Content/Tests/EditMode/TutorialSequenceTests.cs`: 新7ステップ構成・新`TutorialStepKind`・`Title`プロパティに合わせて全面的に更新する（`DefaultSteps_ComposedOfSixStepsInOrder`→7ステップ版に。`Grab`参照テストは`Store`等に置き換え）。

## やらないこと（スコープ外）
- TTS（音声合成）の実装。用意済みのmp3をそのまま再生するのみ。
- 字幕のハイライトや単語単位の同期。
- 音量調整UI・ミキサー設定。
- チュートリアルをスキップする機能の追加・変更（既存`skipTutorial`はそのまま）。
- Playing状態以降のゲームプレイ・スコアリングロジックへの変更。
- バインダー召喚ジェスチャー自体（`BinderSummonGestureDetector`/`LSignPoseEvaluator`）の判定ロジック変更。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity`/`.prefab`/`.asset`は uloop 経由で編集。
- 展示で詰まらせない設計を維持すること（音声・達成のどちらかが欠けても、最終的にはタイムアウトで必ず次のステップに進めるようにする）。
- `TutorialSequence`自体は引き続きMonoBehaviour非依存の純ロジッククラスとして保つこと（UnityAudio関連の状態は`TutorialDirector`/`TutorialView`側に閉じ込める）。
- 全クライアントの本編開始時刻を揃える必要があるため（`GameSession`のコメント参照）、`tutorialDurationSeconds`は今回も固定値のまま（ステップ進行度に応じた動的延長はしない）。プレイヤーの操作が早い/遅いによってステップ7到達時の余裕秒数が変動する既存の制約はそのまま許容する。

## 検証方法
- EditModeテスト: `TutorialSequenceTests.cs`を新7ステップ仕様に更新し全件成功させる（`gipoker-verify`）。
- PlayMode/目視: エディタでチュートリアルを開始し、
  - 各ステップでタイトルが正しく切り替わること
  - 各ステップ表示時に対応する音声が再生されること（エディタの音声出力 or ログでクリップ名を確認）
  - ステップ2はLサインジェスチャー（エディタではSpaceキーでも代用可、`GIPokerBootstrap`のデバッグ用トグルを利用）でバインダーを表示すると進むこと
  - ステップ7到達後、2秒→5秒カウントダウンを経て本編に入ること
  をスクリーンショット・ログで確認する。

## 実機確認が必要な項目
- 展示環境（周囲のノイズ・パススルー視界）での音声の聞き取りやすさ・音量バランス。
- PICO実機でのLサインジェスチャーによるバインダー表示の成立しやすさ（ステップ2の達成しやすさ）。
