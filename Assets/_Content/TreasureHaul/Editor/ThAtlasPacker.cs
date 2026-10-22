using System.IO;
using UnityEditor;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Editor
{
    /// <summary>
    /// アトラスの Metallic と Roughness の 2 枚から、URP Lit 用の 1 枚（R=金属度、A=滑らかさ=1-粗さ、G・B=0）を作るメニュー。
    /// 同じ入力からは同じ画像ができ、もう一度実行すると同じファイルを上書きする。出力は sRGB オフで取り込む。
    /// お宝のアトラスは <see cref="PackTreasures"/>。ステージ用などは <see cref="Pack"/> にパスを渡す。
    /// </summary>
    public static class ThAtlasPacker
    {
        public const string TreasureDir = "Assets/_Content/TreasureHaul/Models/Treasures/";
        public const string TreasureMetallic = TreasureDir + "Treasure_Atlas_Metallic.png";
        public const string TreasureRoughness = TreasureDir + "Treasure_Atlas_Roughness.png";
        public const string TreasureOutput = TreasureDir + "Treasure_Atlas_MetallicSmoothness.png";

        [MenuItem("Tools/Treasure Haul/お宝のアトラスを変換する（MetallicSmoothness）")]
        public static void PackTreasures() => Pack(TreasureMetallic, TreasureRoughness, TreasureOutput);

        /// <summary>1 枚に詰める。metallic・roughness は同じ大きさ（グレースケールの R を使う）。出力のパスを返す</summary>
        public static string Pack(string metallicAssetPath, string roughnessAssetPath, string outputAssetPath)
        {
            var metal = LoadPixels(metallicAssetPath, out int w, out int h);
            var rough = LoadPixels(roughnessAssetPath, out int w2, out int h2);
            if (w != w2 || h != h2) throw new IOException($"Metallic と Roughness の大きさが違います: {w}x{h} / {w2}x{h2}");
            var packed = new Color32[metal.Length];
            for (int i = 0; i < packed.Length; i++)
                packed[i] = new Color32(metal[i].r, 0, 0, (byte)(255 - rough[i].r));

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(packed);
            tex.Apply(false);
            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            File.WriteAllBytes(ToFullPath(outputAssetPath), png);
            AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(outputAssetPath);
            if (importer.sRGBTexture || importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.alphaIsTransparency)
            {
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
            }
            Debug.Log($"[ThAtlasPacker] {outputAssetPath} を作りました（{w}x{h}）");
            return outputAssetPath;
        }

        static Color32[] LoadPixels(string assetPath, out int w, out int h)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(ToFullPath(assetPath)), false)) throw new IOException("画像を読めません: " + assetPath);
                w = tex.width; h = tex.height;
                return tex.GetPixels32();
            }
            finally { Object.DestroyImmediate(tex); }
        }

        static string ToFullPath(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }
}
