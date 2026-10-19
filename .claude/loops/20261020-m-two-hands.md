# M（両手で持つ）

Issue: #40 ／ ブランチ: `repo/feature-m-two-hands`（`repo/develop` から作成）
設計書: `TREASURE_HAUL_DESIGN.md` 第8版 4.2・4.3・4.4（M の手順 1〜4）・6章（6.1・6.2）・9.1／要件定義書 第7版 F-GRAB-4・F-ITEM-5
Issue の追記（設計書 第5版）: M は「片手予約 → 両手成立 → 移動開始」。予約（`ThGrab` 種類=M）ではお宝を動かさず、値段も出さず、「持った」にも数えない。両手成立で `ThLift` → ホストが `ThLifted`（値段つき）を送ったときが「持った」瞬間。予約の解除は、予約した手を開く・〔見失い待ち〕の間見失う → `ThRelease`（予約解除）→ ホストが `ThFreed`。切断・見学への切り替えではホストが自分で解除する。

## 目的
M（壺・石板）を、左右の持ち手を両手で握って持てるようにする。片手で握ると「予約」になり（その人だけが続けて持てる。動かない・値段も出ない）、もう一方の手で反対側の持ち手を握った瞬間に手元へ飛んできて「持った」になる。両手の中点に置かれ、向きは両手を結ぶ線に合う。
持った後に片手が外れても〔M の猶予〕0.5秒は落とさない。本編で M に初めて近づいた人には「両手で持とう」と出る。
これで、コアのお宝（S・M）がすべて持てるようになる。

## ユーザーと決めたこと（2026-10-20 の壁打ち）
- **両手で持った後に片手を見失い、猶予の間に戻らなかったとき**は、#40 では**落とす（Falling）**扱いにする。今の S が見失いを「手を開いた」と同じに扱っているのにそろえ、`TODO(#41)` を付ける。そっと置く（Placing）の動きは #41 で S・M まとめて作る。
- **猶予の間に両手持ちに戻る条件**は、**離した（見失った）その手でもう一度握るだけ**。ビームを持ち手に当て直さなくてよい（手は持ち手のすぐそばにあるので、0.5秒で間に合うように）。
  - オーケストレーターが企画書 5.2・要件 F-GRAB-4・設計書 4.2 にこの内容を追記する（実装エージェントは文書を触らない）。

## 受け入れ基準

### A. メッセージ（`ThMessages.cs`）
- [ ] `ThGrabbedMsg` に `reserve`（bool）を足す。`true` は M の予約の承認で、`value` は 0（値段を載せない）
- [ ] `ThLiftMsg`（端末→ホスト）: `item`、`reqId`（予約の申請ID）、`gen`（予約の承認で受けた世代）
- [ ] `ThLiftedMsg`（ホスト→全員）: `item`、`clientNo`（持ち主）、`reqId`、`gen`、`value`（現在の価値）
- [ ] `ReleaseReason` の**末尾に** `Unreserve = 2`（予約解除）を足す
- [ ] `Rpc.ThLift`・`Rpc.ThLifted` は #37 で追加済み。`Rpc.cs` は触らない

### B. ホスト（`HostItemTable`・`SessionHostCore`。設計書 4.4 の M・6.1）
- [ ] `OnGrab`: お宝の種類と申請の種類が合うときだけ受け付ける（S のお宝は `kind=S`、M のお宝は `kind=M`。L・秘宝は否認）。今の「M はつかめない」を外す
  - M を認めたら: 状態 **Locked**、持ち主・申請ID・判定役を記録、**世代 +1**、**値段は公開しない**（`Revealed` を立てない）、`ExemptReported = false`。`ThGrabbed`（`reserve = true`、`value = 0`）を全員へ送る
  - 同じ申請の再送（同じ持ち主・申請IDで Locked）には同じ予約の承認を送り直す
- [ ] `OnLift(deviceId, clientNo, ThLiftMsg)`（新規）:
  - 送り手が予約の持ち主で、申請ID・世代が一致し、状態が Locked なら: 状態 Held、`Revealed = true`、`ExemptReported = false`、`ThLifted`（今の ClientNo・申請ID・世代・現在の価値）を全員へ送る。**世代は変えない**（設計書 4.2 の「世代を +1 するのは」に入っていない）
  - 同じ持ち主・申請IDですでに Held なら `ThLifted` を送り直す（重複）
  - それ以外は `ThGrabDenied`（お宝、ClientNo、申請ID）を送る
