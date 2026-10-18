# Issue #46: お宝 S 3点の制作

## 目的
仮面・杯・小像を、写実寄りの古びた金属と光る持ち手で統一する。ユーザーの具体的な制作・PR依頼を承認として進める。

## 受け入れ基準
- 仮面・杯・小像の .blend と FBX を BlenderAssets/TreasureHaul/S/ に保存。
- 各モデルは持ち手込みで 5,000 tri 以下、発光マテリアルの持ち手が1つ。
- Blender MCP の look で実物を確認し、tri数・寸法・マテリアルを記録。
- 既存の未保存 Blender シーンを保持。専用コレクションのみ追加し、ライブラリ書き出しで保存。
- 権利・費用・セキュリティの4点を THIRD_PARTY_NOTICES.md に記録。
- 現在の repo/feature-th-asset-models のまま PR を repo/develop に作成。

## 変更対象
- BlenderAssets/TreasureHaul/S/: モデル、制作スクリプト、プレビュー、検証記録、README。
- THIRD_PARTY_NOTICES.md: 使用ツールと自作モデルの確認記録。
- 本仕様書。

## スコープ外
TODO.md、Assets/、Unity取り込み、M/Lのお宝、荷台、瓦礫。

## 検証方法
Blender の評価済みメッシュと FBX の再読み込みでtri数・持ち手・寸法を確認。look で目視。Unityコンパイル・EditMode・実機は未実施として明記（取り込み前の成果物）。

## 外部の素材・サービス
2026-10-18 確認。Smithsonian は検索で杯・仮面候補を発見したが個別 CC0 を確認できず不採用。Poly Haven 公式 https://polyhaven.com/license は CC0・商用可・表記不要。候補 https://polyhaven.com/a/bronze_shark_statue は7K tri、https://polyhaven.com/a/gothic_statue は28K triで、今回の小型遺物3点の統一にそのまま適さず不採用。外部モデル・画像・テクスチャはダウンロードしない。

使用: 既設 Blender 5.1.2 と MCP for Blender のローカル自作モデリング機能のみ。Blender公式ライセンス https://www.blender.org/about/license/ および公式マニュアル https://docs.blender.org/UATEST/manual/en/dev/getting_started/about/license.html により作品はGPLの対象外で商用可。MCPの公式MIT原文 https://github.com/ahujasid/mcp-for-blender/blob/main/LICENSE と公式配布 https://pypi.org/project/mcp-for-blender/ を確認。既設コア機能は無料、外部API・Premium・生成クレジット利用なし。自作の形状とマテリアルだけを用い、人物・ブランド等の模倣なし。追加インストール・秘密情報・外部スクリプト実行なし。開発ツール本体は配布しない。
