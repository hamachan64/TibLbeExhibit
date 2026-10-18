# 配置、S を持つ・置く・回収、表示板

Issue: #38 ／ ブランチ: `repo/feature-grab-collect-s`（`repo/develop` から作成）
設計書: `TREASURE_HAUL_DESIGN.md` 第8版 4.1〜4.4・5章（回収）・6章（6.1・6.2）・9章 ／ 要件定義書 第7版 F-ITEM-1〜4・F-GRAB-1〜5・F-SCORE-1/2・F-DMG-1（人とはぶつからない）
Issue の追記（設計書 第5版・第7版の反映）: 申請ID、受け付け条件 6.1、`ThFreed` と `ThRest`/`ThCollect` の送り直し、**持っているお宝の位置は RPC（`ThPoses`）で送る（`NetSyncObject` は使わない）**、`ConfigureRpcLimit`、32KB、自分の RPC は ClientNo で捨てる、追従は `SweepTest` で障害物に向かう成分を切る。試作コード（`Scripts/Prototype/`）は参考にしてよいが本番から参照しない。

## 目的
S だけで「見つける → ビームを持ち手に当てて握る → 手元へ飛んでくる → 運ぶ → 荷台で回収」が、全端末に同期して動く。
本編が始まるとエリアにお宝が散らばり（全端末で同じ配置・同じ値段）、持った瞬間に値段が全員に見え、荷台に置くと荷台の表示板の合計額が増える。

## ユーザーと決めたこと（2026-10-18 の壁打ち）
- **表示板は「荷台に入っているお宝の合計額」だけを出す**（書式「1,250G」。新しい文言 ID を1つ足す）。目標額・直前に回収したお宝（「+560G 傷物」）は表示板に出さない。**目標額の見せ方と、目標に届いたときの知らせ（F-SCORE-4）は #42 で決める**。オーケストレーターが企画書 5.4・要件 F-SCORE-2・設計書 5章/9章をこの内容に更新する（実装エージェントは文書を触らない）。
- 発見音（音「発見」）は**空のまま**進める（鳴らす処理と ID だけ。音は素材の段階で入れる）。
- Environment に **`VenueRoot` と仮の会場（配置候補点・荷台・瓦礫）を仮で置く**。#46（3D モデル）と会場の作り込みで差し替える。

## 受け入れ基準

### A. 仮の会場とお宝カタログ（設計書 1章・4.1）
- [ ] `TreasureHaulEnvironment` に `VenueRoot` を作り、その子に置く（uloop で編集）:
  - 5m × 11m の範囲。長辺の片方の端に**仮の荷台**（横 約3m・短辺方向に長い箱、腰より低い高さ。上面の上に回収範囲のトリガー）
  - **配置候補点** 30 個以上（6.1 の 4人の S14＋M9＝23 個より多く）。各点は「荷台からの距離帯」0（近い）/1（中）/2（遠い）を持つ。荷台の前（荷台から 1.5m 以内）と、縁から〔縁の余白〕＋警告の壁の距離の内側には置かない（F-ITEM-2）
  - 腰より低い仮の瓦礫（箱など）を数個。床のコライダーは既存の `Ground(Collider)` を使うか、`VenueRoot` の下に作る
  - これらをまとめて参照する MonoBehaviour（例: `ThVenue`）。配置候補点（`VenueRoot` ローカル座標と距離帯）と、荷台の回収範囲（`VenueRoot` ローカルの Bounds）を返す
  - 既存の `VRArea` / `WarningWall` / 旧スコア表示は動かさない（作り直しは別 Issue）