- [ ] `OnRelease` の `Unreserve`: 送り手が持ち主で、申請ID・世代が一致し、状態が Locked なら「つかめる」に戻す（`FreeToRest`: 世代 +1、`ThFreed`）。それ以外は捨てる
  - 予約中・持ち上げ申請中の `Cancel` は今の S と同じ（持ち主なら「つかめる」に戻す）。両手で持った後の `Dropped` も今の S と同じ（Falling、世代は変えない）
- [ ] `ReleaseReservationsOf(deviceId)`（新規）: その端末が予約中（Locked）のお宝をすべて「つかめる」に戻す。`SessionHostCore.OnClientDisconnected` から呼ぶ（Playing 中で、お宝の表があるとき）。見学への切り替えは UI が #44 なので、呼ぶ場所が無ければ `TODO(#44)` を付ける。**持っている（Held 等の）お宝の切断時の扱いは #41**（触らない）
- [ ] 予約中（Locked）のお宝は、置かれているお宝と同じく、ぶつけられれば減額される（`IsDamageExempt` に Locked を入れない）
- [ ] `SessionHostCore.OnRpc` で `ThLift` を受け取り、今の `HandleItemMessage` と同じゲート（Playing・`HostMessageGate`）を通して `OnLift` を呼ぶ。「ThLift などは #40 まで受け取らない」のコメントを消す
- [ ] `ThItems` は今のまま（状態 Locked・持ち主・申請IDが載る）

### C. 端末のモデル（`ClientItemTable`。純粋。設計書 4.4 の端末側）
- [ ] `ClientItem` に足す: 予約した持ち手の番号（`LockHandle`、-1）、予約が認められたか（`Reserved`）、持ち上げの申請中か（`LiftPending`、送った時刻）、予約の承認前に2本目の手が握ったか（`LiftQueued`）、両手のどちらが握っているか（左右）、猶予の状態（始まった時刻・外れた手・理由）
- [ ] **ビームの対象**: `IsGrabbable` は S と M の Resting を対象にする（今の条件に加えて、種類が S か M）。自分が予約中の M は、**予約していない方の手で、予約していない方の持ち手**を狙ったときだけ対象にする（手と持ち手の番号を引数に取るメソッドにする。例: `IsTargetFor(item, hand, handleIndex)`）
- [ ] **予約（手順 1・2）** `TryBeginLock(index, hand, handleIndex, now)`: S と同じ条件で、新しい申請ID、状態 Locked、`Mine`・`ReqPending`、`ThGrab`（`kind = M`）を送る。**お宝は動かさない**
  - 自分宛ての `ThGrabbed`（`reserve = true`、最新の申請ID）: `ReqPending = false`、`Reserved = true`、持ち主 = 自分、世代を更新。**値段は公開しない・発見演出なし・位置も送らない**
  - 他の人宛ての `ThGrabbed`（`reserve = true`）: 状態 Locked・持ち主を記録し、つかめなくする。**値段は公開しない**。お宝は置いた位置のまま
  - 承認前の待ち上限・否認・承認前に予約の手を開いた: 今の S の手順 4 と同じ（`Cancel` を送って Resting）
- [ ] **両手成立（手順 3）** `TryBeginLift(index, hand, handleIndex, now)`: 自分が予約中で、予約していない方の手・持ち手のとき
  - `Reserved` なら: 状態 Flying、`LiftPending`、`ThLift`（申請ID、世代）を送り、手元へ飛び始める
  - まだ予約の承認待ち（`ReqPending`）なら `LiftQueued` にして、承認が届いた瞬間に上と同じことをする（2本目の手がその前に開いたら `LiftQueued` を消す）
  - 自分宛ての `ThLifted`（最新の申請ID）: Held にして、値段の公開・発見演出・位置の送信を始める（今の S の承認と同じ。ここが「持った」瞬間）
  - 他の人宛ての `ThLifted`: 今の S の「他の端末が持った」と同じ（Held、値段の公開、`ThPoses` の位置へ補間）
  - 否認・〔承認の待ち上限〕・`ThLifted` の前にどちらかの手を開いた: 今の S の手順 4 と同じく元の位置へ戻して Resting、`Cancel` を送る（予約も消える）
