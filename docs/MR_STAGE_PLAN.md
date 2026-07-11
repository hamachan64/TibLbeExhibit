# MRステージ企画書: 「島の侵蝕」— グリードアイランド重畳ステージ

対象: `Environment.unity` の `TIB_20260429`（会場フォトグラメトリスキャン）に対する没入型MRステージ。
関連: @GI_POKER_PLAN.md（ゲーム企画）/ @DEVELOPMENT.md（技術規約）

## 1. 課題認識

現状はパススルー映像（現実の会場）の上にアイテムが「ポン置き」されており、
仮想オブジェクトが現実から浮いて見える。MRである意味＝「現実と仮想の融合」が成立していない。

## 2. ストーリー・世界観

> **今日、この会場は「グリードアイランドとの境界が薄くなる場所」になる。**
>
> ゲームマスターの仕掛けた魔法により、5分間だけ島と現実の重なりが開く。
> 床からは光の根（レイライン）が這い、島の草地と遺跡が現実の床・壁を侵蝕していく。
> プレイヤーは「島に招かれた新米プレイヤー」。島が差し出したアイテムをカード化して集め、
> 境界が閉じる前にバインダーを満たす。タイムアップで侵蝕は引いていき、
> 現実の会場に戻る——手元に残るのは集めたカード（スコア）だけ。

このストーリーの要点は **「現実の会場が主役のまま、仮想が現実に侵蝕してくる」** こと。
仮想空間へワープさせるのではなく、いま立っている床・見えている壁に島が「染み込む」ことで、
パススルーであること自体が演出装置になる。

## 3. ゲームフローと演出の対応

| SessionState | ステージフェーズ | 演出 |
| :--- | :--- | :--- |
| WaitingForStart | **Reality** | 会場は素のパススルー。床に魔法陣（境界円）の輪郭だけが淡く脈動し「何かが起きる」予感を作る |
| Tutorial | **Awakening** | 侵蝕開始。床からレイラインが広がり、練習アイテムの足元に小さな侵蝕パッチが生まれる（侵蝕度 0→0.4） |
| Playing | **Eroded** | 完全侵蝕（侵蝕度 1.0）。遺跡の石柱・浮遊岩・草地パッチ・木漏れ日パーティクル。アイテムは「島が差し出した場所」（パッチ・台座）に出現 |
| Playing 残り60秒 | **Unstable** | 境界が不安定に。魔法陣が明滅し侵蝕の縁が金色に色づく。ラストスパートの焦燥感 |
| Finished | **Receding** | 侵蝕が逆再生で引いていく（1.0→0）。現実に戻った会場でリザルト表示 |

## 4. デザイン原則 —「浮かせない」ためのルール

1. **接地**: 常設の仮想物は必ず現実サーフェスとの接触面を持つ（侵蝕パッチ・根本の光輪から「生える」）。空中に単独で置かない。
2. **オクルージョン**: `TIB_20260429` メッシュを深度オクルーダー化し、現実の柱・壁の裏に回った仮想物は正しく隠れる。
3. **影**: Shadow Catcher により仮想物の影が現実の床に落ちる。
4. **ライティング整合**: パススルー映像の輝度に馴染む控えめなエミッシブ。真っ白な発光や彩度の暴力は禁止。
5. **出現・消滅は必ず演出を通す**: ポップイン/アウト禁止。侵蝕から生える／砂のように崩れる。
6. **性能**: PICO実機で72fps維持。透明パス・パーティクルのオーバードローを常に意識する。

## 5. ステージ構成要素

| ID | 要素 | 説明 | 主担当Issue |
| :--- | :--- | :--- | :--- |
| A | **ErosionPatch**（侵蝕パッチ） | 床に円形に広がる草地＋発光ルーン。radial dissolve シェーダーで侵蝕度に応じてじわっと拡大 | #1(シェーダー) / #2(配置) |
| B | **LeyLine**（光の根） | パッチ間・スポーン地点を繋ぐ床上の光の筋。UVスクロール発光。視線誘導を兼ねる | #1 |
| C | **RuinPillar / FloatingRock**（遺跡台座） | アイテムの出現台座。石柱の上・浮遊岩の上にアイテムが乗る | #2 |
| D | **BorderCircle**（境界魔法陣） | プレイエリア境界の大魔法陣。既存 `TiB_BorderLine.mat` の発展形。Unstableで明滅 | #2 |
| E | **Occluder / ShadowCatcher** | TIBメッシュ用 depth-only マテリアル＋影受け | #1 |
| F | **AmbientMotes**（浮遊光粒） | 木漏れ日・胞子のような微細パーティクル。空気感の演出 | #2 |
| G | **StageSpawnAnchor**（出現アンカー） | パッチ・台座上のアイテム出現点。ItemSpawnManager と接続 | #3 |

