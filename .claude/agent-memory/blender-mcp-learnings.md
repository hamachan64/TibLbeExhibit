# Blender MCP 学習メモ

Blender MCP（`mcp__blender__execute_blender_code`）でモデリング作業をした際のハマりどころ。
環境: Blender 5.1.2 / 日本語UI / ポート9875。

## 1. Principled BSDF ノードは名前で取得しない
- 日本語UIでは `nodes.get('Principled BSDF')` が `None` を返すことがある。
- **回避策**: `for n in mat.node_tree.nodes: if n.type == 'BSDF_PRINCIPLED'` でタイプ検索する。
- Principledの入力名はバージョン差があるため `"Transmission Weight"` / `"Transmission"` の両対応で `in bsdf.inputs` チェックしてから設定する。

## 2. `object.copy()` + `collection.objects.link()` で作った複製が描画されない
- Blender 5.1 + MCP経由の実行で、`obj.copy()`（メッシュ共有）で量産→ `bpy.context.collection.objects.link()` したオブジェクトが、
  `visible_get()==True`・位置/メッシュ/コレクション所属もすべて正常なのに、**ビューポートにもレンダーにも一切描画されない**現象が発生した。
  `view_layer.update()` や depsgraph 評価でも解消しなかった。
- **回避策**: `bpy.ops.mesh.primitive_*_add()` で個別に生成し、必要なら `bpy.ops.object.join()` で1メッシュに統合する（チェーン52リンクで実績あり）。

## 3. 同一スクリプト内での「生成→即レンダー」は避ける
- オブジェクト生成とF12レンダー（`bpy.ops.render.render(write_still=True)`）は別のexecute呼び出しに分けたほうが安全。

## 4. チェーン（鎖）のモデリング
- Arrayモディファイア（オブジェクトオフセット+FIT_CURVE）+ Curveモディファイアの定番構成は、MCP経由だとリンクが散乱して安定しなかった。
- **回避策**: 円周上にパラメトリック配置（接線方向に回転 + 交互に±50°ロール）が確実で、ケーブルチェーンらしく見える。

## 5. 既存オブジェクトの移動でもモディファイア評価が固まる
- Shrinkwrap等のモディファイア付きオブジェクトを `obj.location` で移動しても、MCP経由だと再評価されず、
  レンダーに未変形（または旧形状）のまま出ることがある。
- **回避策**: 位置を変えたい場合はオブジェクトを削除して新しい位置で `bpy.ops` から作り直す。

## 6. Unity向けFBXエクスポートの実績レシピ（Blender 5.1）
```python
bpy.ops.export_scene.fbx(
    filepath=..., use_selection=True, object_types={'EMPTY','MESH'},
    use_mesh_modifiers=True,   # Shrinkwrap/Solidify/Booleanを焼き込み
    mesh_smooth_type='OFF', global_scale=0.03,
    apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', bake_space_transform=True)
```
- 空のルート（例: PaladinNecklace）に全パーツを親子付けし、ルートから再帰選択して `use_selection=True` で書き出す。
- ブーリアンのカッターは親子付けしない＋`hide_render=True` にしておけば選択から自然に外れる。
- 書き出し先は `Assets/_Content/Models/`。書き出し後は必ず uloop 経由で `AssetDatabase.Refresh()` → .meta生成を確認（unity_asset_rules準拠）。

## 7. その他
- ビューポート確認は `space.shading.type='MATERIAL'` + `region_3d.view_perspective='CAMERA'` + `overlay.show_overlays=False` にしてから `get_viewport_screenshot`。
- 成果物の .blend は Unity の `Assets/` 外（`BlenderAssets/`）に保存する（.meta汚染防止）。

## 8. CardBinder（ページめくり可能な本）制作での追加学習（2026-07）
- **大量サブパーツは bmesh で1メッシュに直接構築**: `bmesh.ops.create_cube(bm, size=1)` で返る verts を手動スケール/移動し、面の `material_index` を直接設定する方式なら、#2 の copy() 描画バグを完全回避できる（1ページ=箱73個でも一発成功、8ページ分ループ生成OK）。
- **可動パーツのピボット設定**: 3Dカーソル移動 → `bpy.ops.object.origin_set(type='ORIGIN_CURSOR')`。ページめくり用は「各オブジェクトの原点を背表紙軸上（自分の平面高さ）」に置き、Unity側は localRotation 回転のみでめくれる。
- **実寸（メートル）モデリングなら `global_scale=1.0`** + `apply_scale_options='FBX_SCALE_ALL'` でUnityに1:1で入る（Unity側 globalScale=1 / fileScale=1 を確認済み）。#6 の 0.03 はモデルを拡大して作った場合の値。
- **bake_space_transform の座標系変換で、Unity上では子のローカルX座標の符号が反転する**（Blender -0.022 → Unity +0.022）。バグではなく右手系→左手系変換の仕様。
- **カメラの向きは手動Eulerでなく** `(target - cam.location).to_track_quat('-Z','Y').to_euler()` で確実に狙う。
- ビューポートはユーザー操作等でカメラビューから外れることがある。スクリーンショット直前に毎回 `view_perspective='CAMERA'` を設定し直すのが安全。
- **平面に置くフラットな三角/菱形は `primitive_cone_add(vertices=3or4, depth=極小)` をそのまま使う**（三角形の面は最初からXY平面にある）。X90°回転＋スケールで倒そうとすると極薄の縦板になり見えなくなる。向きはZ回転だけで調整し、裏面（-Z側）に見せる場合はX180°回転を使う。
- FBXを**同一パスに上書きエクスポートすればUnityのGUIDは維持される**（プレハブ等の参照が切れない）。モデル修正時は削除→再作成ではなく上書きすること。
- **Unityプロジェクト内（Assets/配下）の画像をBlenderのImage Textureノードに読み込んでFBXエクスポート（path_mode='AUTO'）すると、Unity側で自動的にそのテクスチャがマテリアルの_BaseMapにリンクされる**（URP Lit・ImportViaMaterialDescription経由、リマップ作業不要）。
- 本の表裏スプレッドテクスチャのUVは、ワールドX座標→U の線形写像1本で裏表紙・背・表紙の3オブジェクトに共通適用できる（実寸比とテクスチャ領域比を合わせておくのが前提）。bmeshの `loops.layers.uv.new()` で面ごとに手動UV設定。
- 開いた本を真下から見るビュー（視線≈+Z）は `to_track_quat('-Z','Y')` のロールが縮退して180°回転して見えることがある。**鏡像と回転を混同しない**こと（回転なら正しい）。