- [ ] **予約の解除（手順 4）**: `Reserved` の間に
  - 予約の手を開いた → すぐに `ThRelease`（`Unreserve`）を送り、Resting に戻す（お宝は動いていないので見た目はそのまま）
  - 予約の手を見失った（`GripReleaseReason.Lost`）→ 〔見失い待ち〕（`lostWaitSeconds`、1.5秒）待つ。その間に同じ手が握った状態で戻れば予約のまま。戻らない、または開いた状態で戻ったら `Unreserve` を送って Resting
  - ホストの `ThFreed`（世代が新しい）で、自分の予約が消えていれば手から外す
- [ ] **両手で持っている間（F-GRAB-4、U-17）** 片方の手が外れた（開いた・見失った）:
  - 〔M の猶予〕（`mGraceSeconds`、0.5秒）は落とさない。残った手で持ち続ける（〔見失い待ち〕は使わない）
  - 猶予の間に、外れた手が**もう一度握れば**両手持ちに戻る（ビームの当て直しは要らない。見失った手は、握った状態で戻ったとき）
  - 猶予の間に残った手も外れたら、すぐに落とす（`Dropped`。今の S と同じ）
  - 猶予が切れたら落とす（`Dropped`）。**見失いが原因でも #40 では落とす**（`TODO(#41)`: そっと置く Placing にする）
- [ ] **状態表への追いつき**（6.2）: `ThGrabbed`・`ThLifted`・`ThFreed` を取りこぼしても `ThItems` で合う
  - 自分の予約の承認待ち・ホストは自分の Locked → 予約が認められたとして扱う（`LiftQueued` ならそのまま持ち上げへ）
  - 自分の持ち上げ申請中・ホストは自分の Held → `ThLifted` を受けたとして扱う
  - 自分が予約中のつもり・ホストは Resting で世代が新しい → 予約を消して手から外す
  - 他の人の Locked → Locked・持ち主を記録（値段は公開しない）。他の人の Held → 今のまま
- [ ] 今の S の流れ（つかむ・置く・回収・減額）は変えない。左右の手で S を1つずつ持てるのも今のまま

### D. 手と見た目（`HandGrabber`・`TreasureClient`・`Treasure`）
- [ ] `HandGrabber`: ビームが当たった**持ち手の番号**（`Treasure.Handles` の何番目か）も覚える（狙いの保持〔`aimHoldSeconds`〕も持ち手ごと）。握ったとき、対象が S なら今のとおり、M なら予約か両手成立かを `TreasureClient` に伝える。両手で持っている M は、左右両方の `Held` がそのお宝になる
- [ ] `HandGrabber` の解除（`Released`）で理由（開いた／見失った）を `TreasureClient` に渡す（M の予約・猶予で使う。S は今のまま、理由を問わず手を開いたのと同じ扱い）。猶予の中・予約の見失い待ちの中で、その手が再び握ったことも伝える
- [ ] **両手の目標**: M を両手で持つ（飛ぶ途中を含む）間は、両手の中点に置き、向きは両手を結ぶ線に合わせる目標（`Transform`）を毎フレーム更新し、`FlyToHand` / `BeginFollow` はその目標に向ける
  - お宝の「手に合わせる点」は、M では**2つの持ち手の中点**（お宝のローカル）
  - 向き: お宝の「予約した持ち手 → もう一方の持ち手」の向きが、「予約した手 → もう一方の手」の向きに合う。お宝の上はできるだけ真上（両手の線の回りにねじれない）
  - 猶予の間は、外れた瞬間の「残った手 → お宝」の相対の位置・向きを保って、残った手についていく。両手に戻ったら、また中点と線の向きに合わせる（追従の速度で寄せる。障害物の手前で止まる処理は今のまま）
- [ ] 自分が予約中の M は、持ち手を「狙っている」光のままにする（片手で握ったことが本人にわかるように）
- [ ] 回の終わり・消滅・回収・否認・解除で、両手の目標・猶予・予約の状態を消し、両手の `Held` を外す（今の `ReleaseHolder`・`ClearViews` に含める）

