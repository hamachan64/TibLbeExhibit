# ステージ建築リサーチノート（侵蝕する街アントキバ / 2026-07-19）

Phase 0 リサーチの成果。Zone2「懸賞の街アントキバ」のアートディレクションと建築構造の根拠。

## アートディレクションの核: アントキバ = ローテンブルク・オプ・デア・タウバー

- 原作のアントキバの背景は実在のドイツ・バイエルン州の中世都市 **Rothenburg ob der Tauber** がモデル（Hunterpedia調べ。131話ラストはTourismus Service棟、133話冒頭は名所 Plönlein が元絵）。
- したがって街並みの様式は **南ドイツ木組み建築（Fachwerk）**:
  - 濃茶の木組みフレーム＋クリーム/淡黄の漆喰（プラスター）壁
  - 急勾配の切妻屋根＋**テラコッタ（赤橙）瓦**
  - 1階より2階が張り出す **ジェティ（jetty）** 構造
  - 石畳の街路、鍛鉄の吊り看板、窓辺の花箱
- G.I.は「ゲーム世界」なのでRPG的な明快さ・彩度高めのファンタジー寄りに振ってよい。

## 建築構造の要点（モデリングに反映）

### 木組み（timber frame / wattle & daub）
- 構造材: 垂直の柱（post）・水平の梁（beam）・**斜めの筋交い（brace）** が外壁に露出。間を漆喰パネルが埋める。
- 筋交いは X 字・ハの字に入る。モデリングでは梁を壁面から2〜3cm浮き出させると立体感が出る。
- 屋根: 垂木（rafter）の上に瓦を重ね葺き。軒の出を大きく取ると影が落ちて情報量が増える。

### 市場屋台（market stall）
- 基本構造は「柱の上の屋根＋その下のカウンター」。壁・扉なし＝覗き込み型に最適。
- 木のペグとロープで組む分解可能な構造。**ストライプのキャンバス天幕（awning）** が定番。
- カウンター上に商品陳列（樽・木箱・布掛け台）。

### 井戸（draw well）
- 石積みの円筒ベース＋2本の木柱＋小さな切妻屋根＋**巻き上げ機（windlass）とロープ・桶**。
- 屋根は瓦または板葺き。柱は石積みの外側に立てる。

## テクスチャ方針（PolyHaven）
- 既存実績: rock_face, mossy_rock, mossy_stone_wall, aerial_grass_rock（Stage/Textures）
- 追加候補: 漆喰(plaster)系、古材(weathered wood/planks)系、テラコッタ瓦(roof tiles)系、石畳(cobblestone)系
- 予算: アトラス 2K albedo + 1K normal/AO。AO・陰影はBlenderで焼き込み（ライトマップ不使用のため）

## ソース
- https://hunterxhunter.fandom.com/wiki/Antokiba （Rothenburgモデル説の出典）
- https://www.buildingconservation.com/articles/wattleanddaub/wattleanddaub.htm
- https://en.wikipedia.org/wiki/Wattle_and_daub
- https://www.newworldencyclopedia.org/entry/Timber_framing （jetty構造）
- https://timberframehq.com/history-of-timber-framing/ （post-on-sill工法）
