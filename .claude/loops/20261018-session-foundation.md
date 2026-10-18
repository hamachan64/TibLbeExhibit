# 基盤: パラメータ・ゲーム設定・Rpc・状態遷移・運営UI最小

Issue: #37 ／ ブランチ: `repo/feature-session-foundation`（`repo/develop` から作成）
設計書: `TREASURE_HAUL_DESIGN.md` 第7版 1章・3.1〜3.3・6章（6.1・6.2）・7章・12章 ／ 要件定義書 第7版 F-FLOW・F-OPS-3・N-7・N-8・P章

## 目的
運営PC（ホスト）が進行を決め、全端末が同じ状態・同じ残り時間・同じパラメータで動く「骨組み」を作る。
#38 以降（お宝・オープニング・練習・運営UIの残り）は、この骨組みの上にメッセージと表示を足すだけで作れるようにする。
体験者から見ると、運営PCの「開始」で全端末が同時に 待機→オープニング→練習→本編→結果→待機 と進み、「中断」でどこからでも待機に戻る（まだ画面には何も出ない。状態はログと運営UIで確認する）。

## 受け入れ基準

### A. パラメータ（N-7、設計書 7章）
- [ ] `ThParams`（`[Serializable]`）が P章の6グループ（P-1 進行／P-2 練習／P-3 持ち方／P-4 お宝・減額／P-5 配置・バランス／P-6 運営）をグループごとのフィールドとして持ち、要件定義書 P章と設計書 P章の全項目が初期値どおりに入っている。各値に `[Range]` が付いている
  - 設計書 P章で増えた項目（状態表の送信間隔・再送間隔・重複とみなす時間 等）を含む
  - 握りのしきい値「つかむ」【仮】0.7・「離す」【仮】0.4（#36 の観測値: 開いた手 0.03〜0.19、握った手 0.98〜1.00。#49 で調整）を P-3 に入れる
  - P-5 の人数別の表（出現数 S・M、目標額）は 1〜4人の4行
  - 時間の `[Range]` の下限は、結合確認で短縮できるよう小さめにする（例: オープニング・練習・結果 1秒〜、本編 5秒〜）
- [ ] 組み込みの初期値は ScriptableObject `ThParamsAsset` 1つ（`Assets/_Content/TreasureHaul/Settings/ThParamsDefault.asset`）に入っている
- [ ] 純粋クラス `ThParamsLoader` が、初期値＋JSON 文字列（null 可）から「使う値・版・警告の一覧」を返す:
  - JSON に書いた項目だけ上書きされる（`JsonUtility.FromJsonOverwrite`）
  - JSON が壊れている／null → 初期値のまま（壊れている場合は警告）
  - `[Range]` 外の値 → その項目だけ初期値に戻して警告
  - 組み合わせの矛盾 → **そのグループ全体**を初期値に戻して警告。検査するもの: 減額率3つの合計が 100%／つかむしきい値 ＞ 離すしきい値／価値の範囲の上下限が 100G 刻みで下限≦上限／各人数の出現数（S＋M）≦配置候補点の数／`TutorialSlot` の数 ≧ 4
  - 配置候補点の数と `TutorialSlot` の数は呼び出し側が渡す（`ThEnvironmentInfo`。未定＝null なら、その検査は飛ばす。Environment に置くのは #38・#43）
  - 版 = 重ねた後の値の `JsonUtility.ToJson` の SHA-256 の先頭8桁（16進小文字）。同じ値なら同じ版、1項目でも違えば違う版
- [ ] ホストは起動時と、待機中の「再読み込み」で `Application.persistentDataPath/treasure_haul_params.json` を読む（無ければ初期値・警告なし）
- [ ] エディタメニュー（例: `Tools/Treasure Haul/パラメータの雛形 JSON を書き出す`）で、組み込みの値を JSON に書き出せる