- [ ] 同期する位置はすべて `VenueRoot` のローカル座標で送受信する（設計書 1章）
- [ ] ScriptableObject `ThTreasureCatalog`（`Assets/_Content/TreasureHaul/Settings/`、uloop で作成）: 1件＝種類（S / M / L* / 秘宝*）、表示名、プレハブ、今回の展示で使うか。練習用お宝のプレハブ（1つ）も持つ。`Validate()`（プレハブ欠け・種類ごとに「使う」が0件 等）と EditMode テスト
- [ ] `ThContentSet` に `treasures`（`ThTreasureCatalog`）を足し、`Validate()` に含める
- [ ] 仮のプレハブ（プリミティブで作る。外部素材は使わない）: S を2種類以上、M を1種類（持ち手は左右2つ）、練習用1種類。各プレハブに光る持ち手（ビームが当たる用のコライダー付き。見た目より大きめ）
- [ ] カタログのハッシュ: 「使う」お宝の並び（種類と ID）の SHA-256 先頭8桁。ホストは `ThState` に、端末は `ThResync` に載せ、`DeviceRegistry` の「参加できる」条件に「カタログのハッシュが一致」を加える（パラメータ版と同じ扱い。オープニングの終わりで一致しない端末は外れる）。運営UIの警告にも出す
- [ ] `ThEnvironmentInfo.PlacementPointCount` を `ThVenue` から埋める（ホスト・端末とも。パラメータの組み合わせ検査が働く）

### B. 配置（F-ITEM-1/2、設計書 4.1）
- [ ] 純粋クラス `TreasureSpawner`: 入力＝シード・参加人数・`ThParams`（P-4 の価値、P-5 の人数別の表）・配置候補点の一覧（距離帯つき）・カタログの「使う」お宝の一覧。出力＝お宝ごとに 番号（0 始まり）・種類・元の値段・候補点の番号・`VenueRoot` ローカルの位置・Y 回転・プレハブ（カタログの何番目か）
  - 乱数は `System.Random(seed)` 1つだけ。**呼ぶ順番を固定してコメントに書く**（例: 候補点のシャッフル → 種類の割り当て → 各お宝の値段 → Y 回転 → プレハブ）
  - 値段は 100G 刻み。荷台から遠いほど高くなりやすくする: 範囲の段数から、距離帯 0 は「2回引いて小さい方」、1 は「1回」、2 は「2回引いて大きい方」（初期値の作り方。数値を足さない）
  - 出現数は参加人数の行（1〜4 人。範囲外はクランプ）の S・M。候補点が足りないときは置ける数だけ置き、警告を返す（例外を出さない）
- [ ] ホストと全端末が、本編開始（Playing に入った `ThState` のシード・参加人数）で同じ表を作る。M も置く（見た目は仮の M。**#38 では M はつかめない**: ビームの対象にせず、ホストも種類 M の申請を否認する。#40 で持てるようにする）
- [ ] 回の途中から入った端末（`WaitNextRound`）は、その回のお宝を表示しない。参加していない端末（見学など）は表示するが持てない
- [ ] 待機に戻ったら（結果の終わり・中断）、全端末でお宝・値札・表示・Tween・購読を消す（設計書 9章、N-2）

### C. 握る判定（Core、設計書 4.4）
- [ ] Core（`Assets/_Content/Scripts/System/XR/`）に `HandGripTracker` を新規作成。`HandPinchTracker` は変えない
  - 純粋な判定部分（例: `GripStateMachine`）と、XR Hands から関節を読む MonoBehaviour に分ける
  - 握りの強さ＝人差し指〜小指の曲がり具合の平均（0〜1）。試作の `FingerCurl` と同じ計算を Core 側に**写して**使う（試作を参照しない）
  - しきい値は2つ（つかむ ＞ 離す。ヒステリシス）。握った状態から、強さが「離す」しきい値を〔離す判定〕の間続けて下回ったら開いたとする。少しの間だけ下回って戻ったら開いたことにしない
  - 解除の理由（手を開いた／見失い）を出す。見失いの扱いの細部（Hovering 等）は #41 なので、#38 では「見失ったら解除の理由＝見失い」を出すところまで
  - 状態は R3 で公開（例: `IsGripping`、`Strength`、`IsTracked`、解除の Observable）
  - Core は TreasureHaul を参照できないので、しきい値・時間はプロパティ／引数で受け取り、TreasureHaul 側が `ThParams.grip` から渡す