### E. 「両手で持とう」（F-ITEM-5、設計書 9.1）
- [ ] 本編（自分が参加端末で Playing 中）で、頭（カメラ）から〔ビームの距離〕（`beamDistance`、1.5m）以内に、まだ誰も持っていない M（Resting）が初めて入ったとき、その M の横（値札の高さ）に、文言 `HintTwoHands`（「両手で持とう」。ThTextTable にすでにある）を数秒出す（表示時間などの見た目の値はコードの定数でよい。コメントで理由を書く）
- [ ] 1回の本編で、その端末に1回だけ。その M が消えたら表示も消す。回の終わり（待機に戻る）で消す（`ClearViews` に含める）
- [ ] どの M に出すかの判断は純粋に（例: `ClientItemTable` か小さな純粋クラス。頭の位置・距離・出したかを引数で受ける）してテストする

### F. テスト・確認
- [ ] EditMode テスト（すべて成功。既存のテストも全件成功のまま）:
  - ホスト: M の予約が認められる（Locked・世代 +1・値段非公開・`reserve`）／S の申請で M はつかめない・M の申請で S はつかめない／予約中は他の人の `ThGrab` を否認／`ThLift` の一致で Held・値段公開・`ThLifted`（世代は変わらない）／持ち主・申請ID・世代のどれかが違う `ThLift` は否認／`ThLift` の再送で `ThLifted` を送り直す／`Unreserve` で「つかめる」に戻る・世代 +1・`ThFreed`／持ち主でない・世代違いの `Unreserve` は捨てる／`ReleaseReservationsOf` で予約だけ外れ、Held のお宝は変わらない／Locked のお宝はぶつけられると減額される
  - 端末: 予約の申請で動かない・値段も出ない／予約の承認で `Reserved`、他の人には Locked・値段は「？」のまま／予約していない方の手・持ち手だけが2本目の対象になる（同じ手・同じ持ち手は不可）／承認の前に2本目が握ったら、承認の瞬間に `ThLift`／`ThLifted` で Held・値段公開・発見演出／`ThLift` の否認・待ち上限で Resting・`Cancel`／予約の手を開くと `Unreserve`／予約の手を見失って 1.5秒で `Unreserve`、その前に握って戻れば予約のまま／両手で持った後に片手を開き、0.5秒以内に握り直すと両手に戻る・0.5秒を過ぎると `Dropped`／猶予の中で残った手も開くとすぐ `Dropped`／片手を見失って猶予が切れると `Dropped`（#41 まで）／`ThItems` での追いつき（予約・持ち上げ・解除・他の人の Locked）
  - 「両手で持とう」: 1.5m 以内の Resting の M で1回だけ、遠い・持たれている・S には出ない、2回目は出ない
  - **ホストと端末のつなぎ合わせ**（今の偽の通信でホスト1＋端末2）: 端末A が M を予約 → 端末B は同じ M をつかめない → A が両手成立 → 全員で Held・値段公開 → A が両手を開くと落ちて、荷台の範囲なら回収される／A が予約の手を開くと B がつかめるようになる／A が切断すると予約が外れる
- [ ] エディタでの確認（#38・#39 と同じやり方。結果を報告する）:
  - 端末側をメインのエディタで XR Interaction Simulator の手（両手）で動かし、ホストは ParrelSync のクローンを batchmode で起動する（時間の短縮はメモリ上の上書き）
  - 確かめること: M の持ち手を片手で握ると動かず値札は「？」のまま／もう一方の手で反対側の持ち手を握ると手元へ飛んできて値段が出る／両手を動かすと中点についてきて、両手の線に向きが合う／片手を開いて 0.5秒以内に握り直すと両手に戻る／片手を開いたまま 0.5秒たつと落ちる／荷台の上で両手を開いて回収できる／予約中に手を開くと解除される／M に近づくと「両手で持とう」が1回だけ出る。スクリーンショットを撮る
  - 確認が終わったら、起動したクローンのプロセスと Play を止める