### B. ゲーム設定（N-8）
- [ ] `ThGameSettings`（`[Serializable]`）: モード（目標額＋スコア／目標額のみ*）、追加要素の有無（石像*・L*・ゴーレム*・秘宝*）、持ち方（ビーム／直接*／揺れて運ぶ*）。初期値は「目標額＋スコア・追加要素なし・ビーム」
- [ ] ホストが `persistentDataPath/treasure_haul_settings.json` から読み、`ThState` で配る。無い・壊れている → 初期値（壊れていれば警告）。保存の API（`Save`）はあるが、変更する UI は作らない（F-OPS-5* で作る）

### C. Rpc とメッセージ（設計書 6章）
- [ ] Core の `Rpc` 列挙の**末尾**に、設計書 6.2 の表の全メッセージを足す: `ThState, ThHeartbeat, ThResync, ThParams, ThGrab, ThLift, ThRelease, ThGrabbed, ThGrabDenied, ThLifted, ThFreed, ThPoses, ThRest, ThDamage, ThCollect, ThDamaged, ThCollected, ThForceRest, ThItems`（前作の要素の順番・名前は変えない）。この PR で送受信するのは `ThState / ThHeartbeat / ThResync / ThParams` だけ
- [ ] すべての `Th*` の引数は **`[0]` = RoundId（10進文字列。回が無いときは 0）、`[1]` = 中身の JSON**（`[Serializable]` の DTO を `JsonUtility` で）に統一する。DTO と符号化・復号は `ThMessages`（純粋）にまとめ、壊れた引数・空の引数（65,535 バイト超で空になる場合を含む）は例外を出さずに捨てる
- [ ] 中身:
  - `ThState`: 状態の通し番号、状態、残り秒、シード、参加端末（DeviceId の一覧）、ゲーム設定、パラメータ版、練習の色の割り当て（この PR では空。#43 で入れる）
  - `ThHeartbeat`: 状態の通し番号、残り秒、合計額（この PR では常に 0）、パラメータ版
  - `ThResync`（端末→ホスト）: **端末の状況報告を兼ねる**。DeviceId、知っている RoundId・状態の通し番号、持っているパラメータ版
  - `ThParams`: 宛先の ClientNo（-1 = 全員）、版、パラメータ JSON
- [ ] 全端末とホストは起動時に `NetSyncManager.ConfigureRpcLimit(〔RPC の上限〕60)` を呼ぶ。自分が送った RPC の戻りは ClientNo で捨てる

### D. 進行（設計書 3.1〜3.3、F-FLOW-1/3/4/5/6/8）
- [ ] 純粋クラス `RoundIdGenerator`: `Next(nowUnixMs, previous)` が `now > previous ? now : previous + 1` を返す（同じミリ秒・時計の巻き戻りでも必ず増える）
- [ ] 純粋クラス `DeviceRegistry`: DeviceId を主キーに、今の ClientNo・接続中か・位置合わせ確認済みか・見学か・報告されたパラメータ版を持つ。切断したら「位置合わせ未確認」に戻す。**位置合わせ確認と見学を切り替える UI は #44** なので、この PR では新しく登録された端末を「確認済み・見学でない」で始める（#44 で「未確認」始まりに変える。コードに TODO コメントを残す）
  - 参加できる端末 = 接続中・確認済み・見学でない・報告されたパラメータ版がホストの版と一致
- [ ] 純粋クラス `HostRoundMachine`（時刻は引数で渡す）:
  - `Waiting → Opening`: 「開始」。Waiting 中かつ参加できる端末が1台以上のときだけ。RoundId を `RoundIdGenerator` で作り、シードを決め、参加端末＝その時点で接続中・確認済み・見学でない端末（版の一致は問わない。版が違う端末には `ThParams` を送り、オープニング中に一致すれば残る。設計書 7章。レビューで修正）
  - `Opening → Tutorial`: 〔オープニング〕経過。参加端末から、その時点で参加できない端末（版の不一致を含む）を外す
  - `Tutorial → Playing`: 〔練習〕経過。参加端末を確定する（本編中は変えない。F-FLOW-4）
  - `Playing → Result`: 〔本編〕経過（目標額のみモード*の到達終了は #42 以降）
  - `Result → Waiting`: 〔結果〕経過
  - 「中断」: どの状態からでも Waiting へ（Waiting 中は何もしない）
  - 状態が変わるたびに通し番号を +1 する。残り秒は各状態の長さから数える
  - 保留・「本編開始」・目標額のみモードは**作らない**（#42）。後から足せるよう、遷移の判断を1か所（メソッド1つ）にまとめる
