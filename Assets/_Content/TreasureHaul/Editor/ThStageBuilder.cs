using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace STYLY.TIB_Template.TreasureHaul.Editor
{
    /// <summary>
    /// 高い壁で区切った遺跡内部のステージ（回廊）を、Environment シーンの VenueRoot の下に作り直すメニュー（企画書 7.1、設計書 1章）。
    /// <c>stage_layout.json</c>（Blender の出力）を読み、14 モジュールの配置、壁・縁・家具のコライダー、置き場所 34 か所、練習の枠、回収範囲、灯り（焼き込み）、ライトプローブを作る。
    /// 何度実行しても同じ結果になる（前の Stage・古い仮の会場を消してから作る）。
    /// <para>
    /// <b>X 軸の向き</b>: FBX を Unity に取り込むと、Blender の X が反転して（Unity の X = -Blender の X）読み込まれる。
    /// JSON（コライダー・置き場所）は Blender の X のまま Unity の座標に直してあるので、モジュールをまとめた <c>Modules</c> の localScale を (-1, 1, 1) にして合わせる（<see cref="ModuleScale"/>）。
    /// </para>
    /// </summary>
    public static class ThStageBuilder
    {
        public const string StageDir = "Assets/_Content/TreasureHaul/Models/Stage/";
        public const string LayoutPath = StageDir + "stage_layout.json";
        public const string MaterialPath = StageDir + "TH_StageAtlas.mat";
        public const string BaseColorPath = StageDir + "Stage_Atlas_BaseColor.png";
        public const string MetallicPath = StageDir + "Stage_Atlas_Metallic.png";
        public const string RoughnessPath = StageDir + "Stage_Atlas_Roughness.png";
        public const string EmissionPath = StageDir + "Stage_Atlas_Emission.png";
        public const string MetallicSmoothnessPath = StageDir + "Stage_Atlas_MetallicSmoothness.png";
        public const string EnvironmentScenePath = "Assets/_Content/TreasureHaul/Scenes/TreasureHaulEnvironment.unity";
        public const string LightingSettingsPath = "Assets/_Content/TreasureHaul/Settings/ThEnvironmentLighting.lighting";
        public const string WallLayerName = "ThWall";

        /// <summary>FBX の取り込みで反転する X を戻す（Modules の localScale）</summary>
        public static readonly Vector3 ModuleScale = new Vector3(-1f, 1f, 1f);

        public static readonly string[] ModuleNames =
        {
            "THStage_OuterWall_W", "THStage_OuterWall_E", "THStage_BackWall", "THStage_Block_Shell", "THStage_Block_Partition", "THStage_HallPartitions",
            "THStage_ExtractionBay", "THStage_EntryCamp", "THStage_BlockFront_Altar", "THStage_FrontRoom_Furniture", "THStage_BackRoom_Furniture",
            "THStage_Corridor_Props", "THStage_Hall_Furniture", "THStage_GroundFragments",
        };

        /// <summary>メッシュのコライダー（非凸）にする家具。置き場所が家具の外形の箱の中にあるので、外形の箱は使わない</summary>
        public static readonly string[] MeshColliderModules =
        {
            "THStage_BlockFront_Altar", "THStage_FrontRoom_Furniture", "THStage_BackRoom_Furniture", "THStage_Corridor_Props", "THStage_Hall_Furniture",
        };

        // 灯り（Unity の URP の強さは単位が無いので、見た目で決めた値。Blender の W 数に比例させる）
        const float SpotIntensityPerWatt = 1f / 40f;
        const float BulbIntensity = 1.0f;
        const float SpotRange = 3f;
        const float SpotAngle = 100f;
        const float BulbRange = 1.5f;
        const float LampKelvin = 3000f;
        const float BulbKelvin = 2700f;

        // ライトプローブの格子
        static readonly float[] ProbeHeights = { 0.3f, 1.0f, 1.6f };
        const float ProbeSpacing = 0.8f;
        const float ProbeWallMargin = 0.1f;

        // ---- JSON ----
        [Serializable]
        public sealed class StageCollider
        {
            public string name;
            public string module;
            public string kind;
            public bool beam_blocking;
            public float[] center_unity_xyz;
            public float[] size_unity_xyz;
            public float[] rotation_unity_xyz;
        }

        [Serializable]
        public sealed class StageMarker
        {
            public string name;
            public string role;
            public float[] position_unity_xyz;
            public float[] size;
            public float[] aim_unity_xyz;
            public float[] facing_unity_xz;
            public float[] max_item_size_m;
            public string[] allowed_classes;
            public string zone;
            public bool hidden_intent;
            public float blender_watts;
        }

        [Serializable]
        public sealed class StageLayout
        {
            public StageCollider[] colliders;
            public StageMarker[] markers;
        }

        public static StageLayout LoadLayout()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
            if (asset == null) throw new FileNotFoundException("stage_layout.json がありません: " + LayoutPath);
            return JsonUtility.FromJson<StageLayout>(asset.text);
        }

        static Vector3 V3(float[] a) => new Vector3(a[0], a[1], a[2]);

        // ---- メニュー ----
        [MenuItem("Tools/Treasure Haul/ステージの素材を取り込む")]
        public static void ImportAssets()
        {
            foreach (var n in ModuleNames) ImportModel(StageDir + n + ".fbx");
            ImportTexture(BaseColorPath, true);
            ImportTexture(EmissionPath, true);
            ImportTexture(MetallicPath, false);
            ImportTexture(RoughnessPath, false);
            ThAtlasPacker.Pack(MetallicPath, RoughnessPath, MetallicSmoothnessPath);
            EnsureMaterial();
            AssetDatabase.SaveAssets();
            Debug.Log("[ThStageBuilder] ステージの素材を取り込みました");
        }

        [MenuItem("Tools/Treasure Haul/ステージを作り直す（stage_layout.json）")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ThStageBuilder] 再生中は実行できません"); return; }
            ImportAssets();
            var layout = LoadLayout();
            var scene = EnsureEnvironmentScene();
            var venue = FindVenue(scene);
            if (venue == null) { Debug.LogError("[ThStageBuilder] Environment シーンに ThVenue がありません"); return; }
            var root = venue.Root;
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            // 前の Stage と、古い仮の会場（置き場所の点・瓦礫・仮の荷台）を消す
            foreach (var n in new[] { "Stage", "PlacementPoints", "Rubble", "Cargo" })
            {
                var old = root.Find(n);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            var stage = new GameObject("Stage");
            stage.transform.SetParent(root, false);

            var colliderCounts = BuildModules(stage.transform, material);
            BuildColliders(stage.transform, layout, colliderCounts);
            var slots = BuildTreasureSlots(stage.transform, layout);
            AssignVenue(venue, slots, layout);
            BuildLights(stage.transform, layout);
            int probes = BuildLightProbes(stage.transform, layout);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ThStageBuilder] ステージを作りました: 壁 {colliderCounts["wall"]} / 縁 {colliderCounts["niche_lip"]} / 箱の家具 {colliderCounts["solid"]} / メッシュの家具 {colliderCounts["mesh"]} / 置き場所 {slots.Count} / 灯り {stage.GetComponentsInChildren<Light>(true).Length} / ライトプローブ {probes}");
        }

        [MenuItem("Tools/Treasure Haul/ステージの灯りを焼く")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ThStageBuilder] 再生中は実行できません"); return; }
            var scene = EnsureEnvironmentScene();
            SceneManager.SetActiveScene(scene);
            EnsureLightingSettings();
            EditorSceneManager.SaveScene(scene);
            bool started = Lightmapping.BakeAsync();
            Debug.Log("[ThStageBuilder] 焼き込みを始めました: " + started);
        }

        // ---- 取り込み ----
        static void ImportModel(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) throw new FileNotFoundException("FBX がありません: " + path);
            string before = ModelSignature(imp);
            imp.globalScale = 1f;
            imp.useFileScale = false;
            imp.bakeAxisConversion = false;
            imp.importNormals = ModelImporterNormals.Import;
            imp.importTangents = ModelImporterTangents.None;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.isReadable = Array.IndexOf(MeshColliderModules, Path.GetFileNameWithoutExtension(path)) >= 0;   // MeshCollider に使う家具だけ（他は読み書き不要）
            imp.generateSecondaryUV = true;
            imp.meshCompression = ModelImporterMeshCompression.Off;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importAnimation = false;
            imp.animationType = ModelImporterAnimationType.None;
            if (ModelSignature(imp) != before) imp.SaveAndReimport();
        }

        static string ModelSignature(ModelImporter m) =>
            $"{m.globalScale}|{m.useFileScale}|{m.bakeAxisConversion}|{m.importNormals}|{m.importTangents}|{m.materialImportMode}|{m.isReadable}|{m.generateSecondaryUV}|{m.meshCompression}|{m.importBlendShapes}|{m.importCameras}|{m.importLights}|{m.importAnimation}|{m.animationType}";

        static void ImportTexture(string path, bool srgb)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) throw new FileNotFoundException("テクスチャがありません: " + path);
            if (imp.sRGBTexture == srgb && imp.maxTextureSize >= 1024 && imp.mipmapEnabled) return;
            imp.sRGBTexture = srgb;
            imp.maxTextureSize = 1024;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }

        static void EnsureMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit シェーダーが見つかりません");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool isNew = mat == null;
            if (isNew) mat = new Material(shader) { name = "TH_StageAtlas" };
            mat.shader = shader;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicSmoothnessPath));
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f); // 0 = Metallic の Alpha
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(EmissionPath));
            mat.SetColor("_EmissionColor", Color.white * 1.5f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            if (isNew) AssetDatabase.CreateAsset(mat, MaterialPath);
            else EditorUtility.SetDirty(mat);
        }

        // ---- シーン ----
        static Scene EnsureEnvironmentScene()
        {
            var scene = EditorSceneManager.GetSceneByPath(EnvironmentScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(EnvironmentScenePath, OpenSceneMode.Additive);
            return scene;
        }

        static ThVenue FindVenue(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                var v = go.GetComponentInChildren<ThVenue>(true);
                if (v != null) return v;
            }
            return null;
        }

        static void SetStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        // ---- モジュール ----
        static Dictionary<string, int> BuildModules(Transform stage, Material material)
        {
            var counts = new Dictionary<string, int> { { "wall", 0 }, { "niche_lip", 0 }, { "solid", 0 }, { "mesh", 0 } };
            var modules = new GameObject("Modules");
            modules.transform.SetParent(stage, false);
            modules.transform.localScale = ModuleScale;
            foreach (var n in ModuleNames)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(StageDir + n + ".fbx");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, modules.transform);
                go.name = n;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.sharedMaterial = material;
                    if (n == "THStage_GroundFragments") r.shadowCastingMode = ShadowCastingMode.Off;
                    SetStatic(r.gameObject);
                }
                if (Array.IndexOf(MeshColliderModules, n) >= 0)
                {
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = false;
                        counts["mesh"]++;
                    }
                }
            }
            return counts;
        }

        // ---- コライダー ----
        static void BuildColliders(Transform stage, StageLayout layout, Dictionary<string, int> counts)
        {
            int wallLayer = LayerMask.NameToLayer(WallLayerName);
            if (wallLayer < 0) throw new InvalidOperationException(WallLayerName + " レイヤーがありません");
            var parent = new GameObject("Colliders").transform;
            parent.SetParent(stage, false);
            Transform walls = NewChild(parent, "Walls"), lips = NewChild(parent, "NicheLips"), solids = NewChild(parent, "Solids");
            for (int i = 0; i < layout.colliders.Length; i++)
            {
                var c = layout.colliders[i];
                Transform group;
                int layer = 0;
                switch (c.kind)
                {
                    case "wall": group = walls; layer = wallLayer; break;
                    case "niche_lip": group = lips; break;
                    case "solid":
                        if (Array.IndexOf(MeshColliderModules, c.module) >= 0) continue; // 家具はメッシュのコライダーを使う
                        group = solids; break;
                    default: continue;
                }
                var go = new GameObject($"{c.kind}_{i:000}_{c.name}") { layer = layer };
                go.transform.SetParent(group, false);
                go.transform.localPosition = V3(c.center_unity_xyz);
                go.transform.localRotation = Quaternion.Euler(V3(c.rotation_unity_xyz));
                var box = go.AddComponent<BoxCollider>();
                box.size = V3(c.size_unity_xyz);
                counts[c.kind]++;
            }
        }

        static Transform NewChild(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // ---- 置き場所 ----
        static List<ThPlacementPoint> BuildTreasureSlots(Transform stage, StageLayout layout)
        {
            var parent = NewChild(stage, "TreasureSlots");
            var list = new List<ThPlacementPoint>();
            foreach (var m in layout.markers)
            {
                if (m.role != "treasure_slot") continue;
                var go = new GameObject(m.name);
                go.transform.SetParent(parent, false);
                float yaw = Mathf.Round(Mathf.Atan2(m.facing_unity_xz[0], m.facing_unity_xz[1]) * Mathf.Rad2Deg * 100f) / 100f;
                go.transform.localPosition = V3(m.position_unity_xyz);
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                var p = go.AddComponent<ThPlacementPoint>();
                p.AllowS = Array.IndexOf(m.allowed_classes, "S") >= 0;
                p.AllowM = Array.IndexOf(m.allowed_classes, "M") >= 0;
                p.MaxSize = V3(m.max_item_size_m);
                p.YawDegrees = yaw;
                p.Zone = m.zone;
                p.Hidden = m.hidden_intent;
                list.Add(p);
            }
            return list;
        }

        static void AssignVenue(ThVenue venue, List<ThPlacementPoint> slots, StageLayout layout)
        {
            var so = new SerializedObject(venue);
            var points = so.FindProperty("_points");
            points.arraySize = slots.Count;
            for (int i = 0; i < slots.Count; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];

            // 回収範囲・表示板
            var zone = so.FindProperty("_collectZone").objectReferenceValue as BoxCollider;
            var board = so.FindProperty("_board").objectReferenceValue as Component;
            foreach (var m in layout.markers)
            {
                if (m.role == "collection_volume" && zone != null)
                {
                    zone.transform.position = venue.Root.TransformPoint(V3(m.position_unity_xyz));
                    zone.center = Vector3.zero;
                    zone.size = V3(m.size);
                }
                else if (m.name == "ScoreBoardAnchor" && board != null)
                {
                    board.transform.position = venue.Root.TransformPoint(V3(m.position_unity_xyz));
                }
            }

            // 練習の枠（X の小さい方から色番号 0）
            var tut = new List<StageMarker>();
            foreach (var m in layout.markers) if (m.role == "tutorial") tut.Add(m);
            tut.Sort((a, b) => a.position_unity_xyz[0].CompareTo(b.position_unity_xyz[0]));
            var tutProp = so.FindProperty("_tutorialSlots");
            for (int i = 0; i < tutProp.arraySize; i++)
            {
                var slot = tutProp.GetArrayElementAtIndex(i).objectReferenceValue as ThTutorialSlot;
                if (slot == null || slot.ColorIndex < 0 || slot.ColorIndex >= tut.Count) continue;
                var pos = venue.Root.TransformPoint(V3(tut[slot.ColorIndex].position_unity_xyz));
                slot.transform.position = new Vector3(pos.x, 0f, pos.z);
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(venue);
        }

        // ---- 灯り ----
        static void BuildLights(Transform stage, StageLayout layout)
        {
            var parent = NewChild(stage, "Lights");
            foreach (var m in layout.markers)
            {
                if (m.role != "light_anchor") continue;
                var go = new GameObject(m.name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = V3(m.position_unity_xyz);
                var l = go.AddComponent<Light>();
                bool bulb = m.name.StartsWith("Light_Bulb", StringComparison.Ordinal);
                if (bulb)
                {
                    l.type = LightType.Point;
                    l.range = BulbRange;
                    l.intensity = BulbIntensity;
                    l.colorTemperature = BulbKelvin;
                    l.color = Mathf.CorrelatedColorTemperatureToRGB(BulbKelvin);
                }
                else
                {
                    l.type = LightType.Spot;
                    l.range = SpotRange;
                    l.spotAngle = SpotAngle;
                    l.innerSpotAngle = SpotAngle * 0.6f;
                    l.intensity = m.blender_watts * SpotIntensityPerWatt;
                    l.colorTemperature = LampKelvin;
                    l.color = Mathf.CorrelatedColorTemperatureToRGB(LampKelvin);
                    if (m.aim_unity_xyz != null && m.aim_unity_xyz.Length == 3)
                        go.transform.rotation = Quaternion.LookRotation(stage.TransformPoint(V3(m.aim_unity_xyz)) - go.transform.position);
                }
                l.useColorTemperature = false;
                l.lightmapBakeType = LightmapBakeType.Baked;
                l.shadows = LightShadows.Soft;
            }
        }

        // ---- ライトプローブ ----
        static int BuildLightProbes(Transform stage, StageLayout layout)
        {
            var go = new GameObject("LightProbes");
            go.transform.SetParent(stage, false);
            var group = go.AddComponent<LightProbeGroup>();
            var positions = ComputeProbePositions(layout);
            group.probePositions = positions.ToArray();
            return positions.Count;
        }

        /// <summary>歩ける範囲（会場の中で、壁と家具の外形の箱の外）に、高さ 0.3 / 1.0 / 1.6m の 3 段、約 0.8m 間隔で置く点（Stage ローカル）</summary>
        public static List<Vector3> ComputeProbePositions(StageLayout layout)
        {
            var solids = new List<StageCollider>();
            foreach (var c in layout.colliders) if (c.kind == "wall" || c.kind == "solid") solids.Add(c);
            var result = new List<Vector3>();
            for (float x = -2.0f; x <= 2.001f; x += ProbeSpacing)
            for (float z = 0.4f; z <= 10.801f; z += ProbeSpacing)
            foreach (var y in ProbeHeights)
            {
                var p = new Vector3(Mathf.Round(x * 100f) / 100f, y, Mathf.Round(z * 100f) / 100f);
                if (!InsideAny(solids, p, ProbeWallMargin)) result.Add(p);
            }
            return result;
        }

        static bool InsideAny(List<StageCollider> boxes, Vector3 p, float margin)
        {
            foreach (var c in boxes)
            {
                var local = Quaternion.Inverse(Quaternion.Euler(V3(c.rotation_unity_xyz))) * (p - V3(c.center_unity_xyz));
                var half = V3(c.size_unity_xyz) * 0.5f + Vector3.one * margin;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z) return true;
            }
            return false;
        }

        // ---- 焼き込みの設定 ----
        static void EnsureLightingSettings()
        {
            LightingSettings ls;
            if (!Lightmapping.TryGetLightingSettings(out ls) || ls == null)
            {
                ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
                if (ls == null)
                {
                    ls = new LightingSettings { name = "ThEnvironmentLighting" };
                    AssetDatabase.CreateAsset(ls, LightingSettingsPath);
                }
                Lightmapping.lightingSettings = ls;
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.autoGenerate = false;
            ls.mixedBakeMode = MixedLightingMode.Subtractive;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            ls.lightmapResolution = 40f;
            ls.lightmapMaxSize = 1024;
            ls.lightmapPadding = 2;
            ls.directionalityMode = LightmapsMode.NonDirectional;
            ls.directSampleCount = 64;
            ls.indirectSampleCount = 256;
            ls.environmentSampleCount = 128;
            ls.maxBounces = 2;
            ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(ls);
            AssetDatabase.SaveAssets();
        }
    }
}