## 変更対象
- `Assets/_Content/TreasureHaul/Scripts/Treasure/`: `ClientItemTable.cs`（予約・両手成立・猶予・追いつき・ビームの対象・「両手で持とう」の判断）、`HostItemTable.cs`（`OnGrab` の M・`OnLift`・`Unreserve`・`ReleaseReservationsOf`）、`HandGrabber.cs`（持ち手の番号・解除の理由・握り直し）、`TreasureClient.cs`（配線・両手の目標・予約中の光・「両手で持とう」の表示）、`Treasure.cs`（M の手に合わせる点と向き、必要なら）、`TreasureTypes.cs`（`ReleaseReason.Unreserve`）。判断が大きくなるなら、純粋クラスを新しく分けてよい（例: 両手持ちと猶予の状態）
- `Assets/_Content/TreasureHaul/Scripts/Session/`: `ThMessages.cs`（`ThGrabbedMsg.reserve`・`ThLiftMsg`・`ThLiftedMsg`）、`SessionHostCore.cs`（`ThLift` の受信・切断時の予約の解除）、`SessionClientCore.cs`（`ThLifted` の受信・`ThLift` の送信の経路）
- `Assets/_Content/TreasureHaul/Scripts/View/`: 「両手で持とう」の表示（今の `DamagePopup` を使うか、新規）
- `Assets/_Content/Tests/EditMode/`: テストの追加（既存のファイルに足すか、新しいファイル）
- 触らない: `Rpc.cs`、`ThTextTable.asset`（`HintTwoHands` はある。足す必要が出たら理由を報告）、文書（企画書・要件定義書・設計書・TODO はオーケストレーターが更新する）、シーン・プレハブ（必要になったら理由を報告。`TH_M_Urn` の持ち手は2つある）

## やらないこと（スコープ外）
- 見失い待ち（Hovering）・そっと置く（Placing）・手の飛びと補正・離れたら置く・**持っているお宝の**持ち主の切断（`ThForceRest`）（#41）。M の見失いで猶予が切れたときは落とす（`TODO(#41)`）
- 見学への切り替えでの予約の解除の呼び出し（UI が #44。メソッドだけ用意する）
- L（2人で持つ）・秘宝（追加要素）
- 練習（#43。練習用お宝は S）
- 持ち方の切り替え（5.6 D）
- 本物の M のモデル（素材 #46。今の `TH_M_Urn` を使う）

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity` / `.prefab` / `.asset` は uloop 経由で編集（YAML を直接書かない）
- 判断は純粋クラスに置き、MonoBehaviour は配線と見た目だけ（設計書 1章）。時刻・手の状態は引数で渡す（テストで時間を進められるように）
- 数値は `ThParams`（`mGraceSeconds`・`lostWaitSeconds`・`approvalWaitSeconds`・`beamDistance`・`aimHoldSeconds`・`flyTime`。すでにある）から取る。新しいパラメータは足さない
- 文言は `ThContentSet`（`HintTwoHands`）から取る。列挙は**末尾にだけ**足す
- メッセージは今の形（`[0]` = RoundId、`[1]` = JSON）。`ThGrabbedMsg.reserve` は古い送り手から来ても `false`（JsonUtility の既定値）
- 世代の規則（設計書 4.2）を守る: +1 は持ち主を認めたとき（予約の承認を含む）・つかめるに戻したとき・回収・消滅。`ThLifted`・`Dropped` では変えない

## 検証方法
- EditMode テスト: 上の F のとおり
- PlayMode / 目視: 上の F の「エディタでの確認」。スクリーンショット、ホスト・端末のログ

## 実機確認が必要な項目
- 本物の両手で、2本目のビームを反対側の持ち手に当てて握れるか（片手で握ったまま、もう一方の手を向ける姿勢が自然か。#49）
- 〔M の猶予〕0.5秒で、握りが一瞬ゆるんだ・手が一瞬見えなくなったときに落ちないか（#49）
- 両手を結ぶ線に向きを合わせたとき、手の認識のぶれでお宝が震えないか（#49）
- 4台で、予約・両手成立の承認待ちの遅れが気にならないか（#45）

## 外部の素材・サービス（権利・費用・セキュリティ）
- なし（仮の M のプレハブ `TH_M_Urn`・既存の文言・演出だけを使う）