- [ ] 純粋クラス `HostMessageGate`（設計書 6.1 の土台）: 「RoundId が今の回」かつ「送り手の ClientNo が今の参加端末のもので、その端末が今も参加できる」ときだけ true。#38 以降の受け付け条件はこれを前提に足す
- [ ] 純粋クラス `ClientRoundTracker`（時刻は引数で渡す）:
  - `ThState` を受けたら状態・通し番号・残り秒・参加端末・設定・版を採用し、残り秒から自分で数える
  - `ThHeartbeat` の残り秒と自分の残り秒が〔時間を合わせるずれ〕0.25秒以上ずれたら合わせる。ずれが小さければ合わせない
  - 古い RoundId（自分の知っている回より前）のメッセージは捨てる
  - 通し番号が自分の知っているものと違う、または RoundId が新しいのに `ThState` を受けていない → 再送要求（`ThResync`）が必要と判断する。再送要求は〔再送間隔〕より短い間隔で繰り返さない
  - パラメータ版が自分のものと違う → 再送要求が必要と判断する
  - **回の途中から入った**（Opening より後の状態の、知らない RoundId の `ThState` を受けた）ときは、その回は参加しない（「次の回を待つ」フラグ）。次の Waiting で解除
  - `ThHeartbeat` が〔運営PC切断の待ち〕10秒届かない → 回を中止して Waiting に戻る（F-FLOW-8。Waiting 中なら「運営PC未接続」フラグだけ立てる）
  - 状態は R3 の `ReadOnlyReactiveProperty` で公開する（`State`、`RemainingSeconds`、`IsParticipant`、`IsHostLost`）
- [ ] MonoBehaviour `SessionHost`（運営PC。`TreasureHaulOperatorClient` シーン）: 上の純粋クラスと `RpcManager` をつなぐ薄い層
  - 状態が変わるたびに `ThState` を全員へ、〔Heartbeat 間隔〕1秒ごとに `ThHeartbeat` を全員へ（Waiting 中も送る）
  - `ThResync` を受けたら、`DeviceRegistry` に報告を記録し、版が違えばその端末宛ての `ThParams` を送り、`ThState` を送り直す
  - 接続・切断は `NetSyncManager` の接続イベントと、起動時の `GetAliveClients(includeStealthClients: false)` で `DeviceRegistry` に反映する（購読前に接続済みの端末を取りこぼさない。学習メモ参照）。ステルス（運営PC自身）は数えない
  - 状態は R3 で公開し、運営UIが購読する
- [ ] MonoBehaviour `SessionClient`（端末。`TreasureHaulUserClient` シーン）: `ClientRoundTracker` と `RpcManager` をつなぐ薄い層
  - NetSync の準備ができたら1回 `ThResync`（状況報告）を送る。以後は `ClientRoundTracker` が必要と判断したときに送る
  - `ThParams`（自分宛てか全員宛て）を受けたら、`ThParamsLoader` と同じ検査をして採用し、採用後に `ThResync` で新しい版を報告する
  - 状態が変わるたびに `Debug.Log("[SessionClient] ...")` で状態・RoundId・残り秒を出す（画面の表示は #42）

### E. 運営UIの最小（F-OPS-2/3 の一部、設計書 8章）
- [ ] `TreasureHaulOperatorClient` シーンに `OperatorView`（uGUI）を置く: 状態・残り時間・RoundId・接続中の端末数／参加できる端末数・パラメータ版・パラメータとゲーム設定の警告、ボタン「開始」「中断」「パラメータ再読み込み」
  - 「開始」は Waiting 中かつ参加できる端末が1台以上のときだけ押せる
  - 「中断」は確認あり（1回目で「もう一度押すと中断」に変わり、〔数秒〕以内にもう一度押すと中断。待機中は押せない）
  - 「パラメータ再読み込み」は Waiting 中だけ押せる
  - 文字は既存の日本語フォント（`Noto Sans JP SDF`）を使う
