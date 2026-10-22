using UnityEditor;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Editor
{
    /// <summary>カタログの各件の「大きさ」を、プレハブのモデル（MeshRenderer）の外形から入れ直すメニュー（設計書 4.1）</summary>
    public static class ThTreasureCatalogMenu
    {
        public const string CatalogPath = "Assets/_Content/TreasureHaul/Settings/ThTreasureCatalog.asset";

        [MenuItem("Tools/Treasure Haul/お宝カタログの大きさを入れ直す")]
        public static void RefreshSizes()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ThTreasureCatalog>(CatalogPath);
            if (catalog == null) { Debug.LogError("[ThTreasureCatalogMenu] カタログがありません: " + CatalogPath); return; }
            RefreshSizes(catalog);
        }

        /// <summary>入れ直して保存する。変えた件数を返す</summary>
        public static int RefreshSizes(ThTreasureCatalog catalog)
        {
            var so = new SerializedObject(catalog);
            var list = so.FindProperty("entries");
            int changed = 0;
            for (int i = 0; i < list.arraySize; i++)
            {
                var entry = list.GetArrayElementAtIndex(i);
                var prefab = entry.FindPropertyRelative("prefab").objectReferenceValue as GameObject;
                if (prefab == null) continue;
                var size = MeasureSize(prefab);
                if (size.x <= 0f) continue;
                var w = entry.FindPropertyRelative("width");
                var d = entry.FindPropertyRelative("depth");
                var h = entry.FindPropertyRelative("height");
                if (!Mathf.Approximately(w.floatValue, size.x) || !Mathf.Approximately(d.floatValue, size.y) || !Mathf.Approximately(h.floatValue, size.z)) changed++;
                w.floatValue = size.x; d.floatValue = size.y; h.floatValue = size.z;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ThTreasureCatalogMenu] 大きさを入れ直しました（{changed} 件が変わりました）");
            return changed;
        }

        /// <summary>プレハブのルートのローカルで見た、MeshRenderer の外形の大きさ（x=幅 / y=奥行 / z=高さ）。無ければ 0</summary>
        public static Vector3 MeasureSize(GameObject prefab)
        {
            var root = prefab.transform;
            bool has = false;
            var b = new Bounds();
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var lb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                    else b.Encapsulate(p);
                }
            }
            return has ? new Vector3(b.size.x, b.size.z, b.size.y) : Vector3.zero;
        }
    }
}
