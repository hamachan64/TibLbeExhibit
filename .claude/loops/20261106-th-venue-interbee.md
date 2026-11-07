# 会場を Inter BEE の図面に合わせる ＋ AppConfig の取り込み（#48）

## 目的
hacha さんから Inter BEE 用テンプレートの更新が届いた（2026-11-06。手元の写し: `/Users/hamachan/Downloads/TIB_TemplateProject/`）。
テンプレートの `Assets/_InterBEE/Scenes/InterBEE.unity` は会場図面に合わせて作られていて、`Map/CenterPosition`（ワールド原点）を現実の基準位置に合わせる予定になっている。
Treasure Haul のステージ（`VenueRoot`）を同じ座標に置き、前の会場の残り物を片付ける。これで、全端末の仮想空間が現実のブースに重なるようにする。
あわせて、現場でリビルドせずに基準位置を直せる仕組み（`AppConfig.toml`）を取り込む。

体験者にとっては、納品場所と練習の列が入口（待機エリアから入る口）の目の前に来て、回廊が会場の奥（受付側）へ延びる。

## 座標（InterBEE.unity の値。YAML から計算済み）
- 原点は `Map/CenterPosition` = (0,0,0) で、体験エリアの左下の角。+X は図面の右（長い辺 11.88m）、+Z は待機エリア側（短い辺 4.95m）。
- 腰壁の内側は、およそ x −0.01〜11.95、z −0.03〜4.81。待機エリアから入る口は z≈4.87 の辺の x 8.46〜10.47、受付側の口は x 0.51〜1.96。
- ステージは「入口の辺の中央が原点、幅 X ±2.5、奥行 Z 0〜11」（`BlenderAssets/TreasureHaul/Stage/Output/README.md`）。

## 受け入れ基準

### 会場（`TreasureHaulEnvironment.unity`）
- [ ] `VenueRoot` のワールド位置が (11.45, 0, 2.4)、回転が Y −90°（(0,−90,0)）、拡縮が (1,1,1)。子のローカル値は変えない。これで、納品場所（ローカル z 0.2〜2.0）はワールド x 9.45〜11.25、練習の列（ローカル z 2.5）は x 8.95、大広間の奥の壁（ローカル z 11.0）は x 0.45 に来る。
- [ ] `WarningWall` の子 4 枚のワールドの位置・Y 回転・拡縮が、テンプレートと同じ値になっている（±0.001）。
  - Quad (1): (5.992, 1.3234, 0.15)・Y 180・(11.768264, 2.6468, 1)
  - Quad (2): (5.98, 1.3096675, 4.79)・Y 0・(11.769639, 2.572425, 1)
  - Quad (3): (0.08, 1.3597646, 2.4045)・Y −90・(5.2832355, 2.6468, 1)
  - Quad (4): (11.745, 1.350652, 2.4292)・Y 90・(4.907179, 2.6468, 1)
  - 親の `WarningWall` は原点・回転なし・拡縮 1 のまま。材質（`WarningWall.mat`）は変えない。
- [ ] `VRArea` は (5.981, 1.9783, 1.5207)・回転なし・拡縮 (10.05842, 3.9164, 4.224505)。`activeSelf` は今の false のまま。
- [ ] `Area_Floor` は (5.9703, 0, 2.4138)・回転なし・拡縮 (1.1881883, 3.0683, 0.49167016)。
- [ ] 前の会場のスキャン `TIB_20260429`（子に古い `CenterPosition` を持つ）と、`StageScoreMonitor`・`StageScoreMonitor (1)` は `SetActive(false)` にする（消さない）。
- [ ] ルートに新しく `InterBEE_Guide`（タグ `EditorOnly`。ビルドに入らない）を作り、子に次を置く。ワールド値で、コライダーは付けない。
  - `CenterPosition`: (0,0,0)。小さな印（Cylinder のメッシュ、拡縮 (0.1, 0.01, 0.1) 程度）。
  - `Partition` の下に腰壁の目安 6 個（Cube のメッシュ。`BoxCollider` は外す）。
    - Cube: (5.950, 0.510, −0.090)・Y 0・(12.00, 1.02, 0.12)
    - Cube (1): (5.213, 0.510, 4.870)・Y 0・(6.50, 1.02, 0.12)
    - Cube (2): (0.233, 0.510, 4.870)・Y 0・(0.55, 1.02, 0.12)
    - Cube (3): (11.203, 0.510, 4.870)・Y 0・(1.47, 1.02, 0.12)
    - Cube (4): (−0.054, 0.510, 2.429)・Y 90・(6.79, 1.02, 0.09)
    - Cube (5): (11.990, 0.510, 2.430)・Y 90・(6.79, 1.02, 0.09)
  - 材質は、プロジェクトにある半透明か目立たない物を使う（新しい外部素材は足さない）。影は落とさない（`shadowCastingMode = Off`）。
  - `InterBEE_Guide` の子は、どれも `Static` にしない（焼き込みに入れない）。
