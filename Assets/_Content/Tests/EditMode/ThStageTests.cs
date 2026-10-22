using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using STYLY.TIB_Template.TreasureHaul.Editor;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>高い壁で区切った遺跡内部のステージ（回廊）の取り込み・X 軸の向き・Environment シーンの組み立て（企画書 7.1）</summary>
    public class ThStageTests
    {
        static ThStageBuilder.StageLayout Layout() => ThStageBuilder.LoadLayout();

        /// <summary>FBX のモジュールを、ステージと同じ向き（Modules の X 反転）で置いた物（テスト用の一時物）</summary>
        sealed class ModuleRig : System.IDisposable
        {
            public readonly GameObject Parent;
            public readonly GameObject Module;

            public ModuleRig(string moduleName)
            {
                Parent = new GameObject("TestModules") { hideFlags = HideFlags.HideAndDontSave };
                Parent.transform.localScale = ThStageBuilder.ModuleScale;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ThStageBuilder.StageDir + moduleName + ".fbx");
                Assert.IsNotNull(asset, moduleName);
                Module = Object.Instantiate(asset, Parent.transform);
            }

            public Bounds WorldBounds()
            {
                var rs = Module.GetComponentsInChildren<MeshRenderer>();
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                return b;
            }

            public void Dispose() => Object.DestroyImmediate(Parent);
        }

        static Bounds BoxesBounds(IEnumerable<ThStageBuilder.StageCollider> boxes)
        {
            Bounds? u = null;
            foreach (var c in boxes)
            {
                var cen = new Vector3(c.center_unity_xyz[0], c.center_unity_xyz[1], c.center_unity_xyz[2]);
                var sz = new Vector3(c.size_unity_xyz[0], c.size_unity_xyz[1], c.size_unity_xyz[2]);
                var rot = Quaternion.Euler(c.rotation_unity_xyz[0], c.rotation_unity_xyz[1], c.rotation_unity_xyz[2]);
                for (int i = 0; i < 8; i++)
                {
                    var corner = cen + rot * Vector3.Scale(sz * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    if (u == null) u = new Bounds(corner, Vector3.zero);
                    else { var b = u.Value; b.Encapsulate(corner); u = b; }
                }
            }
            return u.Value;
        }

        // ---- 取り込み ----

        [Test]
        public void 十四のモジュールが法線を読み込み一メートル単位で家具だけ読み書きできライトマップ用のUVを持つ()
        {
            foreach (var n in ThStageBuilder.ModuleNames)
            {
                var path = ThStageBuilder.StageDir + n + ".fbx";
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(imp, path);
                Assert.AreEqual(ModelImporterNormals.Import, imp.importNormals, n);
                Assert.AreEqual(1f, imp.globalScale, n);
                bool collider = ThStageBuilder.MeshColliderModules.Contains(n);
                Assert.AreEqual(collider, imp.isReadable, n + " の読み書き（MeshCollider に使う家具だけ true）");
                Assert.IsTrue(imp.generateSecondaryUV, n);
                var mesh = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<MeshFilter>().sharedMesh;
                Assert.IsTrue(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1), n + " のライトマップ用 UV");
            }
            Assert.AreEqual(14, ThStageBuilder.ModuleNames.Length);
        }

        [Test]
        public void ステージの材質はURPLitの1つで_MetallicSmoothnessとEmissionを使う()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ThStageBuilder.MaterialPath);
            Assert.IsNotNull(mat);
            Assert.AreEqual("Universal Render Pipeline/Lit", mat.shader.name);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(ThStageBuilder.BaseColorPath), mat.GetTexture("_BaseMap"));
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(ThStageBuilder.MetallicSmoothnessPath), mat.GetTexture("_MetallicGlossMap"));
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(ThStageBuilder.EmissionPath), mat.GetTexture("_EmissionMap"));
            Assert.AreEqual(0f, mat.GetFloat("_SmoothnessTextureChannel"), "Smoothness の出どころは Metallic の Alpha");
            Assert.IsTrue(mat.IsKeywordEnabled("_EMISSION"));
            Assert.AreEqual(MaterialGlobalIlluminationFlags.BakedEmissive, mat.globalIlluminationFlags);
            var packed = (TextureImporter)AssetImporter.GetAtPath(ThStageBuilder.MetallicSmoothnessPath);
            Assert.IsFalse(packed.sRGBTexture, "MetallicSmoothness は sRGB オフ");
        }

        // ---- X 軸の向き ----

        [Test]
        public void X軸_前の部屋の扉は西側で奥の部屋の扉は東側にある()
        {
            using (var rig = new ModuleRig("THStage_Block_Shell"))
            {
                var mc = rig.Module.AddComponent<MeshCollider>();
                mc.sharedMesh = rig.Module.GetComponentInChildren<MeshFilter>().sharedMesh;
                Physics.SyncTransforms();
                // 前の部屋の扉 = 西面（X<0）の奥行 4.7〜5.9。X=-1.5 から +X へ撃つと壁（X≈-1.08）に当たらず部屋に入る
                Assert.IsFalse(Hit(mc, new Vector3(-1.5f, 1.2f, 5.3f), Vector3.right, out _), "前の部屋の西の扉が閉じている（X が反転している）");
                // 逆側（東面）は壁
                Assert.IsTrue(Hit(mc, new Vector3(1.5f, 1.2f, 5.3f), Vector3.left, out float eastX));
                Assert.Less(eastX, 1.12f); Assert.Greater(eastX, 0.85f);
                // 奥の部屋の扉 = 東面の奥行 6.6〜7.8
                Assert.IsFalse(Hit(mc, new Vector3(1.5f, 1.2f, 7.2f), Vector3.left, out _), "奥の部屋の東の扉が閉じている（X が反転している）");
                Assert.IsTrue(Hit(mc, new Vector3(-1.5f, 1.2f, 7.2f), Vector3.right, out float westX));
                Assert.Greater(westX, -1.12f); Assert.Less(westX, -0.85f);
            }
        }

        static bool Hit(MeshCollider mc, Vector3 origin, Vector3 dir, out float x)
        {
            x = 0f;
            var hits = Physics.RaycastAll(origin, dir, 1.4f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool any = false;
            foreach (var h in hits)
                if (h.collider == mc && h.distance < best) { best = h.distance; x = h.point.x; any = true; }
            return any;
        }

        [Test]
        public void 壁のモジュールのメッシュの外形とJSONの箱は5cm以内で一致する()
        {
            var layout = Layout();
            foreach (var n in new[] { "THStage_OuterWall_W", "THStage_OuterWall_E", "THStage_BackWall", "THStage_Block_Shell", "THStage_Block_Partition", "THStage_HallPartitions" })
            {
                using (var rig = new ModuleRig(n))
                {
                    var mesh = rig.WorldBounds();
                    var json = BoxesBounds(layout.colliders.Where(c => c.module == n && c.kind == "wall"));
                    float worst = Mathf.Max(
                        Mathf.Max(Mathf.Abs(mesh.min.x - json.min.x), Mathf.Abs(mesh.max.x - json.max.x)),
                        Mathf.Max(Mathf.Max(Mathf.Abs(mesh.min.z - json.min.z), Mathf.Abs(mesh.max.z - json.max.z)), Mathf.Max(Mathf.Abs(mesh.min.y - json.min.y), Mathf.Abs(mesh.max.y - json.max.y))));
                    Assert.LessOrEqual(worst, 0.05f, $"{n}: メッシュ {mesh} と JSON {json} が 5cm 以上ずれている");
                }
            }
        }

        [Test]
        public void 家具のMeshColliderの上面が置き場所の高さと合う()
        {
            // メッシュの家具の置き場所（家具の外形の箱の中）の真上から撃つと、置き場所の高さの面に当たる
            var layout = Layout();
            var rigs = ThStageBuilder.MeshColliderModules.Select(n => new ModuleRig(n)).ToList();
            try
            {
                var cols = new List<MeshCollider>();
                foreach (var r in rigs)
                {
                    var mc = r.Module.AddComponent<MeshCollider>();
                    mc.sharedMesh = r.Module.GetComponentInChildren<MeshFilter>().sharedMesh;
                    cols.Add(mc);
                }
                Physics.SyncTransforms();
                int checkedCount = 0;
                foreach (var m in layout.markers.Where(x => x.role == "treasure_slot"))
                {
                    var pos = new Vector3(m.position_unity_xyz[0], m.position_unity_xyz[1], m.position_unity_xyz[2]);
                    var hits = Physics.RaycastAll(pos + Vector3.up * 0.3f, Vector3.down, 1.0f, ~0, QueryTriggerInteraction.Ignore);
                    bool onFurniture = hits.Any(h => cols.Contains(h.collider));
                    if (!onFurniture) continue;   // 壁龕など（壁のモジュールの上）の置き場所は箱のコライダー側
                    var top = hits.Where(h => cols.Contains(h.collider)).OrderBy(h => h.distance).First();
                    Assert.AreEqual(pos.y, top.point.y, 0.03f, m.name);
                    checkedCount++;
                }
                Assert.Greater(checkedCount, 10, "家具の上の置き場所が少なすぎる");
            }
            finally { foreach (var r in rigs) r.Dispose(); }
        }

        // ---- JSON から作るデータ ----

        [Test]
        public void JSONの内訳_壁78_縁15_箱の家具8_置き場所34_練習4_灯り23()
        {
            var l = Layout();
            Assert.AreEqual(78, l.colliders.Count(c => c.kind == "wall"));
            Assert.AreEqual(15, l.colliders.Count(c => c.kind == "niche_lip"));
            Assert.AreEqual(8, l.colliders.Count(c => c.kind == "solid" && !ThStageBuilder.MeshColliderModules.Contains(c.module)));
            Assert.AreEqual(34, l.markers.Count(m => m.role == "treasure_slot"));
            Assert.AreEqual(4, l.markers.Count(m => m.role == "tutorial"));
            Assert.AreEqual(23, l.markers.Count(m => m.role == "light_anchor"));
            Assert.AreEqual(12, l.markers.Count(m => m.name.StartsWith("Light_Bulb")));
        }

        [Test]
        public void ライトプローブは三段で壁と家具の箱の中に無く間隔は約0_8m()
        {
            var l = Layout();
            var probes = ThStageBuilder.ComputeProbePositions(l);
            Assert.Greater(probes.Count, 100);
            CollectionAssert.AreEquivalent(new[] { 0.3f, 1.0f, 1.6f }, probes.Select(p => p.y).Distinct().ToArray());
            foreach (var p in probes)
            {
                Assert.GreaterOrEqual(p.z, 0.3f); Assert.LessOrEqual(p.z, 10.9f);
                Assert.LessOrEqual(Mathf.Abs(p.x), 2.1f);
                foreach (var c in l.colliders.Where(c => c.kind == "wall" || c.kind == "solid"))
                {
                    var local = Quaternion.Inverse(Quaternion.Euler(c.rotation_unity_xyz[0], c.rotation_unity_xyz[1], c.rotation_unity_xyz[2]))
                        * (p - new Vector3(c.center_unity_xyz[0], c.center_unity_xyz[1], c.center_unity_xyz[2]));
                    var half = new Vector3(c.size_unity_xyz[0], c.size_unity_xyz[1], c.size_unity_xyz[2]) * 0.5f;
                    bool inside = Mathf.Abs(local.x) < half.x && Mathf.Abs(local.y) < half.y && Mathf.Abs(local.z) < half.z;
                    Assert.IsFalse(inside, $"プローブ {p} が {c.name} の中にある");
                }
            }
        }

        // ---- Environment シーン ----

        [Test]
        public void Environmentシーンのステージが仕様どおりに組み立てられている()
        {
            var layout = Layout();
            var existing = SceneManager.GetSceneByPath(ThStageBuilder.EnvironmentScenePath);
            bool opened = !existing.isLoaded;
            var scene = existing.isLoaded ? existing : EditorSceneManager.OpenScene(ThStageBuilder.EnvironmentScenePath, OpenSceneMode.Additive);
            try
            {
                ThVenue venue = null;
                foreach (var go in scene.GetRootGameObjects()) { venue = go.GetComponentInChildren<ThVenue>(true); if (venue != null) break; }
                Assert.IsNotNull(venue);
                var root = venue.Root;
                var stage = root.Find("Stage");
                Assert.IsNotNull(stage, "VenueRoot/Stage");
                Assert.IsNull(root.Find("PlacementPoints"), "古い PlacementPoints が残っている");
                Assert.IsNull(root.Find("Rubble"), "古い Rubble が残っている");
                Assert.IsNotNull(root.Find("VenueFloor"));

                // モジュール 14 個（静的・GI に寄与・X 反転の親の下）
                var modules = stage.Find("Modules");
                Assert.AreEqual(ThStageBuilder.ModuleScale, modules.localScale);
                Assert.AreEqual(14, modules.childCount);
                foreach (Transform m in modules)
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(m.gameObject);
                    Assert.IsTrue((flags & StaticEditorFlags.ContributeGI) != 0, m.name + " は Contribute GI");
                    Assert.AreEqual(ThStageBuilder.MaterialPath, AssetDatabase.GetAssetPath(m.GetComponent<MeshRenderer>().sharedMaterial));
                }

                // コライダー: 壁 78（ThWall の箱）・縁 15（Default の箱）・箱の家具 8（Default）・メッシュの家具 5（Default、非凸）
                int wallLayer = LayerMask.NameToLayer(ThStageBuilder.WallLayerName);
                var boxes = stage.GetComponentsInChildren<BoxCollider>(true);
                Assert.AreEqual(78, boxes.Count(b => b.gameObject.layer == wallLayer));
                Assert.AreEqual(23, boxes.Count(b => b.gameObject.layer == 0));
                var meshes = stage.GetComponentsInChildren<MeshCollider>(true);
                Assert.AreEqual(5, meshes.Length);
                Assert.IsTrue(meshes.All(c => !c.convex && c.gameObject.layer == 0 && c.sharedMesh != null));

                // 置き場所 34（JSON の番号順に ThVenue へ）
                Assert.AreEqual(34, venue.PlacementPointCount);
                var points = venue.GetPlacementPoints();
                var slots = layout.markers.Where(m => m.role == "treasure_slot").ToList();
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    var want = new Vector3(s.position_unity_xyz[0], s.position_unity_xyz[1], s.position_unity_xyz[2]);
                    Assert.AreEqual(0f, Vector3.Distance(want, points[i].LocalPosition), 0.001f, s.name);
                    Assert.AreEqual(s.allowed_classes.Contains("S"), points[i].AllowS, s.name);
                    Assert.AreEqual(s.allowed_classes.Contains("M"), points[i].AllowM, s.name);
                    Assert.AreEqual(s.zone, points[i].Zone, s.name);
                    Assert.AreEqual(s.hidden_intent, points[i].Hidden, s.name);
                    var wantSize = new Vector3(s.max_item_size_m[0], s.max_item_size_m[1], s.max_item_size_m[2]);
                    Assert.AreEqual(0f, Vector3.Distance(wantSize, points[i].MaxSize), 0.001f, s.name + " の MaxSize");
                    float wantYaw = Mathf.Atan2(s.facing_unity_xz[0], s.facing_unity_xz[1]) * Mathf.Rad2Deg;
                    Assert.AreEqual(0f, Mathf.DeltaAngle(wantYaw, points[i].YawDegrees), 0.1f, s.name + " の YawDegrees");
                }

                // 練習の枠（X の小さい方から色番号 0。奥行 2.5）
                var practice = venue.GetPracticeSlots();
                Assert.AreEqual(4, practice.Count);
                var xs = new[] { -1.2f, -0.4f, 0.4f, 1.2f };
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(xs[i], practice[i].LocalPosition.x, 0.001f);
                    Assert.AreEqual(2.5f, practice[i].LocalPosition.z, 0.001f);
                }

                // 回収範囲・表示板
                var bounds = venue.GetCollectBounds();
                Assert.AreEqual(new Vector3(0f, 0.49f, 1.2f), bounds.center);
                Assert.AreEqual(new Vector3(2.3f, 0.92f, 1.56f), bounds.size);
                Assert.AreEqual(new Vector3(0f, 1.5f, 0.32f), venue.Board.transform.position);

                // 灯り 23（すべて焼き込み）。スポット 11・点光源 12
                var lights = stage.GetComponentsInChildren<Light>(true);
                Assert.AreEqual(23, lights.Length);
                Assert.IsTrue(lights.All(l => l.lightmapBakeType == LightmapBakeType.Baked));
                Assert.AreEqual(11, lights.Count(l => l.type == LightType.Spot));
                Assert.AreEqual(12, lights.Count(l => l.type == LightType.Point));

                // ライトプローブ
                var group = stage.GetComponentInChildren<LightProbeGroup>(true);
                Assert.IsNotNull(group);
                Assert.AreEqual(ThStageBuilder.ComputeProbePositions(layout).Count, group.probePositions.Length);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
