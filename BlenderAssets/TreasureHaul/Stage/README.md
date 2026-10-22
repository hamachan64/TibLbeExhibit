# Treasure Haul ステージ（5m × 11m）

発掘中の遺跡の内部。高さ 2.4m の仮想の壁（天井なし）で部屋と通路を区切り、お宝は祭壇・壁龕・棚・作業台のトレー・木箱・石棺などの上や中に置く（方向性と合意事項は [RESEARCH.md](RESEARCH.md)、見た目の見本は `sample_room.py` と `Sample/`）。

出力は [Output/](Output/README.md) の1か所だけ。作り直すたびに上書きし、版の区別は Git のコミットでつける（旧方針の低い遺構の版は、コミット `d020226` 以前の履歴にある）。

## スクリプト（すべて Blender 5.1 のバックグラウンド実行。MCP は不要）
`Stage/` で、上から順に実行する。

| ファイル | 実行方法 | 役割 |
| --- | --- | --- |
| `build_stage.py` | `blender -b --factory-startup --python build_stage.py` | ステージ・プレビュー・アトラスを作り、`Output/` に書き出す |
| `check_context_paths.py` | `blender -b --factory-startup --python check_context_paths.py` | 歩ける範囲、1.15m 幅で回れるか、全置き場所に近づけるか。`approach_checks.json` と `floorplan.svg` |
| `check_search_visibility.py` | `blender -b Output/Stage_Review.blend --python check_search_visibility.py` | 入口から隠れるか、近くで見つかるか、ビームで取れるか、光の当たり方、めり込み・浮き。`search_visibility.json` |
| `verify_stage.py` | `blender -b --factory-startup --python verify_stage.py` | FBX と blend を読み込み直して検証（三角形数、範囲、マーカー、実際の通路幅、宙に浮いた石）。`verification.json` |
| `render_search_views.py` | `blender -b Output/Stage_Review.blend --python render_search_views.py [-- 出力先 視点...]` | 確認用の画像（JPEG）。`slots` を指定すると、置き場所ごとの近接画像も出す |
| `sample_room.py` | `blender -b --factory-startup --python sample_room.py` | 見た目の見本（`Sample/`）。ステージの生成には使わない |

座標は、Unity が X=幅、Y=高さ、Z=奥行。Blender は `(X, -奥行, 高さ)`。原点は入口の辺の幅中央、床面。`.blend` は Blender 5.1 で保存している（4.4 では開けない）。Unity への取り込みと PICO 実機での確認はしていない。

外部の素材・サービスは使っていない（形もテクスチャもスクリプトで生成）。R.E.P.O. から借りたのは「部屋と通路」「家具の上や中のお宝」「サイズ別の置き場所から毎回選ぶ」という作り方だけ。

`../Cart/build_cart.py` は以前このフォルダの旧 `build_stage.py` の部品関数を読み込んでいたため、その部品関数を `build_cart.py` の中へ移した（台車の見た目は変えていない。台車は将来案）。
