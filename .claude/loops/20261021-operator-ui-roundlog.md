# 運営UIの残り・記録 CSV（#44 / TODO 順5）

## 目的
制作者以外のスタッフが、運営PCだけで端末の様子を見て（番号・接続・位置合わせ・手・電池・練習）、見学・位置合わせ確認を切り替えて回せるようにする。
あわせて、第1検証と展示当日の集計をストップウォッチなしでできるよう、1回ごとの記録を CSV に自動で残す（企画書 8.1・10.3、要件 F-OPS-1/4/7、設計書 3.3・7章・8章）。

## 受け入れ基準
- [ ] 運営UIに端末一覧が出る。1行 = 1端末で、端末番号（`treasure_haul_devices.json` に無い端末は「未登録」＋DeviceId の末尾6文字）、接続／切断、位置合わせ 確認済み／未確認、手の認識（両手／片手／なし／不明）、電池（%。〔電池の警告〕以下は警告色、不明は「—」）、練習完了（練習中〜結果の間だけ ✓／—）、見学。端末番号の順に並ぶ（未登録は後ろに DeviceId 順）。
- [ ] 各行に「見学」と「位置合わせ確認」の切り替えボタンがあり、押すと `DeviceRegistry` に反映され、参加できる端末数と「開始」の押せる条件が変わる。
- [ ] 新しく登録された端末・再接続した端末は「位置合わせ未確認」で始まる（#37 の仮の「確認済み始まり」をやめる）。ただしパラメータ `operation.autoConfirmAlignment`（初期値 false）が true なら従来どおり確認済みで始まる（エディタの ParrelSync 検証用）。
- [ ] 回の途中（Tutorial / Playing）で見学・未確認にした端末は、切断と同じく持っている・予約しているお宝をそっと置いて解放される（`ReleaseReservationsOf` / `ForceRestItemsOf`）。参加人数・目標額は変わらない。見学・未確認の間、その端末のつかむ申請は捨てられる。
- [ ] 参加に戻したとき、その回の本編開始時（Tutorial→Playing の時点）の参加端末に入っていた端末は、すぐに再びつかめる【仮】。入っていなかった端末は次の回から。
- [ ] 練習の色の割り当て順が、端末番号の順（未登録は後ろに DeviceId 順）になる。
- [ ] 端末は手の認識状態と電池残量を、新しい RPC `ThStatus`（端末→ホスト、〔状況報告の間隔〕初期値 2秒、`progress` に追加）で送る。ホストは受け取った値を一覧に出す。〔状況報告の間隔〕×3 を過ぎて届かない値は「不明」。
- [ ] パラメータ版・警告・再読み込みは #37 で出来ている。再読み込みで `treasure_haul_devices.json` も読み直す。JSON が読めない・同じ番号が重複するときは警告に出す。
- [ ] `RoundLogger` が、回が終わる（Result→Waiting）か中断したときに、`persistentDataPath/logs/rounds_YYYYMMDD.csv` に1行、`players_YYYYMMDD.csv` に参加端末ごとに1行を追記する（日付は運営PCのローカル日付、回の開始時刻で決める）。1行ごとに書いて閉じる（Flush）。ファイルが無ければ見出し行から書く。書けなかったら警告に出し、進行は止めない。
  - rounds: 開始時刻（ローカル ISO 8601）, RoundId, 終わり方（完了／中断）, 中断した状態, 人数, モード, 追加要素, 持ち方, パラメータ版, 保留の有無, 延長秒, 回収額, 目標額, 目標達成, 損害額
  - players: RoundId, 端末番号, DeviceId, 色番号, 練習完了（本編開始からの相対ではなく回の開始からの秒）, 初めて持った秒, 初めて回収した秒, 回収した数, 回収額, 減額の回数, 減額の額（各秒は回の開始からの経過秒。無ければ空欄）
- [ ] 個人の記録は、ホストが確定した出来事だけから作る（設計書 8章）: 初めて持った＝S は ThGrabbed・M は ThLifted（片手予約は含めない）、減額＝ホストが適用した ThDamaged だけ（確定時の持ち主、いなければ判定役）、回収＝ThCollected（判定役＝最後に持った人）、練習完了＝練習用お宝の回収の確定。
- [ ] CSV は Excel で開けるよう UTF-8（BOM 付き）、カンマ・改行・ダブルクォートを含む値はクォートする。
- [ ] EditMode テストが全件成功（既存＋新規）。