### D. ビームとつかむ（F-GRAB-1/2/5、設計書 4.3・4.4）
- [ ] `HandGrabber`（手ごとに1つ。左右で独立）:
  - **ビーム**: 肩の推定位置（頭から〔肩のずらし量〕）→ 手のひらの方向へ、半径〔吸い付き半径〕・長さ〔ビームの距離〕の SphereCast。持ち手だけに当たる（アバター・瓦礫・床には止められない／当たらない、のどちらにするかは実装で決めて報告）。ビームの見た目（線）を出す
  - ビームが当たっている持ち手は、演出「持ち手（ビームが当たっている）」に切り替える。外れたら「持ち手（通常）」
  - **狙いの保持**: 握った瞬間にビームが外れていても、〔狙いの保持〕以内に当たっていた持ち手を対象にする
  - 「つかめる」でないお宝（他の人が持っている・飛んでいる・荷台の範囲内・回収済み・M）は対象にしない
  - 自分が参加端末でない、または Playing 以外のときは、つかめない（ビームは出してよい）
  - 4.5 のため、「ビームの長さ」と「つかんだ後の目標の決め方」を差し替えられる形にしておく（切り替え自体は作らない）
- [ ] **つかむ手順（S）**（設計書 4.4 の 1〜5。純粋クラスにして EditMode テストする。例: 端末側 `GrabRequestTracker`、ホスト側 `HostItemTable`）:
  1. 端末は新しい申請ID（端末ごとの通し番号）を振り、お宝を Flying にして手元へ〔飛ぶ時間〕で動かし始め、`ThGrab`（お宝、申請ID、知っている世代、種類=S）を送る
  2. ホストは受け付け条件（6.1）を満たせば先着で認め、世代 +1、`ThGrabbed`（お宝、ClientNo、申請ID、世代、現在の価値）を全員へ。満たさなければ `ThGrabDenied`（お宝、ClientNo、申請ID）
  3. 端末は、**そのお宝への自分の最新の申請ID**と一致する `ThGrabbed` だけで Held にし、値札の公開・発見演出・位置の送信を始める。飛び終わっても承認が無ければ手元で止めて待つ
  4. 否認・〔承認の待ち上限〕超過・承認前に手を開いた → 元の位置へ戻して Resting、`ThRelease`（お宝、申請ID、理由=取りやめ）。ホストは、その申請IDを認めていればロックを外して「つかめる」に戻す（世代 +1、`ThFreed`）。まだ届いていない申請なら、その申請IDを取りやめ済みとして覚え、後から届いても認めない
  5. 最新ではない申請IDへの `ThGrabbed` が自分に届いたら、その申請IDで `ThRelease`（取りやめ）を返す。ホストは今の持ち主の申請IDと一致する取りやめだけを受理する
- [ ] 他の端末から見ると: `ThGrabbed` を受けたらそのお宝を「つかめない」にし、以後は持ち主の `ThPoses` の位置へ補間する
- [ ] 左右の手で S を1つずつ同時に持てる。同じお宝を左右の手で取り合わない（片手が申請中・保持中のお宝は、もう一方の手の対象にしない）