- [ ] 前作の仕組みを Treasure Haul のシーンから外す: `TreasureHaulUserClient` の `GameSession`、`TreasureHaulOperatorClient` の `OperatorClient`（`ContentStart` を送る旧ボタン）を取り除き、旧スコア表示パネル（ランキングは U-7 待ち）は非表示にする。取り除く前に、Treasure Haul の3シーンの中にそれらを参照している物が無いか確かめ、あれば取り除かずに報告する。`LeaderboardTracker` は触らない

### F. テスト・確認
- [ ] EditMode テスト（すべて成功。既存のテストも全件成功のまま）:
  - `RoundIdGenerator`: 増える・同じミリ秒・時計の巻き戻り
  - `HostRoundMachine`: 各遷移の時間、参加できる端末0台では開始できない、Waiting 以外での開始は無視、どの状態からでも中断で Waiting、通し番号が状態の変化ごとに増える、Opening→Tutorial で版の合わない端末が外れる、本編中は参加端末が変わらない
  - `DeviceRegistry`: 再接続で ClientNo が変わっても同じ端末、切断で未確認に戻る、参加できる条件
  - `HostMessageGate`: 古い RoundId・参加端末でない送り手・参加できなくなった送り手を拒む
  - `ClientRoundTracker`: ずれの補正（しきい値の前後）、古い RoundId の破棄、通し番号の不一致で再送要求、再送要求の間隔、回の途中参加、Heartbeat 途絶で Waiting
  - `ThParamsLoader`: 上書き（書いた項目だけ）、壊れた JSON、範囲外、組み合わせの矛盾ごとにグループが初期値へ戻る、`ThEnvironmentInfo` が null の検査は飛ばす、版の一致・不一致
  - `ThMessages`: 往復（符号化→復号で同じ値）、壊れた・空の引数で例外を出さない
  - **ホストと端末のつなぎ合わせ**: `SessionHost` / `SessionClient` の中身（送受信の判断）を、送信先を差し替えられる形（例: 送信用のインターフェース）にしておき、テストでメモリ上の偽の通信につないで、ホスト1＋端末2で「開始→各状態→Waiting」を通す。偽の通信で `ThState` をわざと捨てても、`ThHeartbeat` → `ThResync` → `ThState` で追いつくこと、版の違う端末が `ThParams` を受けて参加に戻ること、を確かめる
- [ ] 実際の NetSync での結合確認（エディタ＋ParrelSync のクローン。#36 と同じく、クローンは `-batchmode -executeMethod` で起動する）:
  - 運営PC側（メインのエディタ）で `TreasureHaulOperatorClient` を Play、クローンで端末側を Play（`TreasureHaulUserClient` が batchmode で動かなければ、`SessionClient` と NetSync だけを置いた確認用シーンを `Assets/_Content/TreasureHaul/Prototype/Scenes/` に作ってよい）
  - 短縮したパラメータ JSON（例: オープニング3秒・練習5秒・本編10秒・結果3秒）で、運営UIの「開始」から Waiting に戻るまでを1回通し、両方のログで状態の順番と残り秒のずれ（0.5秒以内。F-FLOW-5）を確かめる
  - 「中断」で端末が Waiting に戻ること、運営PCの Play を止めると端末が約10秒後に Waiting に戻ること、を確かめる
  - 運営UIのスクリーンショットを撮る
  - 確認が終わったら、短縮した JSON を消す（運営PCの persistentDataPath に残さない）

