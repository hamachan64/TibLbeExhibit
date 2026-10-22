# お宝 12 種とステージの Unity への取り込み（#46）

## 目的
Blender で作った「架空の古代文明の出土品」12 種と、高い壁で区切った回廊のステージを Unity に入れ、
今の仮の会場（P00〜P30 の点・Rubble・持ち手付きの仮のお宝）を置き換える。
体験者は、遺跡の部屋の祭壇・壁龕・棚・箱に置かれたお宝を探し、ビームでつかみ、通路を回って納品場所へ運ぶ。
壁の向こうのお宝はビームでつかめない。お宝は見た目（素材）で値段の見当がつき、奥ほど高い素材が出やすい。

## ユーザーの決定（2026-10-21、この仕様の前提）
- ブランチは `repo/feature-th-asset-models` のまま（新しいブランチは切らない）。
- 値段は**お宝ごとの帯**（`stats.json` の `value_band_g`）で決める。奥の置き場所ほど高い素材のお宝が選ばれやすい。P-4 の `sValueMin/Max`・`mValueMin/Max` は S・M には使わなくなる（フィールドとファイルの互換は残す）。
- 灯りは**焼き込み（ベイク）**。動くお宝はライトプローブで照らす。
- 練習用お宝は**土の小壺（clay_pot）**のモデルに体験者の色を付けて使う。
- 壁越しの仲間の見せ方（企画書 12 章）は今回扱わない。

## 受け入れ基準

### A. お宝
- [ ] 12 種の FBX とアトラスが `Assets/_Content/TreasureHaul/Models/Treasures/` にあり、モデルの取り込み設定は Normals = Import、Scale Factor 1（1 単位 = 1m）。
- [ ] Metallic と Roughness から作った 1 枚（R=金属度、A=滑らかさ=1-粗さ、G・B=0）の `Treasure_Atlas_MetallicSmoothness.png` があり、sRGB オフ。作る処理はエディタのメニュー（`ThAtlasPacker`、TreasureHaul/Editor）で、もう一度実行すると同じ物を上書きする。
- [ ] 材質は URP Lit の 1 つ（`TH_TreasureAtlas.mat`。BaseMap=BaseColor、MetallicGlossMap=上の 1 枚、Smoothness の出どころ=Metallic の Alpha）。12 種がすべてこれを使う。
- [ ] 12 種のプレハブ `Assets/_Content/TreasureHaul/Prefabs/Treasures/TH_<S|M>_<PascalName>.prefab`（例 `TH_S_ClayPot`、`TH_M_GildedSunDisc`）。ルートに `Treasure`、子にモデル（MeshRenderer 1 つ）。**底の中心がルートの原点で、底が y=0**。**お宝の正面（Blender の -Y）がルートのローカル +Z**。Blender の `Aim_*` の空オブジェクトはプレハブに残さない（つかむ範囲は #59 の `GrabZoneLayout` が実行時に作る）。値札の位置（`_labelAnchor`）はモデルの上端の少し上。
- [ ] M の 4 種で `GrabZoneLayout.SplitsAlongX` が true（Blender の Aim_Left / Aim_Right と同じ左右に分かれる）。EditMode テストで確かめる。
- [ ] `ThTreasureCatalog.asset` が新しい 12 件だけになる（id はモデル名 `clay_pot` 等、kind、表示名は README の日本語名、`use=true`）。各件に**値段の帯**（`valueMin`/`valueMax`、100G 刻み）と**大きさ**（幅・奥行・高さ m。プレハブのモデルの外形から求めた値）を持つ。大きさを入れ直すエディタのボタン（またはメニュー）がある。
- [ ] 練習用の `TH_Practice.prefab` は clay_pot のモデルに作り直され、`PaintBody` で色が付く。
- [ ] 古いお宝のプレハブ（`TH_S_Cup`・`TH_S_Idol`・`TH_S_Mask`・`TH_M_Urn`）と、どこからも参照されなくなった古いお宝の材質（`TH_TreasureBronze`・`Clay`・`Gold`・`Stone`・`Practice`）を `AssetDatabase.DeleteAsset` で消す（参照が残る物は消さずに報告）。
- [ ] `ThTreasureCatalog.Validate()` が空（問題なし）。

