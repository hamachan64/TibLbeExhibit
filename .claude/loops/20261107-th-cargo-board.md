# 納品場所の表示板（合計額）を「発掘隊の電光掲示板」に作り直す

## 目的
納品場所の上の合計額の表示板は、今は黒い板に既定の字体で黄色の文字を置いただけで、遺跡・発掘隊の雰囲気に合っていない（2026-11-07 ユーザー指摘）。
企画の「廃れた遺跡 × 角張った機械 × 明るい電子表示」の対比（1章）に沿う、**発掘隊が持ち込んだ電光掲示板**に作り直す。
納品場所の機械（くすんだ青緑の鉄・水色の LED）と同じ系統にし、会場の明るいパススルーでも数字が読めるようにする。

見た目の案（ユーザーが A を選択）: スクラッチパッドの `board/concepts.png`（案 A）。

## 決まったこと（2026-11-07 ユーザー判断）
- 見た目: **A 発掘隊の電光掲示板**。くすんだ青緑の鉄の箱、上辺に黄黒の注意帯、左上に「納品額」の札、右上に小さな緑の通電ランプ、四隅のボルト、黒い画面に**琥珀色の 7 セグメント数字**。点いていない桁・セグメントもうっすら見える。足場（2本の柱と横棒）から鎖で吊る。
- 動き: 金額が増えたとき、**数字が約 0.6 秒で新しい額まで数え上がり、表示板が一瞬明るく光る**。
- 出すのは**合計額だけ**（企画 5.4 は変えない）。「+300G」などは出さない。

## 受け入れ基準
### A. モデル（Blender）
- [ ] `BlenderAssets/TreasureHaul/Board/` に、生成スクリプト・.blend・FBX・テクスチャ・確認画像・README がある（外部素材なし。手続き的に自作）。
- [ ] 部品: 箱（前面に画面の凹み、背面も作る）・注意帯・「納品額」の札（文字は入れず、無地の札。文字は Unity の TMP で載せる）・通電ランプ・ボルト・吊りの鎖2本・足場（柱2本＋横棒、足元の板）。
- [ ] 箱の外寸は 幅 約1.9m × 高さ 約0.65m × 奥行 約0.12m。画面の中心が、今の表示板の中心（`CargoBoard` の位置、VenueRoot ローカル (0, 1.5, 0.32)）に来る。
- [ ] 足場の柱は、納品場所の機械（`THStage_ExtractionBay`）・回収範囲（中心 (0, 0.49, 1.2)、大きさ 2.3×0.92×1.56）にめり込まない位置に立つ（`ExcavationDepot_5x11.blend` で寸法を測る）。
- [ ] 汚れ・塗装の剥げ・錆のテクスチャを焼き込み、ステージ（暗めの廃墟）・納品場所の機械と並べて浮かない。三角形 3,000 以下、テクスチャ 1024 以下。
- [ ] 軸は #67 と同じ書き出し方（axis_up='Z', axis_forward='Y' ＋ turn_z180、Unity 側 Bake Axis Conversion）で、Unity で回転 0・Y が上・正面 +Z。
- [ ] 7 セグの画面（数字を出す範囲）の位置と大きさを `stats.json` に書き出す。