### E. 持っている間・置く（設計書 4.2・4.3）
- [ ] Held の追従: 重力なしの Rigidbody（interpolate、ContinuousSpeculative）。目標（握った手の位置・向き）へ、位置は速度、向きは角速度で追従。速度の上限〔追従速度の上限〕。速度を決めた後、`Rigidbody.SweepTest` で**障害物に向かう成分だけを切る**（表面に沿う成分は残す。角のために最大3回）。試作 `ProtoVelocityFollower` の方式を本番側に写す
- [ ] 手を開いたら（`HandGripTracker` の解除＝手を開いた）: 重力ありにして Falling。判定役（持ち主だった端末）が物理を動かし続け、`ThPoses` に載せる
- [ ] 落下が〔止まったとみなす時間〕静止したら Resting にして `ThRest`（お宝、世代、`VenueRoot` ローカルの位置・向き）。床より明らかに下（例: `VenueRoot` の床 −0.5m）に落ちたら、その場の床の高さに戻して Resting
- [ ] **世代の扱い（設計書 4.2 を補う）**: ホストが世代を +1 するのは「持ち主を認めたとき（`ThGrabbed`）」と「つかめるに戻したとき（`ThFreed`）」と「回収したとき」。手を開いて落とす `ThRelease`（理由=落とした）では世代を変えない（ホストは持ち主を外して Falling にするだけ）。こうすると `ThRelease` を取りこぼしても、判定役の `ThRest`/`ThCollect`（承認された世代）がそのまま受理される。オーケストレーターが設計書 4.2 に追記する
- [ ] 判定役でない端末のお宝は kinematic にし、受け取った位置へ補間する
- [ ] 人とはぶつからない（F-DMG-1）: お宝は `HandBone` レイヤーと、自分・他人の NetSync アバター（`Avatar Local (Transparent)` の頭のコライダーを含む）のコライダーと当たらない。方法（レイヤーの追加と衝突表／`Physics.IgnoreCollision`）は実装で選び、ProjectSettings を変えたら理由を報告する（`.claude/rules/unity_asset_rules.md` 4）。既存レイヤーの番号は変えない
- [ ] #38 で**作らない**もの: 見失い→Hovering、手の飛び、〔離れたら置く距離〕で Placing、持ち主の切断の `ThForceRest`（#41）、衝突の減額（#39）。見失った手で持っていたお宝は、#38 では「手を開いた」と同じく Falling にしてよい（#41 で置き換える。コードに TODO を残す）

### F. 位置の同期（設計書 6章）
- [ ] `ThPoses`: 判定役は、自分が動かしているお宝（承認後の Flying・Held・Falling）の `VenueRoot` ローカルの位置と向きを、〔位置の送信間隔〕ごとに**1つの RPC にまとめて**送る（動かしているお宝が無ければ送らない）。1回の RPC は 32KB まで
- [ ] 受け側は、受信時刻で並べて〔補間の遅れ〕だけ過去の時刻を、受け取った2つの位置の間で補間する（遅れて届いたら短い間だけ先読み）。補間の計算は純粋クラスにして EditMode テストする
- [ ] 自分が送った RPC の戻りは ClientNo で捨てる（既存の `SessionClientCore` と同じ）。`ConfigureRpcLimit` は #37 で入っているものを使う

### G. ホストの状態表・回収（設計書 4.2・5章・6.1・6.2、F-SCORE-1）
- [ ] 純粋クラス `HostItemTable`（`SessionHostCore` から使う）: お宝ごとに 状態・持ち主（ClientNo と DeviceId）・承認した申請ID・判定役・世代・元の価値・現在の価値・ぶつけた回数（#38 では常に 0）・値段公開済みか・回収済み/消滅か・置かれている位置・練習用か。本編開始で `TreasureSpawner` から作り、待機で消す
- [ ] 受け付け条件（6.1）を `HostMessageGate` の上に実装: `ThGrab`（つかめる・取りやめ済みでない・本編のお宝は Playing 中・種類 S）／`ThRelease`（今の持ち主・申請ID・世代が一致）／`ThRest`・`ThCollect`（判定役・世代が一致。同じ世代は1回だけ受理）。ほかのメッセージ（`ThLift`・`ThDamage` 等）は #38 では受け取らない
- [ ] **値段の公開**: 初めて `ThGrabbed` を出したときに「値段公開済み」にする（以後ずっと公開）
- [ ] **回収**: 判定役が「荷台の回収範囲の中にあり、誰も持っていない」（手を開いて落ちている途中を含む）を見て `ThCollect`（お宝、世代）。ホストは Playing 中の本編のお宝なら1回だけ受理し、ホストの現在の価値を合計に加え、`ThCollected`（お宝、合計額、価値、傷物か）を全員へ。全端末はそのお宝を消す。Playing 以外で届いた `ThCollect` は数えない（F-SCORE-1）
- [ ] 荷台の回収範囲の中に置かれているお宝は「つかめない」（設計書 4.2）
- [ ] `ThHeartbeat` の合計額（今は常に 0）を本当の合計額にする
- [ ] `ThItems`: Tutorial・Playing の間、〔状態表の送信間隔〕ごとに全お宝の 状態・持ち主・世代・現在の価値・ぶつけた回数・値段公開済みか・回収/消滅・置かれている位置 を送る（お宝が0個なら送らない）。端末は、自分の表示・状態が食い違っていれば `ThItems` に合わせる（値札・つかめるか・置き位置・消す）。`ThGrabbed`・`ThFreed`・`ThCollected` を取りこぼしても次の `ThItems` で追いつく
- [ ] 判定役は、送った `ThRest`・`ThCollect` が `ThItems` に反映されるまで〔再送間隔〕ごとに送り直す（ホストは同じ世代を1回だけ受理）
- [ ] Result に入ったら、全端末でお宝をその場で止め（つかめない）、ホストは以後のお宝のメッセージを受け付けない。持っていたお宝は手から外す（メッセージは送らない）