## 変更対象
- `TreasureHaul/Scripts/Session/DeviceRegistry.cs`: 未確認始まり（`autoConfirm` 切り替え）、端末番号・電池・手・最後の報告時刻のフィールド
- `TreasureHaul/Scripts/Session/ThDeviceNumbers.cs`（新規）: `treasure_haul_devices.json` の読み込み（純粋。`{ "devices": [ { "deviceId": "...", "number": 1 } ] }`）と並び順
- `TreasureHaul/Scripts/Session/SessionHostCore.cs`: 見学・未確認の切り替え（`SetSpectator` / `SetAlignmentConfirmed`）とお宝の解放、本編開始時の参加端末の記憶、つかむ申請の拒否、`ThStatus` の受信、記録用の出来事の受け取り、端末一覧の ReactiveProperty
- `TreasureHaul/Scripts/Session/HostMessageGate.cs` / `HostRoundMachine.cs`: 見学・未確認の端末のお宝のメッセージを捨てる、本編に参加し直せる条件
- `TreasureHaul/Scripts/Treasure/HostItemTable.cs`: 確定した出来事（持った・持ち上げた・減額・回収）を外へ知らせるコールバック（DeviceId 付き）
- `TreasureHaul/Scripts/Treasure/PracticeColors.cs`: 並び順を端末番号順に
- `TreasureHaul/Scripts/Operator/RoundLogger.cs`（新規）: 集計は純粋クラス `RoundRecord`、CSV の組み立ては純粋関数、ファイル書き込みは薄い層
- `TreasureHaul/Scripts/Operator/OperatorView.cs` / `DeviceRowView.cs`（新規）: 端末一覧（行は実行時にプレハブから生成）と切り替えボタン
- `TreasureHaul/Scripts/Session/ThMessages.cs`・`Scripts/System/Rpc.cs`: `ThStatus`（末尾に追加。既存の番号を変えない）
- 端末側（`SessionClient` 付近）: `ThStatus` の定期送信（手は既存の手の追跡状態、電池は `SystemInfo.batteryLevel`）
- `TreasureHaul/Scripts/Params/ThParams.cs`: `operation.autoConfirmAlignment`、`progress.statusInterval`
- `TreasureHaul/Scripts/Content/ThTextTable.cs`: 一覧・ボタン・CSV の見出しの文言
- `Tests/EditMode/`: 新規テスト
- 運営PCのシーン（`TreasureHaulOperatorClient.unity`）・行のプレハブ: uloop 経由で結線
- `TREASURE_HAUL_DESIGN.md`（3.3・6章・7章・8章）・`TODO.md`

## やらないこと（スコープ外）
- 端末番号を運営UIで割り当てる画面（会場では JSON を編集する。雛形は接続中の端末を書き出すボタン1つだけは作る）
- ゲーム設定（モード・追加要素・持ち方）の選択 UI（F-OPS-5*、追加要素）
- ランキング（F-OPS-6*、U-7）
- 位置合わせそのもの（U-1）

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、.unity/.prefab/.asset は uloop 経由で編集
- 判断は純粋クラスに置き、EditMode でテストする（MonoBehaviour は配線だけ）
- `Rpc` の enum は末尾に足す（既存の値を変えない）
- CSV の書き込み失敗で進行を止めない
- **この作業はクラウド環境で行う（2026-10-21 ユーザー承認）**: Unity エディタ・uloop・.NET が無い。コンパイル・EditMode テスト・シーン／プレハブの編集はできないので、
  - C# とテストのコードだけを書く。シーン・プレハブ・.asset は触らない（YAML を直接編集しない）。新規 .cs の .meta は作らない（Unity が開いたときに作る）
  - 端末一覧の行はプレハブを使わず、実行時にコード（`DefaultControls` / `TMP_DefaultControls` 相当）で作る。`OperatorView` の新しい参照（一覧の置き場所）が未設定なら、`_devicesText` と同じキャンバスの下に自動で作り、シーンを結線しなくても動くようにする
  - コンパイルできないので、使う API は既存コードで使われているものに寄せ、Unity / R3 / TMP / NetSync の API はリポジトリ内の使用例で確かめてから使う
  - コンパイル・テスト・PlayMode の確認は、ユーザーがローカルの Unity で行う（PR に明記）
- 端末番号は JSON を手で編集する。雛形は「接続中の端末を書き出す」ボタンで作る（既存の番号は残し、無い端末を number=0 で足す。0 は未登録扱い）

## 検証方法
- EditMode テスト:
  - `DeviceRegistry`: 未確認始まり、再接続で未確認のまま、`autoConfirm` で確認済み始まり
  - `ThDeviceNumbers`: 読み込み・重複の警告・壊れた JSON・並び順
  - 回の途中の見学・未確認でお宝が解放され、つかむ申請が捨てられ、参加人数・目標額が変わらない／参加に戻すと本編開始時の参加端末だけ再びつかめる
  - `RoundRecord`: 初めて持った（S=ThGrabbed、M は ThLifted のみ）、減額は適用分だけ、回収、練習完了、中断の行
  - CSV: 見出し・クォート・空欄・1回分の rounds / players の行
  - 練習の色が端末番号順
- PlayMode/目視（ローカルの Unity で。クラウドではできない）: OperatorClient を起動し、ParrelSync のクローンを2台つないで、一覧の表示、見学・確認の切り替え、1回通した後の CSV の中身を確認する

## 実機確認が必要な項目
- 電池残量（`SystemInfo.batteryLevel`）が PICO で取れるか
- 手の認識状態が実機の見失いに合わせて変わるか

## 外部の素材・サービス（権利・費用・セキュリティ）
- なし
