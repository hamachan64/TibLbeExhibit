using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// カード定義から G.I. カードの見た目（板＋ランク枠＋テキスト）を
    /// プロシージャルに生成するファクトリ。
    /// 専用のカードモデルを用意しなくても、全カード種で統一感のある
    /// 見た目が得られる。バインダーのミニカード表示にも使う。
    /// </summary>
    public static class CardVisualFactory
    {
        public const string GeneratedRootName = "CardVisual(Generated)";

        // G.I.カードの実寸イメージ（縦88mm×横63mm）
        private const float CardWidth = 0.063f;
        private const float CardHeight = 0.088f;
        private const float CardThickness = 0.003f;

        private static readonly Dictionary<CardRank, Material> frameMaterials = new();
        private static Material bodyMaterial;

        /// <summary>
        /// ランクごとのテーマカラー。
        /// GIPokerAssets.RankConfig（CardRankConfig.ThemeColor）が設定されていればそちらを優先し、
        /// 未設定（null）またはランクが見つからない場合は下記のフォールバック値を返す。
        /// </summary>
        public static Color GetRankColor(CardRank rank)
        {
            if (GIPokerAssets.RankConfig != null && GIPokerAssets.RankConfig.TryGetEntry(rank, out var entry))
            {
                return entry.themeColor;
            }
            return rank switch
            {
                CardRank.D => new Color(0.55f, 0.57f, 0.60f), // シルバーグレー
                CardRank.C => new Color(0.30f, 0.65f, 0.35f), // グリーン
                CardRank.B => new Color(0.25f, 0.45f, 0.85f), // ブルー
                CardRank.A => new Color(0.60f, 0.30f, 0.85f), // バイオレット
                CardRank.S => new Color(0.95f, 0.75f, 0.20f), // ゴールド
                CardRank.SS => new Color(0.95f, 0.20f, 0.35f), // クリムゾン
                _ => Color.white,
            };
        }

        /// <summary>この GameObject が本ファクトリ製かどうか</summary>
        public static bool IsGenerated(GameObject go)
        {
            return go != null && go.name == GeneratedRootName;
        }

        /// <summary>
        /// カードの見た目を生成して parent の子として返す。
        /// 生成直後は非アクティブ（表示制御は呼び出し側が行う）。
        /// </summary>
        public static GameObject Create(Transform parent, CardDefinition definition, float scale = 1f, bool withSparkle = true)
        {
            var root = new GameObject(GeneratedRootName);
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * scale;

            var rank = definition != null ? definition.Rank : CardRank.D;
            var rankColor = GetRankColor(rank);

            // カード本体（白い板）
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(CardWidth, CardHeight, CardThickness);
            body.GetComponent<Renderer>().sharedMaterial = GetBodyMaterial();

            // ランク色の枠（本体よりわずかに大きい板を背後に重ねる）
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            Object.Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(root.transform, false);
            frame.transform.localScale = new Vector3(CardWidth + 0.006f, CardHeight + 0.006f, CardThickness * 0.6f);
            frame.GetComponent<Renderer>().sharedMaterial = GetFrameMaterial(rank);

            if (definition != null)
            {
                // 表・裏の両面にテキストを配置する（浮遊回転しても読めるように）
                CreateFaceTexts(root.transform, definition, rankColor, front: true);
                CreateFaceTexts(root.transform, definition, rankColor, front: false);
            }

            if (withSparkle && GIPokerAssets.CardSparkleVfx != null)
            {
                var sparkle = Object.Instantiate(GIPokerAssets.CardSparkleVfx, root.transform);
                sparkle.name = "Sparkle";
                sparkle.transform.localPosition = Vector3.zero;
            }

            root.SetActive(false);
            return root;
        }

        private static void CreateFaceTexts(Transform root, CardDefinition definition, Color rankColor, bool front)
        {
            var zOffset = (CardThickness * 0.5f + 0.0008f) * (front ? -1f : 1f);
            var yaw = front ? 0f : 180f;

            // ランク文字（上部・大）
        	CreateText(root, $"Rank{(front ? "F" : "B")}", definition.Rank.ToString(),
                new Vector3(0f, CardHeight * 0.22f, zOffset), yaw, 0.9f, rankColor, bold: true);

            // カード名（下部・小）
            var title = string.IsNullOrEmpty(definition.DisplayName) ? definition.name : definition.DisplayName;
            CreateText(root, $"Name{(front ? "F" : "B")}", title,
                new Vector3(0f, -CardHeight * 0.30f, zOffset), yaw, 0.35f, new Color(0.15f, 0.13f, 0.10f), bold: false);
        }

        private static void CreateText(Transform root, string name, string text,
            Vector3 localPos, float yaw, float size, Color color, bool bold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f); // TMPは -Z 側から見て正しく読める

            var tmp = go.AddComponent<TextMeshPro>();
            if (GIPokerAssets.JapaneseFont != null)
            {
                tmp.font = GIPokerAssets.JapaneseFont;
            }
            tmp.text = text;
            tmp.fontSize = size * 10f;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.rectTransform.sizeDelta = new Vector2(CardWidth * 9f, CardHeight * 4f);
            go.transform.localScale = Vector3.one * 0.01f;
        }

        private static Material GetBodyMaterial()
        {
            if (bodyMaterial == null)
            {
                bodyMaterial = CreateLitMaterial(new Color(0.93f, 0.91f, 0.86f), emission: Color.black);
            }
            return bodyMaterial;
        }

        private static Material GetFrameMaterial(CardRank rank)
        {
            if (!frameMaterials.TryGetValue(rank, out var mat) || mat == null)
            {
                var color = GetRankColor(rank);
                mat = CreateLitMaterial(color, emission: color * 0.6f);
                frameMaterials[rank] = mat;
            }
            return mat;
        }

        /// <summary>URP Lit のシンプルなマテリアルを生成する（VFX・アイテム生成でも使用）</summary>
        public static Material CreateLitMaterial(Color color, Color emission)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { color = color };
            if (emission.maxColorComponent > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission);
            }
            return mat;
        }
    }
}