- [ ] 図面の画像（テンプレートの `image.png` / `map.mat`）はプロジェクトに入れない。
- [ ] `VenueRoot` を動かした後に、メニュー「Tools/Treasure Haul/ステージの灯りを焼く」で焼き直し、焼き上がりを待つ。ライトプローブがワールドの座標で焼かれているため。焼き上がった灯りの資産（`Lightmap-*`・`LightingData` 等）を変更に含める。
- [ ] `Ground(Collider)`・`ViewManager`・`Directional Light`・`Timeline`・`EventSystem`・非アクティブの `Cube` は触らない（テンプレートの InterBEE.unity でも同じ値）。

### コード
- [ ] `ThVenue` のクラスのコメント「VenueRoot は回転・拡縮しない前提」を、事実に合わせて直す。新しい文は「VenueRoot は Y 軸の回転だけしてよい（拡縮・傾けはしない）。回収範囲は VenueRoot ローカルの軸並行 Bounds なので、CollectZone は VenueRoot に対して回転させない」。
- [ ] `ThStageBuilder` が `VenueRoot` の位置・回転に関係なく正しく作り直せるかを確かめる。
  - 練習の枠を置く処理 `slot.transform.position = new Vector3(pos.x, 0f, pos.z)` は、VenueRoot の y が 0 でないと崩れる。ローカルで y=0 にする形へ直す。
  - そのほかにワールド座標を前提にした処理があれば、同じく直す。
  - `VenueRoot` の transform（位置・回転）は書き換えない。
- [ ] `ThStageTests` で表示板の位置をワールド座標で比べている所（`venue.Board.transform.position` と (0,1.5,0.32)）を、VenueRoot ローカル（`venue.Root.InverseTransformPoint(...)`）の比較に直す。回収範囲の比較など Vector3 の完全一致は、回転による誤差で落ちるなら許容差付きにする。
- [ ] EditMode テストを足す。VenueRoot を Y 回転・平行移動させても、`ThVenue` の `GetCollectBounds`・`GetPracticeSlots`・`GetPlacementPoints` が同じローカル値を返すこと（Environment シーンに依存しない、その場で作った GameObject で確かめる）。
- [ ] Environment シーンの `VenueRoot` が (11.45,0,2.4)・Y −90 にあることを確かめる EditMode テストを足す（会場の位置が誤って戻るのを防ぐ）。値は定数としてテストに書く。

### AppConfig（テンプレートから取り込み）
- [ ] テンプレートの次の2ファイルを、`.meta` ごと（guid を保ったまま）`Assets/_Content/Scripts/System/` にコピーし、`AssetDatabase.Refresh()` する。
  - `Assets/_Content/Scripts/System/AppConfig.cs`（guid 90519bffc9ff498c92872a8aae78f821）
  - `AppConfigLoader.cs`（guid e299a6a1d02444bcaac33f150a39d4a5）
  - 中身はテンプレートのまま。ただし、エディタ用の置き場所の1行だけを直す: `Path.Combine(Application.dataPath, "../../ExternalResources")` → `Path.Combine(Application.dataPath, "../ExternalResources")`。直した行には、本プロジェクトは Unity プロジェクトがリポジトリ直下にあるため1段浅い、とコメントを付ける。
  - Core の asmdef は `com.styly.styly-xr-rig` を参照済みなので、asmdef は変えない。
