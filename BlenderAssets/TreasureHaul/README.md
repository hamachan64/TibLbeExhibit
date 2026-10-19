# Treasure Haul の 3D 素材（Blender）

Unity の `Assets/` の外に置いている制作データ。版はフォルダやファイル名で分けず、Git のコミットで管理する（過去の版は `git log -- BlenderAssets/TreasureHaul` から取り出す）。外部の素材・サービスは使っていない（`THIRD_PARTY_NOTICES.md`）。

| フォルダ | 中身 | 作り方 |
| --- | --- | --- |
| [Treasures/](Treasures/README.md) | お宝 15 種の `.blend` と `.fbx` | `.blend` が正本。直接編集する |
| [Stage/](Stage/README.md) | 5m × 11m のステージ。出力は `Stage/Output/` | `Stage/build_stage.py` で生成 |
| [Cart/](Cart/README.md) | 台車。出力は `Cart/Output/` | `Cart/build_cart.py` で生成 |

- `.blend` は Blender 5.1 で保存している（4.4 では開けない）。
- Unity には、必要な FBX を `Assets/_Content/TreasureHaul/Models/` にコピーして取り込む（台車は取り込み済み。お宝とステージは未取り込み）。Unity のアセットは Editor API で扱い、テキストで編集しない。
- 生成スクリプトは Blender MCP で実行する。専用のシーンを作って書き出し、ユーザーのシーンに戻す。確認・検証用のスクリプトは、できるだけ `blender -b`（バックグラウンド）で動かす。
