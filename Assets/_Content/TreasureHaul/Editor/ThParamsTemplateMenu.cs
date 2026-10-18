using System.IO;
using UnityEditor;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Editor
{
    /// <summary>組み込みのパラメータを JSON の雛形として書き出すメニュー（設計書 7章）。</summary>
    public static class ThParamsTemplateMenu
    {
        const string AssetPath = "Assets/_Content/TreasureHaul/Settings/ThParamsDefault.asset";

        [MenuItem("Tools/Treasure Haul/パラメータの雛形 JSON を書き出す")]
        public static void ExportTemplate()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ThParamsAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError($"[ThParamsTemplateMenu] {AssetPath} がありません");
                return;
            }
            var path = EditorUtility.SaveFilePanel("パラメータの雛形 JSON", Application.persistentDataPath,
                ThSettingsFiles.ParamsFileName, "json");
            if (string.IsNullOrEmpty(path)) return;
            Export(asset, path);
        }

        /// <summary>JSON の書き出し本体（メニューから呼ぶ）。</summary>
        public static void Export(ThParamsAsset asset, string path)
        {
            File.WriteAllText(path, JsonUtility.ToJson(asset.values, true));
            Debug.Log($"[ThParamsTemplateMenu] 書き出しました: {path}");
        }
    }
}
