# ステージの参考調査と方向性（2026-10-21）

前のステージ（Revision5〜7）は、お宝が低い石板や箱に「置かれているだけ」で、遺跡の部屋にも見えなかった。作り直しの前に、R.E.P.O. の作り方、MR の設計指針、実際の発掘現場を調べ、方向性をユーザーと決めた。

## 調べたこと

### R.E.P.O.
- **部屋のブロックを組み合わせて、毎回ランダムに組む。** ブロックは最大 15×15m で、種類はスタート部屋（トラック）・通常（四方に出入口）・通路・行き止まり・回収地点。部屋の見た目は、隣とつながっているかどうかで切り替わる（[REPOLib SDK：レベル](https://repomods.com/apis/repolib/sdk/levels.html)、[Wikipedia](https://en.wikipedia.org/wiki/R.E.P.O.)）。
- **お宝は部屋ごとに決めた出現場所に、サイズ別に置かれる。** サイズは極小・小・中・大・横長・縦長・特大縦長の 7 種類。場所は棚・机・家具の上・箱の中・床（[Valuables（R.E.P.O. wiki）](https://repogame.miraheze.org/wiki/Valuables)、[REPOLib SDK：お宝](https://repomods.com/apis/repolib/sdk/valuables.html)、[GameRant](https://gamerant.com/repo-all-valuables-prices/)。出現場所の仕組みはコミュニティの説明で、開発元の公式な説明ではない）。
- **お宝はその場所にあって当然の物。** 屋敷なら花瓶・時計・絵、北極の研究所なら研究機材、魔法学校なら薬瓶や杖、美術館なら美術品。攻略記事も、収納・引き出し・ベッドの下・棚・家具の上を探すよう勧めている（[Valuables（R.E.P.O. wiki）](https://repogame.miraheze.org/wiki/Valuables)、[Dot Esports](https://dotesports.com/indies/news/r-e-p-o-valuables-list-price-and-all-effects)、[Maps Guide](https://www.repo-game.org/en/repogame-maps)）。
- **公式スクリーンショット 5 枚を目で確認した**（[Steam](https://store.steampowered.com/app/3241660/REPO/)。画像は一時フォルダで見ただけで、成果物には入れていない）。
  - 屋敷の部屋に、恐竜の骨格が専用の展示台ごと置かれ、周りに木の長持ちやソファがある。
  - 部屋は普通の高さの家具で埋まっている。
  - 回収地点は床の枠と大きな金額表示。
  - 全体に暗く、低ポリゴンで、ブラウン管のような映像効果がかかり、数字表示だけが明るい。見つけにくさは、暗さ・家具の陰・部屋の入り組みから来ている。

### MR の設計指針と事例
- **Meta のガイドライン**（[MR の健康と安全](https://developers.meta.com/horizon/design/mr-health-scene)、[境界のない体験](https://developers.meta.com/vr/design/boundaryless-best-practices/)、[パススルー](https://developers.meta.com/horizon/essentials/horizon-os-passthrough/)）
  - 床を覆う大きな仮想物で、つまずく物を隠さない。
  - 部屋を丸ごと作り替える表現は避ける。
  - 歩く以上の細かい動きを求めない。
- **Spatial Ops**（Resolution Games）は、同じ場所にいる複数人が、現実の部屋に置いた仮想の壁や遮蔽物の間を歩いて遊ぶ MR ゲーム。広い空間が要る（[UploadVR](https://www.uploadvr.com/spatial-ops-full-release-impressions/)、[GamesBeat](https://gamesbeat.com/resolution-games-spatial-ops-is-a-frenetic-vr-shooter/)）。

### 発掘現場
- 出土品は、出土した層の番号を書いた袋と札で管理し、洗浄・乾燥・分類はトレーで行う。現場の小屋は、記録・出土品・道具・休憩の拠点になる（[Oxford ArcheOx](https://archeox.conted.ox.ac.uk/finds-processing.html)、[Suffolk](https://heritage.suffolk.gov.uk/responsible-archaeology-excavation)、[Wikipedia](https://en.wikipedia.org/wiki/Excavation_(archaeology))）。
- 作業台・遺物トレー・札・木箱・照明は、お宝が置かれて当然の場所になる。

### 置き方の考え方
- 空間に「何があったか」を想像させる（環境で語る）。物を意味なく置かない（[GDC 2010「What Happened Here?」](https://gdcvault.com/play/1012647/What-Happened-Here-Environmental)）。

## 決まったこと（ユーザーと合意）
- **ステージは、高い仮想の壁で区切った「発掘中の遺跡の内部」にする。** 以前の企画書にあった「仮想物は腰より低く」はやめた（企画書 7.1・7.2、要件 F-ITEM-2・F-SAFE-2 を改訂）。
- **R.E.P.O. から借りるのは作り方だけ。** 部屋と通路、家具の上や中のお宝、サイズ別の置き場所から毎回選ぶ、の3つ。名称・形状・素材・音は使わない。
- **部屋には遺跡の家具と、発掘隊の設備を置く。**
  - 遺跡の家具：祭壇・壁龕・石棺・壁画
  - 発掘隊の設備：作業台・遺物トレー・木箱・照明・明るい画面の機材
  - 企画書にある「廃れた環境・角張った機械・明るい電子表示の対比」を、この組み合わせで出す。
- **床に直に置かない。** すべてのお宝は置かれる理由のある物の上か中に置く。
- **R.E.P.O. との違い：** 暗さで隠すことはしない。展示会場をパススルーで見るため。

## 見本で決まったこと（2026-10-21）
- 壁は高さ 2.4m、天井なし。
- 見た目は R.E.P.O. 寄りの暗めの廃墟にする。崩れた壁の上端、抜け落ちた石、崩落した瓦礫、苔の染み、ひび、倒れた木箱、作業灯の光だまり。暗くできるのは仮想の壁や物だけで、会場は明るいまま見えている。
- お宝は作り直す。暗い部屋では、今の暗い青銅のお宝は持ち手の光る輪しか見えなかった。

## 見本で決めること（未確定）
- 出入口の幅（見本は 1.2m）
- 壁の向こうの仲間の見せ方（Unity 側の実装が必要）
- 作り直すお宝の方向性

見本は `sample_room.py`（`Sample/`）。この見本の材質は見た目の確認用に Blender の手続き的なテクスチャを使っている。ステージ本体では、Unity 用に共有アトラスへ焼き込む。