- [ ] `TreasureHaulUserClient.unity` の `UserClient` GameObject に `AppConfigLoader` を付ける。テンプレートの UserClient.unity と同じ配置。
- [ ] リポジトリ直下に `ExternalResources/AppConfig.toml` を置き、git に入れる。中身はテンプレートの初期値の文面にコメントを1段落足したもので、エディタで遊ぶとき、練習の列の後ろ（ワールド (8.45, 0, 2.4)）から納品場所（+X）を向いて始まる値にする。
  - 値: `CenterPositionX = 2.4`、`CenterPositionY = 0.0`、`CenterPositionZ = -8.45`、`CenterRotationY = -90.0`
  - 足すコメント: このファイルはエディタ専用、実機では `/sdcard/Android/data/com.hamachan.tib/files/AppConfig.toml` を使う（初期値は 0）、上の値はエディタで練習の列の後ろから始めるためのもの。
  - PlayMode で、XR の頭（カメラ）が水平でおよそ (8.45, *, 2.4) にあり、+X を向いて始まることを確かめる。ずれていたら値を直す。
- [ ] `DEVELOPMENT.md` に「AppConfig（現場での基準位置の調整）」の節を足す。
  - 置き場所: エディタはリポジトリの `ExternalResources/`、実機は上のパス。
  - キーの意味（テンプレートのコメントに同じ）。
  - 実機での直し方: `adb push AppConfig.toml /sdcard/Android/data/com.hamachan.tib/files/` → アプリを再起動。アンインストールすると消える。`adb install -r` なら残る。
  - エディタ用のパスを1段浅く直したことと、hacha さんのプロジェクトへ移すときはテンプレート側の `AppConfigLoader.cs` を使うこと（guid は同じ）。
  - ParrelSync の複製でも `ExternalResources/` が無ければ初期値で作られ、会場の角から始まること。
- [ ] `THIRD_PARTY_NOTICES.md` に1行足す。TIB テンプレートの `AppConfig.cs` / `AppConfigLoader.cs`、入手元は hacha さん（STYLY）配布のテンプレート（2026-11-06）。この展示のために主催側から提供されたコード、商用展示可、無料。中身を読み、ネットワーク通信がなく、ローカルの設定ファイルを読み書きするだけであることを確認した、と書く。

### 文書
- [ ] `TREASURE_HAUL_PLAN.md`
  - 9.1: hacha さんのテンプレートで、`Map/CenterPosition` を原点に合わせる予定であることを書く。ずれは端末ごとの `AppConfig.toml` で直す。本作は `VenueRoot` をその座標に置いた（納品場所は入口側の右端）。原点の合わせ方と向きは hacha さんの回答待ち。
  - 12章の U-1 の行: 「ほぼ決定（…回答待ち）」に更新する。
  - 7.1: 配置を会場図面に合わせて書き足す。入口は待機エリア側の長い辺の右寄り、納品場所と練習の列はその目の前の右端、回廊は受付側（左）へ延びる。
  - 8.3: 表に1行足す。「位置がずれている（壁・納品場所が現実とずれる）」→「その端末の `AppConfig.toml` を直して adb push し、アプリを再起動。手順は DEVELOPMENT.md」。
- [ ] `TREASURE_HAUL_REQUIREMENTS.md`: U-1 の行を同じ内容で更新する。前提（19行目付近）の「方法は U-1」はそのまま。
- [ ] `TREASURE_HAUL_DESIGN.md`: 座標の節（15行目付近）と既存資産の表（`VRArea` / `WarningWall` の行、`UserClient` の Recenter の行）を更新する。
  - WarningWall 等はテンプレートの位置のまま VenueRoot の外に置いた。
  - VenueRoot の値。
  - AppConfigLoader が XR Rig を逆向きにずらす。
  - VenueRoot は Y 回転だけ可。
- [ ] `TODO.md`: 会場レイアウト #48 を「👀 PRレビュー中」にし、現在地と判断待ちの U-1 を更新する。決まったことに 2026-11-06 の項を足す。

