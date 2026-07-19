using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// ステージプロップ（Blender 制作の FBX 20個）のマテリアル生成・マテリアルリマップ・
    /// プレハブ化・Environment シーンへの配置を一括で行う Editor セットアップスクリプト。
    ///
    /// メニュー「GIPoker/Stage/Setup Props」で以下を実行する:
    /// A. 共有マテリアル生成（Assets/_Content/Materials/Stage/Props/）
    /// B. 各FBXの ModelImporter マテリアルリマップ（FBX内マテリアル名→共有マテリアルアセット）
    /// C. プレハブ化（Assets/_Content/Prefabs/Stage/Props/）。FBX内の "SA__" 空ノードには
    ///    StageSpawnAnchor を自動付与する（命名規則は BuildSpawnAnchors のコメント参照）
    /// D. Environment.unity を開いて GIPokerProps ルート配下にレイアウト配置し、
    ///    StaticEditorFlags を設定。既存の床アンカー（SpawnAnchors/Anchor_05〜30、非elevatedのみ）
    ///    の高さを 0.45 に是正する
    ///
    /// 冪等: 再実行時、マテリアルは既存アセットをロードして上書き（GUID維持）、
    /// プレハブは SaveAsPrefabAsset で上書き、シーンの GIPokerProps は子を全削除して再生成する。
    ///
    /// シリアライズ済みアセットのYAML直接編集は禁止（.claude/rules/unity_asset_rules.md）のため、
    /// 生成はすべてエディタAPI（AssetDatabase / PrefabUtility / SerializedObject）で行う。
    /// </summary>
    public static class GIPokerPropSetup
    {
        private const string ModelFolder = "Assets/_Content/Models/Stage/Props";
        private const string PropTextureFolder = "Assets/_Content/Models/Stage/Props/Textures";
        private const string SharedTextureFolder = "Assets/_Content/Models/Stage/Textures";
        private const string MaterialFolder = "Assets/_Content/Materials/Stage/Props";
        private const string PrefabFolder = "Assets/_Content/Prefabs/Stage/Props";
        private const string ScenePath = "Assets/_Content/Scenes/Environment.unity";
        private const string PropsRootName = "GIPokerProps";
        private const string SpawnAnchorsRootName = "SpawnAnchors";
        private const float FloorAnchorY = 0.45f;
        private const int FloorAnchorMinIndex = 5;   // Anchor_05
        private const int FloorAnchorMaxIndex = 30;   // Anchor_30

        /// <summary>アンカー空ノード名の "SA" プレフィックス除去後、末尾の連番サフィックス（.001等）除去用</summary>
        private static readonly Regex TrailingIndexSuffix = new Regex(@"\.\d+$", RegexOptions.Compiled);

        // =============================================================
        // マテリアル定義（A）
        // =============================================================
        private class MaterialSpec
        {
            public string Name;
            public string BaseMapFile;   // null可。拡張子なし・フォルダ探索は自動
            public string BumpMapFile;   // null可
            public float Smoothness = 0.1f;
            public Color? BaseColor;
            public Color? EmissionColor;
        }

        private static readonly MaterialSpec[] MaterialSpecs =
        {
            new MaterialSpec { Name = "M_Prop_Bark", BaseMapFile = "PropTex_bark_brown_02_BaseMap", BumpMapFile = "PropTex_bark_brown_02_Normal" },
            new MaterialSpec { Name = "M_Prop_Wood", BaseMapFile = "PropTex_brown_planks_03_BaseMap", BumpMapFile = "PropTex_brown_planks_03_Normal" },
            new MaterialSpec { Name = "M_Prop_Plaster", BaseMapFile = "PropTex_beige_wall_001_BaseMap", BumpMapFile = "PropTex_beige_wall_001_Normal" },
            new MaterialSpec { Name = "M_Prop_Roof", BaseMapFile = "PropTex_clay_roof_tiles_02_BaseMap", BumpMapFile = "PropTex_clay_roof_tiles_02_Normal" },
            new MaterialSpec { Name = "M_Prop_Cobble", BaseMapFile = "PropTex_cobblestone_floor_04_BaseMap", BumpMapFile = "PropTex_cobblestone_floor_04_Normal" },
            new MaterialSpec { Name = "M_Prop_Foliage", BaseMapFile = "PropTex_foliage_BaseMap" },
            new MaterialSpec { Name = "M_Prop_RuinStone", BaseMapFile = "mossy_stone_wall_diffuse_2k", BumpMapFile = "mossy_stone_wall_nor_gl_1k" },
            new MaterialSpec { Name = "M_Prop_Rock", BaseMapFile = "mossy_rock_diffuse_2k", BumpMapFile = "mossy_rock_nor_gl_1k" },
            new MaterialSpec { Name = "M_Prop_Awning", BaseMapFile = "PropTex_awning_BaseMap" },
            new MaterialSpec { Name = "M_Prop_Lantern", BaseColor = new Color(1.0f, 0.85f, 0.45f, 1f), EmissionColor = new Color(2.0f, 1.5f, 0.7f) },
            new MaterialSpec { Name = "M_Prop_Flower", BaseColor = new Color(0.95f, 0.55f, 0.75f, 1f) },
            new MaterialSpec { Name = "M_Prop_Water", BaseColor = new Color(0.25f, 0.5f, 0.65f, 1f), Smoothness = 0.8f, EmissionColor = new Color(0.05f, 0.15f, 0.2f) },
        };

        // =============================================================
        // プレハブ→レイアウト定義（D）
        // =============================================================
        private class PlacementSpec
        {
            public string PrefabName;
            public string InstanceName;
            public Vector3 Position;
            public float RotationY;
        }

        private static readonly PlacementSpec[] Placements =
        {
            new PlacementSpec { PrefabName = "Prop_BigTree", InstanceName = "BigTree_A", Position = new Vector3(-6.05f, 0f, -1.2f), RotationY = 140f },
            new PlacementSpec { PrefabName = "Prop_BigTree", InstanceName = "BigTree_B", Position = new Vector3(-3.55f, 0f, 0.2f), RotationY = -60f },
            new PlacementSpec { PrefabName = "Prop_FallenLog", InstanceName = "FallenLog", Position = new Vector3(-4.1f, 0f, -0.7f), RotationY = 25f },
            new PlacementSpec { PrefabName = "Prop_FlowerPatch", InstanceName = "FlowerPatch", Position = new Vector3(-5.7f, 0f, -2.7f), RotationY = 0f },
            new PlacementSpec { PrefabName = "Prop_Bush_A", InstanceName = "Bush_A1", Position = new Vector3(-3.7f, 0f, -1.9f), RotationY = 70f },
            new PlacementSpec { PrefabName = "Prop_HollowRock", InstanceName = "HollowRock", Position = new Vector3(-3.65f, 0f, -3.6f), RotationY = 180f },
            new PlacementSpec { PrefabName = "Prop_Bush_B", InstanceName = "Bush_B1", Position = new Vector3(-6.2f, 0f, -3.9f), RotationY = 200f },
            new PlacementSpec { PrefabName = "Prop_CobblePath", InstanceName = "CobblePath_A", Position = new Vector3(-4.85f, 0f, -5.6f), RotationY = 0f },
            new PlacementSpec { PrefabName = "Prop_CobblePath", InstanceName = "CobblePath_B", Position = new Vector3(-4.95f, 0f, -8.4f), RotationY = 90f },
            new PlacementSpec { PrefabName = "Prop_AlchemistHut", InstanceName = "AlchemistHut", Position = new Vector3(-6.3f, 0f, -5.9f), RotationY = 90f },
            new PlacementSpec { PrefabName = "Prop_WeaponRack", InstanceName = "WeaponRack", Position = new Vector3(-5.9f, 0f, -4.7f), RotationY = 60f },
            new PlacementSpec { PrefabName = "Prop_TavernStall", InstanceName = "TavernStall", Position = new Vector3(-3.55f, 0f, -6.6f), RotationY = -90f },
            new PlacementSpec { PrefabName = "Prop_MerchantStall", InstanceName = "MerchantStall", Position = new Vector3(-6.25f, 0f, -8.3f), RotationY = 90f },
            new PlacementSpec { PrefabName = "Prop_Well", InstanceName = "Well", Position = new Vector3(-4.85f, 0f, -9.8f), RotationY = 15f },
            new PlacementSpec { PrefabName = "Prop_CrateSet", InstanceName = "CrateSet", Position = new Vector3(-3.5f, 0f, -8.7f), RotationY = 40f },
            new PlacementSpec { PrefabName = "Prop_LanternPost", InstanceName = "LanternPost_1", Position = new Vector3(-3.8f, 0f, -4.9f), RotationY = 180f },
            new PlacementSpec { PrefabName = "Prop_LanternPost", InstanceName = "LanternPost_2", Position = new Vector3(-6.0f, 0f, -7.3f), RotationY = 0f },
            new PlacementSpec { PrefabName = "Prop_LanternPost", InstanceName = "LanternPost_3", Position = new Vector3(-3.9f, 0f, -10.2f), RotationY = 180f },
            new PlacementSpec { PrefabName = "Prop_SignPost", InstanceName = "SignPost", Position = new Vector3(-4.3f, 0f, -4.35f), RotationY = -30f },
            new PlacementSpec { PrefabName = "Prop_Fence", InstanceName = "Fence_A", Position = new Vector3(-3.5f, 0f, 0.9f), RotationY = 90f },
            new PlacementSpec { PrefabName = "Prop_Fence", InstanceName = "Fence_B", Position = new Vector3(-6.15f, 0f, -10.5f), RotationY = 0f },
            new PlacementSpec { PrefabName = "Prop_Fence", InstanceName = "Fence_C", Position = new Vector3(-3.45f, 0f, -11.2f), RotationY = 0f },
            new PlacementSpec { PrefabName = "Prop_Altar", InstanceName = "Altar", Position = new Vector3(-4.9f, 0f, -13.5f), RotationY = 0f },
            new PlacementSpec { PrefabName = "Prop_RuinWall", InstanceName = "RuinWall", Position = new Vector3(-6.3f, 0f, -12.4f), RotationY = 90f },
            new PlacementSpec { PrefabName = "Prop_Pedestal", InstanceName = "Pedestal", Position = new Vector3(-3.6f, 0f, -12.7f), RotationY = 0f },
            // 祭壇(-13.5)と重ならないよう西へオフセットし、開口(ローカル-Z)を回廊中心へ向ける
            new PlacementSpec { PrefabName = "Prop_Shrine", InstanceName = "Shrine", Position = new Vector3(-6.0f, 0f, -14.35f), RotationY = 135f },
        };

        [MenuItem("GIPoker/Stage/Setup Props")]
        public static void SetupProps()
        {
            EnsureFolders();

            // --- A. 共有マテリアル ---
            var materials = BuildMaterials();

            // --- B. FBXマテリアルリマップ ---
            RemapAllFbxMaterials(materials);

            // --- C. プレハブ化 ---
            var prefabs = BuildAllPrefabs();

            // --- D. Environmentシーンへ配置 ---
            PlaceInScene(prefabs);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[GIPokerPropSetup] 完了: マテリアル({MaterialFolder}) / プレハブ({PrefabFolder}) / シーン配置({PropsRootName})");
        }

        // =============================================================
        // A. マテリアル生成
        // =============================================================
        private static Dictionary<string, Material> BuildMaterials()
        {
            var result = new Dictionary<string, Material>();
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                throw new InvalidOperationException("シェーダーが見つかりません: Universal Render Pipeline/Lit");
            }

            foreach (var spec in MaterialSpecs)
            {
                var path = $"{MaterialFolder}/{spec.Name}.mat";
                var mat = CreateOrUpdateMaterial(path, litShader, m =>
                {
                    m.SetFloat("_Smoothness", spec.Smoothness);

                    if (spec.BaseColor.HasValue)
                    {
                        m.SetColor("_BaseColor", spec.BaseColor.Value);
                    }

                    if (!string.IsNullOrEmpty(spec.BaseMapFile))
                    {
                        m.SetTexture("_BaseMap", LoadTexture(spec.BaseMapFile));
                    }

                    if (!string.IsNullOrEmpty(spec.BumpMapFile))
                    {
                        m.SetTexture("_BumpMap", LoadTexture(spec.BumpMapFile));
                        m.EnableKeyword("_NORMALMAP");
                    }

                    if (spec.EmissionColor.HasValue)
                    {
                        m.SetColor("_EmissionColor", spec.EmissionColor.Value);
                        m.EnableKeyword("_EMISSION");
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    }
                });
                result[spec.Name] = mat;
            }

            return result;
        }

        /// <summary>PropTextureFolder→見つからなければSharedTextureFolderの順でテクスチャを探す</summary>
        private static Texture2D LoadTexture(string baseName)
        {
            foreach (var ext in new[] { ".png", ".jpg" })
            {
                foreach (var folder in new[] { PropTextureFolder, SharedTextureFolder })
                {
                    var path = $"{folder}/{baseName}{ext}";
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null)
                    {
                        return tex;
                    }
                }
            }

            throw new InvalidOperationException($"テクスチャが見つかりません: {baseName}（{PropTextureFolder} / {SharedTextureFolder} を確認してください）");
        }

        private static Material CreateOrUpdateMaterial(string path, Shader shader, Action<Material> configure)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        // =============================================================
        // B. FBXマテリアルリマップ
        // =============================================================
        private static void RemapAllFbxMaterials(Dictionary<string, Material> materials)
        {
            foreach (var fbxPath in GetAllPropFbxPaths())
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
                if (importer == null)
                {
                    Debug.LogWarning($"[GIPokerPropSetup] ModelImporterが取得できません: {fbxPath}");
                    continue;
                }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                var fbxMaterialNames = model.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null)
                    .Select(m => m.name)
                    .Distinct();

                var remapped = false;
                foreach (var matName in fbxMaterialNames)
                {
                    if (materials.TryGetValue(matName, out var sharedMat))
                    {
                        var sourceId = new AssetImporter.SourceAssetIdentifier(typeof(Material), matName);
                        importer.AddRemap(sourceId, sharedMat);
                        remapped = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[GIPokerPropSetup] {Path.GetFileName(fbxPath)}: 共有マテリアルに一致しない名前 '{matName}' はリマップされません");
                    }
                }

                if (remapped)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        private static List<string> GetAllPropFbxPaths()
        {
            return Directory.GetFiles(ModelFolder, "*.fbx")
                .Select(p => p.Replace("\\", "/"))
                .OrderBy(p => p)
                .ToList();
        }

        // =============================================================
        // C. プレハブ化
        // =============================================================

        /// <summary>SA__ ノード名パースの結果</summary>
        private struct SpawnAnchorSpec
        {
            public string CardId;      // "any" なら汎用アンカー
            public bool AlignRotation;
        }

        private static Dictionary<string, GameObject> BuildAllPrefabs()
        {
            var result = new Dictionary<string, GameObject>();
            foreach (var fbxPath in GetAllPropFbxPaths())
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                var propName = Path.GetFileNameWithoutExtension(fbxPath); // 例: Prop_BigTree
                var prefab = BuildPrefab(model, propName);
                result[propName] = prefab;
            }

            return result;
        }

        private static GameObject BuildPrefab(GameObject model, string propName)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                // FBX内の "SA__" 空ノードすべてに StageSpawnAnchor を付与する
                var saNodes = instance.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith("SA__", StringComparison.Ordinal))
                    .ToList();

                foreach (var node in saNodes)
                {
                    var spec = ParseSpawnAnchorName(node.name);
                    var anchor = node.gameObject.AddComponent<StageSpawnAnchor>();
                    var so = new SerializedObject(anchor);

                    var isElevated = node.localPosition.y >= 0.5f;
                    so.FindProperty("isElevated").boolValue = isElevated;

                    var allowedCardIdsProp = so.FindProperty("allowedCardIds");
                    if (spec.CardId == "any")
                    {
                        allowedCardIdsProp.arraySize = 0;
                    }
                    else
                    {
                        allowedCardIdsProp.arraySize = 1;
                        allowedCardIdsProp.GetArrayElementAtIndex(0).stringValue = spec.CardId;
                    }

                    so.FindProperty("alignToAnchorRotation").boolValue = spec.AlignRotation;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                var prefabPath = $"{PrefabFolder}/{propName}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"プレハブの保存に失敗: {prefabPath}");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// "SA__&lt;cardId|any&gt;[__align][.NNN]" 形式のノード名をパースする。
        /// 例: "SA__c_king_white_stag_beetle__align.001" → cardId=c_king_white_stag_beetle, align=true
        ///     "SA__any.003" → cardId=any, align=false
        /// </summary>
        private static SpawnAnchorSpec ParseSpawnAnchorName(string nodeName)
        {
            var withoutIndexSuffix = TrailingIndexSuffix.Replace(nodeName, string.Empty);
            var parts = withoutIndexSuffix.Split(new[] { "__" }, StringSplitOptions.None);
            // parts[0] == "SA", parts[1] == cardId or "any", parts[2] (optional) == "align"
            var cardId = parts.Length > 1 ? parts[1] : "any";
            var align = parts.Length > 2 && parts[2] == "align";
            return new SpawnAnchorSpec { CardId = cardId, AlignRotation = align };
        }

        // =============================================================
        // D. Environmentシーンへ配置
        // =============================================================
        private static void PlaceInScene(Dictionary<string, GameObject> prefabs)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var scene = SceneManager.GetActiveScene();

            var root = GameObject.Find(PropsRootName);
            if (root == null)
            {
                root = new GameObject(PropsRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            else
            {
                // 再実行時: 子を全削除して作り直す
                for (var i = root.transform.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                }
            }
            root.transform.SetAsLastSibling();

            foreach (var placement in Placements)
            {
                if (!prefabs.TryGetValue(placement.PrefabName, out var prefab))
                {
                    Debug.LogWarning($"[GIPokerPropSetup] プレハブが見つかりません: {placement.PrefabName}（{placement.InstanceName} の配置をスキップ）");
                    continue;
                }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                go.name = placement.InstanceName;
                go.transform.position = placement.Position;
                go.transform.rotation = Quaternion.Euler(0f, placement.RotationY, 0f);

                foreach (var descendant in go.GetComponentsInChildren<Transform>(true))
                {
                    GameObjectUtility.SetStaticEditorFlags(descendant.gameObject, StaticEditorFlags.BatchingStatic);
                }
            }

            FixFloorAnchorHeights();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>SpawnAnchors/Anchor_05〜30 のうち isElevated=false のものの Y を 0.45 に是正する</summary>
        private static void FixFloorAnchorHeights()
        {
            var anchorsRoot = GameObject.Find(SpawnAnchorsRootName);
            if (anchorsRoot == null)
            {
                Debug.LogWarning($"[GIPokerPropSetup] {SpawnAnchorsRootName} が見つかりません。床アンカーの高さ是正をスキップします");
                return;
            }

            var indexPattern = new Regex(@"^Anchor_(\d+)$", RegexOptions.Compiled);
            var fixedCount = 0;
            foreach (Transform child in anchorsRoot.transform)
            {
                var match = indexPattern.Match(child.name);
                if (!match.Success)
                {
                    continue;
                }

                var index = int.Parse(match.Groups[1].Value);
                if (index < FloorAnchorMinIndex || index > FloorAnchorMaxIndex)
                {
                    continue;
                }

                var anchor = child.GetComponent<StageSpawnAnchor>();
                if (anchor != null && anchor.IsElevated)
                {
                    continue; // elevated アンカーは触らない
                }

                var pos = child.position;
                pos.y = FloorAnchorY;
                child.position = pos;
                fixedCount++;
            }

            Debug.Log($"[GIPokerPropSetup] 床アンカー高さ是正: {fixedCount}件を y={FloorAnchorY} に設定");
        }

        // =============================================================
        // ユーティリティ
        // =============================================================
        private static void EnsureFolders()
        {
            EnsureFolder("Assets/_Content/Materials", "Stage");
            EnsureFolder("Assets/_Content/Materials/Stage", "Props");
            EnsureFolder("Assets/_Content/Prefabs", "Stage");
            EnsureFolder("Assets/_Content/Prefabs/Stage", "Props");
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