### B. Unity
- [ ] 7 セグメント表示は**コードで作るメッシュ**（外部フォントなし）。5 桁＋千の位の区切り（,）＋「G」の 7 セグ。右詰め、上の桁の 0 は消灯（うっすら見える）。99,999 を超えたら 99,999 で止める。
- [ ] 点灯・消灯の色と明るさは、自作の軽いシェーダー（不透明・アンリット・頂点色で点灯／消灯を選ぶ。シングルパス・インスタンシング対応）で出す。
- [ ] `CargoBoardView` は合計額を整数で受け取る（`SetTotal(int)`）。増えたときは DOTween で数え上げ（初期値 0.6 秒）、画面と通電ランプを一瞬明るくする（初期値 0.4 秒）。減ったとき・最初の表示・非表示からの再表示は、数え上げずにすぐその額にする。
- [ ] 表示する期間は今と同じ（練習〜結果。待機・オープニングでは消す）。足場ごと消す。
- [ ] 「納品額」の札の文字は TMP（Noto Sans JP、既存のフォント）。文言は `ThTextTable` に新しい ID を足して取る（末尾に追加）。`BoardTotalFormat` は使わなくなるが、enum の値はずらさない。
- [ ] `TH_CargoBoard.prefab`（モデル＋7 セグ＋TMP の札＋`CargoBoardView`）を作り、`TreasureHaulEnvironment.unity` の今の `CargoBoard` と置き換える（`ThVenue._board` の結線、`ThStageBuilder` の `ScoreBoardAnchor` での位置合わせが引き続き効くこと）。古い `TH_Board.mat` は、他に使われていなければ消す。
- [ ] 当たり判定なし（ビーム・お宝・亡霊の視線を止めない）。

### C. 確認
- [ ] EditMode テスト: 7 セグの数字→セグメントの表（0〜9・G）、桁分け（0・7・1,250・99,999・100,000 の頭打ち）、増えたとき・減ったときの数え上げの有無。
- [ ] PlayMode: 練習〜本編で表示板が出て、お宝の納品で数え上がり光ることをスクリーンショットで確かめる。練習の列の位置から、表示板と納品場所が一緒に見える画を撮る。

## 変更対象
- 新規 `BlenderAssets/TreasureHaul/Board/`（build_board.py・verify・render・README・stats.json・.blend・.fbx・textures・review/）、`BlenderAssets/TreasureHaul/README.md` に1行。
- 新規 `Assets/_Content/TreasureHaul/Models/Board/`（FBX・テクスチャ）、`Materials/TH_CargoBoard*.mat`、`Shaders/TH_SegmentDisplay.shader`、`Prefabs/TH_CargoBoard.prefab`。
- `Scripts/View/CargoBoardView.cs`（作り直し）、新規 `Scripts/View/SevenSegment*.cs`（表と桁分けは純関数にしてテストする）。
- `Scripts/Treasure/TreasureClient.cs`（`SetText` → `SetTotal`）、`Scripts/Content/ThTextTable.cs`（札の文言）。
- `Scenes/TreasureHaulEnvironment.unity`（uloop 経由で差し替え）。
- 新規 `Assets/_Content/Tests/EditMode/ThCargoBoardTests.cs`。
- `THIRD_PARTY_NOTICES.md`（自作の記録）、`TREASURE_HAUL_PLAN.md` 5.4 に見た目を一文、`TODO.md` の決定ログ。

## やらないこと
- 表示する内容（合計額だけ）の変更。目標額・残り時間を表示板に出すこと。
- 運営PCのスコアモニター・結果表示の見た目。
- 納品場所の機械・ステージのモデルの変更。

## 制約
- 規約: UniTask / R3 / DOTween、`.unity/.prefab/.asset/.mat` は uloop のエディタ API で編集。
- PICO（モバイル VR）で軽いこと: 透明は使わない（光りは色の明るさで出す）。描画は 5 回（箱・画面・ランプ・7 セグ・札）＋箱の影 1 回（当初の目安 3〜4 回は、画面を箱のアトラスへまとめる作業に見合わないため 5 回で可とした）。灯りは足さない（回収したお宝・電球と追加ライトを取り合うため。正面の明るさは箱の自己発光で出す）。
- 作業ブランチは `repo/feature-th-enemy-models`（PR #71 にまとめる。2026-11-07 ユーザー指示）。

## 実機確認が必要な項目
- 明るいパススルーの会場で、練習の列（約 2m）・遠い所（約 8m）から数字が読めるか。
- 光り方がまぶしすぎないか。

## 外部の素材・サービス
- なし（モデル・テクスチャは Blender で手続き的に自作、7 セグは自作のメッシュ、札の字体は既存の Noto Sans JP（OFL、記録済み））。
