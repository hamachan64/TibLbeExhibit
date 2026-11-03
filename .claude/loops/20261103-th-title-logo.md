# タイトルロゴ（待機中とオープニングの冒頭）

Issue: [#62](https://github.com/hamachan64/TibLbeExhibit/issues/62)／ブランチ: `repo/feature-th-title-logo`

## 目的
今は、体験者の画面に出るのは頭について来る小さな面の文字だけで、ゲームの名前が見えない。
装着して待っている間、**納品場所の上の空間に「TREASURE HAUL」のロゴを大きく浮かべ**、4人が同じ物を見て「これから始まる」とわかるようにする。
運営PCで開始した瞬間に、空間のロゴを消し、**各体験者の顔の正面にロゴを一瞬（約1.5秒）大きく出す**。開始の合図にし、気分を上げるため。待機中にどこを向いていても気づける。その後は字幕と練習に移る。

決めたこと（2026-11-03 の壁打ち）:
- 待機中は空間に固定（納品場所の上）。オープニングの冒頭は各体験者の正面に一瞬（2026-11-03 追加）。頭について来る面は今のまま（文字の案内・字幕を続ける）。
- ロゴはユーザーが Claude の画像生成で作った `TreasureHaul_title_logo.png`（1866×755、背景透明）をそのまま使う。キービジュアル（`TreasureHaul_キービジュアル.jpg`。Canva で作成）はアプリに入れず、雰囲気の参考だけにする。
- リポジトリが公開なので、ロゴの画像（元画像とテクスチャ）は git に入れない。元画像は `Artwork/TreasureHaul/` に置く（2026-11-03 のユーザー判断。DEVELOPMENT.md「git に入れていない素材」）。画像が無い環境では、ロゴを出さない（白い四角を出さない）。
- オープニングの長さ（〔オープニング〕10秒）は変えない。

## 受け入れ基準
### 空間のロゴ（待機中）
- [ ] 状態が **Waiting** の間、納品場所の上にロゴが出ている（端末の `WaitNextRound` に関係なく出す）。
- [ ] Waiting 以外では出さない。Opening に入った瞬間にフェードで消す。中断（どの状態からでも Waiting に戻る）で Waiting に戻ったら、また出す。
- [ ] 見た目: 出るときは 0.5 秒で「透明・少し小さい（0.9倍）」から元へ。消えるときは 0.4 秒で透明へ。出ている間は上下にゆっくり揺れる（±3cm・周期 約4秒）。

### 正面のロゴ（開始の合図）
- [ ] Waiting → Opening に**変わった瞬間に1回だけ**、その端末のカメラ（頭）の正面に出す。位置は頭から水平方向に 1.5m 先、高さは目の高さ +0.22m。向きは体験者の方を向き、上下の傾きは付けない（頭の前方向を水平に投影して使う）。出した後は**頭に追従させず、その場に止める**（酔いを避ける）。
- [ ] 大きさは幅 約0.9m（縦横比は画像のまま）。出方: 0.25 秒で 0.6倍・透明から 1.1倍へ、続けて 0.15 秒で 1.0倍へ（はずむ）。約1.1 秒止めたあと、0.4 秒で 1.15倍・透明へ消える。全体で約1.9 秒（`ThParams` には足さず、見た目の定数にする）。
- [ ] 回の途中から入った端末・ホストを見失って待機に戻った端末で、Opening に途中から入った（Waiting を見ずに Opening を知った）ときは出さない。出している途中で中断して Waiting に戻ったら、すぐ消す。
- [ ] 頭について来る面の字幕（正面やや下）と重ならない（ロゴは目の高さより上、字幕は下）。描画順でロゴが字幕を隠さない。
- [ ] 効果音は足さない（Opening の始まりで既に鳴る `OpeningSfx` と同じ瞬間に出る）。

### 共通
- [ ] 判断は純粋な静的メソッドにする（例: `ThTitleLogo.ShowVenueLogo(RoundState state)`、`ThTitleLogo.IsStartCue(RoundState prev, RoundState next)`）。EditMode テストで、Waiting で空間のロゴを出す・それ以外は出さない、Waiting→Opening だけが合図になる（Waiting→Waiting、Opening→Opening、ほかの状態→Opening、端末の起動直後に最初に知った状態が Opening のときは合図にしない）を確かめる。
- [ ] Tween はすべて DOTween。状態が途中で変わったら、前の Tween を止めてから次を始める（`OnDestroy` でも止める）。
- [ ] 空間のロゴの置き場所: Environment の `CargoBoard`（表示板）の近くに置く。表示板は Tutorial〜Result だけ出るので、ロゴと同時には出ない。幅 約2.4m（縦横比は画像のまま）、表示板と同じ向きで、開始位置（納品場所の前）に立った目の高さ 1.6m から見上げすぎずに全体が見える高さにする。PlayMode のスクリーンショットで、開始位置から見て、ロゴが欠けたり壁に埋まったりしていないことを確かめる。
- [ ] ロゴは光の影響を受けない透過の面（URP の Unlit・Transparent 相当）で、明るい会場のパススルーの上でも色が沈まない。影を落とさない・受けない。
- [ ] 運営PC（`SessionClient` が無いシーン構成）ではロゴを出さない（既定は非表示で、体験者の端末の配線が出す）。
- [ ] 配線: 空間のロゴは、表示板と同じく `ThVenue` に参照（例: `Title`）を足し、体験者の端末側（`TreasureClient.Bind` の表示板の配線の隣、または同じ形の小さな部品）で `core.Tracker.State` から出し消しする。正面のロゴは UserClient シーンに置き（同じ材質・テクスチャ）、同じ部品が `Camera.main` の位置・向きから置く。R3 で購読し、`AddTo(this)` 等で解除漏れがないこと。
- [ ] `THIRD_PARTY_NOTICES.md` にロゴの行を足す（下の「外部の素材・サービス」の内容）。
- [ ] 企画書 4.1（待機画面）・4.2（オープニング）、要件 F-FLOW-2、設計書の該当箇所（頭について来る面・表示板の記述の近く）に、ロゴの出し方を一文ずつ足す。

## 変更対象
- `Assets/_Content/TreasureHaul/Textures/TH_TitleLogo.png`（新規。ルートの `TreasureHaul_title_logo.png` をコピー。インポート設定: Default、sRGB、Alpha Is Transparency、ミップマップあり、最大 2048、Android は ASTC）。ルートの元の PNG・JPG は動かさない・コミットしない（ユーザーのファイル）。
- `Assets/_Content/TreasureHaul/Materials/TH_TitleLogo.mat`（新規）
- `Assets/_Content/TreasureHaul/Scripts/View/ThTitleLogo.cs`（新規。判断の静的メソッド＋見た目の MonoBehaviour）
- `Assets/_Content/TreasureHaul/Scripts/Treasure/ThVenue.cs`: ロゴの参照を足す
- `Assets/_Content/TreasureHaul/Scripts/Treasure/TreasureClient.cs`（または新しい小さな配線部品）: 状態に合わせて出し消しする
- `Assets/_Content/TreasureHaul/Scenes/TreasureHaulEnvironment.unity`: 空間のロゴの GameObject を置き、`ThVenue` に結線する（uloop 経由）
- `Assets/_Content/TreasureHaul/Scenes/TreasureHaulUserClient.unity`: 正面のロゴの GameObject を置き、配線する部品に結線する（uloop 経由）
- `Assets/_Content/Tests/EditMode/`: 判断のテスト
- `THIRD_PARTY_NOTICES.md`、`TREASURE_HAUL_PLAN.md`、`TREASURE_HAUL_REQUIREMENTS.md`、`TREASURE_HAUL_DESIGN.md`、`TODO.md`

## やらないこと（スコープ外）
- キービジュアルの使用（アプリに入れない）。
- 金の粒が舞う・光の筋などのパーティクル演出（必要なら後で vfx-artist に頼む）。
- 頭について来る面の変更（待機の文字・字幕はそのまま）。
- オープニングの長さ・字幕・ナレーション・効果音の変更。
- 運営PCの画面へのロゴ表示。
- Core（`TibLbeExhibit.Core`）の変更。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、.unity/.prefab/.asset は uloop 経由で編集。Coroutine・UnityEvent・event Action は使わない。
- 全端末で同じタイミングに出し消しするため、判断の材料は同期済みの状態と残り秒だけを使う（端末ごとのタイマーを足さない）。
- PICO で 60fps を保つ: 面は1枚・テクスチャは最大 2048・透明の重ね描きを増やさない。

## 検証方法
- EditMode テスト: `ThTitleLogo.ShouldShow` の4パターン（受け入れ基準どおり）。既存テストがすべて通る。
- PlayMode/目視: `OpeningHostDriver`（ループバック）で Waiting → Opening → Tutorial と進め、スクリーンショット（開始位置・目の高さ 1.6m）で確かめる。(1) Waiting で、納品場所の上にロゴが出ている。(2) Opening に入った直後に、空間のロゴが消え、カメラの正面にロゴが出ている。カメラを納品場所と反対に向けた状態で開始しても、正面に出る。(3) 約2秒後に正面のロゴが消え、字幕だけになる。(4) Tutorial では表示板だけが出ている。

## 実機確認が必要な項目
- 明るい会場のパススルーの上で、ロゴが読める濃さか（暗い背景用に作られた画像なので、縁の金色が会場の明るさで沈まないか）。
- ロゴの高さ・大きさ（装着して見上げすぎないか、4人が並んだときに全員から見えるか）。
- 正面のロゴの距離・大きさ・出ている時間（近すぎて圧迫感がないか、驚かせすぎないか、短すぎて読めないことはないか）。

## 外部の素材・サービス（権利・費用・セキュリティ）
- **タイトルロゴ**（`TreasureHaul_title_logo.png`）: ユーザーが Claude（Anthropic）の画像生成で作成。
  1. 商用利用: Anthropic Consumer Terms §4 で、出力の権利はユーザーに譲渡される。展示での表示に使える（評価目的の利用ではない前提）。
  2. 権利: 第三者の商標・素材、R.E.P.O の素材、Anthropic/Claude の名称・ロゴ（§12）を含まない。AI 生成の表示義務は Consumer Terms・Usage Policy に無い（人が作ったと偽らない）。リポジトリが公開なので、画像は git に入れない（ユーザー判断）。
  3. 費用: 追加の費用なし。
  4. セキュリティ: 画像ファイル1つ（PNG）。実行物を含まない。
  - クレジット表記: 不要。
  - 確認した規約: https://www.anthropic.com/legal/consumer-terms （Effective 2025-10-08）、https://www.anthropic.com/legal/aup （2026-11-03 確認）
- 当初は Canva 製として Canva の規約を確認したが、Canva から書き出されたのはキービジュアル（アプリに入れない）の方だった（2026-11-03 訂正）。
