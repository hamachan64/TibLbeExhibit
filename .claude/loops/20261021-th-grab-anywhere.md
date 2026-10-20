# 光る持ち手の廃止（お宝のどこでもつかめる）と、高い壁でビームを止める

## 目的
ステージを高い仮想の壁（2.4m・天井なし）で区切った遺跡の内部に変えた（2026-10-21 決定）のに合わせて、つかみ方を直す。
- お宝に「光る持ち手」を付けるのをやめ、**お宝のどこにビームを当てても、握れば持てる**ようにする。ビームが当たっている間は**お宝全体の輪郭が光る**。
  新しいお宝のモデル（別ブランチで作り直し中）は持ち手のメッシュを持たないので、プレハブ作業なしで持てるようにする。
- **高い壁はビームを止める**。壁越しにお宝をつかめない。低い瓦礫は今までどおり止めない。

## 決定事項（壁打ち 2026-10-21）
1. つかむ判定（旧: 持ち手のコライダー）は**実行時に自動で作る**。`Treasure.Initialize` で、モデルの大きさ（`ModelBounds`）から見えない箱のトリガーを作る。
   - S: モデル全体を覆う箱 1 つ。
   - M: モデルを、水平の長い方の軸（ローカル X と Z の長い方。同じなら X）で左右 2 つに分けた箱 2 つ。
   - 箱は見た目より少し大きく（各辺に〔余白〕、初期値 0.02m を足す。定数でよい）。吸い付きは今までどおり `snapRadius` の SphereCast で行う。
   - 既存プレハブ（TH_S_Cup・TH_S_Idol・TH_S_Mask・TH_M_Urn・TH_Practice）から、持ち手のメッシュ・コライダー・`_handles`/`_handleRenderers` の参照を Editor API で外す。
2. 名前を「持ち手」から「**つかむ範囲（GrabZone）**」に改名する。
   - `Treasure.Handles`→`GrabZones`、`HandleCount`→`GrabZoneCount`、`HandleLocalPosition`→`GrabZoneLocalPosition`、`SetHandlesActive`→`SetGrabZonesActive`
   - `ClientItem.LockHandle`→`LockZone`、`SecondHandle`→`SecondZone`、`handleIndex` 引数→`zoneIndex`、`HandGrabber.AimedHandle`→`AimedZone` など、関係する識別子・コメント・ツールチップ・ログ文言すべて。
   - レイヤー `TreasureHandle`（番号はそのまま）→ `TreasureGrabZone` に**名前だけ**変える（Editor API。番号の並べ替えはしない）。`ThPlaceSolver` の除外名も合わせる。
   - `ThFxId.HandleNormal` / `HandleAimed` は列挙から消せない（末尾にだけ足す決まり）ので残し、「使わない」コメントを付ける（`CargoGlow` と同じ扱い）。
3. ビームが壁に当たったら、**ビームの線は壁の点で止まる**。

## 受け入れ基準
- [ ] S: ビームがモデルのどこに当たっても（上・下・側面）、握れば持てる。
- [ ] M: 1 本目の手のビームが左右どちらかの半分に当たって握ると予約、もう一方の手のビームが反対の半分に当たって握ると持てる。予約済みの半分は 2 本目の対象にならない（今の `IsTargetFor` と同じ）。両手のふるまい（猶予・見失い・握り直し）は今までどおり（既存テストが通る）。
- [ ] M を持ったときの位置・向き（両手の中点、両手を結ぶ線）は、左右 2 つの箱の中心を使う（今の 2 つの持ち手の中心と同じ扱い）。
- [ ] ビームが当たっているお宝は、お宝全体の輪郭が光る。外れると消える。持っている間・置いてある他の人のお宝・つかめないお宝は光らない（今の `SetAimed` の条件と同じ）。
- [ ] 壁レイヤー（`ThWall`）のコライダーがビームの始点とお宝の間にあると、そのお宝は対象にならず、握っても持てない。ビームの線は壁の点で止まる。
- [ ] 壁より手前のお宝はつかめる。低い瓦礫（`ThWall` 以外のレイヤー）はビームを止めない。
- [ ] 練習用のお宝（TH_Practice）も同じように持てる。
- [ ] 持ち手のメッシュがないプレハブ（Renderer と Rigidbody と Treasure だけ）でも、S・M とも持てる。`ThTreasureCatalog.Validate` は「持ち手の数」ではなく、持てる形か（`MeshRenderer` があるか）を見る。
- [ ] コンパイル エラー 0。EditMode テスト全件成功（今 417 件 + 追加分）。

