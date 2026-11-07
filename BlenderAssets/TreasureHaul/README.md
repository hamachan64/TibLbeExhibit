# Treasure Haul の 3D 素材（Blender）

Unity の `Assets/` の外に置いている制作データ。版はフォルダやファイル名で分けず、Git のコミットで管理する（過去の版は `git log -- BlenderAssets/TreasureHaul` から取り出す）。外部の素材・サービスは使っていない（`THIRD_PARTY_NOTICES.md`）。

| フォルダ | 中身 | 作り方 |
| --- | --- | --- |
| [Treasures/](Treasures/README.md) | お宝（コア 12 種、持ち手なし）の `.blend` と `.fbx`、共有アトラス | `Treasures/build_treasures.py` で生成 |
| [Stage/](Stage/README.md) | 5m × 11m のステージ。出力は `Stage/Output/` | `Stage/build_stage.py` で生成 |
| [Cart/](Cart/README.md) | 台車。出力は `Cart/Output/` | `Cart/build_cart.py` で生成 |
| [Enemies/](Enemies/README.md) | 敵 2 体（番人の石像＝角冠の王、遺跡の亡霊＝墓守の霊）の `.blend` と `.fbx`、テクスチャ、`stats.json` | `Enemies/build_enemies.py` で生成（確認は `verify_enemies.py`・`render_review.py`） |
| [Board/](Board/README.md) | 納品場所の合計額の表示板（発掘隊の電光掲示板。足場と鎖つき）の `.blend` と `.fbx`、テクスチャ、`stats.json`（画面・数字の範囲・札・ランプの位置） | `Board/build_board.py` で生成（確認は `verify_board.py`・`render_board.py`） |

- `.blend` は Blender 5.1 で保存している（4.4 では開けない）。
- Unity には、必要な FBX を `Assets/_Content/TreasureHaul/Models/` にコピーして取り込む（台車は取り込み済み。お宝とステージは未取り込み）。Unity のアセットは Editor API で扱い、テキストで編集しない。
- 生成スクリプトは Blender MCP で実行する。専用のシーンを作って書き出し、ユーザーのシーンに戻す。確認・検証用のスクリプトは、できるだけ `blender -b`（バックグラウンド）で動かす。