### B. 配置（F-ITEM-1・2、`TreasureSpawner`）
- [ ] `ThPlacementPoint` が、置ける種類（S・M のフラグ）、入る大きさ（幅・奥行・高さ）、正面の向き（Y 回転。度）、ゾーン名、隠す意図、距離帯（0〜2）を持ち、`PlacementPoint`（純粋側）にも同じ情報が渡る。
- [ ] 距離帯はゾーンから決める: `block_front`・`corridor_w`・`corridor_e` → 0、`front_room`・`back_room`・`cross` → 1、`hall` → 2。
- [ ] `TreasureSpawner.Spawn` が次を満たす（同じ入力から必ず同じ結果。乱数は `System.Random(seed)` 1 つ。**呼ぶ順番をクラスの summary に書き直す**）:
  1. M は「M を置ける」置き場所にだけ、S は「S を置ける」置き場所にだけ置く。
  2. お宝の大きさ（幅・奥行・高さ）が置き場所の入る大きさ以下のお宝だけを選ぶ（お宝の正面を置き場所の正面に向けたときの寸法で比べる）。
  3. 同じお宝（同じ id）を、水平距離 1.5m 未満の置き場所に置かない。条件を満たす候補が無いときだけこの決まりを外し、`Warnings` に書く。
  4. お宝の選び方は距離帯で偏らせる: 候補を値段の帯の中央値の安い順（同じなら id 順）に並べ、帯 0 は 2 回引いて小さい方、帯 1 は 1 回、帯 2 は 2 回引いて大きい方の添字を選ぶ。
  5. 値段はそのお宝の帯の中で 100G 刻み。帯 0 は 2 回引いて小さい方、帯 1 は 1 回、帯 2 は 2 回引いて大きい方。
  6. 向きは置き場所の正面の向き（ランダムの Y 回転はやめる）。
  7. 置き場所や候補が足りずに出現数を満たせないときは、置ける数だけ置き、`Warnings` に書く。
- [ ] 実際のステージの 34 か所と新しいカタログで、1〜4 人のすべての出現数（P-5 の初期値）を、シード 0〜999 で置いたとき、規則 1・2 を破らず、出現数を満たし、規則 3 を外した回数がゼロ（または警告として数えて報告）。EditMode テスト（置き場所はステージの JSON から作ったデータを使う）。
- [ ] `ThTreasureCatalog.ComputeHash` に値段の帯と大きさも入れる（ビルド違いで配置がずれるのを検出するため）。
- [ ] 既存のテスト（480 件）のうち配置の仕様が変わって合わなくなるものは、新しい仕様に合わせて直す。それ以外は全部そのまま通る。

### C. ステージ
- [ ] 14 個のモジュール FBX とアトラス（BaseColor、Emission、MetallicSmoothness に変換した 1 枚）と `stage_layout.json`（TextAsset）が `Assets/_Content/TreasureHaul/Models/Stage/` にある。Normals = Import。材質は URP Lit の 1 つ（`TH_StageAtlas.mat`。Emission あり）。
- [ ] エディタのメニュー `ThStageBuilder`（TreasureHaul/Editor）が `stage_layout.json` を読み、Environment シーンの `VenueRoot` の下に `Stage` を作り直す（何度実行しても同じ結果。前の `Stage` を消してから作る）:
  - 14 モジュールを置く（静的。Contribute GI）。
  - `colliders` の `kind=wall` を `BoxCollider`（**`ThWall` レイヤー**）、`niche_lip` を `BoxCollider`（Default）にする。家具（`ExtractionBay` 以外の家具系モジュール。置き場所が家具の外形の中にあるため、外形の箱は使わない）は **MeshCollider**（非凸、Default）にする。`ExtractionBay`・`EntryCamp` の `solid` は箱（Default）でよい。
  - `TreasureSlot_NN`（34 か所）を `ThPlacementPoint` 付きで置き、属性を JSON から入れ、`ThVenue` の配置候補点の一覧を JSON の番号順に差し替える。
  - `Tutorial_01〜04` の位置に既存の `TutorialSlot_0〜3`（色番号 0〜3）を移す（X の小さい方から色番号 0）。
  - `CollectionVolume` の位置・大きさに、`ThVenue` の回収範囲（BoxCollider）を合わせる。`ScoreBoardAnchor` に表示板を移す。
  - `Light_*` を灯りにする（`Light_Lamp_*`・`Light_WorkLamp_*` は暖色 3000K のスポット、`aim_unity_xyz` を向く、範囲 約 3m・角度 約 100°。`Light_Bulb_*` は 2700K のポイント、範囲 約 1.5m）。**Mode = Baked**。
