# MR空間（Stage）ライブスコアボードの追加

## 目的
既存のスコアモニター機能（PR #25、未マージ）は運営PC（`OperatorClient.unity`）にのみ表示され、プレイヤーはMR空間から見ることができなかった。「Stage上（プレイヤー全員が見える場所）にもライブスコアを出したい」との要望を受け、`Environment.unity`内の既存プレースホルダー`UIDocument`（座標(1.3, 2.111, 1.951)、テンプレート文言のみの未使用UI Toolkitパネル）をこの用途に転用する。

「本日通算のトップ3」は複数デバイス間で永続化データが食い違う懸念があるため、引き続きOperatorClient専用のまま変更しない。今回はライブ部分（残り時間・今セッションのトップ3）のみをMR空間に追加する。

PR #25と同じブランチ（`feature/gi-poker-score-monitor`）に追加コミットする。

## 受け入れ基準
- [ ] `Environment.unity`の既存`UIDocument`GameObject（座標(1.3, 2.111, 1.951)）の場所に、「残り時間」と「今セッションのトップ3（ニックネーム＋スコア）」を表示するプロシージャルTextMeshPro(3D)のビューが表示される。
- [ ] このビューはUserClient・OperatorClientどちらのデバイスでロードされても、正しく同一プロセス内の`LeaderboardTracker.Instance`を見つけて動作する。
- [ ] `LeaderboardTracker`のシングルトンが二重生成されない（`UserClient.unity`にのみ新規追加し、`Environment.unity`には追加しない）。
- [ ] 「本日通算のトップ3」はこのビューには表示しない。
- [ ] コンパイルエラー0件、既存EditModeテスト（125件）に影響なし。

## 変更対象
- `Assets/_Content/Scripts/System/Leaderboard/StageScoreMonitorView.cs`（新規）: `Environment.unity`内の固定Stage位置に配置するMonoBehaviour。`BinderView.BuildBook()`（`Assets/_Content/Scripts/GIPoker/BinderView.cs`）と同様のプロシージャル生成パターンでTextMeshPro(3D)を2つ（残り時間用・今セッションTOP3用）生成し、`LeaderboardTracker.Instance.RemainingTime`/`LiveTop3`（`DailyTop3`は使わない）をR3で購読して更新する。追従・ビルボードは行わない（Stage上の固定モニターのため、`Camera.main`依存の`LateUpdate`は不要）。`GIPokerAssets.JapaneseFont`には依存せず、`[SerializeField] TMP_FontAsset japaneseFont`を自前で持つ（`GIPokerBootstrap`はUserClient.unity専用でOperatorClient側では未初期化のため、Leaderboard名前空間の「OperatorClient専用ロジックを持たない」設計方針に合わせて自己完結させる）。表示フォーマットは`ScoreMonitorView.cs`の`UpdateRemainingTime`/`UpdateTop3`と同等のロジックをTextMeshPro向けに再実装する（`PlayerNickname.Format(entry.ClientNo)`を使う）。`LeaderboardTracker.Instance`がnullの場合は`ScoreMonitorView.cs`と同じ防御スタイル（`Debug.LogWarning`して以降何もしない）。
- `Assets/_Content/Scenes/UserClient.unity`（uloop経由）: ルート直下に新規GameObject`LeaderboardTracker`を追加し`LeaderboardTracker`コンポーネント（`Assets/_Content/Scripts/System/Leaderboard/LeaderboardTracker.cs`）をアタッチ（`OperatorClient.unity`の既存配置と同一構成、SerializeFieldなし）。これによりUserClient単独プロセスでも`LeaderboardTracker.Instance`が存在するようになる。
- `Assets/_Content/Scenes/Environment.unity`（uloop経由）: 既存の`UIDocument`GameObject（fileID 377414493、`Environment.unity:16263`付近）から
  - `UIDocument`コンポーネント（`m_PanelSettings`/`sourceAsset`=Infomation.uxml参照、fileID 377414494）を削除（`Assets/_Content/UI/PanelSettings.asset`/`Assets/_Content/UI/Infomation.uxml`アセット自体は削除せず放置してよい）
  - `StageScoreMonitorView`コンポーネントを追加し、`japaneseFont`にNoto Sans JP SDF（`GIPokerBootstrap`のInspectorで使われているものと同じフォントアセット）を割り当てる
  - GameObject名を`StageScoreMonitor`に変更
  - Transform（fileID 377414495）の`m_LocalScale`をUI Toolkit World Space由来の`(0.13728829, 0.13728829, 0.13728829)`から`(1, 1, 1)`に戻す（子TMPが独自の0.01スケールを持つため）
  - GameObjectの`m_Layer`をUI Toolkit用の`5`（UI）からデフォルト`0`（Default）に戻す
  - Transformの位置(1.3, 2.111, 1.951)・回転(identity)は変更しない

## やらないこと（スコープ外）
- 「本日通算のトップ3」のStage表示（永続化データの端末間不整合の懸念があるため）。
- `LeaderboardTracker.cs`/`ScoreMonitorView.cs`（PR #25で実装済み・既に他所から依存されている）自体への変更。
- プレイヤー名の自由入力（既存の`PlayerNickname.Format`をそのまま再利用）。
- MRパススルー背景での視認性向上（背景板・アウトラインの本格調整）は、実機確認の結果次第で別タスクとする。
- OperatorClient側のカメラにStage UIが映り込む場合の見た目調整（実害があるか確認した上で必要なら別対応）。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、.unity/.prefab/.asset は uloop経由で編集。
- `LeaderboardTracker`は`[DefaultExecutionOrder(-90)]`で、`UserClient.unity`の通常シーンロード時に確実にAwake/Startが完了してから`AdditiveSceneLoader`が`Environment.unity`を非同期追加ロードするため、`StageScoreMonitorView.Start()`時点で`LeaderboardTracker.Instance`は設定済みのはずだが、念のため`null`チェックで防御すること。

## 検証方法
- EditModeテスト: 新規の純粋ロジックなし（Viewのみのため追加テストなし）。既存125件がリグレッションしていないことを確認する。
- PlayMode/目視:
  - UserClient単独プロセスで`LeaderboardTracker.Instance`が1つだけ生成され、警告が出ないこと
  - Environment側のStage UIがUserClient・OperatorClient双方の世界で`LeaderboardTracker.Instance`を見つけて表示更新されること
  - 日本語ニックネームがOperatorClient側でも文字化けしないこと（`japaneseFont`の割り当て漏れ確認）
  - スクリーンショットで残り時間・トップ3表示のレイアウトを確認

## 実機確認が必要な項目
- MRパススルー環境での実際の視認性（文字サイズ・背景板の要否）
- 複数PICO実機接続時のネットワーク遅延を含めたリアルタイム反映