### H. 値札・発見演出・表示板（F-ITEM-3/4、F-SCORE-2〔変更後〕、設計書 9章・9.1）
- [ ] **値札**: お宝の上に浮かぶ文字（全員に見える、カメラの方を向く）。値段公開前は文言「？」（`PriceUnknown`）、公開後は「{0:N0}G」（`PriceFormat`）で現在の価値
- [ ] **発見演出**: 〔発見演出〕以上のお宝が `ThGrabbed` で**初めて**公開されたとき、全端末で演出「発見の光」と音「発見」（音は空のままで、空なら何もしない）。`ThItems` で追いついて公開を知った場合は値札だけ（演出はしない）
- [ ] **表示板**: 荷台の上に、その回の本編の合計額だけを出す。新しい文言 ID `BoardTotalFormat`（書式「{0:N0}G」）を `ThTextId` の**末尾に**足し、`ThTextTable` に登録する。Tutorial〜Result の間だけ表示し、Waiting・Opening では消す。待機に戻ったら 0 に戻す。合計は `ThCollected` と `ThHeartbeat` から取る
- [ ] 見た目・文言は一覧（`ThContentSet`）から取る: 演出「持ち手（通常）」「持ち手（ビームが当たっている）」「発見の光」はプロジェクト内で作った簡単なマテリアル／パーティクル（外部素材なし）を `ThFxCatalog` に登録する。文言「？」「{0:N0}G」と新しい `BoardTotalFormat`
- [ ] 文言を足したら、フォントのメニュー（`ThFontExtraMenu`）を実行し、全文字が表示できるテスト（`ThContentTests`）を通す
- [ ] 文言「+{0:N0}G」「+{0:N0}G 傷物」は**使わない**（表示板に直前の回収を出さないため。一覧からは消さない）。目標額・目標到達の表示は作らない（#42）

### I. 配線（シーン）
- [ ] `TreasureHaulUserClient`: XR Rig の両手に `HandGripTracker`＋`HandGrabber`。お宝の生成・同期をまとめる端末側の部品（例: `TreasureClient`）を置き、`SessionClient` の状態に従わせる
- [ ] `TreasureHaulOperatorClient`: `SessionHost` に `HostItemTable` をつなぐ（運営PCも Environment の `ThVenue` から配置候補点と荷台の範囲を読む）
- [ ] 判断は純粋クラスに置き、MonoBehaviour は配線だけ（設計書 1章）。時刻は引数で渡す