## 変更対象
- `Assets/_Content/TreasureHaul/Scenes/TreasureHaulEnvironment.unity`（uloop 経由で編集）と、焼き直した灯りの資産
- `Assets/_Content/TreasureHaul/Scenes/TreasureHaulUserClient.unity`（uloop 経由。AppConfigLoader を付ける）
- `Assets/_Content/Scripts/System/AppConfig.cs(.meta)`、`AppConfigLoader.cs(.meta)`（新規。テンプレートから）
- `Assets/_Content/TreasureHaul/Scripts/Treasure/ThVenue.cs`（コメント）
- `Assets/_Content/TreasureHaul/Editor/ThStageBuilder.cs`（ワールド座標前提の修正）
- `Assets/_Content/Tests/EditMode/ThStageTests.cs`（修正）と、新しいテスト（`ThVenueTransformTests.cs` など）
- `ExternalResources/AppConfig.toml`（新規）
- `DEVELOPMENT.md`、`THIRD_PARTY_NOTICES.md`、`TREASURE_HAUL_PLAN.md`、`TREASURE_HAUL_REQUIREMENTS.md`、`TREASURE_HAUL_DESIGN.md`、`TODO.md`

## やらないこと（スコープ外）
- WarningWall の位置・材質・表示距離の変更（テンプレートの値のまま。見え方は実機で確かめる）
- Discovery Port（6400 のまま）・パッケージ名・製品名の変更
- hacha さんのテンプレートプロジェクトへの移行
- 図面画像の取り込み
- ステージ（Blender の出力・`stage_layout.json`）の形の変更
- UserClient の Recenter の変更
- 前作（GIPoker）のシーン・`Assets/_Content/Scenes/` の変更

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用。`.unity`/`.prefab`/`.asset` は uloop 経由のエディタ API で編集する（YAML を直接書かない）。
- `.meta` はコピー元の `.cs` と対にしてコピーし、guid を書き換えない。コピーした後は `AssetDatabase.Refresh()` する。
- テンプレートのファイル（`~/Downloads/TIB_TemplateProject/`）は読むだけで、変更しない。
- ステージの作り直しメニュー（「ステージを作り直す」）は、`ThStageBuilder` を直した後に動作確認として実行してよい。実行した後に、VenueRoot の位置・回転が (11.45,0,2.4)・Y −90 のまま、子のローカル値が変わらないことを確かめる。焼き込みはその後に1回だけ行う。

## 検証方法
- コンパイル、EditMode テストが全件成功（treasure-haul-verify）。
- 追加するテスト:
  - VenueRoot を回転・移動しても `ThVenue` のローカル値が変わらない。
  - Environment の VenueRoot の値。
  - 既存の `ThStageTests` が、VenueRoot を動かした状態で通る。
- PlayMode/目視（エディタ）:
  1. UserClient で再生し、頭が練習の列の後ろにいて、納品場所の方を向いていることを確かめる（AppConfig のエディタ値）。
  2. Scene ビューを真上から写したスクリーンショットで、ステージが腰壁の目安の中に収まっていることを確かめる。外周の壁が腰壁に重なり、入口の区画が待機エリア側の口（x 8.46〜10.47）の前にあること。
  3. 練習の流れ（練習用お宝を握る → 納品場所へ運ぶ → 回収）が、VenueRoot を動かす前と同じく動くことを、XR Interaction Simulator かテスト用の操作で確かめる。少なくとも練習用お宝が練習の列の位置に出て、納品場所で回収されること。
  4. `AppConfigLoader` が「中心位置 … を反映しました」とログに出すこと。

## 実機確認が必要な項目
- 現場で原点を合わせたときに、仮想の外周の壁が現実の腰壁に重なり、納品場所が入口の目の前に来るか。原点の合わせ方と向き（+Z が待機エリア側）は hacha さんの回答待ち。
- WarningWall の見え方: 0.6〜0.75m 以内で見える。通路（幅 1.2m）で頻繁に出ないか。石の壁との前後で見え方が左右で違わないか。
- 実機の `AppConfig.toml` を直して再起動したときに、基準位置が直るか。

## 外部の素材・サービス（権利・費用・セキュリティ）
- TIB テンプレートの `AppConfig.cs` / `AppConfigLoader.cs`（hacha さん＝STYLY 配布。この展示のため主催側が提供）。
  - 商用利用: 展示のために提供された物で可。
  - 権利: 本プロジェクトの Core 自体が同じテンプレートの派生。
  - 費用: 無料。
  - セキュリティ: 全文を読み、通信なし・ローカルファイルの読み書きのみ・実行ファイルなしを確認した。
- `THIRD_PARTY_NOTICES.md` に記録する。
- 図面画像は取り込まない。