## 変更対象
- `Assets/_Content/Scripts/System/Rpc.cs`: 末尾に `Th*` を追加
- `Assets/_Content/TreasureHaul/Scripts/Session/`（新規）: `RoundState`、`RoundIdGenerator`、`HostRoundMachine`、`DeviceRegistry`、`HostMessageGate`、`ClientRoundTracker`、`SessionHost`、`SessionClient`、メッセージ（`ThMessages`）
- `Assets/_Content/TreasureHaul/Scripts/Params/`（新規）: `ThParams`、`ThParamsAsset`、`ThParamsLoader`、`ThEnvironmentInfo`、`ThGameSettings`
- `Assets/_Content/TreasureHaul/Scripts/Operator/`（新規）: `OperatorView`
- `Assets/_Content/TreasureHaul/Editor/`（新規。Editor 専用 asmdef `TibLbeExhibit.TreasureHaul.Editor`）: 雛形 JSON の書き出しメニュー
- `Assets/_Content/TreasureHaul/Settings/ThParamsDefault.asset`（新規。uloop で作成）
- `Assets/_Content/TreasureHaul/Scenes/TreasureHaulOperatorClient.unity` / `TreasureHaulUserClient.unity`: uloop で `SessionHost`・`OperatorView`・`SessionClient` を置き、前作の仕組みを外す
- `Assets/_Content/Tests/EditMode/`: 上のテストを追加
- 試作の `Prototype/` は触らない（確認用シーンを足す場合を除く）

## やらないこと（スコープ外）
- 保留・「本編開始」・目標額のみモード・オープニング／練習／結果の表示（#42）、練習の色の割り当て（#43）
- お宝・`ThItems` の送信・つかむ／減額／回収の送受信（#38 以降。列挙に名前を足すだけ）
- 端末一覧・見学／位置合わせ確認の切り替え・`treasure_haul_devices.json`（端末番号）・記録 CSV（#44）。この PR の端末番号は使わない
- ゲーム設定を変える UI（F-OPS-5*）、ランキング（U-7）
- `UserClient` のシステムメニュー・スリープ抑止（N-4）、`VRArea` / `WarningWall` の作り直し
- 体験者の画面に出す表示（待機画面の一文など。#42）
- `Packages/manifest.json`・プロジェクト設定・ビルドプロファイルの変更

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity` / `.prefab` / `.asset` は uloop 経由で編集（`.claude/rules/unity_asset_rules.md`）
- 判断は純粋な C# クラスに置き、MonoBehaviour は配線だけにする（設計書 1章）。時刻（Unix ミリ秒・経過秒）は引数で渡し、テストで動かせるようにする
- 定期送信・待ちは UniTask（コルーチン禁止）。状態の公開は R3。`event Action` / `UnityEvent` を新しく公開しない（NetSync の UnityEvent を購読するのは可。必ず解除する）
- パラメータの値をコードやシーンに重ねて書かない（N-7）。数値はすべて `ThParams` から読む
- 名前空間は `STYLY.TIB_Template.TreasureHaul`。Core に足すのは `Rpc` の列挙だけ
- 前作（GIPoker）のコード・`Assets/_Content/Scenes/` のシーンは変えない
- NetSync サーバー・クローンの起動は #36 と同じ方法（`.claude/agent-memory/lbe-learnings.md` の「エディタでの試作」）。止めるときは起動したプロセスだけを止める。クローン側でファイルを編集しない

## 検証方法
- EditMode テスト: 上の F のとおり
- PlayMode / 目視: 上の F の「実際の NetSync での結合確認」。運営UIのスクリーンショット、両方のログ

## 実機確認が必要な項目
- 会場の Wi-Fi で、残り時間のずれが 0.5秒以内に収まるか（#45）
- PICO を一度スリープ・再起動・電波の切れ目に入れたとき、自動で再接続して次の回から参加できるか（#45）

## 外部の素材・サービス（権利・費用・セキュリティ）
- STYLY NetSync サーバー（結合確認でだけ使う）: #36 で確認・`THIRD_PARTY_NOTICES.md` に記録済み（Apache-2.0、商用可、無料、公式）
- ほかは使わない