### J. テスト・確認
- [ ] EditMode テスト（すべて成功。既存のテストも全件成功のまま）:
  - `TreasureSpawner`: 同じシード・人数・候補点・カタログで同じ表（決定性）、シードが違えば違う表、人数ごとの出現数、値段が 100G 刻みで範囲内、遠い帯ほど平均が高い（多数のシードで）、候補点が足りないときに例外を出さない、同じ候補点を2回使わない
  - `ThTreasureCatalog`: `Validate()`、ハッシュ（並びが同じなら同じ、1件違えば違う）
  - `HandGripTracker` の判定部分: しきい値の前後、ヒステリシス、〔離す判定〕より短く下回っただけでは離さない、見失いで解除の理由＝見失い
  - つかむ手順: 承認、否認、取りやめ（承認前に手を開いた・待ち上限）、遅れた承認、**取りやめ→再申請→古い承認の到着**（新しい申請が消えない）、同じお宝への同時申請は先着1人、世代番号による古いメッセージの破棄、種類 M の否認、参加端末でない送り手の申請の拒否
  - 回収: `ThCollect` の送り直しを二重に数えない、Playing 以外の `ThCollect` を数えない、荷台の中のお宝はつかめない、合計額
  - `ThItems` による復旧: `ThGrabbed`／`ThFreed`／`ThCollected` を捨てても、次の `ThItems` で端末の状態がホストと一致する
  - `ThPoses` の補間（2点の間・先読みの上限）
  - **ホストと端末のつなぎ合わせ**: #37 の偽の通信（`IThTransport`）でホスト1＋端末2をつなぎ、開始→Playing（両端末で配置が同じ）→端末Aがつかむ→端末Bから見てつかめない→Aが置く→Bがつかめる→Aが荷台で回収→合計額が全員で同じ、を通す
- [ ] エディタでの確認（結果を報告する）:
  - 端末側をメインのエディタで、XRI の「XR Interaction Simulator」の手で動かす（取り込み方は `.claude/agent-memory/lbe-learnings.md` の「エディタでの試作」。`TreasureHaulUserClient` シーン自体にシミュレーターを残さない。確認用シーンを `Assets/_Content/TreasureHaul/Prototype/Scenes/` に作ってよい）
  - ホスト（運営PC）は ParrelSync のクローンを `-batchmode -executeMethod` で起動する（#36・#37 と同じ）。短縮した時間（例: 練習5秒・本編60秒）はファイルを置かずに、起動メソッドの中でメモリ上で上書きする（`persistentDataPath` に一時ファイルを残さない）
  - 確かめること: 本編開始でお宝が散らばる／ビームが持ち手に当たると光る／握ると手元へ飛んでくる／持ったら値札が値段になる／瓦礫に押し付けてもすり抜けず滑る／手を開くと落ちて止まる／荷台で手を開くと回収されて表示板の合計が増える／左右の手で1つずつ持てる。スクリーンショットを撮る
  - 2台目の端末（クローン、batchmode）で、同じ配置になること（配置の表のハッシュをログに出して比べる）と、メインの端末が持ったお宝の位置が届いていること（ログ）を確かめる
  - （任意）端末4台分（クローン3台が疑似の `ThPoses` を 30回/秒で送る）で、捨てられた RPC（`Rate limited`）が無いか、遅れを測る。ディスク・時間の都合でできなければ、理由を報告する
  - 確認が終わったら、起動したクローンのプロセスと Play を止める

## 変更対象
- `Assets/_Content/Scripts/System/XR/`: `HandGripTracker`（新規、Core）と純粋な判定部分・指の曲がり具合の計算
- `Assets/_Content/TreasureHaul/Scripts/Treasure/`（新規）: `TreasureSpawner`、`ThTreasureCatalog`、`ThVenue`、`HostItemTable`、端末側のお宝の状態・つかむ手順・補間、`Treasure`（お宝の MonoBehaviour）、`HandGrabber`、`TreasureClient` 等
- `Assets/_Content/TreasureHaul/Scripts/View/`（新規）: 値札・表示板・発見演出
- `Assets/_Content/TreasureHaul/Scripts/Session/`: `ThMessages`（新しい DTO）、`SessionHostCore` / `SessionHost`（状態表・合計・`ThItems`・カタログのハッシュ）、`SessionClientCore` / `SessionClient`（お宝のメッセージを渡す・カタログのハッシュ）、`DeviceRegistry`（カタログのハッシュ）
- `Assets/_Content/TreasureHaul/Scripts/Content/`: `ThContentSet`（`treasures`）、`ThTextTable`（`BoardTotalFormat` を末尾に）
- `Assets/_Content/TreasureHaul/Settings/`: `ThTreasureCatalog.asset`（新規）、`ThContentSet.asset`・`ThTextTable.asset`・`ThFxCatalog.asset`（登録）
- `Assets/_Content/TreasureHaul/Prefabs/`・`Materials/`（新規）: 仮のお宝プレハブ、持ち手・発見の光のマテリアル
- `Assets/_Content/TreasureHaul/Scenes/` の3シーン（uloop で編集）
- `Assets/_Content/TreasureHaul/Prototype/Scenes/`・`Prototype/Editor/`: 確認用シーン・起動メソッド（必要なら）
- `Assets/_Content/Tests/EditMode/`: 上のテスト
- `ProjectSettings/TagManager.asset`・`DynamicsManager.asset`: レイヤーと衝突表を変える場合のみ（理由を報告）
- 文書（企画書・要件定義書・設計書・`TODO.md`）は**オーケストレーターが更新する**ので触らない

