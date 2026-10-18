# 外部の素材・サービス・パッケージの記録

> `.claude/rules/license_cost_security.md` の4点（商用利用・権利・無料・セキュリティ）を確認した結果を記録する。
> 「状態」が **確認済み** の物だけを使う。**未確認** の物は使う前に公式ページの原文で確かめ、この表を更新する。
> 表示の要否が「要」の物は、待機画面のクレジット欄に出す。

## 使う予定の物（Treasure Haul）

| 名前 | 用途 | 入手元 | ライセンス | 商用 | 費用 | 表示 | 状態 | 確認日 |
| :--- | :--- | :--- | :--- | :---: | :--- | :--- | :--- | :--- |
| Smithsonian Open Access 3D | お宝のモデル（文化財スキャン） | https://3d.si.edu/ | CC0（モデルごとに Open Access 表示を確認） | 可 | 無料 | 不要 | 未確認（二次情報のみ） | ― |
| Poly Haven | 瓦礫・岩などのモデル・テクスチャ | https://polyhaven.com/ | CC0 | 可 | 無料 | 不要 | 未確認 | ― |
| Quaternius | ローポリの遺跡パーツ（予備） | https://quaternius.com/ | CC0 | 可 | 無料 | 不要 | 未確認 | ― |
| STYLY NetSync サーバー（PyPI `styly-netsync-server` 0.17.4、`uvx` で取得） | 開発・展示の同期サーバー（運営PCで動かす）。疑似クライアント `styly-netsync-simulator` を含む | https://pypi.org/project/styly-netsync-server/ （github.com/styly-dev/STYLY-NetSync） | Apache-2.0（PyPI の表示） | 可 | 無料 | 再配布するならライセンス文の同梱（今は再配布しない） | 確認済み | 2026-10-18 |
| MCP for Blender（PyPI `blender-mcp` 2.0.0 → `mcp-for-blender` 2.1.9、`uvx` で取得）＋ Blender アドオン | AI から Blender を操作してモデリングする開発ツール（成果物に同梱しない） | https://pypi.org/project/mcp-for-blender/ （github.com/ahujasid/blender-mcp） | MIT（PyPI の表示） | 可 | 無料（アドオン導入・コア機能。**Premium の AI 3D 生成は有料なので使わない**） | 不要（開発ツール） | 確認済み | 2026-10-18 |
| ナレーション音声 | オープニング | ユーザーが用意 | ― | ― | ― | 生成サービスが求めるなら AI 音声である旨 | ユーザーが確認 | ― |

## プロジェクトに入っている物

| 名前 | 用途 | ライセンス | 商用 | 費用 | 表示 | 状態 |
| :--- | :--- | :--- | :---: | :--- | :--- | :--- |
| UniTask（`com.cysharp.unitask`） | 非同期処理 | MIT | 可 | 無料 | 要（ライセンス文の同梱） | 未確認 |
| R3（`com.cysharp.r3`）、ObservableCollections.R3 | リアクティブ | MIT | 可 | 無料 | 要（ライセンス文の同梱） | 未確認 |
| DOTween（`Assets/Plugins/Demigiant`） | アニメーション | DOTween の独自ライセンス（無料版） | 要確認 | 無料 | 要確認 | 未確認 |
| ParrelSync | エディタでの複数起動 | MIT | 可（エディタのみ） | 無料 | ― | 未確認 |
| uLoopMCP（`io.github.hatayama.uloopmcp`） | AI からのエディタ操作 | 要確認 | エディタのみ | 無料 | ― | 未確認 |
| STYLY NetSync | 同期 | 要確認 | 要確認 | 要確認 | 要確認 | 未確認 |
| PICO OpenXR SDK / PXR_Enterprise | PICO 対応 | PICO SDK の規約 | 要確認 | 無料 | 要確認 | 未確認 |
| Unity 公式パッケージ（URP・XRI・XR Hands・OpenXR 等）と付属サンプル（XRI の「XR Interaction Simulator」「Hands Interaction Demo」「Starter Assets」、XR Hands の「HandVisualizer」「Gestures」） | 基盤、エディタでの手のシミュレーション | Unity Companion License 等 | 可 | 無料 | ― | 未確認 |

## 使わないと決めた物

| 名前 | 理由 | 確認日 |
| :--- | :--- | :--- |
| ElevenLabs（無料プラン） | 無料プランは商用利用不可（公式ヘルプ） | 2026-10-18 |
| MCP for Blender Premium / `generate_3d`（Hyper3D Rodin・Hunyuan3D・Tripo） | 有料・クレジット消費の AI 3D 生成。生成物の商用利用条件も別途確認が必要 | 2026-10-18 |
| MCP for Blender の `search_assets` / `import_asset`（Sketchfab・Poly Pizza 経由） | 素材ごとにライセンスが違い（CC-BY・非商用など）、自動取り込みでは4点確認ができない。Poly Haven の素材を使う場合も人が確認して取り込む | 2026-10-18 |