- [ ] 古い仮の会場（`PlacementPoints` と P00〜P30、`Rubble` と Rubble_0〜5）を消す。`VenueFloor`・`Ground(Collider)`・`WarningWall`・`VRArea`・スコアモニターは残す（床は会場の床をパススルーで見せるので、ステージ側に床は無い）。
- [ ] **X 軸の向き**: Unity で、前の部屋の扉が西（X<0）の面の奥行 4.7〜5.9、奥の部屋の扉が東（X>0）の面の奥行 6.6〜7.8 にある。確かめ方: `Block_Shell` のメッシュに対し、高さ 1.2m・奥行 5.3 で X=-1.5 から +X へ撃った光線が X≈-1.08 の壁に当たらず部屋に入る。奥行 7.2 では X=+1.5 から -X へ撃って同様。逆側（奥行 5.3 の東面、7.2 の西面）は当たる。モジュールのメッシュの外形と JSON の `colliders` の箱の位置が 5cm 以内で一致する。反転していたら取り込み設定かルートの拡縮で直し、直し方を README に書く。
- [ ] 焼き込み: Environment シーンでライトマップを焼き、LightingData とライトマップがコミットされる。動くお宝のためのライトプローブを、歩ける範囲（高さ 0.3m・1.0m・1.6m の 3 段、約 0.8m 間隔、壁の中には置かない）に置く。追加ロードしたシーンのライトプローブが効くよう、必要なら読み込み後に `LightProbes.TetrahedralizeAsync()` を呼ぶ（呼ぶ場所は TreasureHaul 側に置き、Core は変えない）。焼いた後の Environment シーンの見た目を、入口からの目線のスクリーンショットで確かめる。

### D. 動作確認
- [ ] コンパイル エラー 0・警告 0。EditMode テストがすべて成功。
- [ ] PlayMode（UserClient ＋ Environment。既存の検証手順・シミュレーター）で、本編開始後に置き場所 23 か所（4 人の設定）または人数に応じた数のお宝が、置き場所の上に正面を向けて出ている（床や壁の中に無い）。スクリーンショットで確かめる。
- [ ] PlayMode で、お宝の 1 つにビームを当てて握るとつかめる（手元へ飛ぶ）。
- [ ] PlayMode で、壁の向こうのお宝に向けたビームは壁で止まり、つかめない。
- [ ] PlayMode で、持ったお宝を壁へ押し付けても通り抜けない。

