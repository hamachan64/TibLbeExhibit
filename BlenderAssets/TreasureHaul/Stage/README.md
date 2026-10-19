# Treasure Haul ステージ（5m × 11m）

出力は [Output/](Output/README.md) の1か所だけ。作り直すたびに上書きし、版の区別は Git のコミットでつける。過去の版は Git の履歴から取り出す（初版〜Revision4: コミット `07e74b8` まで、Revision5〜7: `d020226`。例: `git show d020226:BlenderAssets/TreasureHaul/Stage/Revision6/README.md`）。

> 今の出力（旧 Revision7）は完成版ではない。見えているお宝5個が低い石板に載っているだけで、床置きに見える。置き場所も格子状に並んだまま。置き場所の配置から作り直す予定。

## スクリプト
| ファイル | 実行方法 | 役割 |
| --- | --- | --- |
| `build_stage.py` | Blender MCP | ステージとプレビューを作り、`Output/` に書き出す。専用のシーンを作り、元のシーンに戻す |
| `check_search_visibility.py` | `blender -b Output/Stage_Review.blend --python check_search_visibility.py` | 隠し場所の判定（入口から隠れるか、近くで見つけられるか、ビームで取れるか、めり込み、床置き） |
| `check_context_paths.py` | `blender -b --factory-startup --python check_context_paths.py` | 人と台車の動線の判定。`floorplan.svg` も書き出す |
| `render_search_views.py` | `blender -b Output/Stage_Review.blend --python render_search_views.py -- Output [eye eye_west top overview peek]` | 確認用の画像を書き出す |
| `verify_stage.py` | Blender MCP（上の2つの判定の後） | FBX と blend を読み込み直して検証し、`verification.json` に書く |

`.blend` は Blender 5.1 で保存している（4.4 では開けない）。座標は、Unity が X=幅、Y=高さ、Z=奥行。Blender が `(X, -奥行, 高さ)`。原点は入口の辺の幅中央、床面。Unity への取り込みと PICO 実機での確認はしていない。