## やらないこと（スコープ外）
- 減額・ヒビ・`ThDamage`（#39）、M をつかむ・`ThLift`・「両手で持とう」（#40）、見失い→Hovering・手の飛び・〔離れたら置く距離〕・`ThForceRest`・持ち主の切断（#41）
- 練習用お宝の配置・色分け・練習の回収（#43。カタログに練習用プレハブを持つところまで）
- 目標額の表示・目標到達の知らせ・荷台を光らせる案内・結果（#42）、記録 CSV（#44）
- 本物の 3D モデル・音（#46・#47）、`VRArea` / `WarningWall` の作り直し、位置合わせ（U-1）
- `Packages/manifest.json`・ビルドプロファイルの変更

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用（コントローラーの入力に頼らない）、`.unity` / `.prefab` / `.asset` は uloop 経由で編集（`.claude/rules/unity_asset_rules.md`）
- 判断は純粋な C# クラスに置き、MonoBehaviour は配線だけ。時刻は引数で渡す
- `event Action` / `UnityEvent` を新しく公開しない。状態の公開は R3。コルーチン禁止（定期送信は UniTask か Update）。手元へ飛ぶ・元の位置へ戻すなどの演出の動きは DOTween でよい（物理で動かす追従・落下は Rigidbody）
- 数値はすべて `ThParams` から読む。コード・シーンに同じ値を重ねて書かない（N-7）。新しい数値が要るときは、作る前に報告する（パラメータは要件定義書 P章で管理）
- 名前空間 `STYLY.TIB_Template.TreasureHaul`（Core は `STYLY.TIB_Template`）。Core に足すのは `HandGripTracker` まわりだけ
- 試作コード（`Scripts/Prototype/`）を本番から参照しない（写すのはよい）
- `Rpc` 列挙は変えない（#37 で `Th*` は足し済み）
- 新しいシリアライズ用の列挙値は末尾にだけ足す
- NetSync サーバー・クローンの起動は #36・#37 と同じ方法。止めるときは起動したプロセスだけ。クローン側でファイルを編集しない。テストの前に Play 中でないか確かめる
- git 操作はしない

## 検証方法
- EditMode テスト: 上の J のとおり
- PlayMode / 目視: 上の J の「エディタでの確認」。スクリーンショット、ホスト・端末のログ

## 実機確認が必要な項目
- 本物の手で、ビームが持ち手に当てやすいか・握る瞬間に狙いが外れないか（〔吸い付き半径〕〔狙いの保持〕〔肩のずらし量〕、握りのしきい値。#49）
- 会場の Wi-Fi で、4台が 30回/秒ずつ `ThPoses` を送っても、仲間の手からお宝が遅れて見えないか（#49・#45）
- 4人・23個で処理が重くならないか（S-5、#49）

## 外部の素材・サービス（権利・費用・セキュリティ）
- 仮のお宝・荷台・瓦礫・マテリアルはプリミティブとプロジェクト内で作る（外部素材なし）
- XRI 3.3.1 のサンプル「XR Interaction Simulator」: エディタでの確認だけに使う（#36 で取り込み済み。Unity 公式パッケージの付属サンプル、`THIRD_PARTY_NOTICES.md` に記載あり）
- STYLY NetSync サーバー: #36 で確認済み（Apache-2.0、商用可、無料、公式）
- ほかは使わない
