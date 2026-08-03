# 全体共有スコアモニター＋プレイヤーネームタグ

## 目的
LBE展示の運営PC画面に、観客・スタッフが一目で状況を確認できる「全体共有スコアモニター」を追加する。「残り時間」「今セッションのトップ3得点者」「本日通算のトップ3得点者」を表示する。あわせて、MR空間で他プレイヤーのアバターが誰なのか分かるよう、頭上にニックネーム（`ハンターNo.xx`形式、ClientNoベースの自動生成）を表示する。

## 受け入れ基準
- [ ] 接続中の各プレイヤー（UserClient）に`ハンターNo.{ClientNo:00}`形式のニックネームが自動的に割り当てられる（名前入力UIは作らない）。
- [ ] 他プレイヤーのリモートアバター（`STYLY Default Avatar A.prefab`）の頭上に、そのプレイヤーのニックネームがTextMeshProで表示される。自分自身のアバターには表示不要。
- [ ] `OperatorClient.unity`のCanvas上に新規パネルが追加され、「残り時間」「今セッションのトップ3（ニックネーム＋スコア）」「本日通算のトップ3（ニックネーム＋スコア）」がリアルタイムに更新される。
- [ ] 「今セッションのトップ3」は接続中の全クライアントの`UserVariable.GiScore`から算出する（絞り込みなし）。
- [ ] 「本日通算のトップ3」は各プレイヤーのセッション終了時（`GameSession.State`がFinishedへ遷移した瞬間）の最終スコアを記録し、上位3件を保持する。記録は運営PCのローカル（`Application.persistentDataPath`配下のJSON）に永続化し、アプリを再起動しても保持される。
- [ ] 日付が変わったら（`DateTime.Now.Date`が保存済みの記録日と異なったら）「本日通算のトップ3」の記録がリセットされる。
- [ ] EditModeテストが通る（下記「検証方法」参照）。

## 変更対象
- `Assets/_Content/Scripts/System/PlayerNickname.cs`（新規）: `static string Format(int clientNo)` で`"ハンターNo.01"`形式の文字列を返す。
- `Assets/_Content/Scripts/GIPoker/PlayerNameTagView.cs`（新規）: リモートアバタープレファブに付与するMonoBehaviour。`NetSyncAvatar.ClientNo`が確定するのを待って`Head Tracker`の上にTextMeshPro(3D)を生成し、`PlayerNickname.Format(clientNo)`を表示。カメラ方向へヨー軸のみビルボード（`BinderView.cs`の`TryComputeFollowTarget`内`flatForward`パターンを踏襲）。
- `Assets/_Content/Scripts/System/Leaderboard/LeaderboardEntry.cs`（新規）: `readonly struct LeaderboardEntry { int ClientNo; int Score; }`
- `Assets/_Content/Scripts/System/Leaderboard/DailyLeaderboard.cs`（新規）: UnityEngine非依存の純粋C#クラス。Top3挿入（同点は既存順維持）、`DateTime`を引数で受け取る日付ロールオーバー判定（`NeedsReset(DateTime now)`のようなメソッド）。
- `Assets/_Content/Scripts/System/Leaderboard/DailyLeaderboardStore.cs`（新規）: `DailyLeaderboard`のJSON（`JsonUtility`）Load/Save。保存先パスをコンストラクタ等で注入可能にする（テスト時は一時パス、実運用時は`Application.persistentDataPath`配下）。
- `Assets/_Content/Scripts/System/Leaderboard/LeaderboardTracker.cs`（新規）: `OperatorClient.unity`に配置するシングルトンMonoBehaviour。
  - `NetSyncManager.Instance.OnAvatarConnected`/`OnAvatarDisconnected`（UnityEvent<int>）を購読して接続中ClientNoの集合を追跡する。
  - `VariableManager.Instance.AsObservableOnChanged<int>(UserVariable.GiScore)`（`VariableManagerBase<TGlobal,TUser>`に実装済み。`Observable<UserVariableData<T>>`を返す。R3ネイティブでUnityEventラップ不要）を購読し、接続中クライアントのスコアを保持・Top3算出して`ReactiveProperty<IReadOnlyList<LeaderboardEntry>> LiveTop3`を更新する。
  - `OnAvatarConnected`受信時は`VariableManager.Instance.Get<int>(UserVariable.GiScore, clientNo, 0)`で初期値を補完する（AsObservableOnChangedは変更時のみ流れるため）。
  - `RpcManager.Instance.AsObservable(Rpc.GiSessionFinished)`（`Observable<RpcData>`、`data.ClientNo`/`data.Parameters[0]`=最終TotalScore）を購読し、`DailyLeaderboard`に記録・`DailyLeaderboardStore`で保存、`ReactiveProperty<IReadOnlyList<LeaderboardEntry>> DailyTop3`を更新する。
  - `VariableManager.Instance.AsObservableOnChanged<float>(UserVariable.GiRemainingTime)`（後述の新規UserVariable）から`ReadOnlyReactiveProperty<float> RemainingTime`相当を公開する（複数クライアントから届くが、直近の値をそのまま採用してよい＝表示上は全員ほぼ同じ値になるはず）。