## 変更対象
- `Assets/_Content/TreasureHaul/Scripts/Treasure/Treasure.cs`
  - `_handles` / `_handleRenderers` を削除。`Initialize` で `ModelBounds` からつかむ範囲の BoxCollider（トリガー、`TreasureGrabZone` レイヤー）を子に作る。M の 2 つは左右の順を固定（ローカル軸の負側が 0、正側が 1）。
  - 箱の作り方は、テストできる純粋な関数に分ける（例: `GrabZoneLayout.Compute(Bounds model, TreasureKind kind, float margin) → Bounds[]`）。
  - 輪郭の光: Initialize 時点のモデルの各 MeshRenderer について、同じメッシュを共有する子（MeshFilter + MeshRenderer、サブメッシュの数だけ輪郭マテリアル）を作り、`SetAimed` で有効・無効を切り替える。影は落とさない・受けない。値札やヒビなど後から付く物は対象外。
  - `HoldPoint`（M は 2 つの箱の中心の中点）は今までと同じ計算を、つかむ範囲に置き換える。
- `Assets/_Content/TreasureHaul/Scripts/Treasure/HandGrabber.cs`
  - 改名。壁: `ThWall` の mask で `Physics.Raycast`（太さなし。出入口の端でビームが止まりすぎないため）を `length` まで撃ち、当たった距離より遠いつかむ範囲の当たりは捨てる。ビームの終点は `min(length, 壁の距離)`。
  - 対象の選び方（当たりの一覧と壁の距離から一番近い有効なものを選ぶ）は、テストできる形に分ける。
- `Assets/_Content/TreasureHaul/Scripts/Treasure/TreasureClient.cs`: 改名、`ThWall` レイヤーを引いて `HandGrabber.Initialize` に渡す。マテリアルの受け渡しを新しい演出 ID に変える。
- `Assets/_Content/TreasureHaul/Scripts/Treasure/ClientItemTable.cs` / `ClientItemTable.TwoHand.cs`: 改名のみ（ロジックは変えない）。
- `Assets/_Content/TreasureHaul/Scripts/Treasure/ThTreasureCatalog.cs`: 検証を「MeshRenderer がある」に変える。
- `Assets/_Content/TreasureHaul/Scripts/Treasure/ThPlaceSolver.cs`: 除外するレイヤー名を `TreasureGrabZone` に。
- `Assets/_Content/TreasureHaul/Scripts/Content/ThFxCatalog.cs`: `ThFxId` の**末尾に** `AimOutline`（輪郭のマテリアル）と `Beam`（ビームの線のマテリアル）を足す。`HandleNormal` / `HandleAimed` に「使わない」コメント。
- `Assets/_Content/TreasureHaul/Settings/ThFxCatalog.asset`（Editor API）: `AimOutline` に輪郭マテリアル、`Beam` に今ビームに使っている `TH_HandleAimed.mat` を設定。
- マテリアル（Editor API）: `TH_HandleAimed.mat` を `TH_Beam.mat` に `AssetDatabase.RenameAsset`（guid は保たれる）。`TH_HandleNormal.mat` は参照が無くなったのを確かめてから `AssetDatabase.DeleteAsset`。新規 `TH_AimOutline.mat`。
- シェーダー新規: `Assets/_Content/TreasureHaul/Shaders/AimOutline.shader`（名前 `TIB/TreasureHaul/AimOutline`）
  - URP・Unlit・背面法（Cull Front で頂点を外へ押し出す）。色・太さ（初期値 0.006m 程度）をプロパティに。PICO（モバイル・シングルパスインスタンシング）で動くこと（`UNITY_VERTEX_INPUT_INSTANCE_ID` / `UNITY_VERTEX_OUTPUT_STEREO` 等）。
  - 角張った低ポリゴンのモデルで法線が割れて輪郭に隙間が出るのを避けるため、押し出す向きは「法線」と「メッシュの中心から頂点への向き」を選べるようにし、既定は隙間が出にくい方にする（今のプレハブで確かめる）。
  - 色は暖かい白〜金（体験者ごとの色は #43 の範囲なので今回は 1 色）。