## 変更対象
- `Assets/_Content/TreasureHaul/Models/Treasures/`（新規）: 12 FBX、アトラス 3 枚＋変換した 1 枚、材質。
- `Assets/_Content/TreasureHaul/Models/Stage/`（新規）: 14 モジュール FBX、アトラス、`stage_layout.json`、材質。
- `Assets/_Content/TreasureHaul/Prefabs/Treasures/`（新規）: 12 プレハブ。`Prefabs/TH_Practice.prefab` を作り直す。古いお宝のプレハブを削除。
- `Assets/_Content/TreasureHaul/Settings/ThTreasureCatalog.asset`: 中身を差し替え。
- `Assets/_Content/TreasureHaul/Scripts/Treasure/ThTreasureCatalog.cs`: 値段の帯・大きさ、検査、ハッシュ。
- `Assets/_Content/TreasureHaul/Scripts/Treasure/ThPlacementPoint.cs`・`ThVenue.cs`・`TreasureTypes.cs`（`PlacementPoint`・`SpawnCatalogEntry`）・`TreasureSpawner.cs`: 配置の規則。
- 配置の結果を使う側（`SessionClientCore`・`SessionHostCore`・`TreasureClient` など）: 向きと値段の受け渡しが変わる分だけ。
- `Assets/_Content/TreasureHaul/Editor/`: `ThAtlasPacker`・`ThStageBuilder`・カタログの大きさの入れ直し（新規）。
- `Assets/_Content/TreasureHaul/Scenes/TreasureHaulEnvironment.unity`（uloop と Editor API で編集）と、焼いたライトマップ・LightingData。
- テスト（TreasureHaul の EditMode テストのフォルダ）: 配置の規則、M の分割軸、カタログの検査。
- 文書: `TREASURE_HAUL_DESIGN.md` 4.1（配置の規則と乱数の順番、カタログの値段の帯・大きさ）、`TREASURE_HAUL_REQUIREMENTS.md` F-ITEM-1（値段はお宝ごとの帯）、企画書 5.2 の表の下に「値段はお宝の素材ごとの帯で決まる」の一文、P-4 の S・M の値段の範囲を使わなくなったことの注記、`BlenderAssets/TreasureHaul/Treasures/README.md`・`Stage/Output/README.md` の「Unity に取り込むとき（未実施）」を実施済みの内容に更新、`TODO.md`。

## やらないこと（スコープ外）
- 壁越しの仲間の見せ方（後回し）。
- 台車（`Cart/`、`build_cart.py`）。`Models/Cart/` と `SalvageCart.prefab`・Prototype 以下は触らない。
- エリアの境界（`VRArea`・`WarningWall`）を新会場に合わせること（#48）。
- Blender 側のモデルの作り直し（取り込みで問題が見つかったら、直さずに報告する）。
- 前作（`Assets/_Content/Scenes/`・GIPoker）と Core のコード。
- PICO 実機での確認（72fps など）。

## 制約
- 規約: UniTask / R3 / DOTween、ハンドトラッキング専用、`.unity`/`.prefab`/`.asset` は uloop（`uloop-execute-dynamic-code`）と Editor API で編集する（YAML を直接書かない）。
- `Assets/` にファイルを足したら `AssetDatabase.Refresh()` で `.meta` を作る。`.meta` を手で作らない・消さない。削除は `AssetDatabase.DeleteAsset`。
- 版をフォルダやファイル名で重ねない（RevisionN や _vN を作らない）。
- 配置は全端末とホストで同じ結果になること（純粋クラスのまま、Unity の乱数や辞書の列挙順に頼らない）。
- ライトマップの焼き込みは時間がかかる。uloop のタイムアウトに注意し、焼いた結果の大きさ（ライトマップの枚数・解像度）を報告する。目安は 1024 の 1〜4 枚。

## 検証方法
- EditMode テスト:
  - 配置: 決定性（同じシードで同じ結果）、M/S の置き場所の規則、大きさの規則、1.5m の規則、値段が帯の中で 100G 刻み、帯 2 の平均値段が帯 0 より高い（多数のシードで）、向きが置き場所の正面、足りないときの警告。実際の 34 か所のデータでの全人数・シード 0〜999。
  - カタログ: 12 件の `Validate()` が空、ハッシュに帯と大きさが入る、M の 4 種の分割軸。
- PlayMode/目視: 受け入れ基準 D。入口からの目線と真上のスクリーンショット（Environment ＋ お宝）。

## 実機確認が必要な項目
- PICO で 72fps を保てるか（ステージ 約 8.3 万三角形＋お宝 23 個、ライトマップ）。
- 焼き込んだ暗い廃墟が、明るい会場のパススルーとどう見えるか。
- 壁の高さ・通路の幅が、実際の体の動きで窮屈でないか。

## 外部の素材・サービス（権利・費用・セキュリティ）
- なし（モデル・テクスチャはすべて本リポジトリの `build_treasures.py`・`build_stage.py` で作った物。外部の素材・サービス・パッケージは使わない）。
