# 台車（Salvage Cart）

出力は [Output/](Output/README.md)。`build_cart.py` で生成し、作り直すたびに上書きする。調査と設計の根拠は [RESEARCH.md](RESEARCH.md)。

| ファイル | 実行方法 | 役割 |
| --- | --- | --- |
| `build_cart.py` | Blender MCP | 台車を作り、`Output/` に書き出す。ステージの部品関数と、ステージの元の共有アトラスを `../Stage/build_stage.py` から使う |
| `verify_cart.py` | Blender MCP | FBX と blend を読み込み直し、寸法・UV・車輪と首振りの軸を検証する |
| `*.csx` | uLoop（Unity） | Prefab と確認シーンの組み立て、PlayMode での確認 |

初版（細いレール型）の出力と `build_cart_v2.py` は、2026-10-20 に1本にまとめた際に削除した（履歴はコミット `e880f0b` 以前）。まとめた後の出力は、以前の Revision2 と一致することを確認した（`cart_stats.json` はルートの名前の番号だけが違い、テクスチャ4枚の画素は同じ）。
