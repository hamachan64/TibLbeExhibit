using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace STYLY.TIB_Template.TreasureHaul.Editor
{
    /// <summary>
    /// 文言（ThTextTable）の文字のうち、本体フォントに無い物だけを集めた小さな Static の予備フォントを、元の .ttf から作り直し、
    /// 本体フォントのフォールバックに 1 つだけ登録する。本体フォントのアトラス・文字表は書き換えない。
    /// </summary>
    public static class ThFontExtraMenu
    {
        public const string MainFontPath = "Assets/_Content/Fonts/Noto_Sans_JP/NotoSansJP-VariableFont_wght SDF.asset";
        public const string SourceFontPath = "Assets/_Content/Fonts/Noto_Sans_JP/NotoSansJP-VariableFont_wght.ttf";
        public const string ExtraFontPath = "Assets/_Content/Fonts/Noto_Sans_JP/NotoSansJP SDF - TreasureHaul Extra.asset";
        public const string TextTablePath = "Assets/_Content/TreasureHaul/Settings/ThTextTable.asset";
        public const string PalettePath = "Assets/_Content/TreasureHaul/Settings/ThPlayerPalette.asset";

        /// <summary>書式の {0:N0} などを除いた文言の文字に、数字と「,」「G」「+」「-」を加えた集合（空白・改行は除く）。</summary>
        public static string CollectCharacters(ThTextTable table, ThPlayerPalette palette = null)
        {
            var set = new SortedSet<char>();
            // プレーヤーの色の記号（●▲■★）と名前も、練習用お宝の上などに出す
            if (palette != null && palette.slots != null)
                foreach (var slot in palette.slots)
                {
                    if (slot == null) continue;
                    foreach (var ch in slot.symbol ?? "") set.Add(ch);
                    foreach (var ch in slot.colorName ?? "") set.Add(ch);
                }
            foreach (var e in table.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.text)) continue;
                foreach (var ch in Regex.Replace(e.text, @"\{[^}]*\}", "")) set.Add(ch);
            }
            // アセットに無い間に使う組み込みの既定の文言（#44 の運営UI・CSV）も集める
            foreach (var text in ThTextTable.FallbackTexts)
                foreach (var ch in Regex.Replace(text ?? "", @"\{[^}]*\}", "")) set.Add(ch);
            foreach (var ch in "0123456789,G+-") set.Add(ch);
            var sb = new StringBuilder();
            foreach (var ch in set) if (!char.IsWhiteSpace(ch) && !char.IsControl(ch)) sb.Append(ch);
            return sb.ToString();
        }

        [MenuItem("Tools/Treasure Haul/文言の文字をフォントに足す")]
        public static void Run()
        {
            var table = AssetDatabase.LoadAssetAtPath(TextTablePath, typeof(ThTextTable)) as ThTextTable;
            var palette = AssetDatabase.LoadAssetAtPath(PalettePath, typeof(ThPlayerPalette)) as ThPlayerPalette;
            var main = AssetDatabase.LoadAssetAtPath(MainFontPath, typeof(TMP_FontAsset)) as TMP_FontAsset;
            var source = AssetDatabase.LoadAssetAtPath(SourceFontPath, typeof(Font)) as Font;
            if (table == null || main == null || source == null)
            {
                Debug.LogError("[ThFontExtraMenu] ThTextTable・本体フォント・元の .ttf のどれかが見つかりません");
                return;
            }

            // 本体フォントに無い文字だけ（フォールバックは見ない）
            var missing = new StringBuilder();
            foreach (var ch in CollectCharacters(table, palette)) if (!main.HasCharacter(ch, false)) missing.Append(ch);
            string chars = missing.ToString();
            Debug.Log($"[ThFontExtraMenu] 本体フォントに無い文字 {chars.Length} 個: {chars}");

            int size = AtlasSizeFor(chars.Length, (int)main.faceInfo.pointSize, main.atlasPadding);
            var extra = AssetDatabase.LoadAssetAtPath(ExtraFontPath, typeof(TMP_FontAsset)) as TMP_FontAsset;
            if (extra == null)
            {
                // 本体と同じ大きさ・余白・描き方。アトラスは入る最小の大きさ
                extra = TMP_FontAsset.CreateFontAsset(source, (int)main.faceInfo.pointSize, main.atlasPadding,
                    main.atlasRenderMode, size, size, AtlasPopulationMode.Dynamic, false);
                extra.name = "NotoSansJP SDF - TreasureHaul Extra";
                AssetDatabase.CreateAsset(extra, ExtraFontPath);
                AddSubAssets(extra);
            }
            else
            {
                extra.atlasPopulationMode = AtlasPopulationMode.Dynamic;   // 作り直すため一時的に Dynamic にする
                var so = new SerializedObject(extra);
                so.FindProperty("m_AtlasWidth").intValue = size;
                so.FindProperty("m_AtlasHeight").intValue = size;
                so.ApplyModifiedPropertiesWithoutUndo();
                extra.ClearFontAssetData(true);
            }

            if (chars.Length > 0)
            {
                extra.TryAddCharacters(chars, out string notAdded);
                if (!string.IsNullOrEmpty(notAdded)) Debug.LogError($"[ThFontExtraMenu] フォントに無い文字（元の .ttf にも無い）: {notAdded}");
            }
            extra.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(extra);
            if (extra.atlasTexture != null) EditorUtility.SetDirty(extra.atlasTexture);
            if (extra.material != null) EditorUtility.SetDirty(extra.material);
            AssetDatabase.SaveAssetIfDirty(extra);

            // 本体フォントのフォールバックに 1 つだけ登録（登録済みなら何もしない）
            if (main.fallbackFontAssetTable == null) main.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!main.fallbackFontAssetTable.Contains(extra))
            {
                main.fallbackFontAssetTable.Add(extra);
                EditorUtility.SetDirty(main);
                AssetDatabase.SaveAssetIfDirty(main);
            }
            Debug.Log($"[ThFontExtraMenu] 予備フォント: 文字 {extra.characterTable.Count} 個 / 大きさ {main.faceInfo.pointSize} / アトラス {extra.atlasWidth}x{extra.atlasHeight}");
        }

        /// <summary>文字数が余裕を持って入る最小のアトラスの一辺（256〜2048）。</summary>
        public static int AtlasSizeFor(int charCount, int pointSize, int padding)
        {
            int cell = pointSize + 2 * padding + 2;
            foreach (var size in new[] { 256, 512, 1024, 2048 })
            {
                int per = size / cell;
                if (per * per >= charCount * 1.5f) return size;
            }
            return 2048;
        }

        static void AddSubAssets(TMP_FontAsset fa)
        {
            // CreateFontAsset のテクスチャとマテリアルをアセットの中に入れる（TMP の標準手順）
            if (fa.material != null && !AssetDatabase.Contains(fa.material)) { fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
            if (fa.atlasTexture != null && !AssetDatabase.Contains(fa.atlasTexture)) { fa.atlasTexture.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa); }
        }
    }
}
