# 外部の素材・サービス・パッケージの記録

## 追加記録: Treasure Haul お宝10種 (2026-10-18)

以下の7モデルと共通柄テクスチャは、このリポジトリのローカル Blender / Blender MCP `execute_blender_code` だけで新規制作した。外部素材・AI生成サービスは利用していない。4点確認: 商用展示可（自作物）／第三者権利・既存ブランド・人物・R.E.P.O素材を含まない／無料（ローカル制作、従量課金なし）／公式配布物のダウンロードやAPIキー利用なし。

| 名前 | 入手元・根拠 | ライセンス・権利 | 商用 | 費用 | セキュリティ | 確認日 |
| :--- | :--- | :--- | :---: | :--- | :--- | :--- |
| Treasure Haul S Scarab Amulet / Ceremonial Lamp | `BlenderAssets/TreasureHaul/` 内で自作 | オリジナル形状・自作柄、第三者素材なし | 可 | 無料 | ローカル制作のみ | 2026-10-18 |
| Treasure Haul M Amphora / Inscribed Stele | `BlenderAssets/TreasureHaul/` 内で自作 | オリジナル形状・自作柄、第三者素材なし | 可 | 無料 | ローカル制作のみ | 2026-10-18 |
| Treasure Haul L Guardian Statue / Relic Chest | `BlenderAssets/TreasureHaul/` 内で自作 | オリジナル形状・自作柄、第三者素材なし | 可 | 無料 | ローカル制作のみ | 2026-10-18 |
| Treasure Haul Secret Sun Mask | `BlenderAssets/TreasureHaul/` 内で自作 | オリジナル形状・自作柄、第三者素材なし | 可 | 無料 | ローカル制作のみ | 2026-10-18 |
| Treasure Haul Bronze Patina / Cinnabar Gilt / Jade Inlay patterns | `BlenderAssets/TreasureHaul/patterns/` 内で自作。Bronze Patina は同梱済み自作S用テクスチャを再利用 | オリジナルの自作画像、第三者素材なし | 可 | 無料 | ローカル生成のみ | 2026-10-18 |

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

### Issue #46: お宝 S（2026-10-18）

外部のモデル・画像・テクスチャは使用せず、既設 Blender と Blender MCP のローカル操作で制作する。以下は制作ツールと自作成果物の記録であり、自作物を第三者の CC0 素材として扱うものではない。

| 名前 | 入手元・根拠 | ライセンス・権利 | 商用 | 費用 | 表示 | セキュリティ | 状態・確認日 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Blender 5.1.2（既設） | [公式ライセンス](https://www.blender.org/about/license/)、[公式マニュアル](https://docs.blender.org/UATEST/manual/en/dev/getting_started/about/license.html) | ソフトウェアは GPL。制作した作品・データファイルに GPL を適用する義務はない | 可 | 無料、ローカル制作 | ツールを同梱しないため不要 | 既設の公式 Blender。追加インストール・外部APIアクセスなし | 確認済み・2026-10-18 |
| MCP for Blender の自作モデリング機能 | [公式MIT原文](https://github.com/ahujasid/mcp-for-blender/blob/main/LICENSE)、[公式配布](https://pypi.org/project/mcp-for-blender/) | MIT、ツール本体は成果物に同梱しない | 可 | 無料のコア機能のみ、Premium・生成クレジットなし | ツールの再配布なし。成果物へのクレジット義務なし | 既設アドオンの execute_blender_code と確認系のみ。全外部ライブラリ・生成機能OFFを確認。秘密情報の使用なし | 確認済み・2026-10-18 |
| Treasure S 仮面 | `BlenderAssets/TreasureHaul/S/`（本リポジトリ内で自作） | オリジナル形状・材質。実在人物・ブランド・既存作品の複製なし | 可 | ローカル制作、追加課金なし | 第三者素材による義務なし | 自作スクリプトのみ、外部モデル・テクスチャなし | 確認済み・2026-10-18 |
| Treasure S 杯 | `BlenderAssets/TreasureHaul/S/`（本リポジトリ内で自作） | オリジナル形状・材質。実在人物・ブランド・既存作品の複製なし | 可 | ローカル制作、追加課金なし | 第三者素材による義務なし | 自作スクリプトのみ、外部モデル・テクスチャなし | 確認済み・2026-10-18 |
| Treasure S 小像 | `BlenderAssets/TreasureHaul/S/`（本リポジトリ内で自作） | オリジナル形状・材質。実在人物・ブランド・既存作品の複製なし | 可 | ローカル制作、追加課金なし | 第三者素材による義務なし | 自作スクリプトのみ、外部モデル・テクスチャなし | 確認済み・2026-10-18 |

#### CC0 素材の調査（不採用、ダウンロードなし）

- Smithsonian Open Access: [公式FAQ](https://www.si.edu/openaccess/faq)は CC0 指定作品の利用・改変・共有を許可。検索では杯・仮面が見つかったが、個別モデルの CC0 表示と取得ファイルを確認できず使用しない。サイト全体を確認済み扱いにはしない。
- Poly Haven: [公式ライセンス](https://polyhaven.com/license)で CC0・商用・改変・再配布可、クレジット不要を確認。[Bronze Shark Statue](https://polyhaven.com/a/bronze_shark_statue)（7K tri）と [Gothic Statue](https://polyhaven.com/a/gothic_statue)（28K tri）は無料・CC0 の候補だが、持ち手込み5,000 triへ軽量化が必要。今回は小型遺物3点の造形と質感をそろえるため採用しない。素材・スクリプトの取得なし。

### 既存パッケージ

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