- `Assets/_Content/Scripts/System/Leaderboard/ScoreMonitorView.cs`（新規）: `OperatorClient.unity`のCanvas配下に配置するuGUIバインダー。`LeaderboardTracker`のReactivePropertyを購読し、3つの`TextMeshProUGUI`（残り時間／今セッションTOP3／本日TOP3）のテキストを更新する。DOTween等の演出は不要（実用重視の薄いView）。
- `Assets/_Content/Scripts/System/UserVariable.cs`: `GiRemainingTime`（float）を追加。
- `Assets/_Content/Scripts/System/Rpc.cs`: `GiSessionFinished`（引数[0]=最終TotalScore、文字列化した`int`）を追加。
- `Assets/_Content/Scripts/GIPoker/CardNetworkSync.cs`: `Initialize(CardEffectService effects, CardBinder binder, GameSession session)`にGameSession引数を追加。
  - `session.RemainingTime`を`ThrottleLast(TimeSpan.FromSeconds(1))`程度で購読し`VariableManager.Instance?.SetSelf(UserVariable.GiRemainingTime, remaining)`。
  - `session.State`がFinishedへ遷移した「エッジ」でのみ（既存の`item.State`購読と同様、前回値との比較で判定）`Send(Rpc.GiSessionFinished, binder.Score.CurrentValue.TotalScore.ToString(CultureInfo.InvariantCulture))`を送信。
- `Assets/_Content/Scripts/GIPoker/GIPokerBootstrap.cs`: `networkSync.Initialize(effectService, binder, session);`に更新（`session`は73行目で既に取得済み）。
- `Assets/_Content/Prefabs/STYLY Default Avatar A.prefab`: ルートに`PlayerNameTagView`をアタッチ（`uloop-execute-dynamic-code`等のエディタAPI経由。YAML直接編集は禁止）。
- `Assets/_Content/Scenes/OperatorClient.unity`: 既存Canvas配下に`ScoreMonitorPanel`（`ScoreMonitorView`+3つの`TextMeshProUGUI`）と、`LeaderboardTracker`を持つGameObjectを追加（エディタAPI経由）。

## やらないこと（スコープ外）
- プレイヤーによる名前の自由入力（バーチャルキーボード等の新規インタラクション実装）。
- `Environment.unity`内の既存`UIDocument`プレースホルダー（`Infomation.uxml`）の変更・置き換え。今回は触らない。
- `GameSession.State`（Playing中かどうか）による「今セッションのトップ3」の絞り込み。
- サーバー権威によるスコアのチート対策（既存の自己申告方式を踏襲する）。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、.unity/.prefab/.asset は uloop経由で編集。
- サードパーティパッケージ（STYLY NetSync）側のAPIが`UnityEvent`を使っている箇所（`NetSyncManager.OnAvatarConnected`/`OnAvatarDisconnected`）は、そのままイベント購読して構わない（プロジェクト規約の禁止対象はプロジェクト内で新規に`UnityEvent`/`event Action`を設計することであり、外部APIの受け口までは含まない）。ただし可能な範囲で`Observable.Create`等でR3にラップし、以降の処理はR3で統一すること。
- `VariableManagerBase<TGlobal,TUser>.AsObservableOnChanged`/`RpcManagerBase<TRpc>.AsObservable`は既にR3ネイティブなのでラップ不要（`Library/PackageCache/com.styly.styly-netsync.utility@57bded8bb994/Runtime/Scripts/VariableManagerBase.cs`参照）。

## 検証方法
- EditModeテスト:
  - `PlayerNicknameTests.cs`: `PlayerNickname.Format`のフォーマット確認（1桁・2桁のClientNoなど）。
  - `DailyLeaderboardTests.cs`: Top3挿入（4件目以降は落ちる）、同点処理、`DateTime`注入による日付ロールオーバー判定。
  - `DailyLeaderboardStoreTests.cs`（任意）: 一時パスへのJSON Save/Load往復。
- PlayMode/目視: ParrelSyncで複数クライアントをエディタ上に立ち上げ、
  - 他クライアントのアバター頭上にニックネームが表示されること
  - スコア変化がOperatorClient側のモニターにリアルタイム反映されること
  - セッション終了で「本日のトップ3」に記録が追加されること
  をスクリーンショットで確認する。

## 実機確認が必要な項目
- 複数PICO実機接続時のネームタグ・モニターの表示遅延・可読性（エディタのParrelSyncでは近似できるが、実機のネットワーク遅延は別途確認が必要）。
- 日付をまたいだ際の「本日のトップ3」リセットの実機での動作（自動テストは`DateTime`注入までしか担保できない）。