## 6. インターフェース契約（Issue間の結合点 — この通りに実装すること）

### 6.1 StageDirector（Issue #1 が実装、#2 が購読）

```csharp
namespace STYLY.TIB_Template.GIPoker
{
    public enum StagePhase { Reality, Awakening, Eroded, Unstable, Receding }

    /// <summary>Environment シーン常駐。侵蝕演出の進行を一元管理する。</summary>
    public class StageDirector : MonoBehaviour
    {
        public static StageDirector Instance { get; private set; }

        /// <summary>侵蝕度 0=現実 → 1=完全侵蝕。DOTween で滑らかに遷移する。</summary>
        public ReadOnlyReactiveProperty<float> ErosionProgress { get; }

        /// <summary>現在のステージフェーズ。</summary>
        public ReadOnlyReactiveProperty<StagePhase> Phase { get; }
    }
}
```

- `GameSession.Instance.State` と `RemainingTime` を購読して Phase を導出する
  （`GameSession` は UserClient シーン側 → `ItemSpawnManager` と同様に `UniTask.WaitUntil` で待つ）。
- シェーダーへの侵蝕度の配信は `Shader.SetGlobalFloat("_GIP_Erosion", value)` を基本とする
  （各要素が個別に購読するより安価で一貫する）。

### 6.2 StageSpawnAnchor（Issue #3 が実装、#2 が配置）

```csharp
namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>アイテム出現候補点。ErosionPatch / RuinPillar 上に配置する。</summary>
    public class StageSpawnAnchor : MonoBehaviour
    {
        [SerializeField] private bool isElevated; // 台座上（true）か床パッチ上（false）か
        public bool IsElevated => isElevated;
    }
}
```

- `ItemSpawnManager` はシーン内の全アンカーを**階層順（Transform ルート順→siblingIndex 深さ優先）で列挙**し、
  シードによる選択のみで決定論的に割り当てる（列挙順がクライアント間で一致することが同期の前提）。
- アンカーが枯渇した場合は従来の箱型ランダム配置にフォールバックする。

## 7. 技術方針

- **シェーダー**: URP / モバイルXR（シングルパスインスタンシング）対応。Unlit ベース＋エミッシブ。
  GrabPass・リアルタイム反射・多重透明レイヤー禁止。
- **アセット生成**: `.unity` / `.prefab` / `.mat` の YAML 直編集は禁止（`.claude/rules/unity_asset_rules.md`）。
  各PRは **Editor セットアップスクリプト**（`Assets/_Content/Scripts/GIPoker/Editor/` にメニューコマンド）を同梱し、
  マテリアル・プレハブ・シーン配置はメインツリーでの統合時にエディタAPIで生成する。
- **非同期/イベント**: UniTask + R3。演出トゥイーンは DOTween（`.claude/rules/async_r3_rules.md`）。
- **既存資産の尊重**: `ItemSpawnManager` の決定論スポーン設計（シード事前計算）を壊さない。
  `TiB_BorderLine.mat` / 既存 Shadow Catcher があれば発展させる。

## 8. Issue 分割と担当

| Issue | 内容 | 担当モデル / エージェント | ブランチ |
| :--- | :--- | :--- | :--- |
| #1 MRステージ基盤 | 侵蝕・レイライン・オクルーダーシェーダー、StageDirector、Editorセットアップ | **Opus** / shader-expert | `feature/mr-stage-foundation` |
| #2 ステージ装飾 | 台座・境界魔法陣・パーティクル、フェーズ演出 | **Sonnet** / vfx-artist | `feature/mr-stage-decoration` |
| #3 アイテム設置 | StageSpawnAnchor、ItemSpawnManager アンカー対応＋テスト | **Sonnet** / general | `feature/mr-stage-item-anchors` |

- 各PRは Opus がレビューし、品質不足の場合は上位モデル（Sonnet→Opus→Fable）に切り替えて再実行する。
- マージ順: #1 → #2 → #3（IF契約は本書 §6 が正）。シーンへの実配置・検証は統合フェーズでオーケストレーターが実施。