- レイヤー（Editor API、`TagManager.asset`）: 空き番号に `ThWall` を追加。`TreasureHandle` を `TreasureGrabZone` に改名。衝突の組み合わせは既定のまま（壁は持っているお宝を止める。今の障害物と同じ）。
- プレハブ（Editor API）: TH_S_Cup・TH_S_Idol・TH_S_Mask・TH_M_Urn・TH_Practice から持ち手の子（メッシュ・コライダー）を削除。
- テスト（`Assets/_Content/Tests/EditMode/`）: 改名への追従と、下の追加テスト。
- 文書（持ち手に関する部分だけ。ほかの記述は書き換えない）:
  - `TREASURE_HAUL_PLAN.md`: 0・1・4.1・4.2・5.2・5.6 D・8.2・9.2・11 章の「持ち手」を新しい挙動に。5.2 に「ビームが当たるとお宝全体の輪郭が光る」「高い壁はビームを止める。低い瓦礫は止めない」を書く。
  - `TREASURE_HAUL_REQUIREMENTS.md`: F-GRAB-1・2・4・5。
  - `TREASURE_HAUL_DESIGN.md`: お宝カタログ・狙いの保持・M の手順の 3 か所。壁レイヤーとつかむ範囲の自動生成を 1〜2 行。
  - `TREASURE_HAUL_NARRATION.md`: 「持ち手を握ろう」→「お宝を握ろう」相当に。
  - `.claude/rules/hand_tracking_interaction.md` は持ち手の記述がなければ触らない。

## やらないこと（スコープ外）
- ステージの壁そのものの作成・配置（ステージはまだ repo/develop にない。壁を作るときに `ThWall` レイヤーにする）。
- 体験者ごとの色のビーム・輪郭（#43）。
- 新しいお宝モデルの取り込み（別ブランチ）。
- 持ち方の切り替え（5.6 D）。
- repo/feature-th-asset-models にしかない文書の記述（7.1・7.2・12 章の追記、RESEARCH.md、TODO の「最近決まったこと」）。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity`/`.prefab`/`.asset`/`.meta` は uloop-execute-dynamic-code と Editor API で編集（テキスト編集禁止）。
- `ThFxId` は末尾にだけ足す（並べ替え・削除しない）。
- レイヤーは番号の並べ替え・再利用をしない（名前の変更と空き番号への追加のみ）。
- ネットワークのメッセージ・ホストの判定は変えない（つかむ範囲の番号 0/1 の意味は今の持ち手の番号と同じ）。

## 検証方法
- EditModeテスト（追加）:
  - `GrabZoneLayout`: S は 1 つでモデルを覆う（余白込み）／M は 2 つで、長い水平軸で左右に分かれ、合わせるとモデルを覆う／Z の方が長いモデルでは Z で分かれる。
  - ビームの対象選び: 壁より手前のお宝は選ばれる、壁より奥は選ばれない、壁が無ければ一番近いもの、予約済みの範囲は除かれる。
  - 持ち手の無いプレハブ相当（Renderer だけの GameObject）で `Treasure.Initialize` すると、S は 1 つ・M は 2 つのつかむ範囲ができ、`TreasureGrabZone` レイヤーのトリガーになる。
  - 物理込み（できれば）: EditMode で `Physics.SyncTransforms` 後に、壁の箱を挟むと `HandGrabber` の対象選びが null になる。
  - カタログの検証: 今の 5 プレハブ（持ち手削除後）でエラーが出ない。
- PlayMode/目視: ユーザークライアントのシーンで XRI シミュレーターの手からビームを当て、S・M・練習用お宝の輪郭が光ること、握って持てることをスクリーンショットで確認。テスト用に一時的に置いた `ThWall` の箱の向こうのお宝が光らないこと（一時物はシーンに保存しない）。

## 実機確認が必要な項目
- 狙いやすさ（つかむ範囲の余白 0.02m と snapRadius の組み合わせで、ハンドトラッキングのぶれを吸収できるか）
- 輪郭の太さ・色がパススルーの明るい会場で見えるか、PICO で負荷が問題ないか
- 出入口の端で、太さなしの壁判定でもビームが自然に感じるか

## 外部の素材・サービス（権利・費用・セキュリティ）
- なし（シェーダーは自作）