## 9.5 Blender MCPは複数タスク間で共有される（2026-07）
- Blender MCP（ポート9875）は単一のBlenderプロセスに接続する共有インスタンス。並行して他のモデリングタスクが動いていると、**作業開始時に指示された`.blend`ファイルが開かれているとは限らない**（例: 「一坪の密林」担当タスク開始時、実際には別タスクの`CardItem_BubbleHorse.blend`が開いた状態だった）。
- **回避策**: 作業前に必ず `bpy.data.filepath` と `bpy.data.objects` 一覧で現在開かれているファイルを確認する。想定と違う場合は `bpy.data.is_dirty` / `bpy.data.is_saved` で他タスクの未保存変更が無いことを確認してから `bpy.ops.wm.open_mainfile(filepath=...)` で目的のファイルに切り替える（未保存の変更があれば安易に切り替えず報告する）。
- 対象の`.blend`が既にディスク上に保存済み（他プロセスがモデリングを完了させて保存済みだが、その後Blender上では別ファイルに切り替わっていた）というケースだったため、ディスク上の最新版を開き直せば作業に支障はなかった。

## 9. 千里眼の蛇（チューブ状生物モデリング）での追加学習（2026-07）
- **`get_viewport_screenshot` が突然真っ黒を返し続けることがある**（tag_redraw等でも回復せず。ウィンドウのオクルージョンが原因の可能性）。確実なのは EEVEE で `bpy.ops.render.render(write_still=True)` によるファイル出力→Readで確認する方法。または `view3d.view_selected` オペレータでフレーミングすると回復した実績あり（`region_3d.view_location`/`view_distance` の手書きは信頼できない）。
- **パス沿いチューブ（蛇・ロープ等）の鉄則: パスの曲率半径 > 断面半径**。違反すると曲がりの内側でリングが交差し「ヒダ／フラップ」状の破綻ジオメトリが出る（顎の下に謎の三角フラップとして現れた）。sin/cosイージングは曲がりが終端に集中して局所曲率が急になりがち。**首のような90°ターンはCatmull-Rom制御点で明示的に設計**し、`連続リング間の角度変化から最小曲率半径を数値チェック`してから確定する。
- **チューブUVのアスペクト補正**: U=周方向・V=弧長比のUVでは、世界空間のスケールが U:周長(〜0.17m) vs V:全長(〜1.2m) と大きく異なる。テクスチャに丸い模様を描くには `rv = ru × (周長/全長)` の補正が必須（忘れると模様が体軸方向に細長く伸びる）。
- **半埋め込みの目玉等は「中心がメッシュ内部にあるのが正常」**。サブエージェントやレビューアが「メッシュ内部にある＝バグ」と誤判定して動かすことがある。指示時に意図を明記するか、結果を必ず自分でレンダリング確認する。
- `mathutils.bvhtree.BVHTree.find_nearest()` の返す法線は**メッシュによっては外向きと逆**のことがある。法線方向オフセットの前に、レイキャストのパリティ判定（内外判定）で±両方向を検証すること。
- uloop-execute-dynamic-code 内の `DestroyImmediate` は `using System;` と衝突して CS0104 になる → `UnityEngine.Object.DestroyImmediate(...)` と完全修飾する。
- パラメトリック生成の中間データ（パス点・フレーム軸・断面プロファイル）は `bpy.app.driver_namespace` に入れると同一Blenderセッション内の後続execute呼び出しから参照できる（目・舌の配置に使用）。
- **【最重要】bmesh手組みメッシュの法線内向き → Unityで「透過」**: bmeshで面を張ると、ワインディング次第で法線が内向きになることがある。Blenderのビューポートは両面表示なので**一見正常に見える**が、UnityのURP Lit（Opaque）は裏面カリングするため、体の向こう側＝背景が透けて見える（ユーザー報告「透過されて見える」＝マテリアルやアルファではなく法線が犯人）。**エクスポート前に必ず符号付き体積 `Σ dot(face.center, face.normal)*face.area` の符号で外向き（正）を検証**し、負なら `bmesh` で全面 `f.normal_flip()` する。閉じたメッシュ（胴体・舌）が対象。目のような凸球は `create_uvsphere` なので元から正しい。
